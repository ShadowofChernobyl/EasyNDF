using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The open mod's changes to the chosen division, shown as <see cref="ModMarks"/> shows the units': a value it changes
/// has "*" after it in the headings' colour, its tooltip naming the mod, the rules (those of the Armory presets
/// for that change) and the game's value. A division the mod makes is not marked at all: every value of it is the
/// mod's, which its place among the mod's own divisions already says, and marks on all of them would say nothing.
/// </summary>
public static class DivisionMarks
{
    private static Dictionary<string, Division> game = new(StringComparer.Ordinal);
    private static string modName = "";
    private static IReadOnlyList<JsonNode?> rules = [];

    /// <summary>
    /// For a generated division shown, the division as its generation makes it, before the user's own changes: what its
    /// values are compared with, as a game division's are with the game's. Null for any other.
    /// </summary>
    private static Division? generated;

    /// <summary>The division shown, as the mod makes it; null with All units.</summary>
    public static Division? Chosen { get; private set; }

    /// <summary>What the marks compare with: the game's divisions, the division shown, the mod and its Armory presets' rules.</summary>
    /// <param name="generatedBefore">For a generated division shown, the division as its generation makes it; null for any other.</param>
    public static void Use(IReadOnlyList<Division> gameDivisions, Division? chosen, string mod, IReadOnlyList<JsonNode?> presetRules, Division? generatedBefore = null)
    {
        generated = generatedBefore;
        game = [];
        foreach (Division division in gameDivisions) game.TryAdd(division.Descriptor, division);
        Chosen = chosen;
        modName = mod;
        rules = presetRules;
    }

    /// <summary>
    /// The tooltip of a value of the chosen division the mod changed; null when it did not change it.
    /// </summary>
    /// <param name="changes">The changes that set it, as the Armory presets name them, such as "Points" or "Unit:Descriptor_Unit_X:Cards".</param>
    /// <param name="write">Writes the value of a division as it is shown: the game's is compared, and given in the tooltip.</param>
    public static string? TipIfChanged(IReadOnlyCollection<string> changes, Func<Division, string> write)
    {
        if (Chosen is not Division shown) return null;
        if (BeforeChanges(shown) is not Division before) return null;
        string was = write(before);
        if (was == write(shown)) return null;
        return ByTip(shown, changes) + "\n" + Texts.Get(WasTip(shown), ("value", was == "" ? UnitNumbers.None : was));
    }

    /// <summary>
    /// The tooltip of one of a unit's transports in the chosen division when the mod gave it or took it away; null when the
    /// game's division has it as the mod's does, and in a division the mod makes.
    /// </summary>
    /// <param name="unit">The unit's descriptor.</param>
    /// <param name="transport">The transport's descriptor.</param>
    /// <param name="hasIt">Whether the unit has the transport in the division as the mod makes it.</param>
    public static string? TransportTipIfChanged(string unit, string transport, bool hasIt)
    {
        if (Chosen is not Division shown) return null;
        if (BeforeChanges(shown) is not Division before) return null;
        bool gameHasIt = UnitIn(before, unit)?.Transports.Contains(transport) == true;
        if (gameHasIt == hasIt) return null;
        string gameValue = Texts.Get(gameHasIt ? "Divisions.Transports.Game.Ticked" : "Divisions.Transports.Game.NotTicked");
        return ByTip(shown, UnitChanges(unit, "Transports")) + "\n" + Texts.Get(WasTip(shown), ("value", gameValue));
    }

    /// <summary>
    /// What the division shown is compared with: the game's own; for a generated division, as its generation makes it.
    /// Null for a division the mod makes otherwise, which is not marked.
    /// </summary>
    private static Division? BeforeChanges(Division shown) =>
        generated?.Descriptor == shown.Descriptor ? generated : game.GetValueOrDefault(shown.Descriptor);

    /// <summary>The tooltip's last line: the game's value, or, for a generated division, the value its generation gives.</summary>
    private static string WasTip(Division shown) =>
        generated?.Descriptor == shown.Descriptor ? "Divisions.Changed.Generated.Tip" : "Units.Changed.Game.Tip";

    /// <summary>"Changed by the mod: its rules", from the Armory presets' rules for the changes; the mod alone when none is (another preset's rule).</summary>
    private static string ByTip(Division shown, IEnumerable<string> changes)
    {
        HashSet<string> wanted = [.. changes];
        List<string> names = [.. rules
            .Where(rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.DivisionKey] == shown.Descriptor
                && wanted.Contains((string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.ChangeKey] ?? ""))
            .Select(rule => (string?)rule?["Name"] ?? "")
            .Where(name => name != "")
            .Distinct()];
        return names.Count == 0
            ? Texts.Get("Units.Changed.By.Tip", ("mod", modName))
            : Texts.Get("Units.Changed.ByRules.Tip", ("mod", modName), ("rules", string.Join(", ", names)));
    }

    /// <summary>
    /// A value to change (a box, a dropdown) marked when the mod changed it: "*" after it in the headings' colour, both
    /// with the tooltip. Gives back what to put in its place: the value, or the value with its "*".
    /// </summary>
    public static FrameworkElement Mark(FrameworkElement value, IReadOnlyCollection<string> changes, Func<Division, string> write)
    {
        if (TipIfChanged(changes, write) is not string tip) return value;
        value.ToolTip = tip;
        if (!ModMarks.AreShown) return value;
        var star = new TextBlock { Text = "*", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), ToolTip = tip };
        star.SetResourceReference(TextBlock.ForegroundProperty, ModMarks.ChangedColour);
        // a value that fills its place keeps the * at its right edge; one of its own width has it right after it
        bool fills = double.IsNaN(value.Width) && value.HorizontalAlignment == HorizontalAlignment.Stretch;
        DockPanel.SetDock(value, fills ? Dock.Right : Dock.Left);
        DockPanel.SetDock(star, fills ? Dock.Right : Dock.Left);
        var marked = new DockPanel { LastChildFill = fills };
        if (fills)
        {
            marked.Children.Add(star);
            marked.Children.Add(value);
        }
        else
        {
            marked.Children.Add(value);
            marked.Children.Add(star);
        }
        return marked;
    }

    /// <summary>A text of the chosen division marked when the mod changed it: "*" after it in the headings' colour, with the tooltip.</summary>
    public static void MarkText(TextBlock text, IReadOnlyCollection<string> changes, Func<Division, string> write)
    {
        if (TipIfChanged(changes, write) is not string tip) return;
        text.ToolTip = tip;
        if (!ModMarks.AreShown) return;
        text.Inlines.Add(ModMarks.Star());
    }

    /// <summary>The changes that set a value of a unit in the chosen division: its own, or its being put in.</summary>
    public static string[] UnitChanges(string unit, params string[] values) => [.. values.Select(value => "Unit:" + unit + ":" + value), "UnitIn:" + unit];

    /// <summary>A unit's line in a division, or null when the division does not have it.</summary>
    public static DivisionUnit? UnitIn(Division division, string unit) => division.Units.FirstOrDefault(each => each.Unit == unit);
}
