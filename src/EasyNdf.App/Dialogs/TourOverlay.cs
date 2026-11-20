using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The tutorial (the HTML app's tour): the app's core steps with WARNO Tactical Overhaul and the game's own files, one
/// step at a time. Each step outlines one part of the app and dims the rest; only the outlined part takes clicks. A step
/// that asks for something (in bold) goes on once it is done; one with a dashed outline is only to look at, and Next
/// goes on. Back, Skip step and the Left and Right arrow keys move through it; End tour stops it (Escape does not, so a
/// window opened during the tour can be closed with it). While one of the app's windows is open (the Export window, a
/// question) the tour steps aside, and comes back when it closes.
/// </summary>
public sealed class TourOverlay : Grid
{
    /// <summary>
    /// One step: what it says, the part it outlines (none: the card alone, in the middle), when it is done (null: only to
    /// look at), and the tab its part is on, brought to the front as the step starts (a step skipped to, or gone back to).
    /// </summary>
    private sealed record Step(string Text, Func<FrameworkElement?> Target, Func<bool>? Done = null, TabItem? Tab = null);

    /// <summary>How far the outline stands off what it outlines.</summary>
    private const double Margin_ = 4;

    private readonly Path dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
    private readonly Rectangle outline = new() { StrokeThickness = 2, IsHitTestVisible = false };
    // over the outlined part of a step that is only to look at: it takes the clicks so nothing there changes
    private readonly Rectangle lookOnly = new() { Fill = Brushes.Transparent };
    private readonly Bevel card = new() { Padding = new Thickness(14, 10, 14, 12), Width = 360 };
    private readonly TextBlock counter = new() { FontSize = 11.5, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly Button backButton = new() { Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button nextButton = new() { Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button endButton = new();
    private readonly Canvas canvas = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private List<Step> steps = [];
    private int at;
    private MainView? view;
    private bool exportWindowSeen;

    /// <summary>Builds the tour, not running.</summary>
    public TourOverlay()
    {
        Visibility = Visibility.Collapsed;
        card.SetResourceReference(Bevel.TopLeftProperty, "hi");
        card.SetResourceReference(Bevel.BottomRightProperty, "lo");
        card.SetResourceReference(Bevel.BackgroundProperty, "panel");
        outline.SetResourceReference(Shape.StrokeProperty, "accent");
        counter.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        text.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        nextButton.Style = (Style)Application.Current.FindResource("GoButton");
        backButton.Click += (_, _) => Go(at - 1);
        nextButton.Click += (_, _) => Go(at + 1);
        endButton.Click += (_, _) => End();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { backButton, nextButton, endButton } };
        card.Child = new StackPanel { Children = { counter, text, buttons } };
        canvas.Children.Add(dim);
        canvas.Children.Add(outline);
        canvas.Children.Add(lookOnly);
        canvas.Children.Add(card);
        Children.Add(canvas);
        timer.Tick += (_, _) => Follow();
    }

    /// <summary>Whether the tour is running.</summary>
    public bool IsRunning => timer.IsEnabled;

    /// <summary>Starts the tour from its first step.</summary>
    public void Start()
    {
        view = MainView.Find(this);
        if (view is null) return;
        MainView v = view;
        Pages.FilesPage files = v.FilesTab;
        const string overhaul = "WARNO Tactical Overhaul";
        steps =
        [
            new(Texts.Get("Welcome.Tour.Start"), () => null),
            new(Texts.Get("Welcome.Tour.ModsTab"), () => v.RulesTabItem, () => v.Tabs.SelectedItem == v.RulesTabItem),
            new(Texts.Get("Welcome.Tour.PickMod", ("mod", overhaul)), () => v.RulesTab.ModBox, () => AppData.Current.OpenMod.Name == overhaul, v.RulesTabItem),
            new(Texts.Get("Welcome.Tour.RuleSets"), () => v.RulesTab.PresetBox, null, v.RulesTabItem),
            new(Texts.Get("Welcome.Tour.FilesTab"), () => v.FilesTabItem, () => v.Tabs.SelectedItem == v.FilesTabItem),
            new(Texts.Get("Welcome.Tour.Source"), () => files.SourceBar.SourceList, () => v.Sources.Chosen is not null || files.Model.Entries.Count > 0, v.FilesTabItem),
            new(Texts.Get("Welcome.Tour.AddItsFiles"), () => files.SourceBar.AddItsFilesButton, () => files.Model.Entries.Count > 0 && files.Model.IsIdle, v.FilesTabItem),
            new(Texts.Get("Welcome.Tour.Preview"), () => files.PreviewButton, () => files.Model.IsIdle && files.Model.Entries.Any(entry => entry.LastRun is not null), v.FilesTabItem),
            new(Texts.Get("Welcome.Tour.Results"), () => files.Panes, null, v.FilesTabItem),
            new(Texts.Get("Welcome.Tour.Export"), () => files.RunButton, () => exportWindowSeen && !v.ExportWindow.IsOpen && files.Model.IsIdle, v.FilesTabItem),
            new(Texts.Get("Welcome.Tour.Done"), () => null),
        ];
        exportWindowSeen = false;
        if (Window.GetWindow(this) is Window window)
        {
            window.PreviewKeyDown -= KeyPressed;
            window.PreviewKeyDown += KeyPressed;
        }
        Visibility = Visibility.Visible;
        timer.Start();
        Go(0);
    }

    /// <summary>Stops the tour; the app is left as the tour left it.</summary>
    public void End()
    {
        timer.Stop();
        Visibility = Visibility.Collapsed;
        if (Window.GetWindow(this) is Window window) window.PreviewKeyDown -= KeyPressed;
    }

    private void Go(int step)
    {
        if (step >= steps.Count)
        {
            End();
            return;
        }
        at = Math.Max(0, step);
        Step shown = steps[at];
        if (shown.Tab is TabItem tab && view is not null) view.Tabs.SelectedItem = tab;
        counter.Text = Texts.Get("Welcome.Tour.Counter", ("step", at + 1), ("count", steps.Count));
        RichText.SetMarkup(text, shown.Text);
        backButton.Content = Texts.Get("Welcome.Tour.Back");
        backButton.IsEnabled = at > 0;
        bool last = at == steps.Count - 1;
        nextButton.Content = Texts.Get(last ? "Welcome.Tour.Finish" : shown.Done is null ? "Welcome.Tour.Next" : "Welcome.Tour.Skip");
        endButton.Content = Texts.Get("Welcome.Tour.End");
        endButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        outline.StrokeDashArray = shown.Done is null ? new DoubleCollection([3, 2]) : null;
        Follow();
    }

    /// <summary>
    /// Several times a second: steps aside while one of the app's windows is open, goes on when the step is done, and
    /// keeps the outline and the card on the part outlined, which may move or appear late.
    /// </summary>
    private void Follow()
    {
        if (view is null || steps.Count == 0) return;
        if (view.ExportWindow.IsOpen) exportWindowSeen = true;
        bool windowOpen = view.Messages.IsOpen || view.ExportWindow.IsOpen || view.Welcome.IsOpen || view.RuleWindow.IsOpen
            || view.Conflicts.IsOpen || view.HelpPopup.IsOpen
            // while Preview or Export runs, its card and Cancel stay within reach
            || view.FilesTab.Model.IsBusy || view.FilesTab.Model.IsPreparing;
        Visibility = windowOpen ? Visibility.Collapsed : Visibility.Visible;
        if (windowOpen) return;
        Step shown = steps[at];
        if (shown.Done?.Invoke() == true)
        {
            Go(at + 1);
            return;
        }
        Rect hole = Rect.Empty;
        if (shown.Target() is FrameworkElement target && target.IsVisible && target.ActualWidth > 0)
        {
            try
            {
                hole = target.TransformToVisual(this).TransformBounds(new Rect(target.RenderSize));
                hole.Inflate(Margin_, Margin_);
            }
            catch (InvalidOperationException) { hole = Rect.Empty; }
        }
        var whole = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        dim.Data = hole.IsEmpty ? whole : new CombinedGeometry(GeometryCombineMode.Exclude, whole, new RectangleGeometry(hole));
        Place(outline, hole);
        Place(lookOnly, shown.Done is null ? hole : Rect.Empty);
        PlaceCard(hole);
    }

    private static void Place(FrameworkElement shape, Rect where)
    {
        shape.Visibility = where.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (where.IsEmpty) return;
        Canvas.SetLeft(shape, where.X);
        Canvas.SetTop(shape, where.Y);
        shape.Width = where.Width;
        shape.Height = where.Height;
    }

    /// <summary>The card under the part outlined, or above it when there is no room, kept inside the window; in the middle when nothing is outlined.</summary>
    private void PlaceCard(Rect hole)
    {
        card.Measure(new Size(card.Width, double.PositiveInfinity));
        double width = card.Width, height = card.DesiredSize.Height;
        double x, y;
        if (hole.IsEmpty)
        {
            x = (ActualWidth - width) / 2;
            y = (ActualHeight - height) / 2;
        }
        else
        {
            x = Math.Clamp(hole.Left, 8, Math.Max(8, ActualWidth - width - 8));
            y = hole.Bottom + 10;
            if (y + height > ActualHeight - 8) y = hole.Top - 10 - height;
            // a part as tall as the window (a list): the card sits over its middle instead
            if (y < 8) y = Math.Max(8, (ActualHeight - height) / 2);
        }
        Canvas.SetLeft(card, Math.Round(x));
        Canvas.SetTop(card, Math.Round(y));
    }

    /// <summary>Left and Right go back and on, outside a box being typed in.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (!IsRunning || Visibility != Visibility.Visible || Keyboard.FocusedElement is TextBoxBase or ComboBox) return;
        if (e.Key == Key.Left && at > 0) { Go(at - 1); e.Handled = true; }
        else if (e.Key == Key.Right) { Go(at + 1); e.Handled = true; }
    }
}
