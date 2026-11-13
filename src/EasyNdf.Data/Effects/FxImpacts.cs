using System.Text.RegularExpressions;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Effects;

/// <summary>The smoke cloud an ammunition leaves where it lands (its SmokeDescriptor in SmokeDescriptor.ndf).</summary>
/// <param name="Radius">How wide it is, in the model's units (the depiction's Radius).</param>
/// <param name="Height">How high it is.</param>
/// <param name="Density">How thick it is.</param>
/// <param name="Seconds">How long it lasts (TimeToLive).</param>
public sealed record SmokeScreen(double Radius, double Height, double Density, double Seconds);

/// <summary>
/// What a shot does where it lands, as the game's files say: the effects its ImpactHappening plays on the ground
/// (MimeticImpactMapping.ndf, one picked at random), and the smoke cloud its SmokeDescriptor makes (fx_binder_smoke_screen
/// with the depiction's Radius, Height and Density).
/// </summary>
public static partial class FxImpacts
{
    private const string ImpactsFile = "Generated/Gameplay/Gfx/MimeticImpactMapping.ndf";
    private const string SmokesFile = "Generated/Gameplay/Gfx/SmokeDescriptor.ndf";

    /// <summary>The effect that draws a smoke cloud (Template_DescriptorSmoke_Depiction's fx_smoke).</summary>
    public const string SmokeScreenEffect = "fx_binder_smoke_screen";

    /// <summary>The effects an impact happening plays on the ground, one of which plays; none when it has none.</summary>
    /// <param name="files">The game's files.</param>
    /// <param name="impactHappening">The ammunition's ImpactHappening, as written ('GrenadeVehicleSmokeX8').</param>
    public static List<string> GroundEffects(GameDataFiles files, string impactHappening)
    {
        string name = impactHappening.Trim().Trim('\'', '"');
        if (files.Read(ImpactsFile) is not string text) return [];
        int start = text.IndexOf("('" + name + "'", StringComparison.Ordinal);
        if (start < 0) return [];
        Match ground = GroundHappening().Match(text, start);
        if (!ground.Success) return [];
        var effects = new List<string>();
        // the ground's happenings name random choices declared in the same file, or effects themselves
        foreach (Match happening in Regex.Matches(ground.Groups[1].Value, @"\b(Fx[A-Za-z0-9_]+)"))
        {
            Match declaration = Regex.Match(text, @"(?m)^" + Regex.Escape(happening.Groups[1].Value) + @"\s+is\b");
            if (!declaration.Success) continue;
            Match next = NextDeclaration().Match(text, declaration.Index + declaration.Length);
            string block = text[declaration.Index..(next.Success ? next.Index : text.Length)];
            effects.AddRange(GameFx().Matches(block).Select(match => match.Groups[1].Value));
        }
        return [.. effects.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The smoke cloud of a smoke descriptor ("$/GFX/Weapon/Descriptor_Smoke_..."); null when none is found.</summary>
    public static SmokeScreen? SmokeOf(GameDataFiles files, string smokeDescriptor)
    {
        string name = smokeDescriptor[(smokeDescriptor.LastIndexOf('/') + 1)..].Trim();
        if (name.Length == 0 || name == "nil" || files.Read(SmokesFile) is not string text) return null;
        Match declaration = Regex.Match(text, @"(?m)^(?:export\s+)?" + Regex.Escape(name) + @"\s+is\b");
        if (!declaration.Success) return null;
        Match next = NextDeclaration().Match(text, declaration.Index + declaration.Length);
        string block = text[declaration.Index..(next.Success ? next.Index : text.Length)];
        double? Value(string field) => Regex.Match(block, @"\b" + field + @"\s*=\s*([^\n]+)") is { Success: true } found ? Worked(found.Groups[1].Value) : null;
        if (Value("Radius") is not double radius) return null;
        return new SmokeScreen(radius, Value("Height") ?? radius, Value("Density") ?? 0.1, Value("TimeToLive") ?? 20);
    }

    /// <summary>A sum written in a file, such as "53 * 26 * 2.83"; null when it is not one.</summary>
    private static double? Worked(string written)
    {
        string sum = written.Split("//")[0].Trim().TrimEnd(')', ',').Trim();
        try
        {
            return Fold(FxSyntax.ReadValue(sum));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static double? Fold(FxNode node) => node switch
    {
        FxNumber number => number.Value,
        FxUnary { Operator: "-" } minus => -Fold(minus.Operand),
        FxBinary binary when Fold(binary.Left) is double left && Fold(binary.Right) is double right => binary.Operator switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "div" => right == 0 ? null : left / right,
            _ => null,
        },
        _ => null,
    };

    /// <summary>An impact's ground happening: everything after "EImpactSurface/Ground," up to the end of its line.</summary>
    [GeneratedRegex(@"EImpactSurface/Ground\s*,\s*([^\n]+)")]
    private static partial Regex GroundHappening();

    [GeneratedRegex(@"\$/GFX/GameFx/([A-Za-z0-9_]+)")]
    private static partial Regex GameFx();

    [GeneratedRegex(@"(?m)^(?:export\s+|private\s+)?[A-Za-z0-9_]+\s+is\b")]
    private static partial Regex NextDeclaration();
}
