using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using EasyNdf.App.Rules;
using EasyNdf.Data;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.App.Text;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The condition or action window (the HTML app's <c>rowOpen</c>, <c>rowDraw</c> and <c>rowClose</c>): one row of the
/// open rule. Every change is written into the row straight away; the rule window saves the rule on its Done. The
/// window's × or Escape puts the row back as it was when the window opened, after asking.
/// </summary>
public partial class RowWindow : UserControl
{
    /// <summary>The operators a condition can have, and those an action can have.</summary>
    public static readonly string[] ConditionOperators =
        ["Starts With", "Ends With", "Contains", "Does Not Contain", "Is Equal To", "Is Not Equal To", "Is Less Than", "Is Less Than Or Equal To",
         "Is Greater Than", "Is Greater Than Or Equal To", "Contains Any Of", "Contains All Of"];
    public static readonly string[] ActionOperators =
        ["Add [int, float]", "Subtract [int, float]", "Multiply [int, float]", "Divide [int, float]", "Set To [int, float, string, boolean]",
         "Set To [list, object]", "Append [string]", "Prepend [string]", "Insert [array]", "Replace [array]", "Remove [array]", "Add Property", "Remove Property"];

    /// <summary>The line height of the code font, for the Value box's lines.</summary>
    private const double CodeLineHeight = 17.390625;

    private RulesModel? model;
    private Func<string, IReadOnlyList<string>> namesFor = _ => [];
    private ScanLists scan = ScanLists.None;
    private string list = RulesModel.Conditions;

    /// <summary>The list the window shows a row of (conditions or actions), for the tutorial.</summary>
    internal string List => list;
    private int index;
    private JsonObject? row;

    /// <summary>The row as it was when the window opened on it, for Cancel.</summary>
    private JsonObject? rowBeforeEditing;
    private JsonObject? ruleOfRow;
    private TextBox? commentText;

    /// <summary>
    /// The rows the window is open on as one (see <see cref="Mixed"/>); none when on a single row. The row shown is then
    /// one made of them, and what was changed in it is written into each when the window closes.
    /// </summary>
    private List<JsonObject> severalRows = [];

    private bool IsSeveral => severalRows.Count > 0;

    /// <summary>Builds the window, closed.</summary>
    public RowWindow()
    {
        InitializeComponent();
        Form.PreviewKeyDown += FormKeyDown;
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>A box or dropdown of the row by its field ("Value", "Operand", "Target", "Operator"), for the tutorial.</summary>
    internal FrameworkElement? Field(string field) =>
        Form.Children.OfType<FrameworkElement>().SelectMany(part => part is Panel inner ? [part, .. inner.Children.OfType<FrameworkElement>()] : new[] { part })
            .FirstOrDefault(part => part.Name == field);

    /// <summary>Raised when the window opens or closes, so the rule window can move aside or back.</summary>
    public event Action<bool>? OpenChanged;

    /// <summary>Opens the window on a row of a list of the open rule; the row becomes the list's selection.</summary>
    /// <param name="rules">The Rules tab's data.</param>
    /// <param name="rowList">Which list.</param>
    /// <param name="place">The row's place in it.</param>
    /// <param name="names">The scanned property names that fill the Operand or Target dropdown, for a list.</param>
    /// <param name="scanned">The scanned descriptor names and types, suggested in a condition's Value; none when left out.</param>
    public void Open(RulesModel rules, string rowList, int place, Func<string, IReadOnlyList<string>> names, ScanLists? scanned = null)
    {
        KeepSeveral();
        scan = scanned ?? ScanLists.None;
        if (rules.RowsOf(rowList) is not JsonArray rows || place < 0 || place >= rows.Count) return;
        if (!ReferenceEquals(model, rules))
        {
            if (model is not null) model.PropertyChanged -= ModelChanged;
            model = rules;
            model.PropertyChanged += ModelChanged;
        }
        namesFor = names;
        list = rowList;
        index = place;
        row = rows[place] as JsonObject;
        rowBeforeEditing = row?.DeepClone() as JsonObject;
        ruleOfRow = rules.OpenRule;
        bool wasOpen = IsOpen;
        Visibility = Visibility.Visible;
        if (!wasOpen) OpenChanged?.Invoke(true);
        rules.SelectOnlyRow(rowList, place);
        rules.ActiveRowList = rowList;
        Draw();
        rules.Refresh();
        // the name takes the keys, all of it selected while the row has none
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (Form.Children.OfType<TextBox>().FirstOrDefault() is TextBox name) { name.Focus(); if (name.Text == "") name.SelectAll(); }
        });
    }

    /// <summary>Opens the window on several rows of a list of the open rule, as one row.</summary>
    /// <param name="rules">The Rules tab's data.</param>
    /// <param name="rowList">Which list.</param>
    /// <param name="places">The rows' places in it.</param>
    /// <param name="names">The scanned property names that fill the Operand or Target dropdown, for a list.</param>
    /// <param name="scanned">The scanned descriptor names and types, suggested in a condition's Value; none when left out.</param>
    public void OpenSeveral(RulesModel rules, string rowList, IReadOnlyList<int> places, Func<string, IReadOnlyList<string>> names, ScanLists? scanned = null)
    {
        KeepSeveral();
        if (rules.RowsOf(rowList) is not JsonArray rows) return;
        List<JsonObject> picked = [.. places.Where(place => place >= 0 && place < rows.Count).Select(place => rows[place]).OfType<JsonObject>()];
        if (picked.Count < 2) return;
        scan = scanned ?? ScanLists.None;
        if (!ReferenceEquals(model, rules))
        {
            if (model is not null) model.PropertyChanged -= ModelChanged;
            model = rules;
            model.PropertyChanged += ModelChanged;
        }
        namesFor = names;
        list = rowList;
        index = places[0];
        severalRows = picked;
        row = Mixed.Merge(picked);
        rowBeforeEditing = row.DeepClone() as JsonObject;
        ruleOfRow = rules.OpenRule;
        bool wasOpen = IsOpen;
        Visibility = Visibility.Visible;
        if (!wasOpen) OpenChanged?.Invoke(true);
        rules.ActiveRowList = rowList;
        Draw();
        rules.Refresh();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (Form.Children.OfType<TextBox>().FirstOrDefault() is TextBox name) name.Focus();
        });
    }

    /// <summary>Several rows as one: what was changed is written into each of them.</summary>
    private void KeepSeveral()
    {
        if (!IsSeveral) return;
        List<JsonObject> rows = severalRows;
        severalRows = [];
        if (row is null || rowBeforeEditing is null || JsonNode.DeepEquals(row, rowBeforeEditing)) return;
        Mixed.Apply(rowBeforeEditing, row, rows);
        model?.SaveRules();
    }

    /// <summary>Done: closes the window, keeping its changes; its list keeps the keys.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        KeepSeveral();
        Visibility = Visibility.Collapsed;
        row = null;
        rowBeforeEditing = null;
        OpenChanged?.Invoke(false);
        if (model is not null)
        {
            model.ActiveRowList = list;
            model.Refresh();
        }
    }

    /// <summary>×, Escape or a click outside: puts the row back as it was when the window opened, asking first, and closes the window.</summary>
    public async Task Cancel()
    {
        if (!IsOpen) return;
        if (row is not null && rowBeforeEditing is not null && !JsonNode.DeepEquals(row, rowBeforeEditing))
        {
            if (!await MainView.Of(this).Messages.Confirm(Texts.Get("Rules.DiscardChanges"), Texts.Get("Rules.Discard"), DialogIcon.Warning)) return;
            RulesModel.PutBack(row, rowBeforeEditing);
        }
        Close();
    }

    /// <summary>The window sits right of the middle, as far from it as the rule window is left of it (see <see cref="RuleWindow.BesideGap"/>).</summary>
    private void Place()
    {
        Box.Margin = new Thickness(0, 0, RuleWindow.BesideGap(ActualWidth), 0);
        // 30rem wide and at most 92% of the app tall, or less in a small window, so its Done button always shows
        if (ActualWidth > 0) Box.Width = Math.Max(200, Math.Min(480, ActualWidth - 20));
        if (ActualHeight > 0) Box.MaxHeight = Math.Max(160, ActualHeight * 0.92);
    }

    /// <summary>
    /// The Rules tab's data changed (Undo, a row moved or deleted in the rule window): the window stays on its row, or
    /// on the same place when Undo brought back copies of the rows; when the row is gone, the window closes.
    /// </summary>
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsOpen || model is null) return;
        if (IsSeveral)
        {
            // the window stays while every one of its rows is still in the list
            if (model.RowsOf(list) is JsonArray still && severalRows.All(each => IndexIn(still, each) >= 0)) return;
            severalRows = [];
            Close();
            return;
        }
        JsonArray? rows = model.RowsOf(list);
        int found = rows is null || row is null ? -1 : IndexIn(rows, row);
        bool otherRule = !ReferenceEquals(model.OpenRule, ruleOfRow);
        if (found < 0 && otherRule && rows is not null && index < rows.Count) found = index;
        if (found < 0 || rows is null) { Close(); return; }
        bool sameRow = ReferenceEquals(rows[found], row);
        index = found;
        if (sameRow) { ShowPosition(); return; }
        row = rows[found] as JsonObject;
        ruleOfRow = model.OpenRule;
        model.SelectOnlyRow(list, found);
        Draw();
    }

    private static int IndexIn(JsonArray rows, JsonNode item)
    {
        for (int i = 0; i < rows.Count; i++) if (ReferenceEquals(rows[i], item)) return i;
        return -1;
    }

    /// <summary>"1 of 3", and ‹ › only where there is a row to go to.</summary>
    private void ShowPosition()
    {
        if (IsSeveral)
        {
            Position.Text = RulesModel.CountRows(list, severalRows.Count);
            // the row its words are in is told they grew (see SnappedRow)
            (Position.Parent as UIElement)?.InvalidateMeasure();
            PreviousButton.IsEnabled = NextButton.IsEnabled = false;
            return;
        }
        int count = model?.RowsOf(list)?.Count ?? 0;
        Position.Text = Texts.Get("Rules.Row.Position", ("number", index + 1), ("count", count));
        (Position.Parent as UIElement)?.InvalidateMeasure();
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index < count - 1;
    }

    /// <summary>Draws the row's boxes (the HTML app's <c>rowDraw</c>), keeping the keys in the box that had them.</summary>
    private void Draw()
    {
        if (row is null) return;
        // the boxes are named after the row's fields, so the one that had the keys gets them back
        string? focused = (Keyboard.FocusedElement as FrameworkElement)?.Name;
        bool isAction = list == RulesModel.Actions;
        Title.Text = isAction ? Texts.Get("Rules.Row.Action") : Texts.Get("Rules.Row.Condition");
        Controls.QuickHelp.SetPage(HelpButton, isAction ? "actions" : "conditions");
        ShowPosition();
        Form.Children.Clear();
        commentText = null;
        if (IsSeveral || Mixed.Has(row))
        {
            // several rows as one: what a change does, and what "..." stands for
            var several = new TextBlock { Margin = new Thickness(0, 0, 0, 9) };
            several.SetResourceReference(StyleProperty, "Note");
            several.Inlines.AddRange(Text.RichText.Pieces(Texts.Get("Rules.Mass.Rows.Note"), bold: text => new Run(text), code: CodeRun, plain: text => new Run(text)));
            Form.Children.Add(several);
        }

        AddLabel(Texts.Get("Rules.Row.Name"), first: true);
        AddText("Name", Texts.Get("Editor.Filter.NameBox.Hint"), mono: false);

        string subject = isAction ? "Target" : "Operand", custom = isAction ? "CustomTarget" : "CustomOperand";
        var customTick = new CheckBox { Content = Texts.Get("Rules.Row.Custom"), IsChecked = Mixed.Checked(row["ManualOverride"]), ToolTip = Texts.Get("Rules.Row.Custom.Tip"), Name = "ManualOverride" };
        customTick.SetResourceReference(StyleProperty, "Tick");
        customTick.Click += (_, _) => CustomClicked(customTick.IsChecked == true);
        AddLabel(isAction ? Texts.Get("Rules.Row.Target") : Texts.Get("Rules.Row.Operand"), right: customTick);
        if (Mixed.IsOn(row["ManualOverride"]))
        {
            TextBox customBox = AddText(custom, isAction ? Texts.Get("Rules.Row.CustomTarget") : Texts.Get("Rules.Row.CustomOperand"), mono: true);
            Controls.ColouredTextBox.SetKind(customBox, RuleTextKind.Path);
            Controls.Suggestions.Attach(customBox, RuleSuggestions.Path(namesFor(list), Variables));
        }
        else
        {
            IEnumerable<string> special = isAction ? RuleEditor.SpecialOperands.Skip(1) : RuleEditor.SpecialOperands;
            // an Edit rule's action can be aimed at a variable in reach, to change it for the rules after
            IEnumerable<string> variables = isAction && AimsAtVariables ? Variables().Select(variable => "{" + variable.Text + "}") : [];
            AddChoice(subject, [.. special, .. variables, .. namesFor(list)], mono: true, notInListSuffix: " " + Texts.Get("Rules.Row.NotInScan"));
        }

        AddLabel(Texts.Get("Rules.Row.Operator"));
        AddChoice("Operator", isAction ? ActionOperators : ConditionOperators, mono: false, notInListSuffix: null);

        AddLabel(Texts.Get("Rules.Row.Value"), tip: Texts.Get("Rules.Row.Value.Tip"));
        AddValueBox();

        if (!isAction)
        {
            // "any of these, but always this one": needed even when the rule's conditions are matched by Any
            var must = new CheckBox { Content = Texts.Get("Rules.Row.MustBeTrue"), IsChecked = Mixed.Checked(row["MustBeTrue"]), ToolTip = Texts.Get("Rules.Row.MustBeTrue.Tip"), Name = "MustBeTrue",
                Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            must.SetResourceReference(StyleProperty, "Toggle");
            must.Click += (_, _) => Changed("MustBeTrue", must.IsChecked == true);
            Form.Children.Add(must);
        }
        if (isAction && VariableAimedAt() is not null)
        {
            // once for the rule unless ticked: "Multiply 1.5" on a rule that matches 300 units would else be done 300 times
            var perMatch = new CheckBox { Content = Texts.Get("Rules.Row.PerMatch"), IsChecked = Mixed.Checked(row["PerMatch"]), ToolTip = Texts.Get("Rules.Row.PerMatch.Tip"), Name = "PerMatch", Margin = new Thickness(0, 9, 0, 0) };
            perMatch.SetResourceReference(StyleProperty, "Tick");
            perMatch.Click += (_, _) => Changed("PerMatch", perMatch.IsChecked == true);
            Form.Children.Add(perMatch);
        }
        if (isAction) AddComment();
        if (focused is { Length: > 0 } && Form.Children.OfType<FrameworkElement>().Concat(Form.Children.OfType<Panel>().SelectMany(p => p.Children.OfType<FrameworkElement>()))
                .FirstOrDefault(c => c.Name == focused) is FrameworkElement again)
            again.Focus();
    }

    /// <summary>.lab: a label in the dim colour, 6 pixels further down than the rest (none for the first), with an extra control at its right.</summary>
    private void AddLabel(string text, bool first = false, FrameworkElement? right = null, string? tip = null)
    {
        var line = new DockPanel { Margin = new Thickness(0, first ? 0 : 9, 0, 0), ToolTip = tip };
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            line.Children.Add(right);
        }
        line.Children.Add(new TextBlock { Text = Texts.WithColon(text) }.Themed(TextBlock.ForegroundProperty, "muted"));
        Form.Children.Add(line);
    }

    /// <summary>A text box for one of the row's fields, 3 pixels under its label.</summary>
    private TextBox AddText(string field, string placeholder, bool mono)
    {
        var box = new TextBox { Text = JsonValues.AsText(row![field]), Tag = placeholder, ToolTip = placeholder, Name = field, Margin = new Thickness(0, 3, 0, 0) };
        if (mono) box.SetResourceReference(StyleProperty, "MonoBox");
        box.TextChanged += (_, _) => Changed(field, box.Text);
        Form.Children.Add(box);
        return box;
    }

    /// <summary>The Value box: the code font, as many lines as the value has, up to 8; Shift+Enter adds one.</summary>
    private void AddValueBox()
    {
        var box = new TextBox { Text = JsonValues.AsText(row!["Value"]), Name = "Value", Margin = new Thickness(0, 3, 0, 0), ToolTip = Texts.Get("Rules.Row.Value.Tip") };
        box.SetResourceReference(StyleProperty, "TextArea");
        box.TextWrapping = TextWrapping.NoWrap;
        Controls.ColouredTextBox.SetKind(box, RuleTextKind.Value);
        JsonObject shownRow = row!;
        Controls.Suggestions.Attach(box, RuleSuggestions.Value(list == RulesModel.Actions,
            operand: () => JsonValues.AsText(shownRow[JsonValues.IsTruthy(shownRow["ManualOverride"]) ? "CustomOperand" : "Operand"]),
            operation: () => JsonValues.AsText(shownRow["Operator"]), scan, Variables));
        void FitLines()
        {
            double lines = Math.Min(8, box.Text.Split('\n').Length) * CodeLineHeight + 10;
            box.Height = lines;
            // a sideways scrollbar takes the last line's place: once the box has laid its text out and shows one, the
            // box is made taller by the bar, so every line stays in sight above it
            box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (box.Template?.FindName("PART_ContentHost", box) is ScrollViewer host && host.ComputedHorizontalScrollBarVisibility == Visibility.Visible
                    && Math.Abs(box.Height - lines) < 0.5)
                    box.Height = lines + 14;
            });
        }
        FitLines();
        box.TextChanged += (_, _) =>
        {
            FitLines();
            Changed("Value", box.Text);
        };
        Form.Children.Add(box);
    }

    /// <summary>
    /// A dropdown for one of the row's fields. A value that is none of the choices is listed first: "(choose)" when it
    /// is empty, or the value itself, marked when it is not in the scan.
    /// </summary>
    private void AddChoice(string field, IReadOnlyList<string> choices, bool mono, string? notInListSuffix)
    {
        string current = JsonValues.AsText(row![field]);
        var options = new List<ChoiceOption>();
        if (current == "") options.Add(new ChoiceOption("", Texts.Get("Rules.Row.Choose")));
        // rows that do not share the choice: "..." until one is chosen for them all
        else if (Mixed.Is(row[field])) options.Add(new ChoiceOption(Mixed.Mark, Mixed.Mark));
        else if (!choices.Contains(current))
            options.Add(notInListSuffix is null ? new ChoiceOption("", Texts.Get("Rules.Row.Choose")) : new ChoiceOption(current, current + notInListSuffix));
        options.AddRange(choices.Select(c => new ChoiceOption(c, c)));
        // the operator is named by its label; a property shows its own name, so a name too long for the box can be read in full
        var choice = new ComboBox { ItemsSource = options, Name = field, Margin = new Thickness(0, 3, 0, 0), ToolTip = field == "Operator" ? Texts.Get("Rules.Row.Operator") : NameOrNone(current) };
        // typing a name's first letters goes to it, in a list of a file's hundred properties
        TextSearch.SetTextPath(choice, nameof(ChoiceOption.Label));
        // a long property name is cut with "…" in the box and its list, which stays inside the window
        NarrowChoices.OfOptions(choice);
        if (mono)
        {
            choice.SetResourceReference(FontFamilyProperty, "mono");
            choice.FontSize = 12;
            choice.SetValue(TextBlock.LineHeightProperty, CodeLineHeight);
        }
        int at = options.FindIndex(o => o.Value == current);
        choice.SelectedIndex = Math.Max(0, at);
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is not ChoiceOption picked) return;
            if (field != "Operator") choice.ToolTip = NameOrNone(picked.Value);
            Changed(field, picked.Value);
        };
        Form.Children.Add(choice);
    }

    /// <summary>An action's comment: on or off; when on, its text and what it includes.</summary>
    private void AddComment()
    {
        if (row!["Comment"] is not JsonObject comment) row["Comment"] = comment = RuleLoading.NewComment();
        CheckBox on = CommentTick(comment, "Enabled", new Run(Texts.Get("Rules.Row.Comment")));
        on.Margin = new Thickness(0, 13, 0, 0);
        on.HorizontalAlignment = HorizontalAlignment.Left;
        Form.Children.Add(on);
        if (Mixed.IsOn(comment["Enabled"]))
        {
            commentText = new TextBox { Text = JsonValues.AsText(comment["Text"]), Name = "CommentText", Margin = new Thickness(0, 3, 0, 0) };
            ShowAutomaticComment();
            commentText.TextChanged += (_, _) => CommentChanged(comment, "Text", commentText.Text);
            Form.Children.Add(commentText);

            // .cmt: what the comment includes, 16 pixels in, wrapping with 6 by 14 pixels between
            string mod = model?.OpenMod.Name ?? "";
            var mark = new Run(mod);
            mark.SetResourceReference(TextElement.ForegroundProperty, "accent");
            var includes = new Controls.FlexRow { Gap = 14, LineGap = 6, Margin = new Thickness(16, 3, 0, 8) };
            // "Edited by" and the mod's name in the accent colour, as one text a translator can reorder
            CheckBox editedBy = CommentTick(comment, "IncludeMod", Text.RichText.Pieces(Texts.Get("Rules.Row.EditedBy", ("mod", mod)),
                bold: name => { var run = new Run(name); run.SetResourceReference(TextElement.ForegroundProperty, "accent"); return run; }, code: name => new Run(name), plain: text => new Run(text)));
            editedBy.ToolTip = Texts.Get("Rules.Row.EditedBy.Tip", ("mod", mod));
            includes.Children.Add(editedBy);
            includes.Children.Add(CommentTick(comment, "IncludeOriginalValue", new Run(Texts.Get("Rules.Row.OriginalValue"))));
            includes.Children.Add(CommentTick(comment, "IncludeTimestamp", new Run(Texts.Get("Rules.Row.Timestamp"))));
            Form.Children.Add(includes);
        }
        // .note: 10 pixels below what is above it, besides the 3 between every part
        var note = new TextBlock { Margin = new Thickness(0, 13, 0, 0), Name = "Note" };
        note.SetResourceReference(StyleProperty, "Note");
        note.Inlines.AddRange(Text.RichText.Pieces(Texts.Get("Rules.Row.ActionNote"),
            bold: text => new Run(text), code: CodeRun, plain: text => new Run(text)));
        Form.Children.Add(note);
    }

    /// <summary>The tooltips of the comment's checkboxes ("Edited by" names the mod, so it gets its own).</summary>
    private static Dictionary<string, string> CommentTips => new()
    {
        ["Enabled"] = Texts.Get("Rules.Row.Comment.Tip"),
        ["IncludeOriginalValue"] = Texts.Get("Rules.Row.OriginalValue.Tip"),
        ["IncludeTimestamp"] = Texts.Get("Rules.Row.Timestamp.Tip"),
    };

    /// <summary>A property's name as a tooltip; none while nothing is chosen.</summary>
    private static string? NameOrNone(string name) => name == "" ? null : name;

    /// <summary>Code in a note, in the code font at 12 pixels.</summary>
    private static Run CodeRun(string text)
    {
        var run = new Run(text) { FontSize = 12 };
        run.SetResourceReference(TextElement.FontFamilyProperty, "mono");
        return run;
    }

    private CheckBox CommentTick(JsonObject comment, string field, params Inline[] label)
    {
        var text = new TextBlock();
        text.Inlines.AddRange(label);
        var tick = new CheckBox { Content = text, IsChecked = Mixed.Checked(comment[field]), Name = "Comment" + field, ToolTip = CommentTips.GetValueOrDefault(field) };
        tick.SetResourceReference(StyleProperty, "Tick");
        tick.Click += (_, _) => CommentChanged(comment, field, tick.IsChecked == true);
        return tick;
    }

    /// <summary>The comment box shows, while empty, the comment the action writes by itself (the engine's automatic comment).</summary>
    private void ShowAutomaticComment()
    {
        if (commentText is null || row is null) return;
        string automatic = "";
        // rows that differ have no one comment to show
        if (!Mixed.Has(row)) try
        {
            var asRule = new JsonArray(new JsonObject { ["Kind"] = "Edit", ["Actions"] = new JsonArray(row.DeepClone()) });
            if (RuleLoading.LoadForEngine(asRule).FirstOrDefault()?.Actions?.FirstOrDefault() is { } action) automatic = NdfEngine.AutomaticComment(action);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException) { }
        // the text box's template shows its Tag as the placeholder
        commentText.Tag = automatic != "" ? automatic : Texts.Get("Rules.Row.CommentText.Hint");
    }

    /// <summary>A field of the row was changed: it is written into the row, and the rule window's list shows it.</summary>
    private void Changed(string field, object value)
    {
        if (row is null || model is null) return;
        bool aimedAtVariable = VariableAimedAt() is not null;
        row[field] = value is bool flag ? JsonValue.Create(flag) : JsonValue.Create((string)value);
        model.SaveRules();
        if (field is "Value" or "Operator") ShowAutomaticComment();
        // "for each match" shows only for an action aimed at a variable
        if (field is "Target" or "CustomTarget" && aimedAtVariable != (VariableAimedAt() is not null)) Draw();
    }

    /// <summary>Whether the open rule's actions can be aimed at a variable: an Edit rule's can.</summary>
    private bool AimsAtVariables => list == RulesModel.Actions && RuleEditor.KindOf(model?.OpenRule ?? []) == "Edit";

    /// <summary>The variable the action shown is aimed at ("{Name}" as its target, Name a variable in reach); null for a property.</summary>
    private string? VariableAimedAt() =>
        AimsAtVariables && Files.SharedVariables.AimedAt(row) is string name && Variables().Any(variable => variable.Text == name) ? name : null;

    /// <summary>"Custom": the name is typed instead of chosen; it starts as the one chosen.</summary>
    private void CustomClicked(bool on)
    {
        if (row is null) return;
        bool isAction = list == RulesModel.Actions;
        string custom = isAction ? "CustomTarget" : "CustomOperand", chosen = isAction ? "Target" : "Operand";
        row["ManualOverride"] = on;
        if (on && !Mixed.Is(row[chosen]) && JsonValues.AsText(row[custom]) == "") row[custom] = row[chosen]?.DeepClone();
        model?.SaveRules();
        Draw();
    }

    private void CommentChanged(JsonObject comment, string field, object value)
    {
        if (model is null) return;
        comment[field] = value is bool flag ? JsonValue.Create(flag) : JsonValue.Create((string)value);
        model.SaveRules();
        if (field == "Enabled") Draw();
    }

    /// <summary>The open preset's variables, suggested after "{".</summary>
    private List<Controls.Suggestion> Variables() => model is null ? [] : RuleSuggestions.VariablesOf(model);

    /// <summary>Enter in a box closes the window, unless it picks a suggestion; Shift+Enter in the Value box adds a line break.</summary>
    private void FormKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        if (e.OriginalSource is not TextBox box || Controls.Suggestions.IsOpenFor(box)) return;
        if (box.AcceptsReturn && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        Close();
    }

    private void PreviousClicked(object sender, RoutedEventArgs e) => Step(-1);

    private void NextClicked(object sender, RoutedEventArgs e) => Step(+1);

    private void Step(int direction)
    {
        if (model is not null) Open(model, list, index + direction, namesFor, scan);
    }

    private void DoneClicked(object sender, RoutedEventArgs e) => Close();

    private async void CancelClicked(object sender, RoutedEventArgs e) => await Cancel();
}
