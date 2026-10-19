using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Editor;

/// <summary>
/// The File Editor's problems, in its status strip: with one, what it is, which goes to it when clicked; with more,
/// how many, which opens a list of them above the strip. A click on a problem in the list goes to it, and the list stays
/// open until its × or Escape closes it, so one problem after another can be looked at.
/// </summary>
public sealed class ProblemList
{
    /// <summary>The list's most height: past it, it scrolls.</summary>
    private const double ListHeight = 240;

    private readonly SyntaxProblems problems;
    private readonly TextBlock stripText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Hyperlink stripLink = new() { TextDecorations = null, Cursor = Cursors.Hand };
    private readonly ListBox list = new() { MaxHeight = ListHeight };

    /// <summary>Builds the strip's text and the list, both hidden while there are no problems.</summary>
    public ProblemList(SyntaxProblems problems)
    {
        this.problems = problems;
        stripText.Inlines.Add(stripLink);
        stripText.SetResourceReference(TextBlock.ForegroundProperty, "warn");
        stripLink.SetResourceReference(TextElement.ForegroundProperty, "warn");
        stripLink.Click += (_, _) => StripClicked();
        Panel = BuildPanel();
        problems.Changed += Show;
    }

    /// <summary>The text for the status strip, right of the caret's line and column.</summary>
    public FrameworkElement StripText => stripText;

    /// <summary>The list, laid over the bottom left of the text, above the strip; collapsed while closed.</summary>
    public FrameworkElement Panel { get; }

    /// <summary>Whether the list is open.</summary>
    public bool IsOpen => Panel.Visibility == Visibility.Visible;

    /// <summary>Closes the list, and gives the text the keys again.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        Panel.Visibility = Visibility.Collapsed;
        problems.FocusText();
    }

    // ---- showing them ----

    /// <summary>Shows the problems found: in the strip, and in the list while it is open.</summary>
    private void Show()
    {
        IReadOnlyList<ProblemMark> found = problems.Problems;
        stripText.Visibility = found.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (found.Count == 0)
        {
            Panel.Visibility = Visibility.Collapsed;
            return;
        }
        stripLink.Inlines.Clear();
        stripLink.Inlines.Add(new Run("⚠ " + (found.Count == 1 ? OneLine(found[0]) : Texts.Count("Editor.Problems.Count", found.Count))));
        stripText.ToolTip = found.Count == 1 ? Texts.Get("Editor.Problems.GoTo.Tip") : Texts.Get("Editor.Problems.Show.Tip");
        if (IsOpen) FillList();
    }

    /// <summary>A problem in a line: "Line 8: expected ")"".</summary>
    private string OneLine(ProblemMark problem) =>
        Texts.Get("Editor.Problems.One", ("line", problem.Line(problems.Document!)), ("problem", problem.Message));

    /// <summary>The strip's text, as it shows now.</summary>
    internal string ShownText => stripText.Visibility == Visibility.Visible ? new TextRange(stripLink.ContentStart, stripLink.ContentEnd).Text : "";

    /// <summary>One problem goes to it; more open the list, or close it again.</summary>
    internal void StripClicked()
    {
        IReadOnlyList<ProblemMark> found = problems.Problems;
        if (found.Count == 1)
        {
            problems.Reveal(found[0]);
            problems.FocusText();
            return;
        }
        if (IsOpen) { Close(); return; }
        FillList();
        Panel.Visibility = Visibility.Visible;
        list.Focus();
    }

    /// <summary>Lists the problems: each one's line, dimmed, then what is wrong.</summary>
    private void FillList()
    {
        list.Items.Clear();
        if (problems.Document is null) return;
        foreach (ProblemMark problem in problems.Problems)
        {
            var line = new TextBlock { Text = Texts.Get("Editor.Problems.Line", ("line", problem.Line(problems.Document))), MinWidth = 64 };
            line.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            var row = new DockPanel { Tag = problem, ToolTip = Texts.Get("Editor.Problems.GoTo.Tip") };
            row.Children.Add(line);
            row.Children.Add(new TextBlock { Text = problem.Message, TextTrimming = TextTrimming.CharacterEllipsis });
            list.Items.Add(row);
        }
    }

    // ---- the list ----

    /// <summary>A raised box with the heading Problems and ×, and the list under them.</summary>
    private Bevel BuildPanel()
    {
        var title = new TextBlock { Text = Texts.Get("Editor.Problems.Title"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        var close = new Button { Content = "×", ToolTip = Texts.Get("Editor.Problems.Close.Tip") };
        close.SetResourceReference(FrameworkElement.StyleProperty, "IconButton");
        close.Click += (_, _) => Close();
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(close, Dock.Right);
        heading.Children.Add(close);
        heading.Children.Add(title);

        list.SetResourceReference(FrameworkElement.StyleProperty, "SuggestionList");
        list.PreviewMouseLeftButtonUp += (_, e) => GoToRowAt(e);
        list.KeyDown += ListKeyDown;
        var content = new DockPanel();
        DockPanel.SetDock(heading, Dock.Top);
        content.Children.Add(heading);
        content.Children.Add(list);

        var panel = new Bevel { Padding = new Thickness(10, 8, 10, 10), Width = 480, Child = content, Visibility = Visibility.Collapsed };
        panel.SetResourceReference(Bevel.TopLeftProperty, "hi");
        panel.SetResourceReference(Bevel.BottomRightProperty, "lo");
        panel.SetResourceReference(Bevel.BackgroundProperty, "panel");
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        panel.VerticalAlignment = VerticalAlignment.Bottom;
        panel.Margin = new Thickness(6, 0, 0, 4);
        return panel;
    }

    /// <summary>Goes to the problem at a place in the list, as a click on it does.</summary>
    internal void GoToRow(int index)
    {
        if (list.Items[index] is DockPanel { Tag: ProblemMark problem }) problems.Reveal(problem);
    }

    /// <summary>A click on a problem goes to it in the text; the list stays open.</summary>
    private void GoToRowAt(MouseButtonEventArgs e)
    {
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null && at != list; at = System.Windows.Media.VisualTreeHelper.GetParent(at))
            if (at is DockPanel { Tag: ProblemMark problem })
            {
                problems.Reveal(problem);
                return;
            }
    }

    /// <summary>Enter goes to the problem chosen with the arrow keys; Escape closes the list.</summary>
    private void ListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && list.SelectedItem is DockPanel { Tag: ProblemMark problem })
        {
            e.Handled = true;
            problems.Reveal(problem);
        }
    }
}
