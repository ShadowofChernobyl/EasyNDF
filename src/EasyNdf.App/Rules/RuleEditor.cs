using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Controls;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.App.Text;

namespace EasyNdf.App.Rules;

/// <summary>
/// Builds what the rule window shows for a rule (the HTML app's <c>renderEditor</c>): its name and switch, then the
/// settings of its type, one method per type, and its lists of conditions and actions. Every change is written into the
/// rule straight away (the window saves it on Done); a change that alters what is shown (another scope, another
/// position) builds it again.
/// </summary>
public sealed class RuleEditor
{
    /// <summary>A list's starting height, 7.5rem; lists share what is left of the window's height.</summary>
    private const double ListHeight = 120;

    private readonly RulesModel model;
    private readonly ScanLists scan;
    private readonly Action changed;
    private readonly Action rebuild;
    private readonly List<UIElement> parts = [];
    private JsonObject rule = [];
    private JsonObject parameters = [];

    /// <summary>The lists of rows built, so the window can draw them again after a change.</summary>
    public List<RowListView> Lists { get; } = [];

    /// <summary>The rule's name box, for the tutorial to point at.</summary>
    internal TextBox? NameBox { get; private set; }

    /// <summary>The "Applies to" line, for the tutorial to point at; null for a rule type without it.</summary>
    internal FrameworkElement? ScopeRow { get; private set; }

    /// <summary>The conditions' Any / All dropdown, for the tutorial to point at.</summary>
    internal ComboBox? GateBox { get; private set; }

    /// <param name="model">The Rules tab's data.</param>
    /// <param name="scan">The property names, descriptor names and types of the scanned file, for the "Pick from scan..." dropdowns.</param>
    /// <param name="changed">Tells the window the rule was changed.</param>
    /// <param name="rebuild">Builds the window again, after a change that alters what it shows.</param>
    public RuleEditor(RulesModel model, ScanLists scan, Action changed, Action rebuild)
    {
        this.model = model;
        this.scan = scan;
        this.changed = changed;
        this.rebuild = rebuild;
    }

    /// <summary>The rule types that apply to one kind of object, or to the constants, as well as to descriptors.</summary>
    private static readonly HashSet<string> ScopedKinds = ["Edit", "Replace", "Scale", "Formula", "Check", "Remove"];

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
        ScopeRow = null;
        GateBox = null;

        string kind = KindOf(rule);
        // several rules as one: what a change does, and what "..." stands for
        if (model.IsConfiguringSeveral) AddNote(Note("Rules.Mass.Note", ("count", model.SeveralCount)));
        AddHeader();
        if (Mixed.Is(rule["Kind"]))
        {
            // rules of different types share only their name and switch
            AddNote(Note("Rules.Mass.Types"));
            return parts;
        }
        // (a Lookup rule has it too, under "Descriptors to edit in this file": BuildLookup)
        if (ScopedKinds.Contains(kind)) AddScope(kind);
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
            case "Text": BuildText(); break;
            case "File": BuildFile(); break;
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
        // "Variable name", as the rule's own name is in the box above it
        TextBox nameBox = TextField("VarName", Texts.Get("Rules.Editor.VarNameHint.ForExampleRangeMultiplier"), mono: true);
        // a data file gives the variables instead, one a row: the name and value boxes then have no say
        bool fromFile = JsonValues.AsText(parameters["DataFile"]).Trim() != "";
        nameBox.IsEnabled = !fromFile;
        AddRow(Texts.Get("Rules.Editor.VariableName"), nameBox);
        TextBox valueBox = TextField("Value", Texts.Get("Rules.Editor.ValueHint.ForExample2"), mono: true);
        valueBox.IsEnabled = !fromFile;
        AddRow(Texts.Get("Rules.Editor.Value"), valueBox);
        AddDataFileRow("Rules.Editor.DataFile.VariableNote");
        // where it can be used: its own rule set unless said otherwise; the note under it follows the choice
        AddRow(Texts.Get("Rules.Editor.Variable.Scope"), Choice("VarScope",
        [
            (Files.SharedVariables.RuleSetScope, Texts.Get("Rules.Editor.Variable.Scope.RuleSet")),
            (Files.SharedVariables.ModScope, Texts.Get("Rules.Editor.Variable.Scope.Mod")),
            (Files.SharedVariables.AllModsScope, Texts.Get("Rules.Editor.Variable.Scope.AllMods")),
        ], rebuildAfter: true));
        string scope = Files.SharedVariables.ScopeOf(rule);

        // a name another variable in reach has already: said at once, as nothing runs while two share a name
        var taken = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        taken.SetResourceReference(TextBlock.ForegroundProperty, "caution");
        void ShowWhetherTaken()
        {
            Files.SharedVariables.Definition? other = model.OpenPreset is Data.ModPreset ruleSet
                ? Files.SharedVariables.TakenBy(nameBox.Text, scope, model.OpenMod, ruleSet, rule, Mods.ModStack.Running)
                : null;
            taken.Visibility = other is null ? Visibility.Collapsed : Visibility.Visible;
            if (other is not null)
                taken.Text = Texts.Get("Rules.Editor.Variable.Taken", ("name", other.Name),
                    ("where", Texts.Get("Files.Run.Variables.Where", ("mod", other.Mod.Name), ("ruleSet", other.RuleSet.Name))));
        }
        nameBox.TextChanged += (_, _) => ShowWhetherTaken();
        ShowWhetherTaken();
        parts.Add(taken);

        string name = JsonValues.AsText(parameters["VarName"]);
        string noteKey = scope == Files.SharedVariables.ModScope ? "Rules.Editor.Variable.Note.Mod"
            : scope == Files.SharedVariables.AllModsScope ? "Rules.Editor.Variable.Note.AllMods" : "Rules.Editor.Variable.Note";
        AddNote(Note(noteKey, ("variable", "{" + (name != "" ? name : Texts.Get("Rules.Editor.Name")) + "}")));
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
        text.TextChanged += typed => { parameters["Text"] = typed; changed(); };
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
        TextBox formula = Coloured(TextField("Expression", Texts.Get("Rules.Editor.FormulaHint", ("sample", "round(0.8 * MaximumRangeGRU, 25)")), mono: true), RuleTextKind.Formula);
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
        AddDataFileRow("Rules.Editor.DataFile.TableNote");
        var table = TextArea("Text", "Name,MaxSpeedInKmph,FuelCapacity\nDescriptor_Unit_A,70,500\nDescriptor_Unit_B,,650");
        table.Height = 12 * 17.390625 + 8 + 2;
        // a data file gives the table instead: the box keeps its text, unused
        table.IsEnabled = JsonValues.AsText(parameters["DataFile"]).Trim() == "";
        parts.Add(table);
        AddNote(Note("Rules.Editor.Table.Note"));
        // a row is applied only to a descriptor that also meets these
        AddConditions(Texts.Get("Rules.Editor.ConditionsOptional"), Texts.Get("Rules.Editor.Table.NoConditions"));
    }

    private void BuildLookup()
    {
        // the scanned files, each stored by the shortest name no other answers to; the one the rule names keeps its name
        string sourceFile = JsonValues.AsText(parameters["SourceFile"]);
        var files = new List<ChoiceOption> { new("", Texts.Get(scan.SourceFiles.Count == 0 && scan.NewFunnel is not null ? "Rules.Editor.Lookup.NoneListed" : "Rules.Editor.Lookup.ChooseFile")) };
        files.AddRange(scan.SourceFiles);
        if (sourceFile != "" && !scan.SourceFileIsScanned && files.All(file => file.Value != sourceFile))
            files.Add(new(sourceFile, Texts.Get("Rules.Editor.Lookup.NotScanned", ("file", sourceFile))));
        AddNote(Note("Rules.Editor.Lookup.Note"));
        ComboBox otherFile = Choice("SourceFile", files, rebuildAfter: true);
        if (scan.NewFunnel is null) AddRow(Texts.Get("Rules.Editor.OtherFile"), otherFile);
        else
        {
            // the file filter's funnel joined to the dropdown's right, as on the Editor tab: the same filter
            Controls.FileFilterFunnel funnel = scan.NewFunnel();
            funnel.Margin = new Thickness(-1, 0, 0, 0);
            void Refill()
            {
                IReadOnlyList<ChoiceOption> listed = scan.SourceFilesNow?.Invoke() ?? scan.SourceFiles;
                // nothing passes the filter: the first line says so, in place of "(choose a file)" over an empty list
                var now = new List<ChoiceOption> { listed.Count == 0 ? new("", Texts.Get("Rules.Editor.Lookup.NoneListed")) : files[0] };
                now.AddRange(listed);
                string chosen = JsonValues.AsText(parameters["SourceFile"]);
                if (chosen != "" && now.All(file => file.Value != chosen) && files.FirstOrDefault(file => file.Value == chosen) is ChoiceOption kept) now.Add(kept);
                otherFile.Tag = Refilling;
                otherFile.ItemsSource = now;
                otherFile.SelectedIndex = Math.Max(0, now.FindIndex(file => file.Value == chosen));
                otherFile.Tag = null;
                funnel.ShowButton();
            }
            funnel.Changed += () => { Refill(); scan.FilterChanged?.Invoke(); };
            RefillFiles = Refill;
            var joined = new DockPanel { MinWidth = 218 };
            DockPanel.SetDock(funnel, Dock.Right);
            joined.Children.Add(funnel);
            joined.Children.Add(otherFile);
            otherFile.ToolTip ??= Texts.Get("Rules.Editor.OtherFile");
            AddRow(Texts.Get("Rules.Editor.OtherFile"), joined);
        }
        AddList(RulesModel.SourceConditions, Texts.Get("Rules.Editor.Lookup.SourceConditions"), Gate("SourceGate", inParams: true), Texts.Get("Rules.Editor.ACondition"), Texts.Get("Rules.Editor.NoConditions") + " " + Texts.Get("Rules.Editor.PressPlus"));
        AddRow(Texts.Get("Rules.Editor.CollectFromEach"), TextField("Collect", Texts.Get("Rules.Editor.CollectHint.DescriptorTitleOr"), mono: true), Fill("Collect", ["Descriptor Title", .. scan.SourceNames]));
        parts.Add(new TextBlock { Text = Texts.Get("Rules.Editor.Lookup.ToEdit"), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) }.Themed(TextBlock.ForegroundProperty, "accent"));
        // what is edited here: each descriptor, each object of one type, or the constants, as an Edit rule's
        AddScope("Lookup");
        AddRow(Texts.Get("Rules.Editor.WhereThisMatchesA"), TextField("MatchOn", Texts.Get("Rules.Editor.CollectHint.DescriptorTitleOr"), mono: true), Fill("MatchOn", ["Descriptor Title", .. scan.Names]));
        AddNote(Note("Rules.Editor.Lookup.ReferenceNote"));
        AddConditions(Texts.Get("Rules.Editor.ExtraConditionsInThis"), null);
        AddActions(Texts.Get("Rules.Editor.Actions"));
    }

    private void BuildText()
    {
        AddNote(Note("Rules.Editor.Text.Note"));
        // as tall as the Table rule's box: twelve lines of the code font
        var lines = TextArea(FileAndTextRules.LinesKey, "MYMOD_UNIT_NAME;Long Range Patrol\nMYMOD_WEAPON_NAME;81 mm Mortar");
        lines.Height = 12 * 17.390625 + 8 + 2;
        parts.Add(lines);
        int count = FileAndTextRules.LinesOf(JsonValues.AsText(parameters[FileAndTextRules.LinesKey])).Count;
        AddNote(Plain(Texts.Count("Rules.Editor.Text.Count", count)));
    }

    private void BuildFile()
    {
        AddNote(Note("Rules.Editor.File.Note"));
        var choose = new Button { Content = Texts.Get("Rules.Editor.File.Choose"), ToolTip = Texts.Get("Rules.Editor.File.Choose.Tip") };
        choose.Click += (_, _) => ChooseCarriedFile();
        AddRow(Texts.Get("Rules.Editor.File.File"), choose, CarriedFileText());
        var destination = TextField(FileAndTextRules.DestinationKey, Texts.Get("Rules.Editor.File.DestinationHint"), mono: true);
        AddRow(Texts.Get("Rules.Editor.File.Destination"), destination);
    }

    /// <summary>What the File rule carries: the chosen file's name and size, or that none is chosen, or that the app no longer has it.</summary>
    private TextBlock CarriedFileText()
    {
        string stored = JsonValues.AsText(parameters[FileAndTextRules.StoredKey]);
        string chosen = JsonValues.AsText(parameters[FileAndTextRules.ChosenNameKey]);
        long size = parameters[FileAndTextRules.SizeKey] is JsonValue value && value.TryGetValue(out long bytes) ? bytes : 0;
        string text = Mixed.Is(parameters[FileAndTextRules.StoredKey]) ? Mixed.Mark
            : stored == "" ? Texts.Get("Rules.Editor.File.NoneChosen")
            : AppData.Current.ModFileStore.PathOf(stored) is null ? Texts.Get("Rules.Editor.File.Missing", ("file", chosen))
            : Texts.Get("Rules.Editor.File.Chosen", ("file", chosen), ("size", SizeText(size)));
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, stored != "" && AppData.Current.ModFileStore.PathOf(stored) is null ? "caution" : "muted");
        return block;
    }

    /// <summary>A size as people read it: "820 KB", "7.7 MB".</summary>
    private static string SizeText(long bytes) =>
        bytes >= 1024 * 1024 ? (bytes / 1024.0 / 1024.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " MB"
        : Math.Max(1, (bytes + 1023) / 1024) + " KB";

    /// <summary>
    /// "Choose File...": the file is kept in the app's data (a later change to the chosen file does not change the
    /// mod), and a rule with no place yet gets one under GameData named after it.
    /// </summary>
    private void ChooseCarriedFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Texts.Get("Rules.Editor.File.Choose.Title") };
        if (dialog.ShowDialog(parts.Count > 0 ? Window.GetWindow(parts[0]) : null) != true) return;
        byte[] content;
        string stored;
        try
        {
            content = System.IO.File.ReadAllBytes(dialog.FileName);
            stored = AppData.Current.ModFileStore.Keep(content, dialog.FileName);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            parameters[FileAndTextRules.StoredKey] = "";
            parameters[FileAndTextRules.ChosenNameKey] = System.IO.Path.GetFileName(dialog.FileName) + " (" + error.Message + ")";
            changed();
            rebuild();
            return;
        }
        string name = System.IO.Path.GetFileName(dialog.FileName);
        parameters[FileAndTextRules.StoredKey] = stored;
        parameters[FileAndTextRules.ChosenNameKey] = name;
        parameters[FileAndTextRules.SizeKey] = content.LongLength;
        if (JsonValues.AsText(parameters[FileAndTextRules.DestinationKey]).Trim() == "") parameters[FileAndTextRules.DestinationKey] = "GameData/" + name;
        changed();
        rebuild();
    }

    private void BuildCheck()
    {
        AddNote(Note("Rules.Editor.Check.Note"));
        var expect = new List<FrameworkElement?> { Choice("Expect", [("atLeast", Texts.Get("Rules.Editor.Choice.AtLeast.AtLeastThis")), ("exactly", Texts.Get("Rules.Editor.Choice.Exactly.ExactlyThisMany")), ("atMost", Texts.Get("Rules.Editor.Choice.AtMost.AtMostThis")), ("none", Texts.Get("Rules.Editor.Choice.None.NoneAtAll"))], rebuildAfter: true) };
        if (JsonValues.AsText(parameters["Expect"]) != "none") expect.Add(TextField("Count", Texts.Get("Rules.Editor.ValueHint.Number"), mono: true));
        AddRow(Texts.Get("Rules.Editor.Expect"), [.. expect]);
        AddRow(Texts.Get("Rules.Editor.MessageWhenItFails"), TextField("Message", Texts.Get("Rules.Editor.MessageHint.OptionalNoteTo"), mono: false));
        CheckBox stop = Tick("StopExport", Texts.Get("Rules.Editor.StopExport"));
        stop.ToolTip = Texts.Get("Rules.Editor.StopExport.Tip");
        AddRow(Texts.Get("Rules.Editor.WhenItFails"), stop);
        AddConditions(Texts.Get("Rules.Editor.ConditionsOptional"), EveryDescriptor);
    }

    // ---- the parts every rule has ----

    /// <summary>.rhead: "Name:" with the Enabled switch at the right, and the name under them.</summary>
    private void AddHeader()
    {
        var enabled = new CheckBox { Content = Texts.Get("Rules.Editor.Enabled"), IsChecked = Mixed.Is(rule["Enabled"]) ? null : !JsonValues.IsFalse(rule["Enabled"]), ToolTip = Texts.Get("Rules.Editor.Enabled.Tip") };
        enabled.SetResourceReference(FrameworkElement.StyleProperty, "Toggle");
        enabled.Click += (_, _) => { rule["Enabled"] = enabled.IsChecked == true; changed(); };
        var label = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
        DockPanel.SetDock(enabled, Dock.Right);
        label.Children.Add(enabled);
        label.Children.Add(Label(Texts.Get("Rules.Editor.Name")));

        var name = new TextBox { Text = JsonValues.AsText(rule["Name"]), Tag = Texts.Get("Rules.Editor.RuleName.Hint"), ToolTip = null };
        name.TextChanged += (_, _) => { rule["Name"] = name.Text; changed(); };
        NameBox = name;
        var header = new StackPanel();
        header.Children.Add(label);
        header.Children.Add(name);
        parts.Add(header);
    }

    /// <summary>.rscope: what the rule applies to: each descriptor, each object of one type, or the top-level constants.</summary>
    private void AddScope(string kind)
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
        ScopeRow = row;
        if (scope == "type")
            AddNote(Note(kind == "Remove" ? "Rules.Editor.Scope.RemoveTypeNote" : "Rules.Editor.Scope.TypeNote"));
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
        if (Mixed.Is(owner[field]))
        {
            gate.Items.Add(new ComboBoxItem { Content = Mixed.Mark, Tag = Mixed.Mark });
            gate.SelectedIndex = 2;
        }
        gate.SelectionChanged += (_, _) => { owner[field] = (string)((ComboBoxItem)gate.SelectedItem).Tag; changed(); };
        GateBox ??= gate;
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
        box.TextChanged += (_, _) => { parameters[key] = box.Text; changed(); };
        return box;
    }

    /// <summary>
    /// "Read from a file": a data file (.csv, .tsv or .txt) the rule reads at every run instead of its own text, typed
    /// or chosen with Browse…, and a note on what it holds. A file inside the Mods folder is kept by its path below it.
    /// </summary>
    private void AddDataFileRow(string noteKey)
    {
        TextBox file = TextField("DataFile", Texts.Get("Rules.Editor.DataFile.Hint"), mono: true);
        string shown = file.Text;
        file.LostKeyboardFocus += (_, _) => { if (file.Text != shown) rebuild(); };
        var browse = new Button { Content = Texts.Get("Rules.Editor.DataFile.Browse"), ToolTip = Texts.Get("Rules.Editor.DataFile.Browse.Tip") };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = Texts.Get("Rules.Editor.DataFile.Title"), Filter = Texts.Get("Rules.Editor.DataFile.Filter") + "|*.csv;*.tsv;*.txt" };
            if (dialog.ShowDialog(Window.GetWindow(browse)) != true) return;
            parameters["DataFile"] = Files.DataFiles.PathToKeep(dialog.FileName);
            changed();
            rebuild();
        };
        AddRow(Texts.Get("Rules.Editor.DataFile"), file, browse);
        string path = shown.Trim();
        AddNote(Note(path != "" && Files.DataFiles.Read(path) is null ? "Rules.Editor.DataFile.NotRead" : noteKey));
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
        // the lines shown while the box is empty are an example: its tooltip says so, on a line of its own above them
        var box = new TextBox { Text = JsonValues.AsText(parameters[key]), Tag = placeholder, ToolTip = placeholder == "" ? null : Texts.Get("Rules.Editor.Example", ("sample", placeholder)) };
        box.SetResourceReference(FrameworkElement.StyleProperty, "TextArea");
        box.TextChanged += (_, _) => { parameters[key] = box.Text; changed(); };
        return box;
    }

    /// <summary>A dropdown for a setting; with <paramref name="rebuildAfter"/>, a choice changes what the window shows.</summary>
    private ComboBox Choice(string key, IReadOnlyList<(string Value, string Label)> options, bool rebuildAfter = false) =>
        Choice(key, [.. options.Select(o => new ChoiceOption(o.Value, o.Label))], rebuildAfter);

    /// <summary>Marks a dropdown whose choices are being listed again, in its Tag.</summary>
    private static readonly object Refilling = new();

    /// <summary>Lists a Lookup rule's other file's choices again, after the file filter changed elsewhere; null without one.</summary>
    public Action? RefillFiles { get; private set; }

    /// <summary>A dropdown for a setting, whose entries may have a dimmed part (a file's folder).</summary>
    private ComboBox Choice(string key, IReadOnlyList<ChoiceOption> options, bool rebuildAfter)
    {
        // a long choice (a file's path) is cut with "…", named whole in the tooltip, and its list stays inside the window
        ComboBox choice = NarrowChoices.OfOptions(new ComboBox { ItemsSource = options }, tipShowsChoice: true);
        // a value that is none of the choices shows the first, as a web page's dropdown does
        string current = JsonValues.AsText(parameters[key]);
        // several rules that do not share the choice: "..." until one is chosen for them all
        if (Mixed.Is(parameters[key]))
        {
            options = [new ChoiceOption(Mixed.Mark, Mixed.Mark), .. options];
            choice.ItemsSource = options;
        }
        int at = options.ToList().FindIndex(o => o.Value == current);
        choice.SelectedIndex = Math.Max(0, at);
        choice.SelectionChanged += (_, _) =>
        {
            // its choices are being listed again (the file filter changed): not the user's choice
            if (ReferenceEquals(choice.Tag, Refilling) || choice.SelectedItem is not ChoiceOption) return;
            parameters[key] = ((ChoiceOption)choice.SelectedItem).Value;
            changed();
            if (rebuildAfter) rebuild();
        };
        return choice;
    }

    /// <summary>A checkbox for a setting.</summary>
    private CheckBox Tick(string key, string label)
    {
        var tick = new CheckBox { Content = label, IsChecked = Mixed.Checked(parameters[key]), ToolTip = label };
        tick.SetResourceReference(FrameworkElement.StyleProperty, "Tick");
        tick.Click += (_, _) => { parameters[key] = tick.IsChecked == true; changed(); };
        return tick;
    }

    /// <summary>"Pick from scan...": a name from the scanned file goes into a setting. None when the scan has no names.</summary>
    private ComboBox? Fill(string key, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return null;
        var fill = new ComboBox { ToolTip = Texts.Get("Rules.Editor.Fill.Tip") };
        fill.Items.Add(new ComboBoxItem { Content = Texts.Get("Rules.Editor.Fill"), Tag = "" });
        foreach (string name in names) fill.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        NarrowChoices.OfItems(fill);
        fill.SelectedIndex = 0;
        fill.SelectionChanged += (_, _) =>
        {
            if (fill.SelectedItem is not ComboBoxItem { Tag: string name } || name == "") return;
            parameters[key] = name;
            changed();
            rebuild();
        };
        return fill;
    }

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
    private static TextBlock Label(string text) => new TextBlock { Text = Texts.WithColon(text) }.Themed(TextBlock.ForegroundProperty, "muted");

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
/// <param name="NewFunnel">Makes the file filter's funnel for the other file's dropdown; null for none.</param>
/// <param name="SourceFilesNow">The other file's choices again, after the file filter changed.</param>
/// <param name="FilterChanged">Tells the window the file filter changed in the editor's funnel.</param>
public sealed record ScanLists(IReadOnlyList<string> Names, IReadOnlyList<string> Titles, IReadOnlyList<string> Types,
    IReadOnlyList<ChoiceOption> SourceFiles, bool SourceFileIsScanned, IReadOnlyList<string> SourceNames,
    Func<Controls.FileFilterFunnel>? NewFunnel = null, Func<IReadOnlyList<ChoiceOption>>? SourceFilesNow = null, Action? FilterChanged = null)
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
