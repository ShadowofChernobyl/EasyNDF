using System.Numerics;

namespace EasyNdf.Data.Effects;

/// <summary>An effect started by <see cref="FxPlayer.Start"/>, which can be stopped (a jet's exhaust when its engines stop).</summary>
public sealed class FxEffect
{
    internal FxEffect(string name) => Name = name;

    /// <summary>The effect's name, as GameFx names it.</summary>
    public string Name { get; }

    /// <summary>Whether it has been stopped: it spawns nothing more, and its particles live out their lives.</summary>
    public bool IsStopped { get; internal set; }
}

/// <summary>
/// Plays the game's effect scripts: runs their actions as the game sequences them (all at once, one after another,
/// repeated, after a wait), spawns their particles in bursts and sprays, starts each particle from its initialisers
/// and moves it on with its evolvers each step, moves projectiles' frames along their flight, and keeps the lights
/// flashes give off. What the viewer cannot show (meshes, trail ribbons, marks on the ground, heat haze, the camera
/// shaking) is listed in <see cref="NotShown"/>; scripts that could not be worked out in <see cref="Problems"/>.
/// </summary>
public sealed class FxPlayer
{
    /// <summary>The most particles alive at once, so a runaway spray cannot take the viewer down.</summary>
    public const int MostParticles = 12000;

    private static readonly string[] InitialiserOrder =
    [
        "Position", "Direction", "Speed", "Size", "ColorRGB", "ColorModifier", "Alpha", "Opacity", "ColorMultiplier", "EmissivePower",
        "Rotation", "RotationSpeed", "ZBias", "Count", "Stretching",
        "CustomFloat_1", "CustomFloat_2", "CustomFloat_3", "CustomFloat_4", "CustomFloat_5", "CustomFloat_6", "CustomFloat_7", "CustomFloat_8", "CustomFloat_9",
        "CustomFloat3_1", "CustomFloat3_2", "CustomFloat3_3", "CustomFloat3_4", "CustomFloat3_5", "CustomFloat3_6", "CustomFloat3_7", "CustomFloat3_8", "CustomFloat3_9",
        "UpVector", "DeathDate", "ScaleForMesh", "RotationAxisForMesh",
    ];

    // a direction before the position: a gun's flash is placed ahead of the muzzle along its direction from its first step
    private static readonly string[] EvolverOrder =
    [
        "Direction", "Position", "Speed", "Size", "ColorRGB", "ColorModifier", "Alpha", "Opacity", "ColorMultiplier", "EmissivePower", "ZBias", "Count",
        "TexCoordU", "Stretching", "Animation", "UpVector",
        "CustomFloat_1", "CustomFloat_2", "CustomFloat_3", "CustomFloat_4", "CustomFloat_5", "CustomFloat_6", "CustomFloat_7", "CustomFloat_8", "CustomFloat_9",
        "CustomFloat3_1", "CustomFloat3_2", "CustomFloat3_3", "CustomFloat3_4", "CustomFloat3_5", "CustomFloat3_6", "CustomFloat3_7", "CustomFloat3_8", "CustomFloat3_9",
        "RotationSpeed", "Rotation", "OffsetAlongDirection", "RotationAxisForMesh",
    ];

    private readonly FxEvaluator evaluator;
    private readonly FxMoment moment = new();
    private readonly List<(FxEffect Effect, Running Action)> running = [];
    private readonly List<FxParticle> particles = [];
    private readonly SortedSet<string> notShown = new(StringComparer.Ordinal);
    private readonly HashSet<string> problems = new(StringComparer.Ordinal);

    /// <summary>The effect whose actions are being made or moved on, which the particles they spawn belong to.</summary>
    private FxEffect? stepping;

    /// <summary>A player of the effects in these files.</summary>
    public FxPlayer(FxLibrary library) => evaluator = new FxEvaluator(library);

    /// <summary>The effects' time, in seconds.</summary>
    public double Now { get; private set; }

    /// <summary>The particles alive now.</summary>
    public IReadOnlyList<FxParticle> Particles => particles;

    /// <summary>What the effects played so far have that the viewer does not show, each named once.</summary>
    public IReadOnlyCollection<string> NotShown => notShown;

    /// <summary>The effect scripts that could not be worked out, each with what went wrong.</summary>
    public IReadOnlyCollection<string> Problems => problems;

    /// <summary>Whether anything is still running or alive.</summary>
    public bool IsBusy => running.Count > 0 || particles.Count > 0;

    /// <summary>Where the camera is, for effects that change with distance.</summary>
    public Vector3 Camera { get; set; }

    /// <summary>
    /// Starts an effect (a GameFx name such as fx_tir_obus_105mm_Abrams_01) in a frame, with values for its parameters
    /// where the game would give them (a projectile's destination); null when no effect file has it.
    /// </summary>
    public FxEffect? Start(string effect, FxFrame frame, IReadOnlyDictionary<string, FxValue>? given = null)
    {
        var started = new FxEffect(effect[(effect.LastIndexOf('/') + 1)..]);
        if (evaluator.Library.Find(effect) is null)
        {
            problems.Add(started.Name + ": no effect file has it");
            return null;
        }
        var named = new List<(string, FxNode, FxScope)>();
        foreach ((string name, FxValue value) in given ?? new Dictionary<string, FxValue>()) named.Add((name, new FxConstant(value), evaluator.Outermost));
        stepping = started;
        Running? action = Guarded(started.Name, () => Make(new FxName(effect), evaluator.Outermost, frame, named));
        stepping = null;
        if (action is not null) running.Add((started, action));
        return started;
    }

    /// <summary>Stops an effect: it spawns nothing more, and its particles live out their lives.</summary>
    public void Stop(FxEffect effect)
    {
        effect.IsStopped = true;
        foreach ((FxEffect each, Running action) in running)
            if (each == effect) action.Stop();
    }

    /// <summary>Moves the effects on by this many seconds: their actions, then their particles.</summary>
    public void Step(double seconds)
    {
        Now += seconds;
        for (int i = 0; i < running.Count; i++)
        {
            (FxEffect effect, Running action) = running[i];
            stepping = effect;
            if (Guarded(effect.Name, () => action.Step(seconds)) != true) running.RemoveAt(i--);
        }
        stepping = null;
        moment.StepSeconds = (float)seconds;
        for (int i = 0; i < particles.Count; i++)
        {
            FxParticle particle = particles[i];
            if (Now >= particle.Dies || (particle.Kind.Infinite && particle.Effect?.IsStopped == true) || !Guarded("a particle of " + particle.Kind.Texture, () => Evolve(particle, (float)seconds)))
            {
                particles[i] = particles[^1];
                particles.RemoveAt(particles.Count - 1);
                i--;
            }
        }
    }

    /// <summary>Stops everything at once and forgets its particles.</summary>
    public void Clear()
    {
        running.Clear();
        particles.Clear();
        particles.TrimExcess();
    }

    /// <summary>Runs part of an effect; when its script cannot be worked out, notes why and gives the default.</summary>
    private T? Guarded<T>(string what, Func<T> run)
    {
        try
        {
            return run();
        }
        catch (FxProblem problem)
        {
            problems.Add(what + ": " + problem.Message);
            return default;
        }
    }

    private FxValue Evaluate(FxBinding binding) => evaluator.Evaluate(binding.Value, binding.Scope, moment);

    private FxValue Evaluate(FxNode node, FxScope scope) => evaluator.Evaluate(node, scope, moment);

    private void SetMoment(FxFrame frame, FxSpawner? spawner = null, FxParticle? particle = null)
    {
        moment.Now = Now;
        moment.Frame = frame;
        moment.Spawner = spawner;
        moment.Particle = particle;
        moment.Camera = Camera;
    }

    // ---- finding what a name or a template stands for ----

    /// <summary>
    /// The object a name, a template's parameter or a template's use stands for, with the scope its values are worked
    /// out in; null when it stands for nothing the effect files have. A template's use is filled out from the
    /// template: its body, with its parameters bound to the use's values or their defaults.
    /// </summary>
    private (FxObject Object, FxScope Scope)? Resolve(FxNode? node, FxScope scope)
    {
        for (int step = 0; step < 64 && node is not null; step++)
        {
            switch (node)
            {
                case FxScoped scoped:
                    node = scoped.Value;
                    scope = scoped.Scope;
                    continue;
                case FxTemplateParameter parameter:
                    if (!scope.TryFind("<" + parameter.Name + ">", out FxBinding given)) return null;
                    (node, scope) = (given.Value, given.Scope);
                    continue;
                case FxName name:
                    if (scope.TryFind(name.Last, out FxBinding bound) && !ReferenceEquals(bound.Value, name))
                    {
                        (node, scope) = (bound.Value, bound.Scope);
                        continue;
                    }
                    if (Member(name, scope) is FxBinding member)
                    {
                        (node, scope) = (member.Value, member.Scope);
                        continue;
                    }
                    if (evaluator.Library.Find(name) is not FxDeclaration declaration) return null;
                    (node, scope) = (declaration.Value, evaluator.Outermost);
                    continue;
                case FxObject thing:
                    if (evaluator.Library.Find(thing.Type) is { TemplateParameters: not null, Value: FxObject body } template)
                    {
                        (node, scope) = Filled(thing, template, body, scope);
                        continue;
                    }
                    if (thing.Members.Count == 0) return (thing, scope);
                    // an object's own declarations (ActionList is [...]) are seen by its values
                    var inside = new FxScope(scope);
                    foreach ((string declared, FxNode value) in thing.Members) inside.Bind(declared, value, inside);
                    return (thing, inside);
                default:
                    return null;
            }
        }
        return null;
    }

    private (FxObject, FxScope) Filled(FxObject use, FxDeclaration template, FxObject body, FxScope useScope)
    {
        var inside = new FxScope(useScope);
        foreach ((string parameter, FxNode? defaultValue) in template.TemplateParameters!)
        {
            if (use.Properties.TryGetValue(parameter, out FxNode? value)) inside.Bind("<" + parameter + ">", value, useScope);
            else if (defaultValue is not null) inside.Bind("<" + parameter + ">", defaultValue, evaluator.Outermost);
        }
        var properties = new Dictionary<string, FxNode>(body.Properties, StringComparer.Ordinal);
        foreach ((string name, FxNode value) in use.Properties)
            if (!template.TemplateParameters!.ContainsKey(name)) properties[name] = new FxScoped(value, useScope);
        return (new FxObject(body.Type, properties, body.Members), inside);
    }

    /// <summary>A value named Owner.Property, as Bomb_Model_List.Value: the property of the object the owner stands for; null for none.</summary>
    private FxBinding? Member(FxName name, FxScope scope)
    {
        int dot = name.Last.IndexOf('.');
        if (dot <= 0 || Resolve(new FxName(name.Last[..dot]), scope) is not (FxObject owner, FxScope ownerScope)) return null;
        return owner.Get(name.Last[(dot + 1)..]) is FxNode value ? new FxBinding(value, ownerScope) : null;
    }

    /// <summary>The items of a list value, each with the scope it is worked out in.</summary>
    private List<FxBinding> Items(FxNode? node, FxScope scope)
    {
        var items = new List<FxBinding>();
        for (int step = 0; step < 64 && node is not null; step++)
        {
            switch (node)
            {
                case FxScoped scoped:
                    (node, scope) = (scoped.Value, scoped.Scope);
                    continue;
                case FxTemplateParameter parameter when scope.TryFind("<" + parameter.Name + ">", out FxBinding given):
                    (node, scope) = (given.Value, given.Scope);
                    continue;
                case FxName name when scope.TryFind(name.Last, out FxBinding bound):
                    (node, scope) = (bound.Value, bound.Scope);
                    continue;
                case FxName name when evaluator.Library.Find(name)?.Value is FxList declared:
                    (node, scope) = (declared, evaluator.Outermost);
                    continue;
                case FxName name when Member(name, scope) is FxBinding member:
                    (node, scope) = (member.Value, member.Scope);
                    continue;
                case FxList list:
                    foreach (FxNode item in list.Items) items.Add(new FxBinding(item is FxPair { Key: FxName, Value: var declared } ? declared : item, scope));
                    return items;
                case FxBinary { Operator: "+" } joined:
                    items.AddRange(Items(joined.Left, scope));
                    items.AddRange(Items(joined.Right, scope));
                    return items;
                case FxChoice choice:
                    node = Evaluate(choice.Condition, scope).IsTrue ? choice.WhenTrue : choice.WhenFalse;
                    continue;
                default:
                    return items;
            }
        }
        return items;
    }

    /// <summary>A MAP[('name', value) ...] of values for an action's parameters, each worked out in this scope when used.</summary>
    private List<(string Name, FxNode Value, FxScope Scope)> Named(FxNode? map, FxScope scope)
    {
        var named = new List<(string, FxNode, FxScope)>();
        while (map is FxScoped scoped) (map, scope) = (scoped.Value, scoped.Scope);
        if (map is FxCall { Callee: FxName { Last: "MAP" } } call)
            foreach (FxNode argument in call.Arguments)
                if (argument is FxPair { Key: FxText key } pair) named.Add((key.Value, pair.Value, scope));
        return named;
    }

    // ---- actions ----

    /// <summary>An action as it runs: <see cref="Step"/> moves it on and says whether it is still running.</summary>
    private abstract class Running
    {
        public abstract bool Step(double seconds);

        public virtual void Stop()
        {
        }
    }

    /// <summary>
    /// An action ready to run, made from its script; null when it has nothing left to do once made (a burst of
    /// particles is spawned as it is made), or the viewer does not run it.
    /// </summary>
    private Running? Make(FxNode node, FxScope scope, FxFrame frame, List<(string Name, FxNode Value, FxScope Scope)>? named)
    {
        if (Resolve(node, scope) is not (FxObject action, FxScope own)) return null;
        bool Off() => action.Get("Disabled") is FxNode disabled && Evaluate(disabled, own).IsTrue;
        switch (action.Type)
        {
            case "SimultaneousActionDeclaration" or "TSimultaneousActionForActionCallTemplate":
            {
                FxScope called = Called(action, own, scope, named);
                FxFrame inside = FrameOf(action.Get("Mobile"), called, frame);
                Pin(action.Get("LocalVariables"), called, inside);
                Running? all = AllAtOnce(Items(action.Get("Actions"), called), inside);
                double most = action.Get("MaxDuration") is FxNode limit ? Evaluate(limit, called).Number : 0;
                return all is not null && most > 0 ? new Limited(all, most) : all;
            }
            case "ActionCallWithParameters" or "Template_ActionWithParameters":
            {
                FxScope called = Called(action, own, scope, named);
                return action.Get("Action") is FxNode inner ? Make(inner, called, frame, Named(action.Get("NamedParameters"), called)) : null;
            }
            case "TActionCall" or "Template_ActionCall":
                return action.Get("Action") is FxNode callee ? Make(callee, own, frame, Named(action.Get("NamedParams") ?? action.Get("NamedParameters"), own)) : null;
            case "Competition" or "TCompetitionAction":
            {
                // all at once, until the first of them ends
                if (Off()) return null;
                FxFrame inside = FrameOf(action.Get("Mobile"), own, frame);
                var racing = new List<Running>();
                foreach (FxBinding each in Items(action.Get("Actions"), own))
                    if (Make(each.Value, each.Scope, inside, null) is Running started) racing.Add(started);
                return racing.Count == 0 ? null : new Racing(racing);
            }
            case "TSimultaneousAction" or "Simultaneous" or "TSimultaneousActionWithMultiLaunchLimit":
            {
                if (Off()) return null;
                FxFrame inside = FrameOf(action.Get("Mobile"), own, frame);
                Pin(action.Get("LocalVariables"), own, inside);
                return AllAtOnce(Items(action.Get("Actions"), own), inside);
            }
            case "TActionDescriptorFireAndForget":
                return action.Get("EffetToLaunch") is FxNode launched ? Make(launched, own, frame, null) : null;
            case "TSequentialAction" or "Sequential":
                return Off() ? null : new InTurn(this, Items(action.Get("Actions"), own), frame);
            case "TWaitInSec" or "WaitInSec":
                return new Waiting(action.Get("Duration") is FxNode duration ? Evaluate(duration, own).Number : 0);
            case "Template_WaitForEver" or "TWaitForEver":
                return new Waiting(double.PositiveInfinity);
            case "Template_RepeatSimultaneous" or "TRepeatSimultaneousAction" or "Template_RepeatSequential" or "TRepeatSequentialAction":
                return Off() ? null : Repeat(action, own, frame);
            case "ParticleBombSpawner" or "TParticleSpawnerActionBomb":
                if (!Off()) Burst(action, own, frame);
                return null;
            case "ParticleSpraySpawnerWithEvaluable" or "TParticleSpawnerActionSpray":
                return Off() ? null : Spray(action, own, frame);
            case "TProjectileInterpolatorActionDescriptor":
                return Flight(action, own, frame);
            case "TActionDescriptor_Select" or "Template_Select":
            {
                // one of a list, by its number counted from 1 (a projectile's model and the smoke it trails)
                if (Off()) return null;
                FxFrame inside = FrameOf(action.Get("Mobile"), own, frame);
                SetMoment(inside);
                List<FxBinding> choices = Items(action.Get("Actions"), own);
                int index = action.Get("Index") is FxNode number ? (int)Evaluate(number, own).Number - 1 : 0;
                if (index < 0 || index >= choices.Count) return null;
                // a choice from a list declared on its own (Roquette_Meshs_List) runs in the choosing action's scope, so it
                // reaches that action's values, as BANK_Traine_Roquette_Simple/DurationInSec
                FxScope chosen = ReferenceEquals(choices[index].Scope, evaluator.Outermost) ? own : choices[index].Scope;
                return Make(choices[index].Value, chosen, inside, null);
            }
            case "Template_ActionDescriptorDestroySceneryRandom":
                // knocks down trees and walls the shot passes: there are none in the viewer
                return null;
            case "ShakeCamera" or "Template_ShakeCamera" or "TCameraShakeActionDescriptor":
                notShown.Add("the camera shaking");
                return null;
            default:
                notShown.Add("actions of the kind " + action.Type);
                return null;
        }
    }

    /// <summary>
    /// The scope an action declaration runs in when called: its parameters bound to the values the caller named, or
    /// else their defaults; names it does not bind are its caller's.
    /// </summary>
    private FxScope Called(FxObject declaration, FxScope declared, FxScope caller, List<(string Name, FxNode Value, FxScope Scope)>? named)
    {
        var called = new FxScope(caller);
        // a template's use gives its parameters (<Name>) where its body is read: the choices in its Actions see them, as
        // a jet engine's "<FlammeColorMultiplier> > 0 ? [flames] : []"
        foreach ((string name, FxBinding given) in declared.Bound)
            if (name.StartsWith('<')) called.Bind(name, given.Value, given.Scope);
        foreach ((string name, FxNode value, FxScope scope) in named ?? []) called.Bind(name, value, scope);
        foreach (FxBinding item in Items(declaration.Get("Params"), declared))
        {
            // parameters are written "parName is Template_Param_Float(DefaultValue = ...)"
            if (FindParameter(declaration, item.Value) is not (string name, FxObject marker) || called.Binds(name)) continue;
            if (marker.Get("DefaultValue") is FxNode defaultValue) called.Bind(name, defaultValue, called);
        }
        return called;
    }

    /// <summary>A parameter's name and its marker (TActionAlias, Template_Param_Float ...), from the declaration's Params.</summary>
    private static (string, FxObject)? FindParameter(FxObject declaration, FxNode marker)
    {
        if (declaration.Get("Params") is not FxList list) return null;
        foreach (FxNode item in list.Items)
            if (item is FxPair { Key: FxName name, Value: FxObject found } && ReferenceEquals(found, marker)) return (name.Value, found);
        return null;
    }

    /// <summary>An action's local variables (LocalVariables = [name is Template_Pinnable(InitialValue = ...)]): worked out once, as it starts.</summary>
    private void Pin(FxNode? variables, FxScope scope, FxFrame frame)
    {
        if (variables is not FxList list) return;
        SetMoment(frame);
        foreach (FxNode item in list.Items)
            if (item is FxPair { Key: FxName name, Value: FxObject pinned } && pinned.Get("InitialValue") is FxNode initial)
                scope.Bind(name.Value, new FxConstant(Evaluate(initial, scope)), scope);
    }

    /// <summary>
    /// The frame a Mobile = TMobileWithLocalRepereMatrixFactory(Position, EulerAngles) places inside the one around it,
    /// following it as it moves; the frame around it when there is none.
    /// </summary>
    private FxFrame FrameOf(FxNode? mobile, FxScope scope, FxFrame around)
    {
        if (mobile is null or FxNil || Resolve(mobile, scope) is not (FxObject factory, FxScope own)) return around;
        SetMoment(around);
        Vector3 position = factory.Get("Position") is FxNode place ? Evaluate(place, own).Vector : Vector3.Zero;
        Vector3 turns = factory.Get("EulerAngles") is FxNode angles ? Evaluate(angles, own).Vector : Vector3.Zero;
        // the template, unlike the type it fills, takes its angles in turns
        if (factory.Type == "Template_MobileWithLocalRepereMatrixFactory") turns *= MathF.Tau;
        return position == Vector3.Zero && turns == Vector3.Zero ? around : around.Inside(position, turns);
    }

    private Running? AllAtOnce(List<FxBinding> actions, FxFrame frame)
    {
        var started = new List<Running>();
        foreach (FxBinding action in actions)
            if (Make(action.Value, action.Scope, frame, null) is Running running) started.Add(running);
        return started.Count == 0 ? null : started.Count == 1 ? started[0] : new Together(started);
    }

    private sealed class Together(List<Running> actions) : Running
    {
        public override bool Step(double seconds)
        {
            for (int i = 0; i < actions.Count; i++)
                if (!actions[i].Step(seconds)) actions.RemoveAt(i--);
            return actions.Count > 0;
        }

        public override void Stop()
        {
            foreach (Running action in actions) action.Stop();
        }
    }

    private sealed class Racing(List<Running> actions) : Running
    {
        public override bool Step(double seconds)
        {
            foreach (Running action in actions)
            {
                if (action.Step(seconds)) continue;
                Stop();
                return false;
            }
            return true;
        }

        public override void Stop()
        {
            foreach (Running action in actions) action.Stop();
        }
    }

    private sealed class Limited(Running action, double seconds) : Running
    {
        private double left = seconds;

        public override bool Step(double step)
        {
            left -= step;
            return action.Step(step) && left > 0;
        }

        public override void Stop() => action.Stop();
    }

    /// <summary>Actions one after another: each is made when the one before it ends, and starts in the same step.</summary>
    private sealed class InTurn(FxPlayer player, List<FxBinding> actions, FxFrame frame) : Running
    {
        private int next;
        private Running? current;
        private bool stopped;

        public override bool Step(double seconds)
        {
            while (true)
            {
                if (current is not null)
                {
                    if (current.Step(seconds)) return true;
                    current = null;
                    seconds = 0;
                }
                if (stopped || next >= actions.Count) return false;
                FxBinding action = actions[next++];
                current = player.Make(action.Value, action.Scope, frame, null);
            }
        }

        public override void Stop()
        {
            stopped = true;
            current?.Stop();
        }
    }

    private sealed class Waiting(double seconds) : Running
    {
        private double left = seconds;
        private bool stopped;

        public override bool Step(double step)
        {
            left -= step;
            return left > 0 && !stopped;
        }

        public override void Stop() => stopped = true;
    }

    /// <summary>
    /// Actions repeated IterationCount times (or forever): all at once each time, a time apart (RepeatSimultaneous),
    /// or each time after the last has ended (RepeatSequential). Each time sees which time it is as currentRepeat.
    /// </summary>
    private Running? Repeat(FxObject action, FxScope scope, FxFrame frame)
    {
        SetMoment(frame);
        bool forever = action.Get("Forever") is FxNode always && Evaluate(always, scope).IsTrue
            || action.Get("InfiniteLoop") is FxNode infinite && Evaluate(infinite, scope).IsTrue;
        int times = forever ? int.MaxValue : action.Get("IterationCount") is FxNode count ? (int)Evaluate(count, scope).Number : 1;
        double apart = action.Get("WaitBetweenRepeats") is FxNode wait ? Evaluate(wait, scope).Number : 0;
        bool together = action.Type.Contains("Simultaneous", StringComparison.Ordinal);
        FxFrame inside = FrameOf(action.Get("Mobile"), scope, frame);
        return times <= 0 ? null : new Repeating(this, action, scope, inside, times, together ? apart : -1);
    }

    private sealed class Repeating(FxPlayer player, FxObject action, FxScope scope, FxFrame frame, int times, double apart) : Running
    {
        private readonly List<Running> going = [];
        private int done;
        private double untilNext;
        private bool stopped;

        public override bool Step(double seconds)
        {
            for (int i = 0; i < going.Count; i++)
                if (!going[i].Step(seconds)) going.RemoveAt(i--);
            untilNext -= seconds;
            // a sequential repeat starts again when the last has ended; a simultaneous one when its wait is over; a
            // few at most each step, so a repeat forever of something instant cannot hold the viewer up
            for (int started = 0; started < 64 && !stopped && done < times && (apart < 0 ? going.Count == 0 : untilNext <= 0); started++)
            {
                var each = new FxScope(scope);
                each.Bind("currentRepeat", new FxNumber(done), each);
                player.Pin(action.Get("LocalVariables"), each, frame);
                if (player.AllAtOnce(player.Items(action.Get("Actions"), each), frame) is Running repeated) going.Add(repeated);
                done++;
                untilNext += Math.Max(apart, 0);
            }
            return going.Count > 0 || (!stopped && done < times);
        }

        public override void Stop()
        {
            stopped = true;
            foreach (Running action in going) action.Stop();
        }
    }

    // ---- projectiles ----

    /// <summary>
    /// A projectile's flight (ProjectileMover): its frame moves from where it is to AbsoluteEndPosition in
    /// DurationInSec, pointing along its path. A bomb's trajectory, and a shot the game's physics fly that is not fired
    /// straight (UsePhysicSolver without UseTirTendu, as a lobbed smoke grenade), curve under gravity; others fly straight.
    /// </summary>
    private Running? Flight(FxObject action, FxScope scope, FxFrame frame)
    {
        SetMoment(frame);
        FxValue end = action.Get("AbsoluteEndPosition") is FxNode to ? Evaluate(to, scope) : FxValue.Nil;
        double seconds = action.Get("DurationInSec") is FxNode duration ? Evaluate(duration, scope).Number : 0;
        if (end.Size < 3 || seconds <= 0) return null;
        bool Says(string name) => action.Get(name) is FxNode said && Evaluate(said, scope).IsTrue;
        bool falls = Says("BombTrajectory") || (Says("UsePhysicSolver") && !Says("UseTirTendu"));
        return new Flying(frame, frame.Position, end.Vector, seconds, falls);
    }

    private sealed class Flying(FxFrame frame, Vector3 start, Vector3 end, double seconds, bool falls) : Running
    {
        private double flown;

        public override bool Step(double step)
        {
            flown = Math.Min(seconds, flown + step);
            float through = (float)(flown / seconds);
            Vector3 place = Place(through), ahead = Place(Math.Min(1, through + 0.01f)) - Place(Math.Max(0, through - 0.01f));
            Vector3 forward = ahead.LengthSquared() > 1e-6f ? Vector3.Normalize(ahead) : Vector3.UnitX;
            Vector3 side = Vector3.Cross(Vector3.UnitZ, forward);
            side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.UnitY;
            Vector3 up = Vector3.Cross(forward, side);
            frame.MovedTo = new Matrix4x4(forward.X, forward.Y, forward.Z, 0, side.X, side.Y, side.Z, 0, up.X, up.Y, up.Z, 0, place.X, place.Y, place.Z, 1);
            return flown < seconds;
        }

        private Vector3 Place(float through)
        {
            Vector3 straight = Vector3.Lerp(start, end, through);
            // a falling bomb's path bows: level at release, steepening, under the game's gravity of about 9.8 m/s²
            return falls ? straight + new Vector3(0, 0, (float)(4.9 * FxEvaluator.UnitsAMetre * seconds * seconds * through * (1 - through))) : straight;
        }
    }

    // ---- spawning particles ----

    /// <summary>Spawns a ParticleBombSpawner's particles, all at once.</summary>
    private void Burst(FxObject action, FxScope scope, FxFrame frame)
    {
        if (Recipe(action.Get("Spawner"), scope) is not FxSpawnRecipe recipe) return;
        SetMoment(frame);
        int count = action.Get("Count") is FxNode many ? (int)MathF.Round(Evaluate(many, scope).Number) : 10;
        Vector3 offset = action.Get("Position") is FxNode place and not FxNil ? Evaluate(place, scope).Vector : Vector3.Zero;
        var spawner = new FxSpawner { Position = frame.Position + offset, Frame = frame };
        for (int i = 0; i < count && particles.Count < MostParticles; i++) Spawn(recipe, spawner, frame, null);
    }

    private Running? Spray(FxObject action, FxScope scope, FxFrame frame)
    {
        if (Recipe(action.Get("Spawner"), scope) is not FxSpawnRecipe recipe) return null;
        SetMoment(frame);
        double Number(string name, double otherwise) => action.Get(name) is FxNode node and not FxNil ? Evaluate(node, scope).Number : otherwise;
        bool forever = action.Get("Forever") is FxNode always && Evaluate(always, scope).IsTrue;
        bool first = action.Get("SpawnFirstParticleAtStart") is FxNode node and not FxNil && Evaluate(node, scope).IsTrue;
        FxNode? offset = action.Get("Position") is FxNode place and not FxNil ? place : null;
        return new Spraying(this, recipe, new FxBinding(action.Get("Frequency") ?? new FxNumber(10), scope), offset is null ? null : new FxBinding(offset, scope),
            frame, Number("InitialDelayInSec", 0), forever ? double.PositiveInfinity : Number("DurationInSec", 10), first);
    }

    /// <summary>
    /// A spray of particles: Frequency a second, from InitialDelayInSec for DurationInSec. Its spawner follows the
    /// frame; particles spawned within a step are spread along the way it moved, so a fast projectile leaves an
    /// unbroken trail.
    /// </summary>
    private sealed class Spraying(FxPlayer player, FxSpawnRecipe recipe, FxBinding frequency, FxBinding? offset, FxFrame frame, double delay, double duration, bool first) : Running
    {
        private readonly FxSpawner spawner = new() { Frame = frame, Position = frame.Position };
        private double elapsed, owed = first ? 1 : 0;
        private bool stopped;

        public override bool Step(double seconds)
        {
            Vector3 from = spawner.Position;
            elapsed += seconds;
            if (stopped || elapsed < delay) return !stopped;
            spawner.LifeRatio = double.IsInfinity(duration) ? 0 : (float)Math.Clamp((elapsed - delay) / Math.Max(duration, 1e-6), 0, 1);
            player.SetMoment(frame, spawner);
            Vector3 to = frame.Position + (offset is FxBinding place ? player.Evaluate(place).Vector : Vector3.Zero);
            spawner.Speed = seconds > 0 ? (to - from) / (float)seconds : Vector3.Zero;
            owed += Math.Max(0, player.Evaluate(frequency).Number) * Math.Min(seconds, elapsed - delay);
            int count = (int)owed;
            owed -= count;
            for (int i = 0; i < count && player.particles.Count < MostParticles; i++)
            {
                spawner.Position = Vector3.Lerp(from, to, (i + 1f) / count);
                player.Spawn(recipe, spawner, frame, null);
            }
            spawner.Position = to;
            return elapsed - delay < duration;
        }

        public override void Stop() => stopped = true;
    }

    /// <summary>What a ParticleSpawner makes, read once for all its particles; null when it is not one.</summary>
    private FxSpawnRecipe? Recipe(FxNode? spawnerNode, FxScope scope)
    {
        if (Resolve(spawnerNode, scope) is not (FxObject spawner, FxScope own)) return null;
        if (Resolve(spawner.Get("ParticleDescriptor"), own) is not (FxObject descriptor, FxScope descriptorScope)) return null;
        FxParticleKind kind = KindOf(descriptor, descriptorScope);
        FxBinding? lifetime = spawner.Get("ParticleLifetime") is FxNode life ? new FxBinding(life, own) : null;
        bool independent = spawner.Get("PositionIndependentFromEmitter") is FxNode placed && Evaluate(placed, own).IsTrue;
        return new FxSpawnRecipe(kind, lifetime, Items(spawner.Get("Initialisers"), own), Items(spawner.Get("PostTransformInitialisers"), own), independent);
    }

    /// <summary>
    /// Starts one particle: its life from ParticleLifetime, its values from the initialisers, given in its frame's own
    /// axes from its spawner; then turned with the frame and placed at its spawner (its place, speed and direction),
    /// and given the initialisers after placing (PostTransformInitialisers), which work in the model's space. A gun's
    /// fire jet is written as a speed along x, which must leave along the gun however it is turned.
    /// </summary>
    private FxParticle Spawn(FxSpawnRecipe recipe, FxSpawner spawner, FxFrame frame, FxParticle? parent, float alongTrail = 0)
    {
        var particle = new FxParticle { Kind = recipe.Kind, Spawner = spawner, Parent = parent, Frame = frame, Born = Now, Effect = parent?.Effect ?? stepping };
        // how far along its trail it was dropped (a ribbon's texture runs by it)
        particle.Fields[FxFields.Of("CurvilinearAbscissa")] = FxValue.Of(alongTrail);
        SetMoment(frame, spawner, particle);
        particle.Dies = recipe.Kind.Infinite ? double.MaxValue : Now + Math.Max(0.01, recipe.Lifetime is FxBinding lifetime ? Evaluate(lifetime).Number : 5);
        foreach (FxBinding initialiser in recipe.Initialisers) Initialise(particle, initialiser, frame);
        if (!recipe.Independent)
        {
            Matrix4x4 turn = frame.Matrix with { M41 = 0, M42 = 0, M43 = 0 };
            FxValue[] fields = particle.Fields;
            fields[FxFields.Position] = FxValue.Of(Vector3.Transform(particle.Position, turn) + spawner.Position);
            fields[FxFields.Speed] = FxValue.Of(Vector3.TransformNormal(fields[FxFields.Speed].Vector, turn));
            fields[FxFields.Direction] = FxValue.Of(Vector3.TransformNormal(fields[FxFields.Direction].Vector, turn));
        }
        foreach (FxBinding initialiser in recipe.AfterPlacing) Initialise(particle, initialiser, frame);
        if (recipe.Kind.CarriedSpawners.Count > 0)
        {
            particle.NextSpawns = new double[recipe.Kind.CarriedSpawners.Count];
            particle.TrailLengths = new float[recipe.Kind.CarriedSpawners.Count];
        }
        particles.Add(particle);
        return particle;
    }

    private void Initialise(FxParticle particle, FxBinding initialiser, FxFrame frame)
    {
        if (Resolve(initialiser.Value, initialiser.Scope) is not (FxObject list, FxScope scope)) return;
        void Set(string field, FxNode function)
        {
            FxValue value = Evaluate(function, scope);
            // the scripts give turns as a share of a whole turn
            if (field is "Rotation" or "RotationSpeed") value = value.Map(x => x * 2 * 3.141f);
            if (field == "DeathDate") particle.Dies = value.Number;
            else if (FxFields.Of(field) is int number and >= 0) particle.Fields[number] = value;
        }
        switch (list.Type)
        {
            case "ParticleInitialiserList":
                foreach (string field in InitialiserOrder)
                    if (list.Get(field) is FxNode function and not FxNil) Set(field, function);
                return;
            case "TParticleInitialiserWithEvaluable":
                if (list.Get("Field") is FxName fieldName && list.Get("InitFunction") is FxNode init) Set(fieldName.Last, init);
                return;
            case "Initialiser_SetMobileWrapper":
                particle.Frame = frame;
                return;
            case "Initialiser_SetSpawningParticleMobileWrapper":
                particle.Frame = particle.Parent?.Frame ?? frame;
                return;
            case "Initialiser_CreateTrailId" or "Template_Post_Initialiser_For_EvolverTrailSpawner":
                return;
            default:
                if (list.Type.StartsWith("Initialiser_", StringComparison.Ordinal) && list.Get("InitFunction") is FxNode initFunction) Set(list.Type["Initialiser_".Length..], initFunction);
                else notShown.Add("particle initialisers of the kind " + list.Type);
                return;
        }
    }

    // ---- kinds of particle ----

    /// <summary>A ParticleDescriptor read into a kind of particle: how it is drawn, and how it changes.</summary>
    private FxParticleKind KindOf(FxObject descriptor, FxScope scope)
    {
        SetMoment(FxFrame.Fixed(Matrix4x4.Identity));
        string? texture = null, notShownBecause = null;
        bool emissive = false, opaque = false;
        FxDrawing drawing = FxDrawing.Picture;
        if (Resolve(descriptor.Get("RenderInfo"), scope) is (FxObject info, FxScope infoScope))
        {
            texture = info.Get("TextureName") is FxNode name ? Evaluate(name, infoScope).Thing as string : MaterialTexture(info, infoScope);
            emissive = info.Get("EmissiveParticle") is FxNode glows && Evaluate(glows, infoScope).IsTrue;
            opaque = info.Get("OpaqueParticle") is FxNode solid && Evaluate(solid, infoScope).IsTrue;
            switch (info.Type)
            {
                case "LitParticleRenderInfo":
                    emissive = opaque = true;
                    break;
                case "ParticleMeshRenderInfo":
                    notShownBecause = "meshes (spent casings, debris)";
                    break;
                case "Template_DistortionParticleRenderInfo":
                    notShownBecause = "heat haze";
                    break;
                case "ParticleRenderInfoSticker":
                    notShownBecause = "marks on the ground";
                    break;
            }
        }
        (FxObject Object, FxScope Scope)? renderer = Resolve(descriptor.Get("ParticleRenderer"), scope);
        switch (renderer?.Object.Type)
        {
            case "TParticleRenderer_LitParticle" or "ParticleRendererStandard" or null:
                break;
            case "ParticleRendererNull":
                drawing = FxDrawing.Nothing;
                break;
            case "TParticleRenderer_PointLight":
                drawing = FxDrawing.Light;
                break;
            case "ParticleRendererMeshTrail":
                drawing = FxDrawing.Ribbon;
                break;
            case "ParticleRendererSticker" or "Template_ParticleRendererPlane":
                notShownBecause = "marks on the ground";
                break;
            case "ParticleRendererRotor":
                drawing = FxDrawing.Rotor;
                break;
            case "Template_ParticleRendererDynamicMesh" or "TParticleRenderer_DynamicMesh":
                // a model of its own: its render info (ParticleMeshRenderInfo) says nothing more
                drawing = FxDrawing.Mesh;
                notShownBecause = null;
                break;
            default:
                notShownBecause = "particles drawn as " + renderer?.Object.Type;
                break;
        }
        if (notShownBecause is not null)
        {
            drawing = FxDrawing.NotShown;
            notShown.Add(notShownBecause);
        }
        var evolvers = new List<(int, FxBinding)>();
        var carried = new List<FxCarriedSpawner>();
        var physics = new PhysicsFound();
        ReadEvolvers(descriptor.Get("Evolvers") ?? descriptor.Get("Evolver"), scope, evolvers, carried, physics);
        return new FxParticleKind
        {
            Texture = texture,
            Drawing = drawing,
            NotShownBecause = notShownBecause,
            Emissive = emissive,
            Opaque = opaque,
            Infinite = descriptor.Get("InfiniteParticles") is FxNode infinite && Evaluate(infinite, scope).IsTrue,
            ComputesSpeed = physics.ComputesSpeed,
            Evolvers = evolvers,
            SetsRotation = evolvers.Any(evolver => evolver.Item1 == FxFields.Rotation),
            Moves = physics.Moves,
            Acceleration = physics.Acceleration,
            FluidFriction = physics.FluidFriction,
            Wind = physics.Wind,
            CarriedSpawners = carried,
        };
    }

    /// <summary>
    /// The model a particle drawn as one shows (its DynamicMesh: Template_MeshResourcesForFxEditor( Value =
    /// 'GameData:/Assets/3D/Ammo/Roquettes/....fbx' )), as its path in the game ("Assets/3D/....fbx"); null for none.
    /// </summary>
    public string? MeshOf(FxParticle particle)
    {
        FxValue value = particle.Fields[FxFields.Of("DynamicMesh")];
        string? path = value.Thing switch
        {
            string text => text,
            FxBinding { Value: FxObject resource } bound when resource.Get("Value") is FxNode named => Evaluate(named, bound.Scope).Thing as string,
            _ => null,
        };
        if (path is null) return null;
        int colon = path.IndexOf(":/", StringComparison.Ordinal);
        return colon >= 0 ? path[(colon + 2)..] : path;
    }

    /// <summary>
    /// The texture a render info gives its material (MeshMaterial = TMeshMaterial( TextureDico = MAP[('OARTexture0',
    /// '...')] )), as the rotor's does; null for none.
    /// </summary>
    private string? MaterialTexture(FxObject info, FxScope scope)
    {
        if (Resolve(info.Get("MeshMaterial"), scope) is not (FxObject material, FxScope materialScope)) return null;
        if (material.Get("TextureDico") is not FxCall { Callee: FxName { Last: "MAP" } } map) return null;
        foreach (FxNode entry in map.Arguments)
            if (entry is FxPair { Key: FxText { Value: "OARTexture0" } } pair && Evaluate(pair.Value, materialScope).Thing is string texture) return texture;
        return null;
    }

    private sealed class PhysicsFound
    {
        public bool Moves { get; set; }
        public bool ComputesSpeed { get; set; }
        public FxBinding? Acceleration { get; set; }
        public FxBinding? FluidFriction { get; set; }
        public FxBinding? Wind { get; set; }
    }

    private void ReadEvolvers(FxNode? node, FxScope scope, List<(int, FxBinding)> evolvers, List<FxCarriedSpawner> carried, PhysicsFound physics)
    {
        if (node is FxList || node is FxBinary)
        {
            foreach (FxBinding item in Items(node, scope)) ReadEvolvers(item.Value, item.Scope, evolvers, carried, physics);
            return;
        }
        if (Resolve(node, scope) is not (FxObject evolver, FxScope own)) return;
        FxBinding? Given(string name) => evolver.Get(name) is FxNode value and not FxNil ? new FxBinding(value, own) : null;
        switch (evolver.Type)
        {
            case "ParticleStandardEvolverList" or "ParticleStandardEvolverListWithPhysic":
                if (evolver.Type.EndsWith("WithPhysic", StringComparison.Ordinal))
                {
                    physics.Moves = true;
                    physics.Acceleration = Given("PhysicAccelerationField") ?? physics.Acceleration;
                    physics.FluidFriction = Given("FluidFriction") ?? physics.FluidFriction;
                    physics.Wind = Given("PhysicWindField") ?? physics.Wind;
                }
                foreach (string field in EvolverOrder)
                {
                    if (Given(field) is not FxBinding function) continue;
                    evolvers.Add((field == "Animation" ? FxFields.Animation : FxFields.Of(field), function));
                }
                if (Given("AdditionnalEvolvers") is FxBinding more) ReadEvolvers(more.Value, more.Scope, evolvers, carried, physics);
                return;
            case "Template_List" or "TParticleEvolver_List":
                ReadEvolvers(evolver.Get("Evolvers"), own, evolvers, carried, physics);
                return;
            case "Template_EvolverField" or "TVersatileParticleEvolver":
                if (Given("EvolveFunction") is FxBinding evolve && evolver.Get("Field") is FxNode evolvedField)
                {
                    string name = evolvedField is FxName path ? path.Last : Evaluate(evolvedField, own).Thing as string ?? "";
                    if (FxFields.Of(name) is int number and >= 0) evolvers.Add((number, evolve));
                }
                return;
            case "Template_EvolverPosition" or "Template_EvolverTexCoordU" or "Template_EvolverScaleForMesh":
                if (Given("EvolveFunction") is FxBinding fieldFunction) evolvers.Add((FxFields.Of(evolver.Type["Template_Evolver".Length..]), fieldFunction));
                return;
            case "Template_TextureAnimation" or "TParticleEvolver_TextureAnimation":
                if (Given("EvolveFunction") is FxBinding animation) evolvers.Add((FxFields.Animation, animation));
                return;
            case "Template_Physics" or "TParticleEvolver_Physics":
                physics.Moves = true;
                physics.Acceleration = Given("AccelerationField") ?? physics.Acceleration;
                physics.FluidFriction = Given("FluidFriction") ?? physics.FluidFriction;
                physics.Wind = Given("WindField") ?? physics.Wind;
                return;
            case "Template_StandardComputeSpeed" or "TParticleEvolver_StandardComputeSpeed":
                physics.Moves = true;
                physics.ComputesSpeed = true;
                return;
            case "EvolverSpraySpawner":
                if (Recipe(evolver.Get("Spawner"), own) is FxSpawnRecipe recipe)
                    carried.Add(new FxCarriedSpawner(Given("Frequency") ?? new FxBinding(new FxNumber(10), own), null, recipe, Given("Condition")));
                return;
            case "TrailSpawner" or "TParticleEvolver_TrailSpawner":
                if (Recipe(evolver.Get("Spawner"), own) is FxSpawnRecipe trail)
                    carried.Add(new FxCarriedSpawner(null, Given("DistanceBetweenSpawn") ?? new FxBinding(new FxNumber(500), own), trail, Given("Condition")));
                return;
            default:
                notShown.Add("particle evolvers of the kind " + evolver.Type);
                return;
        }
    }

    // ---- moving particles on ----

    /// <summary>
    /// Moves a particle on by a step: its physics (speed carries it, acceleration and the air's drag change its speed),
    /// then its evolvers in order (and its speed from how far they moved it, when it computes its speed), its turn, and
    /// the particles it carries spawners for. False when it can go no further.
    /// </summary>
    private bool Evolve(FxParticle particle, float seconds)
    {
        FxParticleKind kind = particle.Kind;
        SetMoment(particle.Frame ?? FxFrame.Fixed(Matrix4x4.Identity), particle.Spawner, particle);
        FxValue[] fields = particle.Fields;
        Vector3 before = fields[FxFields.Position].Vector;
        if (kind.Moves)
        {
            Vector3 speed = fields[FxFields.Speed].Vector, place = fields[FxFields.Position].Vector;
            Vector3 acceleration = kind.Acceleration is FxBinding pushed ? Evaluate(pushed).Vector : Vector3.Zero;
            if (kind.FluidFriction is FxBinding friction || kind.Wind is not null)
            {
                float drag = kind.FluidFriction is FxBinding slowing ? Evaluate(slowing).Number : 0;
                Vector3 wind = kind.Wind is FxBinding blowing ? Evaluate(blowing).Vector : Vector3.Zero;
                speed += (acceleration + (wind - speed) * drag) * seconds;
                place += speed * seconds;
            }
            else
            {
                place += speed * seconds + acceleration * (0.5f * seconds * seconds);
                speed += acceleration * seconds;
            }
            fields[FxFields.Speed] = FxValue.Of(speed);
            fields[FxFields.Position] = FxValue.Of(place);
        }
        foreach ((int field, FxBinding function) in kind.Evolvers) fields[field] = Evaluate(function);
        if (kind.ComputesSpeed && seconds > 0) fields[FxFields.Speed] = FxValue.Of((fields[FxFields.Position].Vector - before) / seconds);
        if (!kind.SetsRotation && fields[FxFields.RotationSpeed].Number != 0)
            fields[FxFields.Rotation] = FxValue.Of(fields[FxFields.Rotation].Number + fields[FxFields.RotationSpeed].Number * seconds);
        for (int i = 0; i < kind.CarriedSpawners.Count; i++) Carry(particle, i, seconds);
        return true;
    }

    /// <summary>
    /// Spawns what a particle's carried spawner owes for this step, from the particle: so many a second (a spray), or
    /// one each time the particle has moved so far (a trail), along the way it moved.
    /// </summary>
    private void Carry(FxParticle particle, int number, float seconds)
    {
        FxCarriedSpawner carried = particle.Kind.CarriedSpawners[number];
        if (particle.Effect?.IsStopped == true || carried.Condition is FxBinding condition && !Evaluate(condition).IsTrue) return;
        var spawner = new FxSpawner { Position = particle.Position, Speed = particle.Fields[FxFields.Speed].Vector, LifeRatio = particle.LifeRatio(Now), Frame = particle.Frame };
        FxFrame frame = particle.Frame ?? FxFrame.Fixed(Matrix4x4.CreateTranslation(particle.Position));
        if (carried.Distance is FxBinding apart)
        {
            int last = FxFields.Of("PreviousEvolverSpawnPosition");
            // DistanceBetweenSpawn is in metres: the flamethrower's script writes 2000 units as "9.3023255814 div ..."
            float step = Math.Max(1, Evaluate(apart).Number * FxEvaluator.UnitsAMetre);
            Vector3 from = particle.NextSpawns![number] == 0 ? particle.Position : particle.Fields[last].Vector, to = particle.Position;
            particle.NextSpawns[number] = 1;
            float travelled = Vector3.Distance(from, to);
            int spawned = 0;
            for (float along = step; along <= travelled && spawned < 64 && particles.Count < MostParticles; along += step, spawned++)
            {
                spawner.Position = Vector3.Lerp(from, to, along / travelled);
                particle.TrailLengths![number] += step;
                Spawn(carried.Recipe, spawner, frame, particle, particle.TrailLengths[number]);
            }
            particle.Fields[last] = FxValue.Of(spawned == 0 ? from : Vector3.Lerp(from, to, spawned * step / travelled));
            return;
        }
        double owed = particle.NextSpawns![number] + Math.Max(0, Evaluate(carried.Frequency!.Value).Number) * seconds;
        int count = (int)owed;
        particle.NextSpawns[number] = owed - count;
        for (int i = 0; i < count && particles.Count < MostParticles; i++) Spawn(carried.Recipe, spawner, frame, particle);
    }
}
