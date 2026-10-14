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

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The condition or action window (the HTML app's <c>rowOpen</c>, <c>rowDraw</c> and <c>rowClose</c>): one row of the
/// open rule. Every change is written into the row and saved straight away.
/// </summary>
public partial class RowWindow : UserControl
{
    /// <summary>The operators a condition can have, and those an action can have.</summary>
    public static readonly string[] ConditionOperators =
        ["Starts With", "Ends With", "Contains", "Does Not Contain", "Is Equal To", "Is Not Equal To", "Is Less Than", "Is Less Than Or Equal To",
         "Is Greater Than", "Is Greater Than Or Equal To", "Contains Any Of", "Contains All Of"];
    public static readonly string[] ActionOperators =
        ["Add [int, float]", "Subtract [int, float]", "Multiply [int, float]", "Divide [int, float]", "Set To [int, float, string, boolean]",
         "Append [string]", "Prepend [string]", "Insert [array]", "Replace [array]", "Remove [array]"];

    /// <summary>The line height of the code font, for the Value box's lines.</summary>
    private const double CodeLineHeight = 17.390625;

    private RulesModel? model;
    private UndoHistory? history;
    private Func<string, IReadOnlyList<string>> namesFor = _ => [];
    private ScanLists scan = ScanLists.None;
    private string list = RulesModel.Conditions;
    private int index;
    private JsonObject? row;
    private JsonObject? ruleOfRow;
    private TextBox? commentText;

    /// <summary>Builds the window, closed.</summary>
    public RowWindow()
    {
        InitializeComponent();
        Form.PreviewKeyDown += FormKeyDown;
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Raised when the window opens or closes, so the rule window can move aside or back.</summary>
    public event Action<bool>? OpenChanged;

    /// <summary>Opens the window on a row of a list of the open rule; the row becomes the list's selection.</summary>
    /// <param name="rules">The Rules tab's data.</param>
    /// <param name="undo">The undo history, so typing in one box is one step.</param>
    /// <param name="rowList">Which list.</param>
    /// <param name="place">The row's place in it.</param>
    /// <param name="names">The scanned property names that fill the Operand or Target dropdown, for a list.</param>
    /// <param name="scanned">The scanned descriptor names and types, suggested in a condition's Value; none when left out.</param>
    public void Open(RulesModel rules, UndoHistory undo, string rowList, int place, Func<string, IReadOnlyList<string>> names, ScanLists? scanned = null)
    {
        scan = scanned ?? ScanLists.None;
        if (rules.RowsOf(rowList) is not JsonArray rows || place < 0 || place >= rows.Count) return;
        if (!ReferenceEquals(model, rules))
        {
            if (model is not null) model.PropertyChanged -= ModelChanged;
            model = rules;
            model.PropertyChanged += ModelChanged;
        }
        history = undo;
        namesFor = names;
        list = rowList;
        index = place;
        row = rows[place] as JsonObject;
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

    /// <summary>Closes the window; its list keeps the keys.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        row = null;
        OpenChanged?.Invoke(false);
        if (model is not null)
        {
            model.ActiveRowList = list;
            model.Refresh();
        }
    }

    /// <summary>The window sits right of the middle, as far from it as the rule window is left of it (see <see cref="RuleWindow.BesideGap"/>).</summary>
    private void Place() => Box.Margin = new Thickness(0, 0, RuleWindow.BesideGap(ActualWidth), 0);

    /// <summary>
    /// The Rules tab's data changed (Undo, a row moved or deleted in the rule window): the window stays on its row, or
    /// on the same place when Undo brought back copies of the rows; when the row is gone, the window closes.
    /// </summary>
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsOpen || model is null) return;
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
        int count = model?.RowsOf(list)?.Count ?? 0;
        Position.Text = (index + 1) + " of " + count;
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
        Title.Text = isAction ? "Action" : "Condition";
        Controls.QuickHelp.SetPage(HelpButton, isAction ? "actions" : "conditions");
        ShowPosition();
        Form.Children.Clear();
        commentText = null;

        AddLabel("Name", first: true);
        AddText("Name", "Optional. Shown in the list", mono: false);

        string subject = isAction ? "Target" : "Operand", custom = isAction ? "CustomTarget" : "CustomOperand";
        var customTick = new CheckBox { Content = "Custom", IsChecked = JsonValues.IsTruthy(row["ManualOverride"]), ToolTip = "Type a name instead of choosing one", Name = "ManualOverride" };
        customTick.SetResourceReference(StyleProperty, "Tick");
        customTick.Click += (_, _) => CustomClicked(customTick.IsChecked == true);
        AddLabel(subject, right: customTick);
        if (JsonValues.IsTruthy(row["ManualOverride"]))
        {
            TextBox customBox = AddText(custom, isAction ? "Custom target" : "Custom operand", mono: true);
            Controls.ColouredTextBox.SetKind(customBox, RuleTextKind.Path);
            Controls.Suggestions.Attach(customBox, RuleSuggestions.Path(namesFor(list), Variables));
        }
        else
        {
            IEnumerable<string> special = isAction ? RuleEditor.SpecialOperands.Skip(1) : RuleEditor.SpecialOperands;
            AddChoice(subject, [.. special, .. namesFor(list)], mono: true, notInListSuffix: " (not in scan)");
        }

        AddLabel("Operator");
        AddChoice("Operator", isAction ? ActionOperators : ConditionOperators, mono: false, notInListSuffix: null);

        AddLabel("Value", tip: "Enter closes this window. Shift+Enter adds a line break.");
        AddValueBox();

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
        line.Children.Add(new TextBlock { Text = text + ":" }.Themed(TextBlock.ForegroundProperty, "muted"));
        Form.Children.Add(line);
    }

    /// <summary>A text box for one of the row's fields, 3 pixels under its label.</summary>
    private TextBox AddText(string field, string placeholder, bool mono)
    {
        var box = new TextBox { Text = JsonValues.AsText(row![field]), Tag = placeholder, ToolTip = placeholder, Name = field, Margin = new Thickness(0, 3, 0, 0) };
        if (mono) box.SetResourceReference(StyleProperty, "MonoBox");
        box.TextChanged += (_, _) => Changed(field, box.Text, typing: true);
        Form.Children.Add(box);
        return box;
    }

    /// <summary>The Value box: the code font, as many lines as the value has, up to 8; Shift+Enter adds one.</summary>
    private void AddValueBox()
    {
        var box = new TextBox { Text = JsonValues.AsText(row!["Value"]), Name = "Value", Margin = new Thickness(0, 3, 0, 0), ToolTip = "Enter closes this window. Shift+Enter adds a line break." };
        box.SetResourceReference(StyleProperty, "TextArea");
        box.TextWrapping = TextWrapping.NoWrap;
        Controls.ColouredTextBox.SetKind(box, RuleTextKind.Value);
        JsonObject shownRow = row!;
        Controls.Suggestions.Attach(box, RuleSuggestions.Value(list == RulesModel.Actions,
            operand: () => JsonValues.AsText(shownRow[JsonValues.IsTruthy(shownRow["ManualOverride"]) ? "CustomOperand" : "Operand"]),
            operation: () => JsonValues.AsText(shownRow["Operator"]), scan, Variables));
        void FitLines() => box.Height = Math.Min(8, box.Text.Split('\n').Length) * CodeLineHeight + 10;
        FitLines();
        box.TextChanged += (_, _) =>
        {
            FitLines();
            Changed("Value", box.Text, typing: true);
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
        if (current == "") options.Add(new ChoiceOption("", "(choose)"));
        else if (!choices.Contains(current))
            options.Add(notInListSuffix is null ? new ChoiceOption("", "(choose)") : new ChoiceOption(current, current + notInListSuffix));
        options.AddRange(choices.Select(c => new ChoiceOption(c, c)));
        // the operator is named by its label; a property shows its own name, so a name too long for the box can be read in full
        var choice = new ComboBox { ItemsSource = options, Name = field, Margin = new Thickness(0, 3, 0, 0), ToolTip = field == "Operator" ? "Operator" : NameOrNone(current) };
        choice.SetResourceReference(ItemsControl.ItemTemplateProperty, "DimmedChoice");
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
            Changed(field, picked.Value, typing: false);
        };
        Form.Children.Add(choice);
    }

    /// <summary>An action's comment: on or off; when on, its text and what it includes.</summary>
    private void AddComment()
    {
        if (row!["Comment"] is not JsonObject comment) row["Comment"] = comment = RuleLoading.NewComment();
        CheckBox on = CommentTick(comment, "Enabled", new Run("Comment"));
        on.Margin = new Thickness(0, 13, 0, 0);
        on.HorizontalAlignment = HorizontalAlignment.Left;
        Form.Children.Add(on);
        if (JsonValues.IsTruthy(comment["Enabled"]))
        {
            commentText = new TextBox { Text = JsonValues.AsText(comment["Text"]), Name = "CommentText", Margin = new Thickness(0, 3, 0, 0) };
            ShowAutomaticComment();
            commentText.TextChanged += (_, _) => CommentChanged(comment, "Text", commentText.Text, typing: true);
            Form.Children.Add(commentText);

            // .cmt: what the comment includes, 16 pixels in, wrapping with 6 by 14 pixels between
            string mod = model?.OpenMod.Name ?? "";
            var mark = new Run(mod);
            mark.SetResourceReference(TextElement.ForegroundProperty, "accent");
            var includes = new Controls.FlexRow { Gap = 14, LineGap = 6, Margin = new Thickness(16, 3, 0, 8) };
            CheckBox editedBy = CommentTick(comment, "IncludeMod", new Run("Edited by "), mark);
            editedBy.ToolTip = "Adds \"Edited by " + mod + "\" to the comment";
            includes.Children.Add(editedBy);
            includes.Children.Add(CommentTick(comment, "IncludeOriginalValue", new Run("Original value")));
            includes.Children.Add(CommentTick(comment, "IncludeTimestamp", new Run("Timestamp")));
            Form.Children.Add(includes);
        }
        // .note: 10 pixels below what is above it, besides the 3 between every part
        var note = new TextBlock { Margin = new Thickness(0, 13, 0, 0) };
        note.SetResourceReference(StyleProperty, "Note");
        note.Inlines.Add(new Run("A target is a property name at any depth, or a pair key such as a MAP entry. Insert and Replace take "));
        var code = new Run("index:value") { FontSize = 12 };
        code.SetResourceReference(TextElement.FontFamilyProperty, "mono");
        note.Inlines.Add(code);
        note.Inlines.Add(new Run("; index is a number, FirstIndex, LastIndex, or an offset like LastIndex-1. \\n is a line break. {Name} inserts a variable."));
        Form.Children.Add(note);
    }

    /// <summary>The tooltips of the comment's checkboxes ("Edited by" names the mod, so it gets its own).</summary>
    private static readonly Dictionary<string, string> CommentTips = new()
    {
        ["Enabled"] = "Add a comment after each edited value",
        ["IncludeOriginalValue"] = "Add the value before the edit",
        ["IncludeTimestamp"] = "Add the date and time",
    };

    /// <summary>A property's name as a tooltip; none while nothing is chosen.</summary>
    private static string? NameOrNone(string name) => name == "" ? null : name;

    private CheckBox CommentTick(JsonObject comment, string field, params Inline[] label)
    {
        var text = new TextBlock();
        text.Inlines.AddRange(label);
        var tick = new CheckBox { Content = text, IsChecked = JsonValues.IsTruthy(comment[field]), Name = "Comment" + field, ToolTip = CommentTips.GetValueOrDefault(field) };
        tick.SetResourceReference(StyleProperty, "Tick");
        tick.Click += (_, _) => CommentChanged(comment, field, tick.IsChecked == true, typing: false);
        return tick;
    }

    /// <summary>The comment box shows, while empty, the comment the action writes by itself (the engine's automatic comment).</summary>
    private void ShowAutomaticComment()
    {
        if (commentText is null || row is null) return;
        string automatic = "";
        try
        {
            var asRule = new JsonArray(new JsonObject { ["Kind"] = "Edit", ["Actions"] = new JsonArray(row.DeepClone()) });
            if (RuleLoading.LoadForEngine(asRule).FirstOrDefault()?.Actions?.FirstOrDefault() is { } action) automatic = NdfEngine.AutomaticComment(action);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException) { }
        // the text box's template shows its Tag as the placeholder
        commentText.Tag = automatic != "" ? automatic : "Comment text";
    }

    /// <summary>A field of the row was changed: it is written and saved; typing in one box is one step to undo.</summary>
    private void Changed(string field, object value, bool typing)
    {
        if (row is null || model is null) return;
        row[field] = value is bool flag ? JsonValue.Create(flag) : JsonValue.Create((string)value);
        if (typing) history?.TypingIn("f|" + model.OpenPreset?.Name + "|" + model.CurrentRule + "|" + list + "|" + index + "|" + field);
        model.SaveRules();
        if (field is "Value" or "Operator") ShowAutomaticComment();
    }

    /// <summary>"Custom": the name is typed instead of chosen; it starts as the one chosen.</summary>
    private void CustomClicked(bool on)
    {
        if (row is null) return;
        bool isAction = list == RulesModel.Actions;
        string custom = isAction ? "CustomTarget" : "CustomOperand", chosen = isAction ? "Target" : "Operand";
        row["ManualOverride"] = on;
        if (on && JsonValues.AsText(row[custom]) == "") row[custom] = row[chosen]?.DeepClone();
        model?.SaveRules();
        Draw();
    }

    private void CommentChanged(JsonObject comment, string field, object value, bool typing)
    {
        if (model is null) return;
        comment[field] = value is bool flag ? JsonValue.Create(flag) : JsonValue.Create((string)value);
        if (typing) history?.TypingIn("f|" + model.OpenPreset?.Name + "|" + model.CurrentRule + "|" + list + "|" + index + "|m" + field);
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
        if (model is not null && history is not null) Open(model, history, list, index + direction, namesFor, scan);
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
