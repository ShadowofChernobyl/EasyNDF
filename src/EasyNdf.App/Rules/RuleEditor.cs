using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Controls;
using EasyNdf.Data;
using EasyNdf.App.Text;

namespace EasyNdf.App.Rules;

/// <summary>
/// Builds what the rule window shows for a rule (the HTML app's <c>renderEditor</c>): its name and switch, then the
/// settings of its type, one method per type, and its lists of conditions and actions. Every change is written into the
/// rule straight away and saved; a change that alters what is shown (another scope, another position) builds it again.
/// </summary>
public sealed class RuleEditor
{
    /// <summary>A list's starting height, 7.5rem; lists share what is left of the window's height.</summary>
    private const double ListHeight = 120;

    private readonly RulesModel model;
    private readonly ScanLists scan;
    private readonly Action<string> saved;
    private readonly Action rebuild;
    private readonly List<UIElement> parts = [];
    private JsonObject rule = [];
    private JsonObject parameters = [];

    /// <summary>The lists of rows built, so the window can draw them again after a change.</summary>
    public List<RowListView> Lists { get; } = [];

    /// <param name="model">The Rules tab's data.</param>
    /// <param name="scan">The property names, descriptor names and types of the scanned file, for the "Pick from scan..." dropdowns.</param>
    /// <param name="saved">Saves a change; it is given the box typed in ("" for a click), so keys typed into one box are one step to undo.</param>
    /// <param name="rebuild">Builds the window again, after a change that alters what it shows.</param>
    public RuleEditor(RulesModel model, ScanLists scan, Action<string> saved, Action rebuild)
    {
        this.model = model;
        this.scan = scan;
        this.saved = saved;
        this.rebuild = rebuild;
    }

    /// <summary>The rule types that apply to one kind of object, or to the constants, as well as to descriptors.</summary>
    private static readonly HashSet<string> ScopedKinds = ["Edit", "Replace", "Scale", "Formula", "Check"];

    /// <summary>The operand and target choices every descriptor has, before the scanned property names.</summary>
    public static readonly string[] SpecialOperands = ["Entire Descriptor", "Descriptor Title", "Descriptor Type"];

    /// <summary>The window's title for a rule type: "Rule" for Edit, otherwise the type's name in title case.</summary>
    public static string TitleOf(string kind) =>
        kind == "Edit" ? Texts.Get("Rules.Editor.Title") : Text.TitleCase.Of(RuleKindNames.Of(kind));

    /// <summary>A rule's type, or Edit for one this app does not know.</summary>
    public static string KindOf(JsonObject rule) =>
        (string?)rule["Kind"] is { } kind && EasyNdf.Engine.Mods.RuleLoading.Kinds.ContainsKey(kind) ? kind : "Edit";

    /// <summary>Builds the parts for a rule, in order.</summary>
    public IReadOnlyList<UIElement> Build(JsonObject openRule)
    {
        rule = openRule;
        if (rule["Params"] is not JsonObject found) rule["Params"] = found = [];
        parameters = found;
        parts.Clear();
        Lists.Clear();

        string kind = KindOf(rule);
        AddHeader();
        if (ScopedKinds.Contains(kind)) AddScope();
        switch (kind)
        {
            case "Edit": BuildEdit(); break;
            case "Group": BuildGroup(); break;
            case "Variable": BuildVariable(); break;
            case "Add": BuildAdd(); break;
            case "Clone": BuildClone(); break;
            case "Remove": BuildRemove(); break;
            case "Replace": BuildReplace(); break;
            case "Scale": BuildScale(); break;
            case "Formula": BuildFormula(); break;
            case "Table": BuildTable(); break;
            case "Lookup": BuildLookup(); break;
            case "Check": BuildCheck(); break;
        }
        return parts;
    }

    // ---- one method per rule type ----

    private static string EveryDescriptor => Texts.Get("Rules.Editor.EveryDescriptor");

    private void BuildEdit()
    {
        AddConditions(Texts.Get("Rules.Editor.Conditions"), EveryDescriptor);
        AddActions(Texts.Get("Rules.Editor.Actions"));
    }

    private void BuildGroup()
    {
        AddNote(Note("Rules.Editor.Group.Note"));
        AddConditions(Texts.Get("Rules.Editor.SharedConditions"), null);
    }

    private void BuildVariable()
    {
        AddRow(Texts.Get("Rules.Editor.Name"), TextField("VarName", Texts.Get("Rules.Editor.VarNameHint.ForExampleRangeMultiplier"), mono: true));
        AddRow(Texts.Get("Rules.Editor.Value"), TextField("Value", Texts.Get("Rules.Editor.ValueHint.ForExample2"), mono: true));
        string name = JsonValues.AsText(parameters["VarName"]);
        AddNote(Note("Rules.Editor.Variable.Note", ("variable", "{" + (name != "" ? name : Texts.Get("Rules.Editor.Name")) + "}")));
    }

    private void BuildAdd()
    {
        AddRow(Texts.Get("Rules.Editor.DescriptorName"), TextField("Name", Texts.Get("Rules.Editor.NameHint.NameOfThe"), mono: true));
        var where = new List<FrameworkElement?> { Choice("Position", [("end", Texts.Get("Rules.Editor.Choice.End.AtTheEnd")), ("after", Texts.Get("Rules.Editor.Choice.After.AfterADescriptor"))], rebuildAfter: true) };
        if (JsonValues.AsText(parameters["Position"]) == "after")
        {
            where.Add(TextField("After", Texts.Get("Rules.Editor.AfterHint.DescriptorToAdd"), mono: true));
            where.Add(Fill("After", scan.Titles));
        }
        AddRow(Texts.Get("Rules.Editor.Where"), [.. where]);
        // .rcm: the descriptor's text, in the File Editor's editor, 12rem tall at least and taking the room left
        var text = new Editor.NdfTextBox(JsonValues.AsText(parameters["Text"]), parameters)
        {
            ToolTip = Texts.Get("Rules.Editor.Add.Text.Tip"),
            MinHeight = 192,
        };
        text.TextChanged += typed => { parameters["Text"] = typed; saved(TypingKey("p", "Text")); };
        FlexColumn.SetGrowBasis(text, 192);
        parts.Add(text);
        AddNote(Note("Rules.Editor.Add.Note"));
    }

    private void BuildClone()
    {
        AddRow(Texts.Get("Rules.Editor.DescriptorToCopy"), TextField("Source", Texts.Get("Rules.Editor.SourceHint.ExistingDescriptorName"), mono: true), Fill("Source", scan.Titles));
        AddRow(Texts.Get("Rules.Editor.NameOfTheCopy"), TextField("NewName", Texts.Get("Rules.Editor.NewNameHint.NewDescriptorName"), mono: true));
        AddRow(Texts.Get("Rules.Editor.IDs"), Tick("NewIds", Texts.Get("Rules.Editor.NewIds")));
        AddActions(Texts.Get("Rules.Editor.ActionsAppliedToThe"));
        AddNote(Note("Rules.Editor.Clone.Note"));
    }

    private void BuildRemove()
    {
        AddNote(Note("Rules.Editor.Remove.Note"));
        AddConditions(Texts.Get("Rules.Editor.Conditions"), null);
    }

    private void BuildReplace()
    {
        AddRow(Texts.Get("Rules.Editor.Find"), TextField("Find", Texts.Get("Rules.Editor.FindHint.ValueToLook"), mono: true));
        AddRow(Texts.Get("Rules.Editor.ReplaceWith"), TextField("Replace", Texts.Get("Rules.Editor.ReplaceHint.NewValue"), mono: true));
        AddRow(Texts.Get("Rules.Editor.Match"), Choice("Mode", [("whole", Texts.Get("Rules.Editor.Choice.Whole.WholeValueOnly")), ("part", Texts.Get("Rules.Editor.Choice.Part.AnyPartOf"))]));
        AddCommentRow();
        AddNote(Note("Rules.Editor.Replace.Note"));
        AddConditions(Texts.Get("Rules.Editor.ConditionsOptional"), EveryDescriptor);
    }

    private void BuildScale()
    {
        AddRow(Texts.Get("Rules.Editor.Target"), Coloured(TextField("Target", Texts.Get("Rules.Editor.TargetHint.PropertyNameOr"), mono: true), RuleTextKind.Path), Fill("Target", scan.Names));
        AddRow(Texts.Get("Rules.Editor.Operation"),
            Choice("Op", [("Multiply", Texts.Get("Rules.Editor.Choice.Multiply.MultiplyBy")), ("Add", Texts.Get("Rules.Editor.Choice.Add.Add")), ("Subtract", Texts.Get("Rules.Editor.Choice.Subtract.Subtract")), ("Divide", Texts.Get("Rules.Editor.Choice.Divide.DivideBy")), ("None", Texts.Get("Rules.Editor.Choice.None.NoneOnlyRound"))]),
            TextField("Value", Texts.Get("Rules.Editor.ValueHint.Number"), mono: true));
        AddRow(Texts.Get("Rules.Editor.RoundToAStep"), TextField("Step", Texts.Get("Rules.Editor.StepHint.ForExample5"), mono: true),
            Choice("RoundMode", [("nearest", Texts.Get("Rules.Editor.Choice.Nearest.Nearest")), ("down", Texts.Get("Rules.Editor.Choice.Down.Down")), ("up", Texts.Get("Rules.Editor.Choice.Up.Up"))]));
        AddRow(Texts.Get("Rules.Editor.Limits"), TextField("Min", Texts.Get("Rules.Editor.MinHint.MinimumEmptyFor"), mono: true), TextField("Max", Texts.Get("Rules.Editor.MaxHint.MaximumEmptyFor"), mono: true));
        AddCommentRow();
        AddConditions(Texts.Get("Rules.Editor.ConditionsOptional"), EveryDescriptor);
    }

    private void BuildFormula()
    {
        AddRow(Texts.Get("Rules.Editor.Target"), Coloured(TextField("Target", Texts.Get("Rules.Editor.TargetHint.PropertyNameOr"), mono: true), RuleTextKind.Path), Fill("Target", scan.Names));
        TextBox formula = Coloured(TextField("Expression", "For example round(0.8 * MaximumRangeGRU, 25)", mono: true), RuleTextKind.Formula);
        Suggestions.Attach(formula, RuleSuggestions.Formula(scan.Names, () => RuleSuggestions.VariablesOf(model)));
        AddRow(Texts.Get("Rules.Editor.Formula"), formula);
        AddCommentRow();
AddNote(Note("Rules.Editor.Formula.Note"));
        AddConditions(Texts.Get("Rules.Editor.ConditionsOptional"), EveryDescriptor);
    }

    private void BuildTable()
    {
        AddRow(Texts.Get("Rules.Editor.EachValueIs"), Choice("Op", [("Set To", Texts.Get("Rules.Editor.Choice.SetTo.SetAsThe")), ("Multiply", Texts.Get("Rules.Editor.Choice.Multiply.MultipliedIntoThe")), ("Add", Texts.Get("Rules.Editor.Choice.Add.AddedToThe"))]));
        AddCommentRow();
        // textarea rows="12": twelve lines of the code font, 4 pixels in at the top and bottom
        var table = TextArea("Text", "Name,MaxSpeedInKmph,FuelCapacity\nDescriptor_Unit_A,70,500\nDescriptor_Unit_B,,650");
        table.Height = 12 * 17.390625 + 8 + 2;
        parts.Add(table);
        AddNote(Note("Rules.Editor.Table.Note"));
    }

    private void BuildLookup()
    {
        // the scanned files, each stored by the shortest name no other answers to; the one the rule names keeps its name
        string sourceFile = JsonValues.AsText(parameters["SourceFile"]);
        var files = new List<ChoiceOption> { new("", Texts.Get("Rules.Editor.Lookup.ChooseFile")) };
        files.AddRange(scan.SourceFiles);
        if (sourceFile != "" && !scan.SourceFileIsScanned) files.Add(new(sourceFile, Texts.Get("Rules.Editor.Lookup.NotScanned", ("file", sourceFile))));
        AddNote(Note("Rules.Editor.Lookup.Note"));
        AddRow(Texts.Get("Rules.Editor.OtherFile"), Choice("SourceFile", files, rebuildAfter: true));
        AddList(RulesModel.SourceConditions, Texts.Get("Rules.Editor.Lookup.SourceConditions"), Gate("SourceGate", inParams: true), Texts.Get("Rules.Editor.ACondition"), Texts.Get("Rules.Editor.NoConditions") + " " + Texts.Get("Rules.Editor.PressPlus"));
        AddRow(Texts.Get("Rules.Editor.CollectFromEach"), TextField("Collect", Texts.Get("Rules.Editor.CollectHint.DescriptorTitleOr"), mono: true), Fill("Collect", ["Descriptor Title", .. scan.SourceNames]));
        parts.Add(new TextBlock { Text = Texts.Get("Rules.Editor.Lookup.ToEdit"), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) }.Themed(TextBlock.ForegroundProperty, "accent"));
        AddRow(Texts.Get("Rules.Editor.WhereThisMatchesA"), TextField("MatchOn", Texts.Get("Rules.Editor.CollectHint.DescriptorTitleOr"), mono: true), Fill("MatchOn", ["Descriptor Title", .. scan.Names]));
        AddNote(Note("Rules.Editor.Lookup.ReferenceNote"));
        AddConditions(Texts.Get("Rules.Editor.ExtraConditionsInThis"), null);
        AddActions(Texts.Get("Rules.Editor.Actions"));
    }

    private void BuildCheck()
    {
        AddNote(Note("Rules.Editor.Check.Note"));
        var expect = new List<FrameworkElement?> { Choice("Expect", [("atLeast", Texts.Get("Rules.Editor.Choice.AtLeast.AtLeastThis")), ("exactly", Texts.Get("Rules.Editor.Choice.Exactly.ExactlyThisMany")), ("atMost", Texts.Get("Rules.Editor.Choice.AtMost.AtMostThis")), ("none", Texts.Get("Rules.Editor.Choice.None.NoneAtAll"))], rebuildAfter: true) };
        if (JsonValues.AsText(parameters["Expect"]) != "none") expect.Add(TextField("Count", Texts.Get("Rules.Editor.ValueHint.Number"), mono: true));
        AddRow(Texts.Get("Rules.Editor.Expect"), [.. expect]);
        AddRow(Texts.Get("Rules.Editor.MessageWhenItFails"), TextField("Message", Texts.Get("Rules.Editor.MessageHint.OptionalNoteTo"), mono: false));
        AddConditions(Texts.Get("Rules.Editor.ConditionsOptional"), "No conditions: every descriptor.");
    }

    // ---- the parts every rule has ----

    /// <summary>.rhead: "Name:" with the Enabled switch at the right, and the name under them.</summary>
    private void AddHeader()
    {
        var enabled = new CheckBox { Content = Texts.Get("Rules.Editor.Enabled"), IsChecked = !JsonValues.IsFalse(rule["Enabled"]), ToolTip = Texts.Get("Rules.Editor.Enabled.Tip") };
        enabled.SetResourceReference(FrameworkElement.StyleProperty, "Toggle");
        enabled.Click += (_, _) => { rule["Enabled"] = enabled.IsChecked == true; saved(""); };
        var label = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
        DockPanel.SetDock(enabled, Dock.Right);
        label.Children.Add(enabled);
        label.Children.Add(Label(Texts.Get("Rules.Editor.Name")));

        var name = new TextBox { Text = JsonValues.AsText(rule["Name"]), Tag = Texts.Get("Rules.Editor.RuleName.Hint"), ToolTip = null };
        name.TextChanged += (_, _) => { rule["Name"] = name.Text; saved(TypingKey("", "Name")); };
        var header = new StackPanel();
        header.Children.Add(label);
        header.Children.Add(name);
        parts.Add(header);
    }

    /// <summary>.rscope: what the rule applies to: each descriptor, each object of one type, or the top-level constants.</summary>
    private void AddScope()
    {
        string scope = JsonValues.AsText(parameters["Scope"]);
        if (scope is not ("constants" or "type")) scope = "";
        var row = new FlexRow { Gap = 8, LineGap = 4 };
        row.Children.Add(new TextBlock { Text = Texts.Get("Rules.Editor.AppliesTo") }.Themed(TextBlock.ForegroundProperty, "muted"));
        ComboBox choice = Choice("Scope", [("", Texts.Get("Rules.Editor.Choice.None.EachDescriptor")), ("type", Texts.Get("Rules.Editor.Choice.Type.EachObjectOf")), ("constants", Texts.Get("Rules.Editor.Choice.Constants.TopLevelConstants"))], rebuildAfter: true);
        FlexRow.SetGrow(choice, 1);
        row.Children.Add(choice);
        if (scope == "type")
        {
            TextBox type = TextField("ScopeType", Texts.Get("Rules.Editor.ScopeTypeHint.ObjectTypeFor"), mono: true);
            FlexRow.SetBasis(type, FlexRow.FullLine);
            row.Children.Add(type);
            if (Fill("ScopeType", scan.Types) is ComboBox fill) row.Children.Add(fill);
        }
        parts.Add(row);
        if (scope == "type")
            AddNote(Note("Rules.Editor.Scope.TypeNote"));
        else if (scope == "constants")
            AddNote(Note("Rules.Editor.Scope.ConstantsNote"));
    }

    /// <summary>"Comment on each edit": the comment's text, and whether it includes the original value.</summary>
    private void AddCommentRow() =>
        AddRow(Texts.Get("Rules.Editor.CommentOnEachEdit"), TextField("CommentText", Texts.Get("Rules.Editor.CommentTextHint.EmptyForNone"), mono: false), Tick("CommentOriginal", Texts.Get("Rules.Editor.CommentOriginal")));

    private void AddConditions(string title, string? emptyHint) =>
        AddList(RulesModel.Conditions, title, Gate("LogicGate", inParams: false), Texts.Get("Rules.Editor.ACondition"), (emptyHint ?? Texts.Get("Rules.Editor.NoConditions")) + " " + Texts.Get("Rules.Editor.PressPlus"));

    private void AddActions(string title) => AddList(RulesModel.Actions, title, null, Texts.Get("Rules.Editor.AnAction"), Texts.Get("Rules.Editor.NoActions") + " " + Texts.Get("Rules.Editor.PressPlus"));

    private void AddList(string list, string title, FrameworkElement? gate, string what, string empty)
    {
        var view = new RowListView(model, list, title, gate, what, empty);
        FlexColumn.SetGrowBasis(view, ListHeight);
        Lists.Add(view);
        parts.Add(view);
    }

    /// <summary>The Any/All dropdown of a list of conditions.</summary>
    private ComboBox Gate(string field, bool inParams)
    {
        JsonObject owner = inParams ? parameters : rule;
        var gate = new ComboBox { ToolTip = Texts.Get("Rules.Editor.Gate.Tip") };
        gate.Items.Add(new ComboBoxItem { Content = Texts.Get("Rules.Editor.Gate.Any"), Tag = "Any" });
        gate.Items.Add(new ComboBoxItem { Content = Texts.Get("Rules.Editor.Gate.All"), Tag = "All" });
        gate.SelectedIndex = JsonValues.AsText(owner[field]) == "All" ? 1 : 0;
        gate.SelectionChanged += (_, _) => { owner[field] = (string)((ComboBoxItem)gate.SelectedItem).Tag; saved(""); };
        return gate;
    }

    // ---- fields bound to the rule's Params ----

    /// <summary>A text box for a setting; mono for names and values written in the game's files.</summary>
    private TextBox TextField(string key, string placeholder, bool mono)
    {
        var box = new TextBox { Text = JsonValues.AsText(parameters[key]), Tag = placeholder, ToolTip = TipOrNone(placeholder) };
        if (mono) box.SetResourceReference(FrameworkElement.StyleProperty, "MonoBox");
        FlexRow.SetGrow(box, 1);
        FlexRow.SetBasis(box, 144);
        box.TextChanged += (_, _) => { parameters[key] = box.Text; saved(TypingKey("p", key)); };
        return box;
    }

    /// <summary>A text box coloured as what it holds: a formula or a property name. A property name gets suggestions too.</summary>
    private TextBox Coloured(TextBox box, RuleTextKind kind)
    {
        Controls.ColouredTextBox.SetKind(box, kind);
        if (kind == RuleTextKind.Path) Suggestions.Attach(box, RuleSuggestions.Path(scan.Names, () => RuleSuggestions.VariablesOf(model)));
        return box;
    }

    /// <summary>A text box's placeholder as its tooltip, so it can still be read once the box has text; none when empty.</summary>
    private static string? TipOrNone(string placeholder) => placeholder == "" ? null : placeholder;

    /// <summary>A text box of several lines in the code font, for a setting.</summary>
    private TextBox TextArea(string key, string placeholder)
    {
        var box = new TextBox { Text = JsonValues.AsText(parameters[key]), Tag = placeholder, ToolTip = TipOrNone(placeholder) };
        box.SetResourceReference(FrameworkElement.StyleProperty, "TextArea");
        box.TextChanged += (_, _) => { parameters[key] = box.Text; saved(TypingKey("p", key)); };
        return box;
    }

    /// <summary>A dropdown for a setting; with <paramref name="rebuildAfter"/>, a choice changes what the window shows.</summary>
    private ComboBox Choice(string key, IReadOnlyList<(string Value, string Label)> options, bool rebuildAfter = false) =>
        Choice(key, [.. options.Select(o => new ChoiceOption(o.Value, o.Label))], rebuildAfter);

    /// <summary>A dropdown for a setting, whose entries may have a dimmed part (a file's folder).</summary>
    private ComboBox Choice(string key, IReadOnlyList<ChoiceOption> options, bool rebuildAfter)
    {
        var choice = new ComboBox { ItemsSource = options };
        choice.SetResourceReference(ItemsControl.ItemTemplateProperty, "DimmedChoice");
        // a value that is none of the choices shows the first, as a web page's dropdown does
        string current = JsonValues.AsText(parameters[key]);
        int at = options.ToList().FindIndex(o => o.Value == current);
        choice.SelectedIndex = Math.Max(0, at);
        choice.SelectionChanged += (_, _) =>
        {
            parameters[key] = ((ChoiceOption)choice.SelectedItem).Value;
            saved("");
            if (rebuildAfter) rebuild();
        };
        return choice;
    }

    /// <summary>A checkbox for a setting.</summary>
    private CheckBox Tick(string key, string label)
    {
        var tick = new CheckBox { Content = label, IsChecked = JsonValues.IsTruthy(parameters[key]), ToolTip = label };
        tick.SetResourceReference(FrameworkElement.StyleProperty, "Tick");
        tick.Click += (_, _) => { parameters[key] = tick.IsChecked == true; saved(""); };
        return tick;
    }

    /// <summary>"Pick from scan...": a name from the scanned file goes into a setting. None when the scan has no names.</summary>
    private ComboBox? Fill(string key, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return null;
        var fill = new ComboBox { ToolTip = Texts.Get("Rules.Editor.Fill.Tip") };
        fill.Items.Add(new ComboBoxItem { Content = Texts.Get("Rules.Editor.Fill"), Tag = "" });
        foreach (string name in names) fill.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        fill.SelectedIndex = 0;
        fill.SelectionChanged += (_, _) =>
        {
            if (fill.SelectedItem is not ComboBoxItem { Tag: string name } || name == "") return;
            parameters[key] = name;
            saved("");
            rebuild();
        };
        return fill;
    }

    /// <summary>The name of the box typed in, so keys typed into one box are one step to undo.</summary>
    private string TypingKey(string where, string field) =>
        "f|" + model.OpenPreset?.Name + "|" + model.CurrentRule + "|" + where + "||" + field;

    // ---- text ----

    /// <summary>.prow: a label, then its controls on a wrapping line, 3 pixels under it.</summary>
    private void AddRow(string label, params FrameworkElement?[] controls)
    {
        var row = new StackPanel();
        var text = Label(label);
        text.Margin = new Thickness(0, 0, 0, 3);
        row.Children.Add(text);
        var line = new FlexRow { Gap = 6, LineGap = 6 };
        foreach (FrameworkElement? control in controls)
        {
            if (control is null) continue;
            // a control without a tooltip of its own is named by its row's label, as the browser's labels do
            control.ToolTip ??= label;
            line.Children.Add(control);
        }
        row.Children.Add(line);
        parts.Add(row);
    }

    /// <summary>A label in the dim colour, with the colon the HTML app's style adds.</summary>
    private static TextBlock Label(string text) => new TextBlock { Text = text + ":" }.Themed(TextBlock.ForegroundProperty, "muted");

    /// <summary>.note: dim text, wrapped, made of plain, code and caution parts.</summary>
    private void AddNote(params Inline[] text)
    {
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap };
        note.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        note.Inlines.AddRange(text);
        parts.Add(note);
    }

    private static Run Plain(string text) => new(text);

    /// <summary>A note's text by name, its `code` in the code font and its **warnings** in the caution colour.</summary>
    private static Inline[] Note(string name, params (string Name, object? Value)[] blanks) =>
        Text.RichText.Pieces(Texts.Get(name, blanks), bold: Caution, code: Mono, plain: Plain);

    /// <summary>span.mono: code, in the code font at 12 pixels.</summary>
    private static Run Mono(string text)
    {
        // a web page does not break a line after a slash in a path; WPF would, unless a word joiner holds it
        var run = new Run(text.Replace("/", "/\u2060")) { FontSize = 12 };
        run.SetResourceReference(TextElement.FontFamilyProperty, "mono");
        return run;
    }

    /// <summary>span.caut: a warning, in the caution colour.</summary>
    private static Run Caution(string text)
    {
        var run = new Run(text);
        run.SetResourceReference(TextElement.ForegroundProperty, "caution");
        return run;
    }
}

/// <summary>What a scanned file gives the rule window's "Pick from scan..." dropdowns.</summary>
/// <param name="Names">The open preset's scanned file's property names.</param>
/// <param name="Titles">Its descriptors' names.</param>
/// <param name="Types">Its objects' types.</param>
/// <param name="SourceFiles">Every scanned file, as the choices for a Lookup rule's other file.</param>
/// <param name="SourceFileIsScanned">Whether the file a Lookup rule names is scanned.</param>
/// <param name="SourceNames">The property names of a Lookup rule's other file.</param>
public sealed record ScanLists(IReadOnlyList<string> Names, IReadOnlyList<string> Titles, IReadOnlyList<string> Types,
    IReadOnlyList<ChoiceOption> SourceFiles, bool SourceFileIsScanned, IReadOnlyList<string> SourceNames)
{
    /// <summary>No file scanned.</summary>
    public static readonly ScanLists None = new([], [], [], [], false, []);
}

/// <summary>A dropdown's entry: the value it stands for, its text, and a part shown dimmed after it ("" for none).</summary>
public sealed record ChoiceOption(string Value, string Label, string Dim = "")
{
    /// <summary>The dimmed part, after a space.</summary>
    public string DimText => Dim == "" ? "" : " " + Dim;
}

/// <summary>Small helpers for building the window in code.</summary>
internal static class ElementExtensions
{
    /// <summary>Gives a property a theme colour that follows the theme, and returns the element.</summary>
    public static T Themed<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}
