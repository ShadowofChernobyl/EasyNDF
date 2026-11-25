using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Editor;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Rules;

namespace EasyNdf.App.Files;

/// <summary>
/// The widths of the columns of Details' edit list, shared by its headings and its lines: the first follows what is in
/// it, within a part of the width; the two after it share the rest. Dragged, they keep their shares of the width.
/// </summary>
internal sealed class EditColumns
{
    /// <summary>A column is never dragged narrower than this.</summary>
    private const double Narrowest = 30;

    /// <summary>How many characters the first column's longest text has.</summary>
    public int Longest { get; set; }

    /// <summary>The most of the width the first column takes by itself.</summary>
    public double MostOfTheWidth { get; set; } = 0.3;

    private double? firstShare;
    private double secondShare = 0.5;

    /// <summary>How wide the first column is, in a list of a width.</summary>
    public double First(double width) =>
        firstShare is double share ? Math.Round(width * share) : Math.Round(Math.Min(Math.Max(90, Longest * 7.3 + 18), width * MostOfTheWidth));

    /// <summary>How wide the second of three columns is.</summary>
    public double Second(double width) => Math.Round((width - First(width)) * secondShare);

    /// <summary>The line after a column moved this far from where it started, in a list of a width.</summary>
    public void Drag(int column, double moved, double width, double startFirst, double startSecond)
    {
        double first = startFirst, second = startSecond;
        if (column == 0)
        {
            first = Math.Clamp(startFirst + moved, Narrowest, startFirst + startSecond - Narrowest);
            second = startFirst + startSecond - first;
        }
        else second = Math.Clamp(startSecond + moved, Narrowest, width - startFirst - Narrowest);
        firstShare = first / width;
        secondShare = second / (width - first);
    }

    /// <summary>Puts the columns back as they follow what is in them.</summary>
    public void Reset()
    {
        firstShare = null;
        secondShare = 0.5;
    }
}

/// <summary>A line of Details' edit list, or its headings: its children side by side in the shared columns.</summary>
internal sealed class EditRowPanel(EditColumns columns) : Panel
{
    /// <summary>The width the columns were last measured for.</summary>
    private double measuredFor;

    /// <summary>Whether the children are being measured again for the width the line is placed at.</summary>
    private bool measuringToPlace;

    /// <summary>
    /// A line just out of sight is measured without the scrollbar and placed with it, as the list only measures again
    /// what is in sight. Its text wraps to the width it is placed at, which changes what the text asks for, but that does
    /// not ask for the line to be measured again: that measured it at the old width and placed it at the new, over and
    /// over, until WPF gave up on laying out the window. It did so at each step of a drag, and went on after it, keeping
    /// a core busy and SizeChanged from being raised. Tuned for speed: Keep it that way when tidying.
    /// </summary>
    protected override void OnChildDesiredSizeChanged(UIElement child)
    {
        if (!measuringToPlace) base.OnChildDesiredSizeChanged(child);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, height = 0;
        measuredFor = width;
        for (int column = 0; column < InternalChildren.Count; column++)
        {
            InternalChildren[column].Measure(new Size(WidthOf(column, width), double.PositiveInfinity));
            height = Math.Max(height, InternalChildren[column].DesiredSize.Height);
        }
        // wanting no width of its own: a list gives a line at least what it wants, so a line measured before the
        // scrollbar came would run under it
        return new Size(0, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Math.Abs(finalSize.Width - measuredFor) > 0.5)
        {
            measuredFor = finalSize.Width;
            measuringToPlace = true;
            for (int column = 0; column < InternalChildren.Count; column++)
                InternalChildren[column].Measure(new Size(WidthOf(column, finalSize.Width), double.PositiveInfinity));
            measuringToPlace = false;
        }
        double left = 0;
        for (int column = 0; column < InternalChildren.Count; column++)
        {
            double width = WidthOf(column, finalSize.Width);
            InternalChildren[column].Arrange(new Rect(left, 0, width, finalSize.Height));
            left += width;
        }
        return finalSize;
    }

    private double WidthOf(int column, double width) => (InternalChildren.Count, column) switch
    {
        (1, _) => width,
        (2, 0) => columns.First(width),
        (2, _) => width - columns.First(width),
        (_, 0) => columns.First(width),
        (_, 1) => columns.Second(width),
        _ => width - columns.First(width) - columns.Second(width),
    };
}

/// <summary>
/// Details' box for the chosen rule: its name and counts at the top, its warnings, and its edits: what changed in each
/// value in full, in the colours of a comparison, with only a little of what stayed the same around it. A double-click
/// on an edit shows its descriptor in the File Editor, in the file as exported.
/// </summary>
internal sealed class DetailsEditList : Bevel
{
    /// <summary>In a box narrower than this, a line's parts go one under the other.</summary>
    private const double NarrowBelow = 420;

    /// <summary>How many of a rule's warnings are listed before the rest are counted.</summary>
    private const int ShownWarnings = 20;

    private sealed record DescriptorHeading(EditGroup Group);
    private sealed record Edit(EditGroup Group, EditRow Row);
    private sealed record OneTargetHeading(string Target, int Count);
    private sealed record NamedChange(EditGroup Group);
    private sealed record NamedNote(EditGroup Group);
    private sealed record EndNote(string Text);

    private readonly DockPanel holder = new() { LastChildFill = true };
    private readonly Bevel heading = new() { Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 3, 5, 3) };
    private readonly Decorator symbol = new() { Width = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock ruleName = DetailsText.Line();
    private readonly TextBlock ruleCounts = DetailsText.Line("muted");
    private readonly Button openRule = new() { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 0, 0, 0) };
    private readonly StackPanel notes = new();
    private readonly ScrollViewer notesScroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly Grid content = new();
    private readonly DetailsRowList list = new();
    private readonly ScrollViewer body = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly EditColumns columns = new();

    private FileEntry? row;
    private RunDetails? run;
    private string find = "";
    private bool isNarrow;
    private EditRowPanel? columnHeadings;
    private FrameworkElement? headings;

    /// <summary>Raised by Open Rule.</summary>
    public event Action? OpenRuleClicked;

    /// <summary>Makes the box, showing nothing until <see cref="Show"/>.</summary>
    public DetailsEditList()
    {
        SetResourceReference(TopLeftProperty, "lo");
        SetResourceReference(BottomRightProperty, "hi");
        SetResourceReference(BackgroundProperty, "well");

        ruleCounts.FontSize = 11.5;
        ruleCounts.VerticalAlignment = VerticalAlignment.Center;
        var title = new FlexRow { Gap = 8, LineGap = 0, Children = { ruleName, ruleCounts } };
        openRule.Content = Texts.Get("Files.Details.OpenRule");
        openRule.ToolTip = Texts.Get("Files.Details.OpenRule.Tip");
        openRule.SetResourceReference(StyleProperty, "SmallButton");
        openRule.Click += (_, _) => OpenRuleClicked?.Invoke();
        var top = new DockPanel();
        DockPanel.SetDock(openRule, Dock.Right);
        DockPanel.SetDock(symbol, Dock.Left);
        top.Children.Add(openRule);
        top.Children.Add(symbol);
        top.Children.Add(title);
        heading.Child = top;
        heading.SetResourceReference(BottomRightProperty, "line");
        heading.SetResourceReference(BackgroundProperty, "panel");
        DockPanel.SetDock(heading, Dock.Top);
        holder.Children.Add(heading);

        notesScroller.Content = notes;
        DockPanel.SetDock(notesScroller, Dock.Top);
        holder.Children.Add(notesScroller);

        list.LineOf = LineOf;
        list.SizeChanged += (_, e) =>
        {
            // with Wrap text in lists off, a line keeps its columns at any width
            bool narrow = WrapsLines && e.NewSize.Width < NarrowBelow;
            if (e.WidthChanged && narrow != isNarrow) ShowNarrow(narrow);
        };
        content.Children.Add(new WholeRows { Child = list });
        content.Children.Add(body);
        holder.Children.Add(content);
        Child = holder;
        // the warnings never take the room of the edits
        SizeChanged += (_, e) =>
        {
            if (e.HeightChanged) notesScroller.MaxHeight = Math.Max(40, e.NewSize.Height * 0.4);
        };
    }

    /// <summary>Shows a rule; with none, says to choose one.</summary>
    public void Show(RunDetails details, int rule, string textToFind, FileEntry file)
    {
        row = file;
        run = details;
        find = textToFind;
        notes.Children.Clear();
        if (rule < 0)
        {
            heading.Visibility = Visibility.Collapsed;
            ShowBody(null, Texts.Get("Files.Details.ChooseARule"), null);
            return;
        }
        heading.Visibility = Visibility.Visible;
        RuleStats stats = details.Stats[rule];
        ShowHeading(stats, details.ErrorsOf(rule));
        ShowNotes(details, rule, stats);

        RuleEdits edits = details.EditsOf(rule, textToFind);
        if (edits.Groups.Count == 0)
        {
            ShowNoEdits(details, rule, stats);
            return;
        }
        ShowEdits(edits, stats, details.LogOf(rule).Count);
    }

    private void ShowHeading(RuleStats stats, IReadOnlyList<RunValueError> errors)
    {
        (string glyph, string colour) = Rules.RuleRow.SymbolOf(stats.Kind);
        // a rule that wrote values that would stop mod generation (or crash the game) is marked as an error, as in the rule list
        symbol.Child = errors.Count > 0 ? NoticeLayer.Icon(NoticeKind.Problem, 16) : FolderGlyph.For(glyph, colour, 16);
        symbol.ToolTip = errors.Count > 0 ? DetailsCounts.WhatErrorsDo(errors) : Rules.RuleKindNames.Of(stats.Kind);
        ruleName.Inlines.Clear();
        DetailsText.Add(ruleName.Inlines, stats.Name, null, find);
        ruleCounts.Inlines.Clear();
        ruleCounts.Visibility = RunDetails.MakesNoEdits(stats.Kind) ? Visibility.Collapsed : Visibility.Visible;
        if (ruleCounts.Visibility != Visibility.Visible) return;
        // each count stays on one line: the line wraps between counts
        ruleCounts.Inlines.Add(DetailsText.Piece(WithoutBreaks(DetailsCounts.Matched(stats.Matched)) + " · " + WithoutBreaks(DetailsCounts.Edits(stats.AllEdits)), null));
        if (stats.Skipped == 0) return;
        ruleCounts.Inlines.Add(DetailsText.Piece(" · ", null));
        ruleCounts.Inlines.Add(DetailsText.Piece(WithoutBreaks(DetailsCounts.Skipped(stats.Skipped)), "caution"));
    }

    private static string WithoutBreaks(string text) => text.Replace(' ', ' ');

    /// <summary>
    /// The rule's values that would stop mod generation with no line of their own below (written where no edit of the
    /// rule shows them, or past the edits listed), its warnings, and what skipped means when it skipped something.
    /// </summary>
    private void ShowNotes(RunDetails details, int rule, RuleStats stats)
    {
        List<RunValueError> unlisted = [.. details.UnlistedErrorsOf(rule)];
        foreach (RunValueError error in unlisted.Take(ShownWarnings)) notes.Children.Add(NoteLine(DetailsCounts.ErrorLine(error), NoticeKind.Problem));
        if (unlisted.Count > ShownWarnings) notes.Children.Add(NoteLine(Texts.Get("Files.Details.MoreWarnings", ("count", Texts.Number(unlisted.Count - ShownWarnings))), NoticeKind.Problem));
        IReadOnlyList<RunWarning> warnings = details.WarningsOf(rule);
        // the first ones, then how many more: a rule that warns on every descriptor made a wall of text
        foreach (RunWarning warning in warnings.Take(ShownWarnings)) notes.Children.Add(NoteLine(warning.Message));
        if (warnings.Count > ShownWarnings) notes.Children.Add(NoteLine(Texts.Get("Files.Details.MoreWarnings", ("count", Texts.Number(warnings.Count - ShownWarnings)))));
        if (stats.Skipped > 0) notes.Children.Add(NoteLine(Texts.Get("Files.Details.SkippedMeans")));
        notesScroller.Visibility = notes.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Bevel NoteLine(string text, NoticeKind kind = NoticeKind.Caution)
    {
        FrameworkElement icon = NoticeLayer.Icon(kind, 13);
        icon.Margin = new Thickness(0, 2, 7, 0);
        TextBlock words = DetailsText.Line(kind == NoticeKind.Problem ? "warn" : "caution");
        words.Text = DetailsText.Soft(text);
        words.FontSize = 12;
        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        line.Children.Add(words);
        var box = new Bevel { Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 4, 8, 4), Child = line };
        box.SetResourceReference(BottomRightProperty, "line");
        box.SetResourceReference(BackgroundProperty, "panel");
        return box;
    }

    /// <summary>A rule with no edits listed: why, in words.</summary>
    private void ShowNoEdits(RunDetails details, int rule, RuleStats stats)
    {
        if (RunRecord.IsIdle(stats))
        {
            string how = stats.Matched == 0 ? Texts.Get("Files.Details.MatchedNone") : Texts.Get("Files.Details.MatchedButNoEdits", ("descriptors", DetailsCounts.Descriptors(stats.Matched)));
            ShowBody(NoticeKind.Caution, Texts.Get("Files.Details.ChangedNothing"), how + " " + Texts.Get("Files.Details.NotAnError"));
        }
        else if (stats.Edits > 0) ShowBody(null, Texts.Get("Files.Details.ListSwitchedOff"), null);
        else if (stats.Kind == "File") ShowBody(null, Texts.Get("Files.Details.FileRuleBody"), null);
        else if (stats.Kind == "Check")
        {
            if (details.WarningsOf(rule).Count > 0) ShowBody(null, Texts.Get("Files.Details.CheckBody"), null);
            else ShowBody(NoticeKind.Done, Texts.Get("Files.Details.CheckPassedBody", ("descriptors", DetailsCounts.Descriptors(stats.Matched))), null);
        }
        else ShowBody(null, Texts.Get("Files.Details.ChangedNothing"), null);
    }

    /// <summary>Words in place of the edits, with what they are about in the caution colour when there is a warning to give.</summary>
    private void ShowBody(NoticeKind? icon, string first, string? rest)
    {
        list.Visibility = Visibility.Collapsed;
        list.ItemsSource = null;
        var words = new StackPanel();
        TextBlock line = DetailsText.Line(icon == NoticeKind.Caution ? "caution" : "muted");
        line.Text = first;
        words.Children.Add(line);
        if (rest is not null)
        {
            TextBlock more = DetailsText.Line("muted");
            more.Text = rest;
            words.Children.Add(more);
        }
        var box = new DockPanel { Margin = new Thickness(10) };
        if (icon is NoticeKind kind)
        {
            FrameworkElement shown = NoticeLayer.Icon(kind, 16);
            shown.Margin = new Thickness(0, 1, 9, 0);
            DockPanel.SetDock(shown, Dock.Left);
            box.Children.Add(shown);
        }
        box.Children.Add(words);
        body.Content = box;
        body.Visibility = Visibility.Visible;
    }

    /// <summary>Lists a rule's edits, in the shape that reads best for them.</summary>
    private void ShowEdits(RuleEdits edits, RuleStats stats, int logged)
    {
        body.Visibility = Visibility.Collapsed;
        body.Content = null;
        list.Visibility = Visibility.Visible;
        var lines = new List<object>();
        switch (edits.Shape)
        {
            case EditListShape.OneTarget:
                columns.Longest = edits.Groups.Max(group => group.Title.Length);
                columns.MostOfTheWidth = 0.4;
                lines.Add(new OneTargetHeading(edits.Groups[0].Rows[0].Target, edits.Groups.Count));
                lines.AddRange(edits.Groups.Select(group => new NamedChange(group)));
                break;
            case EditListShape.OneNoteEach:
                columns.Longest = edits.Groups.Max(group => group.Title.Length);
                columns.MostOfTheWidth = 0.4;
                lines.AddRange(edits.Groups.Select(group => new NamedNote(group)));
                break;
            default:
                columns.Longest = edits.Groups.SelectMany(group => group.Rows).Where(edit => edit.Kind != EditRowKind.Note).Select(edit => edit.Target.Length).DefaultIfEmpty(0).Max();
                columns.MostOfTheWidth = 0.3;
                foreach (EditGroup group in edits.Groups)
                {
                    lines.Add(new DescriptorHeading(group));
                    lines.AddRange(group.Rows.Select(edit => new Edit(group, edit)));
                }
                break;
        }
        if (stats.AllEdits > logged)
            lines.Add(new EndNote(Texts.Get("Files.Details.ShowingFirst", ("shown", Texts.Number(logged)), ("edits", Texts.Number(stats.AllEdits)))));
        ShowHeadings(edits);
        list.ShowLines(lines, fromTheTop: true);
    }

    /// <summary>The headings over the columns, when any line has a before and an after; they stay while the lines scroll.</summary>
    private void ShowHeadings(RuleEdits edits)
    {
        columnHeadings = null;
        headings = null;
        if (edits.Shape == EditListShape.OneTarget) headings = MakeHeadings("Files.Details.Descriptor");
        else if (edits.HasChange && edits.Shape != EditListShape.OneNoteEach) headings = MakeHeadings("Files.Details.Target");
        list.Tag = isNarrow ? null : headings;
    }

    private FrameworkElement MakeHeadings(string firstColumn)
    {
        columnHeadings = new EditRowPanel(columns);
        string[] words = [Texts.Get(firstColumn), Texts.Get("Files.Details.Before"), Texts.Get("Files.Details.After")];
        for (int column = 0; column < words.Length; column++)
        {
            var cell = new Bevel { Child = new TextBlock { Text = words[column] } };
            cell.SetResourceReference(StyleProperty, "ColumnHeading");
            cell.Padding = new Thickness(8, 2, 8, 2);
            var holding = new Grid { Children = { cell } };
            if (column < words.Length - 1) holding.Children.Add(DragLine(column));
            columnHeadings.Children.Add(holding);
        }
        var under = new Bevel { Edges = new Thickness(0, 0, 0, 1), Child = columnHeadings };
        under.SetResourceReference(BottomRightProperty, "lo");
        under.SetResourceReference(BackgroundProperty, "panel");
        return under;
    }

    /// <summary>
    /// The strip over the line at a heading's right, 7 pixels wide: dragged, it moves the line between that column and
    /// the next; double-clicked, it puts the columns back as at first. In the accent colour under the mouse and while
    /// dragged.
    /// </summary>
    private Border DragLine(int column)
    {
        var line = new Border { Width = 7, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, ToolTip = Texts.Get("Units.Table.Resize.Tip") };
        (double X, double First, double Second)? dragging = null;
        void ShowState()
        {
            if (line.IsMouseOver || line.IsMouseCaptured) line.SetResourceReference(Border.BackgroundProperty, "accent");
            else line.Background = Brushes.Transparent;
            line.Opacity = 0.55;
        }
        line.MouseEnter += (_, _) => ShowState();
        line.MouseLeave += (_, _) => ShowState();
        line.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                columns.Reset();
                ShowColumns();
                return;
            }
            double width = list.ActualWidth - ScrollBarWidth();
            dragging = (e.GetPosition(this).X, columns.First(width), columns.Second(width));
            line.CaptureMouse();
        };
        line.MouseMove += (_, e) =>
        {
            if (dragging is not var (x, first, second)) return;
            columns.Drag(column, e.GetPosition(this).X - x, list.ActualWidth - ScrollBarWidth(), first, second);
            ShowColumns();
        };
        line.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            line.ReleaseMouseCapture();
        };
        line.LostMouseCapture += (_, _) =>
        {
            dragging = null;
            ShowState();
        };
        return line;
    }

    private double ScrollBarWidth() =>
        list.Scroller is { ComputedVerticalScrollBarVisibility: Visibility.Visible } scroller && scroller.Template.FindName("PART_VerticalScrollBar", scroller) is FrameworkElement bar ? bar.ActualWidth : 0;

    /// <summary>Lays out the headings and the lines in sight again, after the columns changed.</summary>
    private void ShowColumns()
    {
        columnHeadings?.InvalidateMeasure();
        foreach (FrameworkElement line in list.LinesMade())
            if (line is Decorator { Child: EditRowPanel panel }) panel.InvalidateMeasure();
    }

    /// <summary>The box became narrower or wider than where a line's parts change places: the lines are made again.</summary>
    private void ShowNarrow(bool narrow)
    {
        isNarrow = narrow;
        list.Tag = narrow ? null : headings;
        list.Items.Refresh();
    }

    private FrameworkElement LineOf(object item) => item switch
    {
        DescriptorHeading heading => MakeDescriptorHeading(heading.Group),
        Edit edit when edit.Row.Kind == EditRowKind.Note => MakeNote(edit),
        Edit edit => isNarrow ? MakeNarrowEdit(edit) : MakeEdit(edit),
        OneTargetHeading oneTarget => MakeOneTargetHeading(oneTarget),
        NamedChange named => isNarrow ? MakeNarrowNamedChange(named.Group) : MakeNamedChange(named.Group),
        NamedNote named => MakeNamedNote(named.Group),
        EndNote end => MakeEndNote(end.Text),
        _ => new Decorator(),
    };

    /// <summary>A descriptor's heading, with its button to the File Editor when it has a name to look for.</summary>
    private FrameworkElement MakeDescriptorHeading(EditGroup group)
    {
        TextBlock name = OneLineUnlessWrapping(DetailsText.Code());
        name.VerticalAlignment = VerticalAlignment.Center;
        DetailsText.Add(name.Inlines, group.Title, null, find);
        var line = new DockPanel { MinHeight = 22 };
        // a variable an action changed is nowhere in the file
        if (group.Name != "" && !(group.Name.StartsWith('{') && group.Name.EndsWith('}')))
        {
            var show = new Button { Content = Texts.Get("Files.Details.ShowInEditor"), ToolTip = Texts.Get("Files.Details.ShowInEditor.Tip"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            show.SetResourceReference(StyleProperty, "SmallButton");
            show.Click += (_, _) => ShowInEditor(group);
            DockPanel.SetDock(show, Dock.Right);
            line.Children.Add(show);
        }
        line.Children.Add(name);
        var box = new Bevel { Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 2, 3, 2), Child = line };
        box.SetResourceReference(BottomRightProperty, "line");
        box.SetResourceReference(BackgroundProperty, "panel");
        return box;
    }

    private void ShowInEditor(EditGroup group)
    {
        if (row is not null && group.Name != "") _ = MainView.Of(this).EditorTab.ShowDescriptorInExport(row, group.Name);
    }

    /// <summary>A line with a faint line under it, lit under the mouse, which a double-click shows in the File Editor.</summary>
    private Bevel EditLine(EditGroup group, UIElement inside)
    {
        var line = new Bevel { Edges = new Thickness(0, 0, 0, 1), Child = inside, ToolTip = Texts.Get("Files.Details.DoubleClickTip"), Background = Brushes.Transparent };
        line.SetResourceReference(BottomRightProperty, "line");
        line.MouseEnter += (_, _) => line.SetResourceReference(BackgroundProperty, "sel");
        line.MouseLeave += (_, _) => line.Background = Brushes.Transparent;
        line.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            e.Handled = true;
            ShowInEditor(group);
        };
        return line;
    }

    private static TextBlock Cell(TextBlock text)
    {
        text.Padding = new Thickness(8, 3, 8, 3);
        return OneLineUnlessWrapping(text);
    }

    /// <summary>
    /// Whether Settings › Wrap text in lists is on: each edit's text then wraps, and a narrow box puts a line's parts one
    /// under the other; off, every edit keeps to one line, its text cut short with "…".
    /// </summary>
    private static bool WrapsLines => AppData.Current.Settings["wrapRows"]?.GetValue<bool>() == true;

    /// <summary>A line's text on one line, cut short with "…", unless Wrap text in lists is on.</summary>
    private static TextBlock OneLineUnlessWrapping(TextBlock text) => ListText.Follow(text);

    /// <summary>Wrap text in lists changed: a narrow box's lines are made again, as they go one part under the other only when wrapping.</summary>
    public void ApplyListWrap()
    {
        bool narrow = WrapsLines && list.ActualWidth > 0 && list.ActualWidth < NarrowBelow;
        if (narrow != isNarrow) ShowNarrow(narrow);
    }

    private TextBlock TargetCell(string target, string? colour = null)
    {
        TextBlock cell = Cell(DetailsText.Code(colour));
        DetailsText.Add(cell.Inlines, target, null, find);
        return cell;
    }

    private TextBlock ValueCell(IReadOnlyList<ValuePiece> pieces, int leftOut, string changedColour)
    {
        TextBlock cell = Cell(DetailsText.Code());
        DetailsText.AddValue(cell.Inlines, pieces, leftOut, changedColour, find);
        return cell;
    }

    /// <summary>A target and its value's before and after; a value only added to or taken from has one side, across both columns.</summary>
    private FrameworkElement MakeEdit(Edit edit)
    {
        var panel = new EditRowPanel(columns) { Children = { TargetCell(edit.Row.Target) } };
        ValueChange change = edit.Row.Change!;
        EditLogEntry entry = edit.Row.Entry;
        RunValueError? error = run?.ErrorAt(entry);
        string after = error is null ? "d-add" : "warn";
        switch (edit.Row.Kind)
        {
            case EditRowKind.Added: panel.Children.Add(Flagged(OneSided("+", "d-add", change.After, entry.NewLeftOut, after), error)); break;
            case EditRowKind.Removed: panel.Children.Add(OneSided("−", "d-del", change.Before, entry.OldLeftOut, "d-del")); break;
            case EditRowKind.SpacingOnly: panel.Children.Add(Flagged(SpacingOnly(change, after), error)); break;
            default:
                panel.Children.Add(ValueCell(change.Before, entry.OldLeftOut, "d-del"));
                panel.Children.Add(Flagged(ValueCell(change.After, entry.NewLeftOut, after), error));
                break;
        }
        return EditLine(edit.Group, panel);
    }

    private TextBlock OneSided(string mark, string colour, IReadOnlyList<ValuePiece> pieces, int leftOut, string changedColour)
    {
        TextBlock cell = Cell(DetailsText.Code());
        System.Windows.Documents.Run marked = DetailsText.Piece(mark + " ", colour);
        marked.FontWeight = FontWeights.Bold;
        cell.Inlines.Add(marked);
        DetailsText.AddValue(cell.Inlines, pieces, leftOut, changedColour, find);
        return cell;
    }

    private TextBlock SpacingOnly(ValueChange change, string colour = "d-add")
    {
        TextBlock cell = Cell(DetailsText.Code());
        cell.Inlines.Add(DetailsText.Note(Texts.Get("Files.Details.OnlySpacing") + " "));
        DetailsText.AddValue(cell.Inlines, change.After, 0, colour, find);
        return cell;
    }

    /// <summary>A zigzag to cut to the width of a value, as the File Editor marks a wrong one.</summary>
    private static readonly Geometry Wave = SyntaxProblems.WavyLine(0, 1600, 2.5);

    /// <summary>
    /// A value an edit wrote, as it is when it is fine; when it would stop mod generation, with a wavy line under it in
    /// the error colour and what is wrong under that, in a few words, the tip saying it in full.
    /// </summary>
    private static FrameworkElement Flagged(TextBlock cell, RunValueError? error)
    {
        if (error is null) return cell;
        Thickness padding = cell.Padding;
        cell.Padding = new Thickness(padding.Left, padding.Top, padding.Right, 0);
        cell.HorizontalAlignment = HorizontalAlignment.Left;
        var wave = new Path { Data = Wave, StrokeThickness = 1, Height = 3, Width = 0, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(padding.Left, 0, 0, 0) };
        wave.SetResourceReference(Shape.StrokeProperty, "warn");
        // under the value's words, however wide they are
        cell.SizeChanged += (_, e) => wave.Width = Math.Max(0, e.NewSize.Width - padding.Left - padding.Right);
        FrameworkElement icon = NoticeLayer.Icon(NoticeKind.Problem, 11);
        icon.Margin = new Thickness(0, 2, 5, 0);
        DockPanel.SetDock(icon, Dock.Left);
        TextBlock why = OneLineUnlessWrapping(DetailsText.Line("warn"));
        why.FontSize = 11.5;
        why.Text = ValueMessages.ShortOf(error.Error.Problem, error.Error.FirstLine);
        var reason = new DockPanel { Margin = new Thickness(padding.Left, 1, padding.Right, padding.Bottom), Children = { icon, why } };
        return new StackPanel { Children = { cell, wave, reason }, Background = Brushes.Transparent, ToolTip = DetailsCounts.ErrorTip(error) };
    }

    /// <summary>In a narrow box: the target, then the before and after under it, each marked.</summary>
    private FrameworkElement MakeNarrowEdit(Edit edit)
    {
        var parts = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        TextBlock target = TargetCell(edit.Row.Target);
        target.Padding = new Thickness(8, 1, 8, 1);
        parts.Children.Add(target);
        ValueChange change = edit.Row.Change!;
        EditLogEntry entry = edit.Row.Entry;
        if (edit.Row.Kind == EditRowKind.SpacingOnly) parts.Children.Add(Flagged(Narrow(SpacingOnly(change, run?.ErrorAt(entry) is null ? "d-add" : "warn")), run?.ErrorAt(entry)));
        else
        {
            if (edit.Row.Kind != EditRowKind.Added) parts.Children.Add(Marked("−", "d-del", change.Before, entry.OldLeftOut));
            if (edit.Row.Kind != EditRowKind.Removed) parts.Children.Add(Marked("+", "d-add", change.After, entry.NewLeftOut, run?.ErrorAt(entry)));
        }
        return EditLine(edit.Group, parts);
    }

    private static TextBlock Narrow(TextBlock cell)
    {
        cell.Padding = new Thickness(8, 1, 8, 1);
        return cell;
    }

    private DockPanel Marked(string mark, string colour, IReadOnlyList<ValuePiece> pieces, int leftOut, RunValueError? error = null)
    {
        var marker = new TextBlock { Text = mark, Width = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 1, 0, 1), VerticalAlignment = VerticalAlignment.Top };
        marker.SetResourceReference(TextBlock.ForegroundProperty, colour);
        DockPanel.SetDock(marker, Dock.Left);
        TextBlock value = ValueCell(pieces, leftOut, error is null ? colour : "warn");
        value.Padding = new Thickness(0, 1, 8, 1);
        return new DockPanel { Children = { marker, Flagged(value, error) } };
    }

    private FrameworkElement MakeNote(Edit edit)
    {
        TextBlock note = OneLineUnlessWrapping(DetailsText.Line("muted"));
        note.Padding = new Thickness(8, 3, 8, 3);
        DetailsText.Add(note.Inlines, edit.Row.Entry.New, "muted", find);
        return EditLine(edit.Group, note);
    }

    private FrameworkElement MakeOneTargetHeading(OneTargetHeading heading)
    {
        TextBlock line = OneLineUnlessWrapping(DetailsText.Code());
        DetailsText.Add(line.Inlines, heading.Target, null, find);
        System.Windows.Documents.Run onEach = DetailsText.Piece("  " + Texts.Get("Files.Details.OnEachOf", ("descriptors", DetailsCounts.Descriptors(heading.Count))), "muted");
        onEach.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "ui-font");
        onEach.FontSize = 11.5;
        line.Inlines.Add(onEach);
        var box = new Bevel { Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 3, 8, 3), Child = line };
        box.SetResourceReference(BottomRightProperty, "line");
        box.SetResourceReference(BackgroundProperty, "panel");
        return box;
    }

    private FrameworkElement MakeNamedChange(EditGroup group)
    {
        EditRow edit = group.Rows[0];
        RunValueError? error = run?.ErrorAt(edit.Entry);
        var panel = new EditRowPanel(columns)
        {
            Children =
            {
                TargetCell(group.Title),
                ValueCell(edit.Change!.Before, edit.Entry.OldLeftOut, "d-del"),
                Flagged(ValueCell(edit.Change.After, edit.Entry.NewLeftOut, error is null ? "d-add" : "warn"), error),
            },
        };
        return EditLine(group, panel);
    }

    private FrameworkElement MakeNarrowNamedChange(EditGroup group)
    {
        EditRow edit = group.Rows[0];
        TextBlock name = TargetCell(group.Title);
        name.Padding = new Thickness(8, 1, 8, 1);
        var parts = new StackPanel { Margin = new Thickness(0, 2, 0, 2), Children = { name, Marked("−", "d-del", edit.Change!.Before, edit.Entry.OldLeftOut), Marked("+", "d-add", edit.Change.After, edit.Entry.NewLeftOut, run?.ErrorAt(edit.Entry)) } };
        return EditLine(group, parts);
    }

    private FrameworkElement MakeNamedNote(EditGroup group)
    {
        TextBlock note = Cell(DetailsText.Line("muted"));
        DetailsText.Add(note.Inlines, group.Rows[0].Entry.New, "muted", find);
        return EditLine(group, new EditRowPanel(columns) { Children = { TargetCell(group.Title), note } });
    }

    private static TextBlock MakeEndNote(string text)
    {
        TextBlock note = DetailsText.Line("caution");
        note.Text = text;
        note.FontSize = 12;
        note.Padding = new Thickness(8, 6, 8, 6);
        return note;
    }
}
