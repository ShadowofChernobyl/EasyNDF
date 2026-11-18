using System.Numerics;

namespace EasyNdf.Data.Effects;

/// <summary>A name bound to a calculation and the scope it is worked out in: worked out afresh each time it is used.</summary>
/// <param name="Value">The calculation.</param>
/// <param name="Scope">Where its own names are looked up.</param>
public readonly record struct FxBinding(FxNode Value, FxScope Scope);

/// <summary>
/// The names an effect's calculations can use: an action's parameters and local values, inside those of the action
/// that called it, so a building block can use what its caller was given (the scripts' "public" parameters).
/// </summary>
/// <param name="parent">The scope around this one; null for the outermost.</param>
public sealed class FxScope(FxScope? parent)
{
    private Dictionary<string, FxBinding>? names;

    /// <summary>The scope around this one.</summary>
    public FxScope? Parent => parent;

    /// <summary>Binds a name in this scope.</summary>
    public void Bind(string name, FxNode value, FxScope scope) => (names ??= new(StringComparer.Ordinal))[name] = new FxBinding(value, scope);

    /// <summary>Whether this scope itself binds the name.</summary>
    public bool Binds(string name) => names?.ContainsKey(name) == true;

    /// <summary>The names this scope itself binds.</summary>
    public IEnumerable<(string Name, FxBinding Binding)> Bound => names?.Select(pair => (pair.Key, pair.Value)) ?? [];

    /// <summary>
    /// The scopes of the functions called from this one, by the function and what it was given: a function's names are
    /// bound the same way each time it is called from here, so they are bound once, not on every particle's every step.
    /// </summary>
    internal Dictionary<(FxObject Function, IReadOnlyList<FxNode> Arguments), FxScope>? Calls { get; set; }

    /// <summary>A name, from this scope out; false when no scope binds it.</summary>
    public bool TryFind(string name, out FxBinding found)
    {
        for (FxScope? at = this; at is not null; at = at.Parent)
            if (at.names is not null && at.names.TryGetValue(name, out found)) return true;
        found = default;
        return false;
    }
}

/// <summary>What a calculation depends on when it is worked out: the particle, its spawner, the frame, the time.</summary>
public sealed class FxMoment
{
    /// <summary>The particle being started or moved on; null outside a particle.</summary>
    public FxParticle? Particle { get; set; }

    /// <summary>The spawner of what is being worked out; null outside one.</summary>
    public FxSpawner? Spawner { get; set; }

    /// <summary>The frame the action runs in.</summary>
    public FxFrame? Frame { get; set; }

    /// <summary>The effects' time, in seconds.</summary>
    public double Now { get; set; }

    /// <summary>The time since the last step (EvolverDt).</summary>
    public float StepSeconds { get; set; }

    /// <summary>How far through its life the action is, 0 to 1.</summary>
    public float ActionLifeRatio { get; set; }

    /// <summary>Seconds since the action started.</summary>
    public float ActionSeconds { get; set; }

    /// <summary>Where the camera is.</summary>
    public Vector3 Camera { get; set; }
}

/// <summary>
/// Works out the calculations of the game's effect scripts: arithmetic on numbers and vectors, the functions of
/// CommonData/Fx/Bank/@Evaluable.ndf (those written as code are run as written; the game's built-in ones, named
/// there but not written out, are done here from their names), random values, and the values of particles, spawners
/// and frames. A name is looked up in its scope, then among the effect files' declarations.
/// </summary>
/// <param name="library">The effect files.</param>
public sealed class FxEvaluator(FxLibrary library)
{
    /// <summary>The game's units in a metre (GameUnitsPerLBU in CommonData/System/Globals.ndf).</summary>
    public const float UnitsAMetre = 215;

    /// <summary>How deep calculations may call each other, so a name that refers to itself fails instead of hanging.</summary>
    private const int DeepestCall = 120;

    private readonly FxScope outermost = new(null);

    // particles are moved on several threads at once (FxPlayer.Step): each counts its own depth
    [ThreadStatic] private static int depth;

    /// <summary>The effect files.</summary>
    public FxLibrary Library => library;

    /// <summary>The scope around every action.</summary>
    public FxScope Outermost => outermost;

    /// <summary>A random number from 0 up to 1.</summary>
    public float RandomUnit() => Random.Shared.NextSingle();

    /// <summary>Works out a calculation in a scope, at a moment.</summary>
    public FxValue Evaluate(FxNode node, FxScope scope, FxMoment moment)
    {
        if (++depth > DeepestCall)
        {
            depth = 0;
            throw new FxProblem("a calculation calls itself without end");
        }
        try
        {
            return node switch
            {
                FxNumber number => FxValue.Of(number.Value),
                FxConstant constant => constant.Value,
                FxScoped scoped => Evaluate(scoped.Value, scoped.Scope, moment),
                FxName name => Named(name, scope, moment),
                FxBinary binary => Binary(binary, scope, moment),
                FxUnary { Operator: "-" } minus => Evaluate(minus.Operand, scope, moment).Map(x => -x),
                FxUnary not => FxValue.Of(!Evaluate(not.Operand, scope, moment).IsTrue),
                FxCall call => Call(call, scope, moment),
                FxChoice choice => Evaluate(Evaluate(choice.Condition, scope, moment).IsTrue ? choice.WhenTrue : choice.WhenFalse, scope, moment),
                FxObject thing => Object(thing, scope, moment),
                FxTemplateParameter parameter => scope.TryFind("<" + parameter.Name + ">", out FxBinding bound) ? Evaluate(bound.Value, bound.Scope, moment) : FxValue.Nil,
                FxText text => FxValue.OfThing(text.Value),
                FxNil => FxValue.Nil,
                _ => FxValue.OfThing(node),
            };
        }
        finally
        {
            depth = Math.Max(0, depth - 1);
        }
    }

    // ---- names ----

    private FxValue Named(FxName name, FxScope scope, FxMoment moment)
    {
        string key = name.Last;
        if (!name.IsPath && scope.TryFind(key, out FxBinding bound)) return Evaluate(bound.Value, bound.Scope, moment);
        switch (key)
        {
            case "true" or "True": return FxValue.Of(1);
            case "false" or "False": return FxValue.Of(0);
            case "GameUnitsPerLBU": return FxValue.Of(UnitsAMetre);
        }
        // the player's graphics settings: effects are shown at their best
        if (name.Value.StartsWith("$/GraphicOption/", StringComparison.Ordinal)) return FxValue.Of(name.Value.Contains("Quality", StringComparison.Ordinal) ? 100 : 1);
        if (library.Find(name) is FxDeclaration declaration) return Evaluate(declaration.Value, outermost, moment);
        return FxValue.OfThing(name);
    }

    /// <summary>The declarations that stand for a value the game works out itself: a particle's, a spawner's, the frame's, a random one.</summary>
    private FxValue Object(FxObject thing, FxScope scope, FxMoment moment)
    {
        switch (thing.Type)
        {
            case "TConstantFloat" or "TConstantInteger" or "TConstantBool":
                return thing.Get("Value") is FxNode value ? Evaluate(value, scope, moment) : FxValue.Of(0);
            case "Template_ParticleAccessor_ForBank":
                return ParticleValue((thing.Get("Field") as FxName)?.Value ?? "", moment);
            case "Template_SpawnerAccessor_ForBank":
                return SpawnerValue((thing.Get("Field") as FxText)?.Value ?? "", moment);
            case "Template_AparametricFunction_ForBank":
                return RandomShape((thing.Get("Function") as FxText)?.Value ?? "");
            case "Template_ActionDependentValue_ForBank":
                return ActionValue((thing.Get("ValueName") as FxText)?.Value ?? "", moment);
            case "TActionDependentValue":
                return ActionValue((thing.Get("Name") as FxText)?.Value ?? "", moment);
            case "Template_GetEnv_Float_ForBank":
                return FxValue.Of(moment.StepSeconds);
            case "Template_CameraPosition_ForBank":
                return FxValue.Of(moment.Camera);
            case "Template_CameraDirection_ForBank" or "Template_CameraAxeX_ForBank" or "Template_CameraAxeY_ForBank" or "Template_CameraDeltaPosition_ForBank":
                return FxValue.Of(Vector3.Zero);
            case "Template_TimeInSecondsNonPausable_ForBank":
                return FxValue.Of(moment.Now);
            case "TVariable":
                // the weather's wind: the viewer has no weather
                return FxValue.Of(Vector3.Zero);
            default:
                return FxValue.OfThing(new FxBinding(thing, scope));
        }
    }

    private static FxValue ParticleValue(string field, FxMoment moment)
    {
        FxParticle? particle = field.StartsWith("$/ParticleAsSpawner/", StringComparison.Ordinal) ? moment.Particle?.Parent : moment.Particle;
        if (particle is null) return FxValue.Of(0);
        string name = field[(field.LastIndexOf('/') + 1)..];
        return name switch
        {
            "LifeRatio" => FxValue.Of(particle.LifeRatio(moment.Now)),
            "CreationDate" => FxValue.Of(particle.Born),
            "DeathDate" => FxValue.Of(particle.Dies),
            "MobileWrapper" => particle.Frame is null ? FxValue.Nil : FxValue.OfThing(particle.Frame),
            "Opacity" when particle.Fields[FxFields.Opacity].Number < 0 => FxValue.Of(1),
            _ => FxFields.Of(name) is int number and >= 0 ? particle.Fields[number] : FxValue.Of(0),
        };
    }

    private static FxValue SpawnerValue(string field, FxMoment moment)
    {
        FxSpawner? spawner = moment.Spawner ?? moment.Particle?.Spawner;
        return field switch
        {
            "Position" => FxValue.Of(spawner?.Position ?? Vector3.Zero),
            "Speed" => FxValue.Of(spawner?.Speed ?? Vector3.Zero),
            "LifeRatio" => FxValue.Of(spawner?.LifeRatio ?? 0),
            _ => FxValue.Of(0),
        };
    }

    private static FxValue ActionValue(string name, FxMoment moment) => name switch
    {
        "MobilePosition" => FxValue.Of(moment.Frame?.Position ?? Vector3.Zero),
        "MobileDirection" => FxValue.Of(moment.Frame?.AxisX ?? Vector3.UnitX),
        "MobileWrapper" => moment.Frame is null ? FxValue.Nil : FxValue.OfThing(moment.Frame),
        "LifeRatio" => FxValue.Of(moment.ActionLifeRatio),
        "ElapsedTime" => FxValue.Of(moment.ActionSeconds),
        _ => FxValue.Of(0),
    };

    // ---- random values ----

    /// <summary>
    /// A random value of a shape, as the game's built-in random functions name them: MinusOneToOne... spread from -1 to
    /// 1, PositiveUnit... from 0 to 1; a disc or circle lies flat (z 0), a hemisphere is the upper half.
    /// </summary>
    private FxValue RandomShape(string shape)
    {
        if (shape is "UnitRand" or "HomogeneousUnit") return FxValue.Of(RandomUnit());
        bool positive = shape.Contains("PositiveUnit", StringComparison.Ordinal);
        string form = shape.Replace("Homogeneous", "", StringComparison.Ordinal).Replace("PositiveUnit", "", StringComparison.Ordinal).Replace("MinusOneToOne", "", StringComparison.Ordinal).Replace("Random", "", StringComparison.Ordinal);
        Vector3 point = form switch
        {
            "Circle" or "ClosedCircle" => OnCircle(),
            "Disc" or "Plane" => InDisc(),
            "SphereVolume" => InBall(),
            "SphereSurface" => Vector3.Normalize(InBall() + new Vector3(1e-6f)),
            "HemisphereVolume" => Upper(InBall()),
            "HemisphereSurface" => Upper(Vector3.Normalize(InBall() + new Vector3(1e-6f))),
            "CylinderVolume" => InDisc() + new Vector3(0, 0, RandomUnit() * 2 - 1),
            "CylinderSurface" => OnCircle() + new Vector3(0, 0, RandomUnit() * 2 - 1),
            "BoxSurface" => OnBox(),
            _ => new Vector3(RandomUnit() * 2 - 1, RandomUnit() * 2 - 1, RandomUnit() * 2 - 1),
        };
        return FxValue.Of(positive ? (point + Vector3.One) / 2 : point);
    }

    private Vector3 OnCircle()
    {
        float angle = RandomUnit() * MathF.Tau;
        return new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
    }

    private Vector3 InDisc() => OnCircle() * MathF.Sqrt(RandomUnit());

    private Vector3 InBall()
    {
        while (true)
        {
            var point = new Vector3(RandomUnit() * 2 - 1, RandomUnit() * 2 - 1, RandomUnit() * 2 - 1);
            if (point.LengthSquared() <= 1) return point;
        }
    }

    private static Vector3 Upper(Vector3 point) => point with { Z = MathF.Abs(point.Z) };

    private Vector3 OnBox()
    {
        var point = new Vector3(RandomUnit() * 2 - 1, RandomUnit() * 2 - 1, RandomUnit() * 2 - 1);
        int side = Random.Shared.Next(3);
        float sign = Random.Shared.Next(2) == 0 ? -1 : 1;
        return side == 0 ? point with { X = sign } : side == 1 ? point with { Y = sign } : point with { Z = sign };
    }

    // ---- operators ----

    private FxValue Binary(FxBinary binary, FxScope scope, FxMoment moment)
    {
        if (binary.Operator == "&") return FxValue.Of(Evaluate(binary.Left, scope, moment).IsTrue && Evaluate(binary.Right, scope, moment).IsTrue);
        if (binary.Operator == "|") return FxValue.Of(Evaluate(binary.Left, scope, moment).IsTrue || Evaluate(binary.Right, scope, moment).IsTrue);
        FxValue left = Evaluate(binary.Left, scope, moment), right = Evaluate(binary.Right, scope, moment);
        return binary.Operator switch
        {
            "+" => FxValue.Combine(left, right, static (a, b) => a + b),
            "-" => FxValue.Combine(left, right, static (a, b) => a - b),
            "*" => FxValue.Combine(left, right, static (a, b) => a * b),
            "div" => FxValue.Combine(left, right, static (a, b) => b == 0 ? 0 : a / b),
            "%" => FxValue.Combine(left, right, static (a, b) => b == 0 ? 0 : a % b),
            "==" => FxValue.Of(Same(left, right)),
            "!=" => FxValue.Of(!Same(left, right)),
            "<" => FxValue.Of(left.Number < right.Number),
            ">" => FxValue.Of(left.Number > right.Number),
            "<=" => FxValue.Of(left.Number <= right.Number),
            ">=" => FxValue.Of(left.Number >= right.Number),
            _ => throw new FxProblem($"the operator {binary.Operator} is not known"),
        };
    }

    /// <summary>
    /// Whether two values are the same: nil only as nil, vectors number by number (the rotor's axis is float3[0, 0, 1],
    /// not float3[0, 1, 0]), texts and other things as they are.
    /// </summary>
    private static bool Same(FxValue left, FxValue right)
    {
        if (left.IsNil || right.IsNil) return left.IsNil == right.IsNil;
        if (left.Size == 0 || right.Size == 0) return Equals(left.Thing, right.Thing);
        FxValue each = FxValue.Combine(left, right, static (a, b) => a == b ? 1 : 0);
        return each.Numbers.X != 0 && (each.Size < 2 || each.Numbers.Y != 0) && (each.Size < 3 || each.Numbers.Z != 0) && (each.Size < 4 || each.Numbers.W != 0);
    }

    // ---- calls ----

    private FxValue Call(FxCall call, FxScope scope, FxMoment moment)
    {
        if (call.Callee is not FxName callee) return FxValue.Nil;
        string name = callee.Last;
        IReadOnlyList<FxNode> arguments = call.Arguments;
        // a name bound in the scope stands for a declaration, as a table passed as a parameter
        if (!callee.IsPath && scope.TryFind(name, out FxBinding bound))
        {
            if (bound.Value is FxName boundName) return Call(new FxCall(boundName, arguments), bound.Scope, moment);
            // a function an action declares among its own values (private currentRepeat is Template_FunctionDeclaration),
            // working in that action's scope
            if (bound.Value is FxObject { Type: "Template_FunctionDeclaration" } local) return CallFunction(local, arguments, scope, moment, bound.Scope);
        }
        if (library.Find(callee) is FxDeclaration declaration && declaration.Value is FxObject function)
        {
            switch (function.Type)
            {
                case "Template_FunctionDeclaration":
                    return CallFunction(function, arguments, scope, moment);
                case "Template_FloatTable" or "Template_Float3Table" or "Template_EvaluableTable":
                    return TableValue(function, arguments, scope, moment);
                case "Template_NamedUnaryIntrinsic_ForBank" or "TNamedBinaryIntrinsic" when function.Get("Operator") is FxText builtIn:
                    return Intrinsic(builtIn.Value, arguments, scope, moment);
            }
        }
        return Intrinsic(name, arguments, scope, moment);
    }

    /// <summary>
    /// A function of @Evaluable.ndf: its parameters stand for the calculations it was given (worked out in the caller's
    /// scope each time they are used), its own local values for theirs; then its Code, or the built-in it names.
    /// </summary>
    private FxValue CallFunction(FxObject function, IReadOnlyList<FxNode> arguments, FxScope caller, FxMoment moment, FxScope? declaredIn = null)
    {
        if (function.Get("Intrinsic") is FxName intrinsic)
        {
            string builtIn = intrinsic.Last;
            // "~/IntrinsicOperatorX" names a declaration that names the built-in
            if (builtIn.StartsWith("IntrinsicOperator", StringComparison.Ordinal) && library.Find(builtIn)?.Value is FxObject named
                && named.Get("Operator") is FxText operatorName)
                builtIn = operatorName.Value;
            return Intrinsic(builtIn, arguments, caller, moment);
        }
        if (function.Get("Code") is not FxNode code) return FxValue.Nil;
        // Tuned for speed: the scope is made once for each place the function is called from
        // (particles are moved on several threads at once, so the list is kept under a lock)
        FxScope? own;
        lock (caller)
        {
            caller.Calls ??= new(SameCall.Instance);
            if (!caller.Calls.TryGetValue((function, arguments), out own))
            {
                own = new FxScope(declaredIn ?? outermost);
                if (function.Get("Params") is FxList parameters)
                    for (int i = 0; i < parameters.Items.Count && i < arguments.Count; i++)
                        if (parameters.Items[i] is FxPair { Key: FxName parameter }) own.Bind(parameter.Value, arguments[i], caller);
                foreach ((string name, FxNode value) in function.Members) own.Bind(name, value, own);
                caller.Calls[(function, arguments)] = own;
            }
        }
        return Evaluate(code, own, moment);
    }

    /// <summary>A value picked from a table by a number from 0 to 1, as Table[RandomUnit].</summary>
    private FxValue TableValue(FxObject table, IReadOnlyList<FxNode> arguments, FxScope scope, FxMoment moment)
    {
        if (table.Properties.Values.OfType<FxList>().FirstOrDefault() is not FxList values || values.Items.Count == 0) return FxValue.Nil;
        float at = arguments.Count > 0 ? Evaluate(arguments[0], scope, moment).Number : 0;
        int index = Math.Clamp((int)(at * values.Items.Count), 0, values.Items.Count - 1);
        return Evaluate(values.Items[index], outermost, moment);
    }

    /// <summary>
    /// The game's built-in functions and the vector makers (float3[...]), done from their names: the scripts only name
    /// them. Those whose working is a guess from the name: creneau, sinusoid, sawtooth and triangle (waves of period 1).
    /// Attack, Decay and AttackDecay repeat over each whole unit of their first number, as a life ratio or a time that
    /// runs past 1 starts again: Attack rises from 0 to 1 over the share given, then holds; Decay holds at 1 until the
    /// point given, then falls to 0 at the end; AttackDecay rises to 1 at the point given and falls back (0 rises all the
    /// way, 1 falls all the way).
    /// </summary>
    private FxValue Intrinsic(string name, IReadOnlyList<FxNode> arguments, FxScope scope, FxMoment moment)
    {
        FxValue Argument(int number) => number < arguments.Count ? Evaluate(arguments[number], scope, moment) : FxValue.Of(0);
        switch (name)
        {
            case "ifte":
                return Argument(0).IsTrue ? Argument(1) : Argument(2);
            case "float" or "ToFloat":
                return FxValue.Of(Argument(0).Number);
            case "float2" or "float3" or "float4":
                return MakeVector(name[^1] - '0', arguments, scope, moment);
            case "LinearColor":
                return (arguments.Count >= 3 ? MakeVector(3, arguments, scope, moment) : Argument(0)).Map(Linear);
            case "GenFloat3" or "ToFloat3":
                // one number spread over all three, or three made into one vector
                return MakeVector(3, arguments, scope, moment);
        }
        FxValue a = Argument(0);
        switch (name)
        {
            case "abs": return a.Map(MathF.Abs);
            case "sin": return a.Map(MathF.Sin);
            case "cos": return a.Map(MathF.Cos);
            case "tan": return a.Map(MathF.Tan);
            case "asin": return a.Map(x => MathF.Asin(Math.Clamp(x, -1, 1)));
            case "acos": return a.Map(x => MathF.Acos(Math.Clamp(x, -1, 1)));
            case "atan": return a.Map(MathF.Atan);
            case "log": return a.Map(x => x <= 0 ? 0 : MathF.Log(x));
            case "exp": return a.Map(MathF.Exp);
            case "sqrt": return a.Map(x => MathF.Sqrt(Math.Max(0, x)));
            case "sqr": return a.Map(x => x * x);
            case "rsqrt": return a.Map(x => x <= 0 ? 0 : 1 / MathF.Sqrt(x));
            case "rcp": return a.Map(x => x == 0 ? 0 : 1 / x);
            case "ceil": return a.Map(MathF.Ceiling);
            case "floor": return a.Map(MathF.Floor);
            case "round": return a.Map(MathF.Round);
            case "sign": return a.Map(x => MathF.Sign(x));
            case "negate": return a.Map(x => -x);
            case "sat" or "saturate": return a.Map(x => Math.Clamp(x, 0, 1));
            case "creneau": return a.Map(x => Fraction(x) < 0.5f ? 1 : 0);
            case "sinusoid": return a.Map(x => 0.5f + 0.5f * MathF.Sin(x * MathF.Tau));
            case "sawtooth": return a.Map(Fraction);
            case "triangle": return a.Map(x => 1 - MathF.Abs(2 * Fraction(x) - 1));
            case "CosSin": return FxValue.Of(new Vector4(MathF.Cos(a.Number), MathF.Sin(a.Number), 0, 0), 2);
            case "getX": return FxValue.Of(a.Numbers.X);
            case "getY": return FxValue.Of(a.Numbers.Y);
            case "getZ": return FxValue.Of(a.Numbers.Z);
            case "getW": return FxValue.Of(a.Numbers.W);
            case "getXY0": return FxValue.Of(new Vector3(a.Numbers.X, a.Numbers.Y, 0));
            case "ToInt": return a.Map(MathF.Truncate);
            case "length": return FxValue.Of(a.Size == 1 ? MathF.Abs(a.Number) : a.Numbers.Length());
            case "normalized": return a.Numbers.LengthSquared() < 1e-12f ? a : FxValue.Of(Vector4.Normalize(a.Numbers), a.Size);
            case "MobileWrapperPosition": return FxValue.Of((a.Thing as FxFrame)?.Position ?? Vector3.Zero);
            case "MobileWrapperAxeX": return FxValue.Of((a.Thing as FxFrame)?.AxisX ?? Vector3.UnitX);
            case "MobileWrapperAxeY": return FxValue.Of((a.Thing as FxFrame)?.AxisY ?? Vector3.UnitY);
            case "MobileWrapperAxeZ": return FxValue.Of((a.Thing as FxFrame)?.AxisZ ?? Vector3.UnitZ);
            case "MobileWrapperScale": return FxValue.Of(Vector3.One);
            case "AltitudeRelative": return FxValue.Of(a.Numbers.Z);
            case "RotationFromVectorXY": return FxValue.Of(MathF.Atan2(a.Numbers.Y, a.Numbers.X));
            case "PositionUnScaled": return a;
            case "ToString": return FxValue.Nil;
        }
        FxValue b = Argument(1);
        switch (name)
        {
            case "min": return FxValue.Combine(a, b, MathF.Min);
            case "max": return FxValue.Combine(a, b, MathF.Max);
            case "pow": return FxValue.Combine(a, b, (x, y) => x <= 0 ? 0 : MathF.Pow(x, y));
            case "mod": return FxValue.Combine(a, b, (x, y) => y == 0 ? 0 : x % y);
            case "dot": return FxValue.Of(Vector4.Dot(a.Numbers, b.Numbers));
            case "cross": return FxValue.Of(Vector3.Cross(a.Vector, b.Vector));
            case "distance": return FxValue.Of(Vector4.Distance(a.Numbers, b.Numbers));
            case "Attack": return FxValue.Of(b.Number <= 0 ? 1 : Math.Min(1, Fraction(a.Number) / b.Number));
            case "Decay": return FxValue.Of(a.Number < b.Number ? 1 : b.Number >= 1 ? 0 : Math.Max(0, 1 - Fraction(a.Number - b.Number) / (1 - b.Number)));
            case "AttackDecay":
            {
                float at = Fraction(a.Number), peak = b.Number;
                if (peak == 0) return FxValue.Of(1 - at);
                if (peak == 1 || at <= peak) return FxValue.Of(at / peak);
                return FxValue.Of((1 - at) / (1 - peak));
            }
            case "lerp":
                FxValue to = Argument(2);
                float t = a.Number;
                return FxValue.Combine(b, to, (x, y) => x + (y - x) * t);
        }
        throw new FxProblem($"the function {name} is not known");
    }

    private FxValue MakeVector(int size, IReadOnlyList<FxNode> arguments, FxScope scope, FxMoment moment)
    {
        if (arguments.Count == 1)
        {
            FxValue only = Evaluate(arguments[0], scope, moment);
            return only.Size == 1 ? FxValue.Of(new Vector4(only.Number), size) : only with { Size = size };
        }
        var numbers = new float[4];
        int at = 0;
        foreach (FxNode argument in arguments)
        {
            FxValue value = Evaluate(argument, scope, moment);
            for (int i = 0; i < Math.Max(1, value.Size) && at < 4; i++) numbers[at++] = i == 0 ? value.Numbers.X : i == 1 ? value.Numbers.Y : i == 2 ? value.Numbers.Z : value.Numbers.W;
        }
        return FxValue.Of(new Vector4(numbers[0], numbers[1], numbers[2], numbers[3]), size);
    }

    private static float Fraction(float x) => x - MathF.Floor(x);

    /// <summary>A screen colour value turned into light (sRGB to linear), as the scripts' LinearColor does.</summary>
    private static float Linear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
}

/// <summary>The same function given the same calculations: the very same ones, not ones that read alike.</summary>
internal sealed class SameCall : IEqualityComparer<(FxObject Function, IReadOnlyList<FxNode> Arguments)>
{
    public static readonly SameCall Instance = new();

    public bool Equals((FxObject Function, IReadOnlyList<FxNode> Arguments) x, (FxObject Function, IReadOnlyList<FxNode> Arguments) y) =>
        ReferenceEquals(x.Function, y.Function) && ReferenceEquals(x.Arguments, y.Arguments);

    public int GetHashCode((FxObject Function, IReadOnlyList<FxNode> Arguments) call) =>
        HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(call.Function), System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(call.Arguments));
}

/// <summary>A value already worked out, as a local variable pinned when its action starts.</summary>
public sealed record FxConstant(FxValue Value) : FxNode;

/// <summary>A calculation carried with the scope it is worked out in, as a template's use passes its own values in.</summary>
public sealed record FxScoped(FxNode Value, FxScope Scope) : FxNode;

/// <summary>Something in an effect script the viewer cannot work out.</summary>
public sealed class FxProblem(string message) : Exception(message);
