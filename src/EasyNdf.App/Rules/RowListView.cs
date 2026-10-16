using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.Data;
using EasyNdf.App.Text;

namespace EasyNdf.App.Rules;

/// <summary>
/// One list of the open rule's conditions or actions in the rule window (the HTML app's <c>.sect</c>): a heading line with
/// its buttons, and a sunken list with one line per row, showing the row's name or what it does. A click selects rows,
/// Ctrl-click and Shift-click select several; the keys act on the list clicked last.
/// </summary>
public sealed class RowListView : Grid
{
    private readonly RulesModel model;
    private readonly Bevel box = new();
    private readonly StackPanel lines = new();
    private readonly ScrollViewer scroller;
    private readonly string emptyText;

    /// <summary>Which list of the rule it shows: <see cref="RulesModel.Conditions"/>, <see cref="RulesModel.SourceConditions"/> or <see cref="RulesModel.Actions"/>.</summary>
    public string List { get; }

    /// <summary>The sunken box the rows are in.</summary>
    internal FrameworkElement Box => box;

    /// <summary>Raised when a row asks to be opened (double-click, Enter, Configure), with its place.</summary>
    public event Action<string, int>? RowOpenAsked;

    /// <summary>Raised for a right-click, with the row under the mouse or -1, so the window can show the menu.</summary>
    public event Action<RowListView, int>? MenuAsked;

    /// <summary>Builds a list.</summary>
    /// <param name="model">The Rules tab's data, which holds the rows and their selection.</param>
    /// <param name="list">Which list of the rule.</param>
    /// <param name="title">The heading, such as "Conditions (optional)".</param>
    /// <param name="gate">The Any/All dropdown that goes in the heading line, if the list has one.</param>
    /// <param name="what">"a condition" or "an action", for the + button's tooltip.</param>
    /// <param name="emptyText">What the list says with no rows.</param>
    public RowListView(RulesModel model, string list, string title, FrameworkElement? gate, string what, string emptyText)
    {
        this.model = model;
        List = list;
        this.emptyText = emptyText;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // .hline: 4 pixels below what is above it; the heading, then (after an empty span, so 12 pixels on) the rest
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 3) };
        var titleText = new TextBlock
        {
            Text = Texts.WithColon(title),
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            // #editor h3: 6 pixels above, which a web page keeps even in this line
            Margin = new Thickness(0, 6, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Themed(titleText, TextBlock.ForegroundProperty, "accent");
        heading.Children.Add(titleText);
        if (gate is not null)
        {
            gate.Margin = new Thickness(0, 0, 6, 0);
            heading.Children.Add(gate);
        }
        heading.Children.Add(IconButton("↑", Texts.Get("Rules.Rows.MoveUp.Tip"), () => model.MoveRows(list, -1), 6));
        heading.Children.Add(IconButton("↓", Texts.Get("Rules.Rows.MoveDown.Tip"), () => model.MoveRows(list, +1), 6));
        heading.Children.Add(IconButton("+", Texts.Get("Rules.Rows.Add.Tip", ("what", what)), AddRow, 0));
        Children.Add(heading);

        // .lv.sunken: the rows, 2 pixels in; the accent edge shows while the keys act on this list
        scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = lines };
        box.Child = scroller;
        box.Padding = new Thickness(2);
        Themed(box, Bevel.BackgroundProperty, "well");
        box.Focusable = true;
        box.FocusVisualStyle = null;
        box.PreviewMouseDown += Pressed;
        box.MouseRightButtonUp += (_, e) => { e.Handled = true; MenuAsked?.Invoke(this, RowAt(e.OriginalSource)); };
        box.KeyDown += KeyPressed;
        SetRow(box, 1);
        Children.Add(box);
        Refresh();
    }

    /// <summary>Draws the rows again, with their selection, after any change.</summary>
    public void Refresh()
    {
        bool isActive = model.ActiveRowList == List;
        box.SetResourceReference(Bevel.TopLeftProperty, isActive ? "accent" : "lo");
        box.SetResourceReference(Bevel.BottomRightProperty, isActive ? "accent" : "hi");

        lines.Children.Clear();
        JsonArray rows = model.RowsOf(List) ?? [];
        if (rows.Count == 0)
        {
            var empty = new TextBlock { Text = emptyText, Padding = new Thickness(3, 2, 3, 2), TextWrapping = TextWrapping.Wrap };
            Themed(empty, TextBlock.ForegroundProperty, "muted");
            lines.Children.Add(empty);
            return;
        }
        // the numbers are as wide as the longest of them (the HTML app's --nw, in the numbers' own font)
        double numberWidth = rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length * DigitWidth();
        for (int i = 0; i < rows.Count; i++) lines.Children.Add(Line(i, rows[i] as JsonObject ?? [], numberWidth));
    }

    /// <summary>One line: the row's number, then its name, or what it does in dim text.</summary>
    private Border Line(int index, JsonObject row, double numberWidth)
    {
        string name = JsonValues.AsText(row["Name"]).Trim();
        var number = new TextBlock
        {
            Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 11,
            // a web page's line for 11-pixel text, which sits on the same baseline as the name beside it
            LineHeight = 15.9375,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            MinWidth = numberWidth,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 2, 6, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var text = Controls.ListText.Follow(new TextBlock
        {
            Text = name != "" ? JsonValues.AsText(row["Name"]) : RulesModel.RowSummary(List, row),
        });
        Themed(number, TextBlock.ForegroundProperty, "muted");
        Themed(text, TextBlock.ForegroundProperty, name != "" ? "ink" : "muted");
        var content = new DockPanel();
        DockPanel.SetDock(number, Dock.Left);
        content.Children.Add(number);
        content.Children.Add(text);
        // .it: 2 by 3 pixels in; a selected row in the selection colour (a transparent one still takes clicks)
        var line = new Border { Padding = new Thickness(3, 2, 3, 2), Child = content, Tag = index, Background = Brushes.Transparent };
        if (model.IsRowSelected(List, index)) Themed(line, Border.BackgroundProperty, "sel");
        return line;
    }

    /// <summary>The width of a digit in the numbers' font, the CSS "ch".</summary>
    private double DigitWidth()
    {
        var typeface = new Typeface(FontFamily(), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var digit = new FormattedText("0", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, Brushes.Black, 1);
        return digit.WidthIncludingTrailingWhitespace;
    }

    private FontFamily FontFamily() => (FontFamily)GetValue(TextElement.FontFamilyProperty);

    /// <summary>
    /// A press on the list: it takes the keys, and the row under the mouse is selected. A double-click opens that row,
    /// or adds one on the list's empty part, unless the setting "Double-click adds" is off.
    /// </summary>
    private void Pressed(object sender, MouseButtonEventArgs e)
    {
        if (IsOnScrollBar(e.OriginalSource)) return;
        box.Focus();
        ModifierKeys keys = Keyboard.Modifiers;
        int index = RowAt(e.OriginalSource);
        model.PressRow(List, index, keys.HasFlag(ModifierKeys.Control), keys.HasFlag(ModifierKeys.Shift), e.ChangedButton == MouseButton.Right);
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2) return;
        if (index >= 0) RowOpenAsked?.Invoke(List, index);
        else if (AppData.Current.Settings["dblClickAdds"]?.GetValue<bool>() != false) AddRow(); // the setting "Double-click adds"
    }

    /// <summary>Whether a press is on the list's scrollbar, which only scrolls.</summary>
    private static bool IsOnScrollBar(object source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is System.Windows.Controls.Primitives.ScrollBar) return true;
        return false;
    }

    /// <summary>Enter opens the selected row; Up and Down move the selection, Shift with them extends it; Home and End go to the ends.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        ModifierKeys keys = Keyboard.Modifiers;
        if (keys.HasFlag(ModifierKeys.Control) || keys.HasFlag(ModifierKeys.Alt)) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            IReadOnlyList<int> picked = model.SelectedRowsOf(List);
            if (picked.Count == 1) RowOpenAsked?.Invoke(List, picked[0]);
            return;
        }
        int step = e.Key switch { Key.Down or Key.End => 1, Key.Up or Key.Home => -1, _ => 0 };
        if (step == 0) return;
        e.Handled = true;
        int at = model.StepRow(List, step, toEnd: e.Key is Key.Home or Key.End, shift: keys.HasFlag(ModifierKeys.Shift));
        if (at >= 0 && at < lines.Children.Count) ((FrameworkElement)lines.Children[at]).BringIntoView();
    }

    /// <summary>Adds a row below the selected one; it then opens.</summary>
    private void AddRow()
    {
        int at = model.AddRow(List);
        if (at >= 0) RowOpenAsked?.Invoke(List, at);
    }

    /// <summary>Gives the list the keys, as a click on it does.</summary>
    public void TakeKeys() => box.Focus();

    /// <summary>The place of the row an element is on, or -1 for the list's empty part.</summary>
    private int RowAt(object source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null && !ReferenceEquals(at, box); at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is Border { Tag: int index } line && ReferenceEquals(line.Parent, lines)) return index;
        return -1;
    }

    private Button IconButton(string symbol, string tip, Action run, double gapAfter)
    {
        var button = new Button { Content = symbol, ToolTip = tip, Margin = new Thickness(0, 0, gapAfter, 0), VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "IconButton");
        button.Click += (_, _) => { model.ActiveRowList = List; run(); };
        return button;
    }

    /// <summary>Gives a property a theme colour that follows the theme.</summary>
    private static void Themed(FrameworkElement element, DependencyProperty property, string key) => element.SetResourceReference(property, key);
}
