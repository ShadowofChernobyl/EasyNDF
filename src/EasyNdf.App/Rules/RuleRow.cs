using System.Text.Json.Nodes;
using System.Windows;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.Data.Divisions;

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

    /// <summary>
    /// Whether the rule is locked: made from the Armory in an Armory preset, and changed there. It shows a lock
    /// in place of its move, copy and delete buttons.
    /// </summary>
    public bool IsLocked { get; init; }

    /// <summary>What a rule made from the Armory changes: "Name" or "Picture" for those, else its kind; "" for any other rule.</summary>
    public string Shows => (string?)Rule[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.ShowsKey] ?? "";

    /// <summary>Whether the rule's symbol is a picture: a division's emblem, for a rule that changes one.</summary>
    public bool ShowsPicture => Shows == "Picture";

    /// <summary>
    /// The emblem a picture rule gives, small, in place of the type's symbol: a picture of the user's own, or one of
    /// the game's.
    /// </summary>
    public FrameworkElement? Picture
    {
        get
        {
            if (!ShowsPicture || Rule[DivisionChangeRules.ArmoryKey] is not JsonObject armory) return null;
            return new DivisionEmblem((string?)armory[DivisionChangeRules.PictureSourceKey], 16, keepsRoom: true, (string?)armory[DivisionChangeRules.PictureFileKey]);
        }
    }

    /// <summary>The rule's type: "Edit", "Add", ... "Group", "Variable".</summary>
    public string Kind => (string?)Rule["Kind"] is { Length: > 0 } kind ? kind : "Edit";

    /// <summary>Whether this is a Group's heading.</summary>
    public bool IsGroup => Kind == "Group";

    /// <summary>The rule's name, or "(unnamed)".</summary>
    public string Name => (string?)Rule["Name"] is { Length: > 0 } name ? name : Texts.Get("Rules.Rule.Unnamed");

    /// <summary>The rule's own switch.</summary>
    public bool IsOn => Rule["Enabled"]?.GetValue<bool>() != false;

    /// <summary>Whether this Group's rules are folded away.</summary>
    public bool IsFolded => Rule["Collapsed"]?.GetValue<bool>() == true;

    /// <summary>
    /// The type's symbol (the HTML app's <c>KSYM</c>), such as ✎ for Edit; "Aa" for a rule that names a division, and
    /// none for one that gives it a picture, which shows the picture instead.
    /// </summary>
    public string SymbolText => Shows switch
    {
        "Name" => NameSymbol,
        "Picture" => "",
        _ => Symbols.GetValueOrDefault(Kind, ("", "muted")).Item1,
    };

    /// <summary>The theme colour the symbol is drawn in, such as "muted" for Edit.</summary>
    public string SymbolColour => Shows == "Name" ? "accent" : Symbols.GetValueOrDefault(Kind, ("", "muted")).Item2;

    /// <summary>The symbol of a rule that names a division.</summary>
    public const string NameSymbol = "Aa";

    /// <summary>The symbol's tooltip, such as "Edit rule. Click to open."</summary>
    public string SymbolTip => Shows switch
    {
        "Name" => Texts.Get("Rules.Rule.SymbolTip", ("kind", Texts.Get("Rules.Kind.DivisionName"))),
        "Picture" => Texts.Get("Rules.Rule.SymbolTip", ("kind", Texts.Get("Rules.Kind.DivisionEmblem"))),
        _ => Texts.Get("Rules.Rule.SymbolTip", ("kind", RuleKindNames.Of(Kind))),
    };

    /// <summary>A rule type's symbol and the theme colour it is drawn in, as the rule list shows them.</summary>
    public static (string Text, string Colour) SymbolOf(string kind) => Symbols.GetValueOrDefault(kind, ("", "muted"));

    /// <summary>What each rule type does, in a line: the New rule menu's tooltips, from the Help page on rule types.</summary>
    public static string SummaryOf(string kind) => kind switch
    {
        "Edit" => Texts.Get("Rules.KindSummary.Edit"),
        "Add" => Texts.Get("Rules.KindSummary.Add"),
        "Clone" => Texts.Get("Rules.KindSummary.Clone"),
        "Remove" => Texts.Get("Rules.KindSummary.Remove"),
        "Replace" => Texts.Get("Rules.KindSummary.Replace"),
        "Scale" => Texts.Get("Rules.KindSummary.Scale"),
        "Formula" => Texts.Get("Rules.KindSummary.Formula").Replace("`", ""),
        "Table" => Texts.Get("Rules.KindSummary.Table"),
        "Lookup" => Texts.Get("Rules.KindSummary.Lookup"),
        "Text" => Texts.Get("Rules.KindSummary.Text"),
        "File" => Texts.Get("Rules.KindSummary.File"),
        "Check" => Texts.Get("Rules.KindSummary.Check"),
        "Group" => Texts.Get("Rules.KindSummary.Group"),
        "Variable" => Texts.Get("Rules.KindSummary.Variable").Replace("`", ""),
        _ => "",
    };

    /// <summary>The Group rule's symbol, a folder: drawn by <see cref="Controls.FolderGlyph"/> wherever it is shown, not written.</summary>
    public const string FolderSymbol = "\U0001F5C0";

    private static readonly Dictionary<string, (string, string)> Symbols = new()
    {
        ["Edit"] = ("✎", "muted"), ["Add"] = ("⊞", "d-add"), ["Clone"] = ("⧉", "s-keyword"),
        ["Remove"] = ("⊟", "d-del"), ["Replace"] = ("⇄", "s-param"), ["Scale"] = ("⇅", "s-number"),
        ["Formula"] = ("ƒ", "s-func"), ["Table"] = ("▦", "s-field"), ["Lookup"] = ("⌕", "s-const"),
        ["Text"] = ("¶", "s-string"), ["File"] = ("❏", "s-keyword"),
        ["Check"] = ("✓", "ok"), ["Variable"] = ("\U0001D465", "s-string"), ["Group"] = (FolderSymbol, "accent"),
    };
}
