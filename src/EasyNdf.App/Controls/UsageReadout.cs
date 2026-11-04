using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EasyNdf.App.Text;

namespace EasyNdf.App.Controls;

/// <summary>A part of the app's memory the Memory figure's tooltip names: what holds it, and about how much.</summary>
/// <param name="Name">What holds it, such as "Files on the Files tab".</param>
/// <param name="Bytes">How much it takes, in bytes.</param>
/// <param name="IsAbout">Whether the size is worked out roughly, not counted: written with ≈.</param>
public sealed record MemoryPart(string Name, long Bytes, bool IsAbout = false)
{
    /// <summary>The size as the card and the report write it: "≈ 1.44 GB" for a rough one.</summary>
    public string SizeText(Func<double, string> size) => (IsAbout ? "≈ " : "") + size(Bytes / (1024.0 * 1024));
}

/// <summary>
/// The usage readout (Settings, Debug, "Show usage readout"): a bar along the bottom of the window with what the app
/// takes from the computer, each figure with its last minute as a small graph; the job the app is busy with; its slow
/// moments (<see cref="UsageReadout"/>'s SlowMoments part); and a button that copies all of it as a report. A figure
/// over its aim turns the caution colour, and well over it the warning colour; hovering over one shows its peak since
/// the app started, a larger graph and what it measures. It measures only while it is shown (<see cref="UsageSampler"/>).
/// </summary>
public sealed partial class UsageReadout : Bevel
{
    /// <summary>How many readings each graph shows.</summary>
    private const int GraphLength = 60;

    private const double Megabyte = 1024 * 1024;

    /// <summary>
    /// The theme colours of the memory parts, in order, one each: files, rule sets, Armory data, pictures, editor, Undo,
    /// mods' Armory changes, other .NET data, unused (grey) and outside .NET. A part past them has the last one.
    /// </summary>
    private static readonly string[] PartColours =
        ["accent", "side-a", "caution", "usage-modded", "side-b", "s-const", "s-number", "s-keyword", "s-func", "usage-waiting", "muted", "s-param"];

    private readonly List<Figure> figures;
    private readonly TextBlock busyLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly TextBlock busyText = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private UsageSampler? sampler;
    private UsageReading? last;

    /// <summary>The parts of the app's memory it can count, for the Memory figure's tooltip; set by the main view.</summary>
    public Func<UsageReading, IReadOnlyList<MemoryPart>>? MemoryParts { get; set; }

    /// <summary>Where in the app the user is, such as "Armory › Divisions", for the slow moments; set by the main view.</summary>
    public Func<string>? Where { get; set; }

    /// <summary>Builds the bar, hidden until <see cref="Apply"/> finds it switched on.</summary>
    public UsageReadout()
    {
        Visibility = Visibility.Collapsed;
        // sunk into the window, as the file list and the other boxes are, with the 10 pixels round it that each tab's page
        // has (the page above already keeps its own 10 below it)
        Margin = new Thickness(10, 0, 10, 10);
        SetResourceReference(TopLeftProperty, "lo");
        SetResourceReference(BottomRightProperty, "hi");
        SetResourceReference(BackgroundProperty, "well");
        TextElement.SetFontSize(this, 12);
        busyLabel.SetResourceReference(TextBlock.ForegroundProperty, "muted");

        figures =
        [
            new("Main.Usage.Cpu", "Main.Usage.Cpu.What", r => r.Cpu, Percent, Caution: 80, Warning: 95, Scale: 100,
                Details: r => [(Texts.Get("Main.Usage.Threads"), r.Threads.ToString(CultureInfo.CurrentCulture)),
                               (Texts.Get("Main.Usage.Handles"), r.Handles.ToString(CultureInfo.CurrentCulture))],
                Footer: LastJobs),
            new("Main.Usage.Memory", "Main.Usage.Memory.What", r => r.Memory / Megabyte, Size, Caution: 900, Warning: 2048, Scale: 1024,
                Parts: r => PartsOf(r)),
            new("Main.Usage.DotNet", "Main.Usage.DotNet.What", r => r.DotNet / Megabyte, Size, Caution: double.MaxValue, Warning: double.MaxValue, Scale: 1024,
                Details: r => [(Texts.Get("Main.Usage.Paused"), Texts.Get("Main.Usage.Paused.Value",
                                    ("share", r.PausedShare.ToString("0.0", CultureInfo.CurrentCulture)), ("seconds", r.Paused.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)))),
                               (Texts.Get("Main.Usage.CleanUps"), Texts.Get("Main.Usage.CleanUps.Counts", ("small", r.CleanUps.Small), ("middle", r.CleanUps.Middle), ("full", r.CleanUps.Full)))]),
            new("Main.Usage.Gpu", "Main.Usage.Gpu.What", r => r.Gpu ?? double.NaN, Percent, Caution: 80, Warning: 95, Scale: 100),
            new("Main.Usage.Disk", "Main.Usage.Disk.What", r => r.DiskPerSecond / Megabyte, PerSecond, Caution: double.MaxValue, Warning: double.MaxValue, Scale: 10),
            new("Main.Usage.UiDelay", "Main.Usage.UiDelay.What", r => r.UiDelay, Milliseconds, Caution: UsageSampler.SlowWait, Warning: 250, Scale: 120),
        ];

        var row = new DockPanel { Height = 30 };
        Button copy = CopyButton();
        DockPanel.SetDock(copy, Dock.Right);
        row.Children.Add(copy);
        Border slow = SlowMomentsButton();
        DockPanel.SetDock(slow, Dock.Right);
        row.Children.Add(slow);
        foreach (Figure figure in figures)
        {
            DockPanel.SetDock(figure.Cell, Dock.Left);
            row.Children.Add(figure.Cell);
        }
        // what fills the rest: the job the app is busy with, or "Idle"
        row.Children.Add(new DockPanel { Margin = new Thickness(10, 0, 10, 0), Children = { busyLabel, busyText } });
        Child = row;
        ShowNames();
        Texts.LanguageChanged += () => { ShowNames(); if (last is not null) Show(last); };
    }

    /// <summary>Shows the bar and measures, or hides it and stops, as the setting says; a new update time starts again.</summary>
    public void Apply()
    {
        sampler?.Dispose();
        sampler = null;
        bool on = AppData.Current.Settings["usageReadout"]?.GetValue<bool>() == true;
        Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        WatchInput(on);
        if (!on) return;
        string every = (string?)AppData.Current.Settings["usageEvery"] ?? "1";
        double seconds = double.TryParse(every, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 1;
        sampler = new UsageSampler(Dispatcher, TimeSpan.FromSeconds(Math.Clamp(seconds, 0.5, 10)), Show, SlowMoment);
    }

    /// <summary>Shows a reading.</summary>
    private void Show(UsageReading reading)
    {
        last = reading;
        foreach (Figure figure in figures) figure.Show(reading);
        ShowBusy();
    }

    /// <summary>The job the app is busy with and how long it has run, or "Idle" in grey when none runs.</summary>
    private void ShowBusy()
    {
        if (Work.Current is (string name, TimeSpan soFar))
        {
            busyLabel.Text = Texts.Get("Main.Usage.Busy");
            busyLabel.Visibility = Visibility.Visible;
            busyText.Text = Texts.Get("Main.Usage.Busy.Job", ("job", name), ("seconds", (int)soFar.TotalSeconds));
            busyText.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        }
        else
        {
            busyLabel.Visibility = Visibility.Collapsed;
            busyText.Text = Texts.Get("Main.Usage.Idle");
            busyText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        }
    }

    /// <summary>The last jobs that finished and how long each took, for CPU's tooltip; empty when none has.</summary>
    private static string LastJobs()
    {
        IReadOnlyList<Work.FinishedJob> jobs = Work.Finished;
        if (jobs.Count == 0) return "";
        return Texts.Get("Main.Usage.LastJobs", ("jobs", string.Join(", ", jobs.Select(job =>
            Texts.Get("Main.Usage.LastJobs.One", ("job", job.Name), ("seconds", job.Took.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)))))));
    }

    /// <summary>
    /// The parts of the memory the app can count, and the rest, which is the app itself, its screens and what Windows
    /// keeps for it; none when the main view gives no parts.
    /// </summary>
    private List<MemoryPart> PartsOf(UsageReading reading)
    {
        if (MemoryParts is null) return [];
        return [.. MemoryParts(reading)];
    }

    // ---- how each figure is written ----

    private static string Percent(double value) => double.IsNaN(value) ? "–" : Texts.Get("Main.Usage.Percent", ("n", Math.Round(value).ToString(CultureInfo.CurrentCulture)));

    private static string Size(double megabytes) => megabytes >= 1024
        ? Texts.Get("Main.Usage.GB", ("n", (megabytes / 1024).ToString("0.00", CultureInfo.CurrentCulture)))
        : Texts.Get("Main.Usage.MB", ("n", Math.Round(megabytes).ToString(CultureInfo.CurrentCulture)));

    private static string PerSecond(double megabytes) => Texts.Get("Main.Usage.MBPerSecond",
        ("n", megabytes < 0.05 ? "0" : megabytes < 10 ? megabytes.ToString("0.0", CultureInfo.CurrentCulture) : Math.Round(megabytes).ToString(CultureInfo.CurrentCulture)));

    private static string Milliseconds(double value) => Texts.Get("Main.Usage.Milliseconds", ("n", Math.Round(value).ToString(CultureInfo.CurrentCulture)));

    /// <summary>How a figure stands against its aim: below it, over it, or well over it.</summary>
    private enum Level { Fine, Caution, Warning }

    /// <summary>The theme colour of a level.</summary>
    private static string ColourOf(Level level) => level switch { Level.Warning => "warn", Level.Caution => "caution", _ => "accent" };

    /// <summary>A text in a colour of the theme.</summary>
    private static TextBlock Coloured(string text, string colour)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(TextBlock.ForegroundProperty, colour);
        return block;
    }

    /// <summary>
    /// One figure of the bar: its name, its value and its graph, and the tooltip with the details.
    /// </summary>
    /// <param name="Name">The text name of its label, such as "Main.Usage.Cpu".</param>
    /// <param name="What">The text name of what it measures, for the tooltip.</param>
    /// <param name="ValueOf">The figure in a reading, in the unit it is written in; NaN when it is not known.</param>
    /// <param name="Write">How the figure is written.</param>
    /// <param name="Caution">Above this, the figure takes the caution colour (the aim: 900 MB of memory, 100 ms of delay).</param>
    /// <param name="Warning">Above this, the warning colour.</param>
    /// <param name="Scale">The least the graphs reach up to, so a quiet minute stays low on them.</param>
    /// <param name="Details">More lines for the tooltip, as label and value.</param>
    /// <param name="Parts">What the figure is made of, for a bar split by part in the tooltip, each with its line.</param>
    /// <param name="Footer">A line at the tooltip's end, such as the last jobs; nothing when empty.</param>
    private sealed record Figure(string Name, string What, Func<UsageReading, double> ValueOf, Func<double, string> Write,
        double Caution, double Warning, double Scale, Func<UsageReading, List<(string Label, string Value)>>? Details = null,
        Func<UsageReading, List<MemoryPart>>? Parts = null, Func<string>? Footer = null)
    {
        private readonly List<double> history = [];
        private double peak;
        private UsageReading? shownReading;
        private Border? cell;
        private ToolTip? tip;
        private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock value = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Right, Margin = new Thickness(6, 0, 6, 0) };
        private readonly Graph graph = new() { Width = 54, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock tipHeading = new() { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
        private readonly Grid tipRows = new();
        private readonly Grid tipSplit = new() { Height = 8, Margin = new Thickness(0, 6, 0, 2) };
        private readonly Grid tipParts = new();
        private readonly Graph tipGraph = new() { Width = 270, Height = 40, Margin = new Thickness(0, 6, 0, 6), Lines = true };
        private readonly TextBlock tipFooter = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 270, Margin = new Thickness(0, 0, 0, 4) };
        private readonly TextBlock tipWhat = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 270 };

        /// <summary>The figure with its name, for the report: as it is now, or its peak since the app started.</summary>
        public string Line(UsageReading reading, bool peakToo) => Texts.Get(Name) + " " + (peakToo ? Write(peak) : Write(ValueOf(reading)));

        /// <summary>The figure's place on the bar, made the first time it is asked for.</summary>
        public Border Cell => cell ??= MakeCell();

        private Border MakeCell()
        {
            label.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            tipHeading.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            tipWhat.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            tipFooter.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            foreach (Grid grid in new[] { tipRows, tipParts })
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }
            // above the bar, which sits at the bottom of the window
            tip = new ToolTip
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                Content = new StackPanel { Width = 270, Children = { tipHeading, tipRows, tipSplit, tipParts, tipGraph, tipFooter, tipWhat } },
            };
            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.Transparent,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, value, graph } },
                ToolTip = tip,
            };
            border.SetResourceReference(Border.BorderBrushProperty, "line");
            tip.Opened += (_, _) => { if (shownReading is not null) ShowTip(shownReading, ValueOf(shownReading)); };
            ToolTipService.SetInitialShowDelay(border, 300);
            ToolTipService.SetShowDuration(border, int.MaxValue);
            return border;
        }

        /// <summary>Shows a reading: the value, its colour and the graphs, and the tooltip while it is open.</summary>
        public void Show(UsageReading reading)
        {
            shownReading = reading;
            double now = ValueOf(reading);
            history.Add(double.IsNaN(now) ? 0 : now);
            if (history.Count > GraphLength) history.RemoveAt(0);
            if (!double.IsNaN(now)) peak = Math.Max(peak, now);
            Level level = now > Warning ? Level.Warning : now > Caution ? Level.Caution : Level.Fine;

            label.Text = Texts.Get(Name);
            value.Text = Write(now);
            value.SetResourceReference(TextBlock.ForegroundProperty, level == Level.Fine ? "ink" : ColourOf(level));
            foreach (Graph shown in new[] { graph, tipGraph })
            {
                shown.Show(history, Math.Max(Scale, history.Max() * 1.1), ColourOf(level));
                shown.Mark = Caution < double.MaxValue ? Caution : double.NaN;
            }
            // the tooltip's lines are worked out only while it is open: the memory's parts take some counting
            if (tip?.IsOpen == true || cell?.IsMouseOver == true) ShowTip(reading, now);
        }

        private void ShowTip(UsageReading reading, double now)
        {
            tipHeading.Text = Texts.Get(Name);
            tipWhat.Text = Texts.Get(What);
            List<(string Label, string Value)> rows = [(Texts.Get("Main.Usage.Now"), Write(now)), (Texts.Get("Main.Usage.Peak"), Write(peak))];
            if (Details is not null) rows.AddRange(Details(reading));
            ShowRows(tipRows, rows.Select(row => ((UIElement)Coloured(row.Label, "muted"), row.Value)).ToList());
            List<MemoryPart> parts = Parts?.Invoke(reading) ?? [];
            ShowParts(parts, reading.Memory);
            string footer = Footer?.Invoke() ?? "";
            tipFooter.Text = footer;
            tipFooter.Visibility = footer == "" ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>The memory split by part: a bar of their shares, then a line each with its colour.</summary>
        private void ShowParts(List<MemoryPart> parts, long total)
        {
            tipSplit.Children.Clear();
            tipSplit.ColumnDefinitions.Clear();
            tipSplit.Visibility = tipParts.Visibility = parts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            var lines = new List<(UIElement, string)>();
            for (int i = 0; i < parts.Count; i++)
            {
                string colour = PartColours[Math.Min(i, PartColours.Length - 1)];
                if (parts[i].Bytes > 0 && total > 0)
                {
                    tipSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(parts[i].Bytes, GridUnitType.Star) });
                    var share = new Border();
                    share.SetResourceReference(Border.BackgroundProperty, colour);
                    Grid.SetColumn(share, tipSplit.ColumnDefinitions.Count - 1);
                    tipSplit.Children.Add(share);
                }
                var name = new TextBlock { Margin = new Thickness(0, 0, 12, 0) };
                var square = new Run("■ ");
                square.SetResourceReference(TextElement.ForegroundProperty, colour);
                var words = new Run(parts[i].Name);
                words.SetResourceReference(TextElement.ForegroundProperty, "muted");
                name.Inlines.Add(square);
                name.Inlines.Add(words);
                lines.Add((name, parts[i].Bytes > 0 ? parts[i].SizeText(Size) : Texts.Get("Main.Usage.Part.None")));
            }
            ShowRows(tipParts, lines);
        }

        private static void ShowRows(Grid grid, List<(UIElement Label, string Value)> rows)
        {
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                var shown = new TextBlock { Text = rows[i].Value, TextAlignment = TextAlignment.Right };
                Grid.SetRow(rows[i].Label, i);
                Grid.SetRow(shown, i);
                Grid.SetColumn(shown, 1);
                grid.Children.Add(rows[i].Label);
                grid.Children.Add(shown);
            }
        }
    }

    /// <summary>A figure's last minute: a line with a light fill under it, the newest reading on the right.</summary>
    private sealed class Graph : FrameworkElement
    {
        private double[] values = [];
        private double top = 1;
        private string colour = "accent";

        /// <summary>Whether to draw faint lines across at a quarter, half and three quarters, as the tooltip's larger graph does.</summary>
        public bool Lines { get; init; }

        /// <summary>The aim, drawn as a dashed line when it is within the graph; NaN for none.</summary>
        public double Mark { get; set; } = double.NaN;

        public void Show(List<double> history, double reachesUpTo, string colourName)
        {
            values = [.. history];
            top = reachesUpTo;
            colour = colourName;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            double width = ActualWidth, height = ActualHeight;
            if (TryFindResource("line") is Brush faint && Lines)
            {
                var pen = new Pen(faint, 1);
                foreach (double part in new[] { 0.25, 0.5, 0.75 }) drawing.DrawLine(pen, new Point(0, height * part), new Point(width, height * part));
            }
            if (Lines && !double.IsNaN(Mark) && Mark < top && TryFindResource("caution") is SolidColorBrush caution)
            {
                double y = height - Mark / top * height;
                drawing.DrawLine(new Pen(caution, 1) { DashStyle = DashStyles.Dash }, new Point(0, y), new Point(width, y));
            }
            if (values.Length < 2 || TryFindResource(colour) is not SolidColorBrush stroke) return;

            double step = width / (GraphLength - 1);
            double left = (GraphLength - values.Length) * step;
            Point At(int i) => new(left + i * step, height - 1 - Math.Min(values[i], top) / top * (height - 2));
            var line = new StreamGeometry();
            using (StreamGeometryContext context = line.Open())
            {
                context.BeginFigure(At(0), isFilled: false, isClosed: false);
                for (int i = 1; i < values.Length; i++) context.LineTo(At(i), isStroked: true, isSmoothJoin: false);
            }
            var area = new StreamGeometry();
            using (StreamGeometryContext context = area.Open())
            {
                context.BeginFigure(new Point(left, height), isFilled: true, isClosed: true);
                for (int i = 0; i < values.Length; i++) context.LineTo(At(i), isStroked: false, isSmoothJoin: false);
                context.LineTo(new Point(left + (values.Length - 1) * step, height), isStroked: false, isSmoothJoin: false);
            }
            var fill = new SolidColorBrush(Color.FromArgb(42, stroke.Color.R, stroke.Color.G, stroke.Color.B));
            drawing.DrawGeometry(fill, null, area);
            drawing.DrawGeometry(null, new Pen(stroke, Lines ? 2 : 1.5), line);
        }
    }
}
