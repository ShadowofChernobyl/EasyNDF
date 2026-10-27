using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// How the Armory shows the open mod's changes, the same way everywhere: a value one of its rules changed has a "*"
/// after it in the theme's heading colour, the value itself keeping its own colour (Paul's choice), and its tooltip
/// names the mod and the rules, and gives the game's value. With a mod that changes nothing, nothing is marked. With
/// the setting "Mark changed values" off, the "*" is left out everywhere in the Armory, and the tooltip still says what
/// changed the value.
/// </summary>
public static class ModMarks
{
    /// <summary>A value as it is shown (see <see cref="Mark"/>): its text, whether the open mod changed it, and its tooltip.</summary>
    public sealed record MarkedValue(string Text, bool IsChanged, object? Tip);

    /// <summary>The open mod's changes to the units shown; none until they have been worked out.</summary>
    public static UnitChanges Changes { get; set; } = UnitChanges.None;

    /// <summary>The colour of a changed value: the headings' colour.</summary>
    public const string ChangedColour = "accent";

    /// <summary>Whether the Armory marks changed values with the colour and "*" (the setting "Mark changed values").</summary>
    public static bool AreShown => AppData.Current.Settings["armoryMarks"]?.GetValue<bool>() != false;

    /// <summary>
    /// Whether the Armory's panes show, under each value's name, the property it is in, in the game's files (the
    /// setting "Show names in the files").
    /// </summary>
    public static bool ShowFileNames => AppData.Current.Settings["armoryFileNames"]?.GetValue<bool>() == true;

    /// <summary>Whether the open mod changed a value of a unit.</summary>
    public static bool IsChanged(Unit unit, string field) => Changes.Of(unit)?.Changed(field) == true;

    /// <summary>Whether a value of a unit is drawn marked: the open mod changed it, and the marks are shown.</summary>
    public static bool IsMarked(Unit unit, string field) => AreShown && IsChanged(unit, field);

    /// <summary>
    /// Shows a value in a text block: as it is, in its own colour and with its own tooltip; or, when the open mod
    /// changed it, marked. A text block the list reuses for another unit is set back each time.
    /// </summary>
    /// <param name="block">Where the value is shown.</param>
    /// <param name="unit">The unit, as the mod makes it.</param>
    /// <param name="field">Which value it is (<see cref="UnitField"/>).</param>
    /// <param name="text">The value, written as the place shows it.</param>
    /// <param name="write">Writes the same value of another unit the same way: the game's, for the tooltip.</param>
    /// <param name="colour">The value's own colour.</param>
    /// <param name="tip">The value's own tooltip, or null.</param>
    public static void Show(TextBlock block, Unit unit, string field, string text, Func<Unit, string> write, string colour = "ink", object? tip = null)
    {
        MarkedValue shown = Mark(unit, field, text, write, tip);
        ShowText(block, shown.Text, shown.IsChanged);
        block.SetResourceReference(TextBlock.ForegroundProperty, colour);
        block.ToolTip = shown.Tip;
    }

    /// <summary>
    /// A text in a text block, with the changed values' "*" after it when <paramref name="isMarked"/>: only the "*" is
    /// in the changed values' colour, the text keeps the block's own.
    /// </summary>
    public static void ShowText(TextBlock block, string text, bool isMarked)
    {
        block.Text = text;
        if (isMarked) block.Inlines.Add(Star());
    }

    /// <summary>The changed values' "*", in their colour, to put after a value in a text.</summary>
    public static Run Star()
    {
        var star = new Run("*");
        star.SetResourceReference(TextElement.ForegroundProperty, ChangedColour);
        return star;
    }

    /// <summary>
    /// A value as it is shown, for a place that is not a text block: as it is, with its own tooltip; or, when the open
    /// mod changed it, marked (<see cref="MarkedValue.IsChanged"/>) with the tooltip saying what changed it. The place
    /// puts the "*" after it, in the changed values' colour (<see cref="ChangedColour"/>).
    /// </summary>
    /// <param name="unit">The unit, as the mod makes it.</param>
    /// <param name="field">Which value it is (<see cref="UnitField"/>).</param>
    /// <param name="text">The value, written as the place shows it.</param>
    /// <param name="write">Writes the same value of another unit the same way: the game's, for the tooltip.</param>
    /// <param name="tip">The value's own tooltip, or null.</param>
    public static MarkedValue Mark(Unit unit, string field, string text, Func<Unit, string> write, object? tip = null)
    {
        UnitChange? change = Changes.Of(unit);
        if (change is null || !change.Changed(field) || text == "") return new MarkedValue(text, IsChanged: false, tip);
        // unmarked, the tooltip still says what changed it
        if (!AreShown) return new MarkedValue(text, IsChanged: false, TipFor(change, field, write));
        return new MarkedValue(text, IsChanged: true, TipFor(change, field, write));
    }

    /// <summary>
    /// What a changed value's tooltip says: "Changed by WARNO Tactical Overhaul: Faster tanks", then "Game value: 45";
    /// for a unit the mod adds, "Added by WARNO Tactical Overhaul".
    /// </summary>
    public static string TipFor(UnitChange change, string field, Func<Unit, string> write)
    {
        string mod = Changes.ModName;
        if (change.Game is not Unit game) return Texts.Get("Units.Changed.Added.Tip", ("mod", mod));
        string before = write(game);
        return ChangedBy(change.RulesFor(field), mod) + "\n" + Texts.Get("Units.Changed.Game.Tip", ("value", before == "" ? UnitNumbers.None : before));
    }

    /// <summary>
    /// Which mods and rules changed a value, a line for each mod: "Changed by WARNO Tactical Overhaul: Faster tanks".
    /// With mods running together, a value can be changed by another mod than the open one. When no rule is known, the
    /// open mod is named.
    /// </summary>
    /// <param name="rules">The rules that changed it, with their mods.</param>
    /// <param name="openMod">The open mod's name.</param>
    public static string ChangedBy(IReadOnlyList<RuleOfMod> rules, string openMod)
    {
        if (rules.Count == 0) return Texts.Get("Units.Changed.By.Tip", ("mod", openMod));
        var lines = new List<string>();
        foreach (IGrouping<string, RuleOfMod> byMod in rules.GroupBy(rule => rule.Mod, StringComparer.Ordinal))
        {
            string names = string.Join(", ", byMod.Select(rule => rule.Rule).Distinct(StringComparer.Ordinal));
            lines.Add(Texts.Get("Units.Changed.ByRules.Tip", ("mod", byMod.Key), ("rules", names)));
        }
        return string.Join("\n", lines);
    }
}
