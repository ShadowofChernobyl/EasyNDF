using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyNdf.App.Text;

namespace EasyNdf.App.Controls;

/// <summary>
/// The usage readout's slow moments: each time a click or a key waited longer than 100 ms, a line with when, how long,
/// where in the app the user was, what they had just clicked or pressed, and the job running then. The bar's Slow
/// Moments button opens the list, and ⧉ copies everything the readout knows as a report for a bug.
/// </summary>
public sealed partial class UsageReadout
{
    /// <summary>How many slow moments are kept; the oldest go first.</summary>
    private const int SlowMomentsKept = 50;

    /// <summary>How long after a click or a key a slow moment is still put down to it.</summary>
    private static readonly TimeSpan ActionCounts = TimeSpan.FromSeconds(3);

    /// <summary>A slow moment.</summary>
    /// <param name="At">When the wait ended.</param>
    /// <param name="Waited">How long it was, in milliseconds.</param>
    /// <param name="Where">Where in the app the user was.</param>
    /// <param name="Did">What they had just clicked or pressed.</param>
    /// <param name="Job">The job running then; empty for none.</param>
    private sealed record SlowMomentLine(DateTime At, double Waited, string Where, string Did, string Job);

    private readonly List<SlowMomentLine> slowMoments = [];
    private readonly TextBlock slowCount = new() { TextAlignment = TextAlignment.Center, MinWidth = 18, Padding = new Thickness(4, 0, 4, 0) };
    private readonly Border slowCountBox = new() { BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private Border? slowButton;
    private readonly TextBlock slowName = new() { VerticalAlignment = VerticalAlignment.Center };
    private Button? copyButton;
    private readonly Popup slowList = new() { Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true };
    private readonly Popup copied = new() { Placement = PlacementMode.Top, AllowsTransparency = true };

    /// <summary>The last click or key: what it was and when.</summary>
    private (string What, DateTime At)? lastAction;

    /// <summary>When the app was first free after starting; a wait before then is the app starting, not something the user did.</summary>
    private DateTime readySince = DateTime.MaxValue;

    /// <summary>Whether clicks and keys are being noted, while the readout is on.</summary>
    private bool watching;

    /// <summary>Whether the window-wide handlers are in place; they are put in once and look at <see cref="watching"/>.</summary>
    private static bool handlersAdded;

    private static readonly List<WeakReference<UsageReadout>> readouts = [];

    // ---- what the user clicked or pressed ----

    /// <summary>Starts or stops noting the clicks and keys that slow moments are put down to.</summary>
    private void WatchInput(bool on)
    {
        watching = on;
        if (on && readySince == DateTime.MaxValue)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => readySince = DateTime.Now);
        if (!on || handlersAdded) return;
        handlersAdded = true;
        // every window's clicks and keys, the handled ones too, before anything acts on them
        EventManager.RegisterClassHandler(typeof(Window), PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) => Noted(Clicked(e.OriginalSource as DependencyObject))), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(Window), PreviewKeyDownEvent, new KeyEventHandler((_, e) => Noted(Pressed(e))), handledEventsToo: true);
        lock (readouts) readouts.Add(new WeakReference<UsageReadout>(this));
        WatchGestures();
    }

    private static void Noted(string? what)
    {
        if (what is null) return;
        lock (readouts)
            foreach (WeakReference<UsageReadout> reference in readouts)
                if (reference.TryGetTarget(out UsageReadout? readout) && readout.watching) readout.lastAction = (what, DateTime.Now);
    }

    /// <summary>
    /// What a click was on, as the user sees it: the text of the button, tab or line clicked ("clicked Run Export"),
    /// else the nearest text under the pointer, else a box's name.
    /// </summary>
    private static string Clicked(DependencyObject? hit)
    {
        for (DependencyObject? node = hit; node is not null; node = ParentOf(node))
            if (node is ScrollBar) return Texts.Get("Main.Usage.ScrollBar");
        for (DependencyObject? node = hit; node is not null; node = ParentOf(node))
            if (node is ButtonBase or TabItem or ListBoxItem or MenuItem or TreeViewItem or HeaderedContentControl && TextIn(node) is string text)
                return Texts.Get("Main.Usage.Clicked", ("what", text));
        for (DependencyObject? node = hit; node is not null; node = ParentOf(node))
            if (node is TextBlock { Text: { Length: > 0 } words } && !string.IsNullOrWhiteSpace(words))
                return Texts.Get("Main.Usage.Clicked", ("what", Shortened(words)));
        for (DependencyObject? node = hit; node is not null; node = ParentOf(node))
            if (node is FrameworkElement { Name.Length: > 0 } named)
                return Texts.Get("Main.Usage.Clicked", ("what", named.Name));
        return Texts.Get("Main.Usage.ClickedSomewhere");
    }

    /// <summary>A key and its modifiers ("pressed Ctrl+S"); typing in a box is just "typing".</summary>
    private static string Pressed(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return Texts.Get("Main.Usage.Typing");
        ModifierKeys modifiers = Keyboard.Modifiers;
        bool isTyping = Keyboard.FocusedElement is TextBoxBase && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0
            && (key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9 or Key.Space or Key.Back or >= Key.Oem1 and <= Key.Oem102);
        if (isTyping) return Texts.Get("Main.Usage.Typing");
        string combination = (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "")
            + (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + key;
        return Texts.Get("Main.Usage.Pressed", ("key", combination));
    }

    /// <summary>The first text a control shows: its header or content when that is text, else the first text inside it.</summary>
    private static string? TextIn(DependencyObject control)
    {
        if (control is HeaderedContentControl { Header: string header } && !string.IsNullOrWhiteSpace(header)) return Shortened(header);
        if (control is ContentControl { Content: string content } && !string.IsNullOrWhiteSpace(content)) return Shortened(content);
        var waiting = new Queue<DependencyObject>([control]);
        for (int looked = 0; waiting.Count > 0 && looked < 200; looked++)
        {
            DependencyObject node = waiting.Dequeue();
            if (node is TextBlock { Text: { Length: > 0 } words } && !string.IsNullOrWhiteSpace(words)) return Shortened(words);
            if (node is not Visual) continue;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) waiting.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    private static string Shortened(string text)
    {
        string oneLine = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length > 50 ? oneLine[..47] + "…" : oneLine;
    }

    /// <summary>An element's parent on screen, or the element that holds it (a Run's TextBlock).</summary>
    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    // ---- the slow moments ----

    /// <summary>The screen waited longer than it should: a line is kept, put down to the last click or key if it was recent.</summary>
    private void SlowMoment(double waited)
    {
        DateTime now = DateTime.Now;
        DateTime started = now - TimeSpan.FromMilliseconds(waited);
        // during a drag or a scroll the screen is redrawn step after step and gets to clicks only once the mouse pauses:
        // the wait is then the gesture's length, so the gesture is told by its steps instead (FinishGesture)
        if (DuringGesture(started)) return;
        string did = lastAction is (string what, DateTime at) && at >= started - ActionCounts ? what
            : started < readySince ? Texts.Get("Main.Usage.Starting") : Texts.Get("Main.Usage.NoAction");
        // the job running, or one that ended during the wait
        string job = Work.Current?.Name ?? Work.Finished.FirstOrDefault(finished => finished.EndedAt >= started)?.Name ?? "";
        AddSlowMoment(now, waited, did, job);
    }

    /// <summary>Keeps a slow moment's line, the newest first, and shows the count.</summary>
    private void AddSlowMoment(DateTime at, double waited, string did, string job)
    {
        slowMoments.Insert(0, new SlowMomentLine(at, waited, Where?.Invoke() ?? "", did, job));
        SessionLog.Write("Slow", $"{Math.Round(waited)} ms · {Where?.Invoke()} · {did}" + (job == "" ? "" : " · " + job));
        if (slowMoments.Count > SlowMomentsKept) slowMoments.RemoveAt(slowMoments.Count - 1);
        ShowSlowCount();
        if (slowList.IsOpen) FillSlowList();
    }

    /// <summary>The bar's Slow Moments button: its name and how many there are, yellow when there are any.</summary>
    private Border SlowMomentsButton()
    {
        slowCountBox.Child = slowCount;
        slowButton = new Border
        {
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Focusable = true,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { slowName, slowCountBox } },
        };
        slowButton.SetResourceReference(Border.BorderBrushProperty, "line");
        slowButton.MouseLeftButtonUp += (_, _) => ToggleSlowList();
        slowButton.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; ToggleSlowList(); } };
        slowButton.MouseEnter += (_, _) => slowButton.SetResourceReference(Border.BackgroundProperty, "sel");
        slowButton.MouseLeave += (_, _) => slowButton.Background = Brushes.Transparent;
        slowList.PlacementTarget = slowButton;
        ShowSlowCount();
        return slowButton;
    }

    /// <summary>The bar's own texts, again in the language in use.</summary>
    private void ShowNames()
    {
        slowName.Text = Texts.Get("Main.Usage.SlowMoments");
        if (copyButton is not null) copyButton.ToolTip = Texts.Get("Main.Usage.Copy.Tip");
        ShowBusy();
    }

    private void ShowSlowCount()
    {
        slowCount.Text = slowMoments.Count.ToString(CultureInfo.CurrentCulture);
        string colour = slowMoments.Count > 0 ? "caution" : "muted";
        slowCount.SetResourceReference(TextBlock.ForegroundProperty, colour);
        slowCountBox.SetResourceReference(Border.BorderBrushProperty, slowMoments.Count > 0 ? "caution" : "line");
    }

    private void ToggleSlowList()
    {
        if (slowList.IsOpen) { slowList.IsOpen = false; return; }
        FillSlowList();
        slowList.IsOpen = true;
        // right-aligned with the button, as the list is wider than it
        slowList.UpdateLayout();
        if (slowList.Child is FrameworkElement shown && slowButton is not null)
            slowList.HorizontalOffset = slowButton.ActualWidth - shown.ActualWidth;
    }

    /// <summary>The list: a heading with Clear and ✕, a line for each slow moment, and a note.</summary>
    private void FillSlowList()
    {
        var heading = new DockPanel { Margin = new Thickness(10, 6, 6, 6) };
        var close = new Button { Content = "✕", Style = (Style)FindResource("IconButton"), Margin = new Thickness(6, 0, 0, 0), ToolTip = Texts.Get("Main.Usage.SlowMoments.Close") };
        close.Click += (_, _) => slowList.IsOpen = false;
        var clear = new Button { Content = Texts.Get("Main.Usage.SlowMoments.Clear"), Style = (Style)FindResource("SmallButton"), IsEnabled = slowMoments.Count > 0 };
        clear.Click += (_, _) => { slowMoments.Clear(); ShowSlowCount(); FillSlowList(); };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(clear, Dock.Right);
        heading.Children.Add(close);
        heading.Children.Add(clear);
        heading.Children.Add(Coloured(Texts.Get("Main.Usage.SlowMoments"), "accent"));

        var body = new StackPanel();
        body.Children.Add(heading);
        if (slowMoments.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = Texts.Get("Main.Usage.SlowMoments.None"), Margin = new Thickness(10, 0, 10, 10), TextWrapping = TextWrapping.Wrap, MaxWidth = 560 });
        }
        else
        {
            body.Children.Add(SlowTable());
            var note = Coloured(Texts.Get("Main.Usage.SlowMoments.Kept", ("count", SlowMomentsKept)), "muted");
            note.Margin = new Thickness(10, 6, 10, 8);
            body.Children.Add(note);
        }
        var panel = new Bevel { Child = body, MinWidth = 380 };
        panel.SetResourceReference(Bevel.TopLeftProperty, "hi");
        panel.SetResourceReference(Bevel.BottomRightProperty, "lo");
        panel.SetResourceReference(Bevel.BackgroundProperty, "panel");
        TextElement.SetFontSize(panel, 12);
        panel.SetResourceReference(TextElement.ForegroundProperty, "ink");
        slowList.Child = panel;
    }

    /// <summary>The slow moments as a table: when, how long, where, what the user did, and the job running.</summary>
    private Grid SlowTable()
    {
        var table = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        string[] headings = [Texts.Get("Main.Usage.SlowMoments.When"), Texts.Get("Main.Usage.SlowMoments.HowLong"), Texts.Get("Main.Usage.SlowMoments.Where"),
            Texts.Get("Main.Usage.SlowMoments.Did"), Texts.Get("Main.Usage.SlowMoments.Job")];
        foreach (string _ in headings) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition());
        for (int column = 0; column < headings.Length; column++) Place(table, Coloured(headings[column], "muted"), 0, column);
        for (int i = 0; i < slowMoments.Count; i++)
        {
            SlowMomentLine line = slowMoments[i];
            table.RowDefinitions.Add(new RowDefinition());
            Place(table, new TextBlock { Text = line.At.ToString("T", CultureInfo.CurrentCulture) }, i + 1, 0);
            var waited = Coloured(Milliseconds(line.Waited), line.Waited > 250 ? "warn" : "caution");
            waited.TextAlignment = TextAlignment.Right;
            Place(table, waited, i + 1, 1);
            Place(table, new TextBlock { Text = line.Where }, i + 1, 2);
            Place(table, new TextBlock { Text = line.Did }, i + 1, 3);
            Place(table, Coloured(line.Job == "" ? Texts.Get("Main.Usage.SlowMoments.NoJob") : line.Job, "muted"), i + 1, 4);
        }
        return new Grid { Children = { new ScrollViewer { Content = table, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } };
    }

    private static void Place(Grid table, TextBlock cell, int row, int column)
    {
        cell.Margin = new Thickness(column == 0 ? 0 : 14, 2, 0, 2);
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        table.Children.Add(cell);
    }

    // ---- the report ----

    /// <summary>The ⧉ button: copies the report, and says so for a moment above it.</summary>
    private Button CopyButton()
    {
        var copy = new Button
        {
            Content = "⧉",
            Style = (Style)FindResource("IconButton"),
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            FontSize = 14,
            Margin = new Thickness(6, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        copyButton = copy;
        copied.PlacementTarget = copy;
        copy.Click += (_, _) => CopyReport();
        return copy;
    }

    private void CopyReport()
    {
        try { Clipboard.SetText(Report()); }
        catch (System.Runtime.InteropServices.ExternalException) { return; }
        var said = Coloured(Texts.Count("Main.Usage.Copied", slowMoments.Count), "accent");
        said.Margin = new Thickness(10, 4, 10, 4);
        var panel = new Bevel { Child = said };
        panel.SetResourceReference(Bevel.TopLeftProperty, "hi");
        panel.SetResourceReference(Bevel.BottomRightProperty, "lo");
        panel.SetResourceReference(Bevel.BackgroundProperty, "panel");
        TextElement.SetFontSize(panel, 12);
        copied.Child = panel;
        copied.IsOpen = true;
        copied.UpdateLayout();
        if (copied.PlacementTarget is FrameworkElement button) copied.HorizontalOffset = button.ActualWidth - panel.ActualWidth;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) => { timer.Stop(); copied.IsOpen = false; };
        timer.Start();
    }

    /// <summary>Everything the readout knows, as text for a bug report.</summary>
    private string Report()
    {
        var report = new StringBuilder();
        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        report.AppendLine(Texts.Get("Main.Usage.Report.Title", ("version", version), ("time", DateTime.Now.ToString("g", CultureInfo.CurrentCulture)),
            ("windows", Environment.OSVersion.VersionString), ("cores", Environment.ProcessorCount)));
        report.AppendLine();
        if (last is UsageReading reading)
        {
            report.AppendLine(Texts.Get("Main.Usage.Report.Now", ("figures", string.Join(" · ", figures.Select(figure => figure.Line(reading, peakToo: false))))));
            report.AppendLine(Texts.Get("Main.Usage.Report.Peaks", ("figures", string.Join(" · ", figures.Select(figure => figure.Line(reading, peakToo: true))))));
            List<MemoryPart> parts = PartsOf(reading);
            if (parts.Count > 0)
                report.AppendLine(Texts.Get("Main.Usage.Report.Parts", ("parts", string.Join(" · ", parts.Select(part => part.Name + " " + part.SizeText(Size))))));
            report.AppendLine(Texts.Get("Main.Usage.Report.Paused", ("share", reading.PausedShare.ToString("0.0", CultureInfo.CurrentCulture))));
            report.AppendLine(Texts.Get("Main.Usage.Report.Threads", ("threads", reading.Threads), ("handles", reading.Handles)));
        }
        report.AppendLine();
        IReadOnlyList<Work.FinishedJob> jobs = Work.Finished;
        report.AppendLine(Texts.Get("Main.Usage.Report.Jobs", ("jobs", jobs.Count == 0 ? Texts.Get("Main.Usage.Report.None")
            : string.Join("; ", jobs.Select(job => job.EndedAt.ToString("T", CultureInfo.CurrentCulture) + " " + job.Name + " (" + job.Took.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s)")))));
        report.AppendLine();
        report.AppendLine(Texts.Count("Main.Usage.Report.SlowMoments", slowMoments.Count));
        foreach (SlowMomentLine line in slowMoments)
            report.AppendLine("  " + line.At.ToString("T", CultureInfo.CurrentCulture) + "  " + Milliseconds(line.Waited) + "  " + line.Where + "  " + line.Did
                + "  [" + (line.Job == "" ? Texts.Get("Main.Usage.SlowMoments.NoJob") : line.Job) + "]");
        return report.ToString();
    }
}
