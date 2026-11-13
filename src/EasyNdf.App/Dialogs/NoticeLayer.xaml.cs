using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Dialogs;

/// <summary>Shows <see cref="Notices"/>: popups that fade away, and the list of the recent ones.</summary>
public partial class NoticeLayer : UserControl
{
    private const int MostShown = 3;
    private static readonly TimeSpan RoutineTime = TimeSpan.FromSeconds(4), ProblemTime = TimeSpan.FromSeconds(8), FadeTime = TimeSpan.FromMilliseconds(250);

    /// <summary>The popup of the work under way, if any.</summary>
    private FrameworkElement? working;

    /// <summary>Makes the layer, which shows what is said from now on.</summary>
    public NoticeLayer()
    {
        InitializeComponent();
        Notices.Said += notice => Dispatcher.BeginInvoke(() => Show(notice));
        Notices.WorkEnded += () => Dispatcher.BeginInvoke(EndWork);
    }

    /// <summary>Raised when another notification joins the recent list.</summary>
    public event Action? RecentChanged;

    /// <summary>Whether the list of the recent notifications is open.</summary>
    public bool IsRecentOpen => RecentBox.Visibility == Visibility.Visible;

    /// <summary>Whether a point of this layer is on the list of the recent notifications.</summary>
    public bool IsOnRecent(Point point) => IsRecentOpen && new Rect(RecentBox.TranslatePoint(new Point(), this), RecentBox.RenderSize).Contains(point);

    /// <summary>Keeps the popups above what runs along the bottom of the app.</summary>
    public void KeepAbove(double height) => Toasts.Margin = new Thickness(0, 0, 10, height + 10);

    /// <summary>Opens the list of the recent notifications, its top right corner where given.</summary>
    public void OpenRecent(double top, double right)
    {
        RecentBox.Margin = new Thickness(0, top, right, 0);
        RecentBox.Visibility = Visibility.Visible;
        FillRecent();
    }

    /// <summary>Closes the list of the recent notifications.</summary>
    public void CloseRecent() => RecentBox.Visibility = Visibility.Collapsed;

    private void CloseRecentClicked(object sender, RoutedEventArgs e) => CloseRecent();

    private void Show(Notice notice)
    {
        // the result takes the place of the work that led to it
        if (notice.Kind != NoticeKind.Working) RecentChanged?.Invoke();
        if (IsRecentOpen) FillRecent();
        EndWork();
        // the same words again, such as a search that still finds nothing, show longer instead of stacking up
        if (Toasts.Children.OfType<FrameworkElement>().FirstOrDefault(toast => toast.Tag is Toast same && same.Text == notice.Text) is { Tag: Toast again })
        {
            again.Restart();
            return;
        }
        while (Toasts.Children.Count >= MostShown) Toasts.Children.RemoveAt(0);
        var toast = new Toast(notice, this);
        Toasts.Children.Add(toast.Box);
        if (notice.Kind == NoticeKind.Working) working = toast.Box;
    }

    private void EndWork()
    {
        if (working is not null) Toasts.Children.Remove(working);
        working = null;
    }

    private void FillRecent()
    {
        IReadOnlyList<Notice> recent = Notices.Recent;
        RecentEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentScroll.Visibility = recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentRows.Children.Clear();
        foreach (Notice notice in recent)
        {
            var row = new DockPanel { Margin = new Thickness(8, 5, 8, 5) };
            UIElement icon = Icon(notice.Kind, 14);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var time = new TextBlock { Text = notice.At.ToString("t"), FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
            time.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            DockPanel.SetDock(time, Dock.Right);
            row.Children.Add(time);
            FrameworkElement words = WithAction(new TextBlock { Text = notice.Text, TextWrapping = TextWrapping.Wrap }, notice.Action, CloseRecent);
            words.Margin = new Thickness(7, 0, 0, 0);
            row.Children.Add(words);
            var line = new Bevel { Edges = new Thickness(0, 0, 0, 1), Child = row };
            line.SetResourceReference(Bevel.BottomRightProperty, "line");
            RecentRows.Children.Add(line);
        }
    }

    /// <summary>A notification's words, with its button under them when it has one; the button closes what it is on, then acts.</summary>
    private static FrameworkElement WithAction(TextBlock words, NoticeAction? action, Action close)
    {
        if (action is null) return words;
        var button = new Button { Content = action.Label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        button.SetResourceReference(StyleProperty, "SmallButton");
        button.Click += (_, _) =>
        {
            close();
            action.Act();
        };
        return new StackPanel { Children = { words, button } };
    }

    /// <summary>A notification's icon, drawn as the message window's are, in the theme's colours.</summary>
    internal static FrameworkElement Icon(NoticeKind kind, double size)
    {
        var canvas = new Canvas { Width = 32, Height = 32 };
        void Shape(Shape shape, string fill)
        {
            shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, fill);
            canvas.Children.Add(shape);
        }
        void Stroke(string data)
        {
            var path = new Path { Data = Geometry.Parse(data), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Square, StrokeEndLineCap = PenLineCap.Square };
            path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, kind == NoticeKind.Working ? "muted" : "panel");
            canvas.Children.Add(path);
        }
        void Ring()
        {
            var ring = new Ellipse { Width = 27, Height = 27, StrokeThickness = 3 };
            Canvas.SetLeft(ring, 2.5);
            Canvas.SetTop(ring, 2.5);
            ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "muted");
            canvas.Children.Add(ring);
        }
        switch (kind)
        {
            case NoticeKind.Problem:
                Shape(new Path { Data = Geometry.Parse("M10 2.5h12l7.5 7.5v12L22 29.5H10L2.5 22V10z") }, "warn");
                Stroke("M10.5 10.5l11 11M21.5 10.5l-11 11");
                break;
            case NoticeKind.Caution:
                Shape(new Path { Data = Geometry.Parse("M16 3L30 28.5H2z") }, "caution");
                Stroke("M16 12v8M16 24v0");
                break;
            case NoticeKind.Done:
                var disc = new Ellipse { Width = 27, Height = 27 };
                Canvas.SetLeft(disc, 2.5);
                Canvas.SetTop(disc, 2.5);
                Shape(disc, "ok");
                Stroke("M9.5 16.5l4.5 4.5 9-10");
                break;
            default:
                Ring();
                Stroke("M16 9v7l5 3");
                break;
        }
        return new Viewbox { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top, Child = canvas };
    }

    /// <summary>A popup: it fades away after its time unless the mouse is on it, and × closes it at once.</summary>
    private sealed class Toast
    {
        private readonly NoticeLayer layer;
        private readonly DispatcherTimer? timer;

        public Toast(Notice notice, NoticeLayer layer)
        {
            this.layer = layer;
            Text = notice.Text;
            var close = new Button { Content = "×", ToolTip = Texts.Get("Main.Notices.Dismiss.Tip"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
            close.SetResourceReference(StyleProperty, "IconButton");
            close.Click += (_, _) => Remove();
            DockPanel.SetDock(close, Dock.Right);
            FrameworkElement icon = Icon(notice.Kind, 18);
            icon.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(icon, Dock.Left);
            var words = new TextBlock { Text = notice.Text, TextWrapping = TextWrapping.Wrap };
            System.Windows.Automation.AutomationProperties.SetLiveSetting(words, System.Windows.Automation.AutomationLiveSetting.Polite);
            var parts = new DockPanel();
            parts.Children.Add(close);
            parts.Children.Add(icon);
            parts.Children.Add(WithAction(words, notice.Action, Remove));
            var box = new Bevel { Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 6, 0, 0), Child = parts, Tag = this };
            box.SetResourceReference(Bevel.TopLeftProperty, "hi");
            box.SetResourceReference(Bevel.BottomRightProperty, "lo");
            box.SetResourceReference(Bevel.BackgroundProperty, "panel");
            Box = box;
            if (notice.Kind == NoticeKind.Working) return;
            timer = new DispatcherTimer { Interval = notice.Kind is NoticeKind.Caution or NoticeKind.Problem ? ProblemTime : RoutineTime };
            timer.Tick += (_, _) => FadeAway();
            box.MouseEnter += (_, _) => Hold();
            box.MouseLeave += (_, _) => Restart();
            timer.Start();
        }

        public FrameworkElement Box { get; }

        public string Text { get; }

        /// <summary>Starts its time again, and brings it back if it was fading.</summary>
        public void Restart()
        {
            Hold();
            timer?.Start();
        }

        private void Hold()
        {
            timer?.Stop();
            Box.BeginAnimation(OpacityProperty, null);
        }

        private void FadeAway()
        {
            timer?.Stop();
            var fade = new DoubleAnimation(0, FadeTime);
            fade.Completed += (_, _) => { if (Box.Opacity == 0) Remove(); };
            Box.BeginAnimation(OpacityProperty, fade);
        }

        private void Remove()
        {
            timer?.Stop();
            layer.Toasts.Children.Remove(Box);
        }
    }
}
