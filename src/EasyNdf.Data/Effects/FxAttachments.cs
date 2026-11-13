using System.Numerics;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Effects;

/// <summary>An effect a unit plays at one of its model's nodes, such as a jet's exhaust at fx_reacteur.</summary>
/// <param name="Anchor">The node it plays at.</param>
/// <param name="Effect">The effect, as GameFx names it.</param>
/// <param name="Given">The values the action calls it with (a rotor blur's parPaleLength ...); null when it is played as it is.</param>
public sealed record FxAttachment(string Anchor, string Effect, IReadOnlyDictionary<string, FxValue>? Given = null);

/// <summary>
/// The effects a depiction's actions play at the model's nodes (GameData/Gameplay/Gfx/Units/DepictionActions.ndf):
/// DepictionAction_MovementFX_Tracked plays dust at the tracks' fx_fumee_chenille nodes, ..._DoubleReactorAirplane
/// the exhaust at fx_reacteur_1 and _2, Template_DepictionAction_Rotor a rotor's blur at its hub with the blades'
/// length and count. The file is read with the effects' reader, as its happenings are written with the same templates
/// and MAPs.
/// </summary>
public static class FxAttachments
{
    private const string ActionsFile = "GameData/Gameplay/Gfx/Units/DepictionActions.ndf";

    /// <summary>
    /// The effects these actions play at nodes, each once; none when the file cannot be read. Effects called with
    /// values are left out: those of a vehicle's movement are its splashes in water, given its size as it swims.
    /// </summary>
    public static List<FxAttachment> Read(GameDataFiles files, IEnumerable<string> actions)
    {
        var found = new List<FxAttachment>();
        if (Declarations(files) is not Dictionary<string, FxDeclaration> declarations) return found;
        foreach (string action in actions)
            if (declarations.TryGetValue(action, out FxDeclaration? declaration)) Walk(declaration.Value, new Dictionary<string, FxNode>(), declarations, found, 0);
        return [.. found.Where(attachment => attachment.Given is null).Distinct()];
    }

    /// <summary>
    /// The effects an action written out in a depiction plays at nodes, such as a rotor's
    /// Template_DepictionAction_Rotor( PaleLength = 1800 PaleCount = 5 ... ), given as it is written; none when it cannot
    /// be read.
    /// </summary>
    public static List<FxAttachment> ReadWritten(GameDataFiles files, string written)
    {
        var found = new List<FxAttachment>();
        if (Declarations(files) is not Dictionary<string, FxDeclaration> declarations) return found;
        try
        {
            Walk(FxSyntax.ReadValue(written), new Dictionary<string, FxNode>(), declarations, found, 0);
        }
        catch (FormatException)
        {
            return [];
        }
        return found;
    }

    /// <summary>The actions file's declarations by name; null when it cannot be read.</summary>
    private static Dictionary<string, FxDeclaration>? Declarations(GameDataFiles files)
    {
        if ((files.ReadPath(ActionsFile) ?? files.Read(ActionsFile)) is not string text) return null;
        try
        {
            return FxSyntax.ReadFile(text).Where(each => each.Name.Length > 0).GroupBy(each => each.Name).ToDictionary(group => group.Key, group => group.Last());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void Walk(FxNode node, Dictionary<string, FxNode> given, Dictionary<string, FxDeclaration> declarations, List<FxAttachment> found, int depth)
    {
        if (depth > 24) return;
        switch (node)
        {
            case FxTemplateParameter parameter when given.TryGetValue(parameter.Name, out FxNode? value):
                Walk(value, given, declarations, found, depth + 1);
                return;
            case FxName name when declarations.TryGetValue(name.Last, out FxDeclaration? declared):
                Walk(declared.Value, given, declarations, found, depth + 1);
                return;
            case FxBinary { Operator: "+" } joined:
                Walk(joined.Left, given, declarations, found, depth + 1);
                Walk(joined.Right, given, declarations, found, depth + 1);
                return;
            case FxCall { Callee: FxName { Last: "MAP" } } map:
                foreach (FxNode entry in map.Arguments)
                    if (entry is FxPair pair) Walk(pair.Value, given, declarations, found, depth + 1);
                return;
            case FxList list:
                foreach (FxNode item in list.Items) Walk(item, given, declarations, found, depth + 1);
                return;
            case FxObject happening:
                Happening(happening, given, declarations, found, depth);
                return;
        }
    }

    private static void Happening(FxObject happening, Dictionary<string, FxNode> given, Dictionary<string, FxDeclaration> declarations, List<FxAttachment> found, int depth)
    {
        // a template of the file: its body, with its parameters given by the use or their defaults
        if (declarations.TryGetValue(happening.Type, out FxDeclaration? template) && template.TemplateParameters is not null)
        {
            var inner = new Dictionary<string, FxNode>();
            foreach ((string parameter, FxNode? defaultValue) in template.TemplateParameters)
            {
                FxNode? value = happening.Properties.TryGetValue(parameter, out FxNode? passed) ? Given(passed, given) : defaultValue;
                if (value is not null) inner[parameter] = value;
            }
            Walk(template.Value, inner, declarations, found, depth + 1);
            return;
        }
        switch (happening.Type)
        {
            case "TCompositeHappening":
                if (happening.Get("SubHappenings") is FxNode subHappenings) Walk(subHappenings, given, declarations, found, depth + 1);
                return;
            case "TIntroduceMobileHappening":
                if (Given(happening.Get("Anchor"), given) is FxText anchor && EffectOf(happening.Get("Happening"), given) is (string effect, var values))
                    found.Add(new FxAttachment(anchor.Value, effect, values));
                return;
        }
    }

    /// <summary>
    /// The effect a TSequencingActionHappening plays, as GameFx names it, with the values it is called with when it is a
    /// TActionCall( Action = ... NamedParams = MAP[...] ); null for none, or one written out in place.
    /// </summary>
    private static (string Effect, IReadOnlyDictionary<string, FxValue>? Values)? EffectOf(FxNode? happening, Dictionary<string, FxNode> given)
    {
        if (Given(happening, given) is not FxObject { Type: "TSequencingActionHappening" } sequencing) return null;
        switch (Given(sequencing.Get("ActionDescriptor"), given))
        {
            case FxName effect:
                return (effect.Last, null);
            case FxObject { Type: "TActionCall" } call when Given(call.Get("Action"), given) is FxName effect:
                var values = new Dictionary<string, FxValue>(StringComparer.Ordinal);
                if (Given(call.Get("NamedParams"), given) is FxCall { Callee: FxName { Last: "MAP" } } map)
                    foreach (FxNode entry in map.Arguments)
                        if (entry is FxPair { Key: FxText name } pair && ValueOf(Given(pair.Value, given)) is FxValue value) values[name.Value] = value;
                return (effect.Last, values);
            default:
                return null;
        }
    }

    /// <summary>A value an action gives an effect, as written: a number, a vector (float3[0, 0, 1]), a text, true or false; null for anything else.</summary>
    private static FxValue? ValueOf(FxNode? written) => written switch
    {
        FxNumber number => FxValue.Of(number.Value),
        FxUnary { Operator: "-", Operand: FxNumber number } => FxValue.Of(-number.Value),
        FxText text => FxValue.OfThing(text.Value),
        FxName { Value: "True" or "true" } => FxValue.Of(true),
        FxName { Value: "False" or "false" } => FxValue.Of(false),
        FxCall { Callee: FxName { Last: "float2" or "float3" or "float4" }, Arguments: var parts } when parts.Count is >= 2 and <= 4 && parts.All(part => ValueOf(part) is { Size: 1 }) =>
            FxValue.Of(new Vector4(parts.Select(part => ValueOf(part)!.Value.Number).Concat([0f, 0f]).Take(4).ToArray()), parts.Count),
        _ => null,
    };

    /// <summary>A value, with a template's parameter replaced by what it was given.</summary>
    private static FxNode? Given(FxNode? value, Dictionary<string, FxNode> given) =>
        value is FxTemplateParameter parameter ? given.GetValueOrDefault(parameter.Name) : value;
}
