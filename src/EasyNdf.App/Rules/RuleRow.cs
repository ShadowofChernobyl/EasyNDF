using System.Text.Json.Nodes;

namespace EasyNdf.App.Rules;

/// <summary>
/// One line of the rule list (the HTML app's <c>renderRuleList</c>): its switch, its type's symbol, its name, and the
/// buttons that move, copy and delete it. A Group's line has a fold button too, and the rules under it are indented.
/// </summary>
public sealed class RuleRow
{
    /// <summary>The rule's place in the preset, from 0.</summary>
    public required int Index { get; init; }

    /// <summary>The rule, as stored in the mod.</summary>
    public required JsonObject Rule { get; init; }

    /// <summary>Whether the line is selected.</summary>
    public required bool IsSelected { get; init; }

    /// <summary>Whether the rule sits under a Group's heading.</summary>
    public required bool IsInGroup { get; init; }

    /// <summary>Whether the rule is switched off, or its group is.</summary>
    public required bool IsOff { get; init; }

    /// <summary>For a folded Group: how many rules it hides.</summary>
    public required int HiddenCount { get; init; }

    /// <summary>The rule's type: "Edit", "Add", ... "Group", "Variable".</summary>
    public string Kind => (string?)Rule["Kind"] is { Length: > 0 } kind ? kind : "Edit";

    /// <summary>Whether this is a Group's heading.</summary>
    public bool IsGroup => Kind == "Group";

    /// <summary>The rule's name, or "(unnamed)".</summary>
    public string Name => (string?)Rule["Name"] is { Length: > 0 } name ? name : "(unnamed)";

    /// <summary>The rule's own switch.</summary>
    public bool IsOn => Rule["Enabled"]?.GetValue<bool>() != false;

    /// <summary>Whether this Group's rules are folded away.</summary>
    public bool IsFolded => Rule["Collapsed"]?.GetValue<bool>() == true;

    /// <summary>The type's symbol (the HTML app's <c>KSYM</c>), such as ✎ for Edit.</summary>
    public string SymbolText => Symbols.GetValueOrDefault(Kind, ("", "muted")).Item1;

    /// <summary>The theme colour the symbol is drawn in, such as "muted" for Edit.</summary>
    public string SymbolColour => Symbols.GetValueOrDefault(Kind, ("", "muted")).Item2;

    /// <summary>The symbol's tooltip, such as "Edit rule. Click to open."</summary>
    public string SymbolTip => (Kind is "Variable" or "Group" ? Kind : Kind + " rule") + ". Click to open.";

    /// <summary>A rule type's symbol and the theme colour it is drawn in, as the rule list shows them.</summary>
    public static (string Text, string Colour) SymbolOf(string kind) => Symbols.GetValueOrDefault(kind, ("", "muted"));

    /// <summary>What each rule type does, in a line: the New rule menu's tooltips, from the Help page on rule types.</summary>
    public static readonly IReadOnlyDictionary<string, string> Summaries = new Dictionary<string, string>
    {
        ["Edit"] = "Changes values in every descriptor that matches. The one you'll use most.",
        ["Add"] = "Adds a new descriptor, written exactly as you type it.",
        ["Clone"] = "Copies a descriptor under a new name, then changes the copy.",
        ["Remove"] = "Deletes every descriptor that matches. Needs at least one condition.",
        ["Replace"] = "Replaces a value wherever it appears.",
        ["Scale"] = "Changes a number, then rounds it and keeps it within limits you set.",
        ["Formula"] = "Works out a value from other values, like round(0.8 * MaximumRangeGRU, 25).",
        ["Table"] = "Applies values pasted from a spreadsheet: one row per descriptor.",
        ["Lookup"] = "Changes descriptors based on a list in another file.",
        ["Check"] = "Changes nothing. Warns you when the number of matches isn't what you expect.",
        ["Group"] = "A heading with one on/off switch for the rules under it.",
        ["Variable"] = "A named value you can use in other rules as {Name}. Change it once to change many rules.",
    };

    /// <summary>The Group rule's symbol, a folder: drawn by <see cref="Controls.FolderGlyph"/> wherever it is shown, not written.</summary>
    public const string FolderSymbol = "\U0001F5C0";

    private static readonly Dictionary<string, (string, string)> Symbols = new()
    {
        ["Edit"] = ("✎", "muted"), ["Add"] = ("⊞", "d-add"), ["Clone"] = ("⧉", "s-keyword"),
        ["Remove"] = ("⊟", "d-del"), ["Replace"] = ("⇄", "s-param"), ["Scale"] = ("⇅", "s-number"),
        ["Formula"] = ("ƒ", "s-func"), ["Table"] = ("▦", "s-field"), ["Lookup"] = ("⌕", "s-const"),
        ["Check"] = ("✓", "ok"), ["Variable"] = ("\U0001D465", "s-string"), ["Group"] = (FolderSymbol, "accent"),
    };
}
