using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Dialogs;

/// <summary>The tutorial's chapters, in the order it runs them; any can be started from the Help tab's Tutorials list.</summary>
public enum Chapter
{
    /// <summary>The Files tab: where files come from, and the sample Ammunition.ndf the tutorial adds.</summary>
    Files,

    /// <summary>The Mods tab: mods, a new mod, and a rule set for Ammunition.ndf.</summary>
    ModsAndRules,

    /// <summary>A rule in that rule set, with a condition and an action, made field by field.</summary>
    Rules,

    /// <summary>Preview, the results and Details, and the Export window (which exports nothing in the tutorial).</summary>
    PreviewAndExport,

    /// <summary>The Editor tab: the file, its tips, Show Export, Changes Only and the Descriptor Tree.</summary>
    Editor,

    /// <summary>The Armory: units, their values, the 3D model viewer, divisions, battlegroups and weapons.</summary>
    Armory,
}

/// <summary>
/// The tutorial (the HTML app's tour), in chapters, one step at a time. Each step outlines one part of the app, also inside its
/// windows and menus, and dims the rest; only the outlined part takes clicks. A step that asks for something (in bold)
/// goes on once it is done; one with a dashed outline is only to look at, and Next goes on. The card says one short line,
/// and it and the outline slide from step to step, so the eye follows them. Back, Skip Step and the Left and Right arrow
/// keys move through it; End Tour stops it (Escape does not, so a window opened during the tour can be closed with it).
/// A window the step is not about (a question the app asks) and a run of Preview have the tour step aside. Nothing is
/// exported while a tutorial runs (<see cref="ExportWindow.TourOnly"/>).
/// </summary>
public sealed partial class TourOverlay : Grid
{
    /// <summary>
    /// One step: what it says (one line), the part it outlines (none: the card alone, in the middle), when it is done
    /// (null: Next goes on), and the tab its part is on, brought to the front as the step starts.
    /// </summary>
    /// <param name="LookOnly">For a step without Done: whether the part is only to look at (dashed, takes no clicks), or to use before Next.</param>
    /// <param name="Undone">When the step before it should come back: a question it waits on was cancelled.</param>
    /// <param name="Also">A second part outlined with the first, as one: a list and the button beside it.</param>
    /// <param name="OnEnter">Done as the step begins, such as adding the sample file.</param>
    /// <param name="SkipIf">When the step is passed over: what it makes is there already (a second run), or what it opens is open.</param>
    /// <param name="Show">Puts the app as the step begins it, each time it is come to, by Next or by Back: the view it
    /// shows, or, for a step that asks for a click, the view before that click; so the app and the tour never part.</param>
    /// <param name="Needed">For a step with Done: the rest of the tutorial needs what it asks for (the mod, the rule, its
    /// values, the run), so Skip Step is off until it is done; without it the later chapters would have nothing to show.</param>
    /// <param name="Ready">For a step without Done: when Next may go on, such as once a name is typed; null for always.</param>
    private sealed record Step(string Text, Func<FrameworkElement?> Target, Func<bool>? Done = null, TabItem? Tab = null,
        bool LookOnly = true, Func<bool>? Undone = null, Func<FrameworkElement?>? Also = null, Action? OnEnter = null,
        Func<bool>? SkipIf = null, Action? Show = null, bool Needed = false, Func<bool>? Ready = null)
    {
        /// <summary>The chapter the step is in; set as the steps are put together.</summary>
        public Chapter Chapter { get; set; }
    }

    /// <summary>How far the outline stands off what it outlines.</summary>
    private const double Margin_ = 4;

    /// <summary>How long the card and the outline take to slide to the next step.</summary>
    private static readonly Duration SlideTime = new(TimeSpan.FromMilliseconds(320));

    private static readonly IEasingFunction SlideEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

    // the outlined part: the dim's hole, the outline and the look-only cover share one rectangle, which slides
    private readonly RectangleGeometry hole = new(Rect.Empty);
    private readonly RectangleGeometry whole = new();
    private readonly Path dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
    private readonly Path outline = new() { StrokeThickness = 2, IsHitTestVisible = false };
    // over the outlined part of a step that is only to look at: it takes the clicks so nothing there changes
    private readonly Path lookOnly = new() { Fill = Brushes.Transparent };
    private readonly Bevel card = new() { Padding = new Thickness(14, 9, 14, 11) };
    private readonly TextBlock counter = new() { FontSize = 11.5, Margin = new Thickness(0, 0, 0, 3) };
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 0, 9) };
    private readonly Button backButton = new() { Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button nextButton = new() { Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button endButton = new();
    private readonly Canvas canvas = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private List<Step> steps = [];
    private int at;
    private MainView? view;

    /// <summary>Gone back to by the user: its Done waits for Next, or the step would go straight on again.</summary>
    private bool held;

    /// <summary>Whether the step's part has shown since the step began; a step's Undone waits for it.</summary>
    private bool targetSeen;

    /// <summary>
    /// Whether Next or Skip Step may go on from a step: not past one the rest of the tutorial needs until it is done
    /// (gone back to and done already, it may), and not from one that waits for something typed until it is.
    /// </summary>
    private bool CanGoOn(Step step) => step.Done is null ? step.Ready?.Invoke() != false : !step.Needed || held;

    /// <summary>Next or Skip Step is greyed, with why in its tooltip, while the step cannot be passed.</summary>
    private void ShowCanGoOn(Step step)
    {
        bool can = CanGoOn(step);
        if (nextButton.IsEnabled == can) return;
        nextButton.IsEnabled = can;
        nextButton.ToolTip = can ? null : Texts.Get("Tour.Needed");
    }

    /// <summary>
    /// Next, or Skip Step: on to the next step, past the steps that only happen inside the question or window the
    /// skipped step would have opened (they have an Undone), which would otherwise send the tour straight back.
    /// </summary>
    private void Skip()
    {
        if (!CanGoOn(steps[at])) return;
        // a question the skipped step waits on is put away, or it would hide the next step
        if (view?.Messages.IsOpen == true) view.Messages.Close(DialogChoice.Cancel);
        int next = at + 1;
        // (a step whose Show opens its window again is not passed over: it is gone to, with its window)
        if (steps[at].Done is not null)
            while (next < steps.Count && steps[next].Undone is not null && steps[next].Show is null && steps[next].Target() is not FrameworkElement { IsVisible: true }) next++;
        Go(next);
    }

    /// <summary>Where the hole and the card were sent last; empty before the first step is shown.</summary>
    private Rect holeTo = Rect.Empty;
    private Point cardTo = new(double.NaN, double.NaN);

    /// <summary>Builds the tour, not running.</summary>
    public TourOverlay()
    {
        Visibility = Visibility.Collapsed;
        dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, whole, hole);
        outline.Data = hole;
        lookOnly.Data = hole;
        card.SetResourceReference(Bevel.TopLeftProperty, "hi");
        card.SetResourceReference(Bevel.BottomRightProperty, "lo");
        card.SetResourceReference(Bevel.BackgroundProperty, "panel");
        outline.SetResourceReference(Shape.StrokeProperty, "accent");
        counter.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        text.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        nextButton.Style = (Style)Application.Current.FindResource("GoButton");
        ToolTipService.SetShowOnDisabled(nextButton, true);
        backButton.Click += (_, _) => Go(at - 1, byUser: true);
        nextButton.Click += (_, _) => Skip();
        endButton.Click += (_, _) => End();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { backButton, nextButton, endButton } };
        card.Child = new StackPanel { Children = { counter, text, buttons } };
        canvas.Children.Add(dim);
        canvas.Children.Add(outline);
        canvas.Children.Add(lookOnly);
        canvas.Children.Add(card);
        Children.Add(canvas);
        timer.Tick += (_, _) => Follow();
        SizeChanged += (_, _) => whole.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
    }

    /// <summary>Whether a tutorial is running.</summary>
    public bool IsRunning => timer.IsEnabled;

    /// <summary>A chapter's name, as the Help tab's Tutorials list and the card's top line give it.</summary>
    public static string NameOf(Chapter chapter) => Texts.Get(chapter switch
    {
        Chapter.ModsAndRules => "Tour.Chapter.ModsAndRules",
        Chapter.Rules => "Tour.Chapter.Rules",
        Chapter.PreviewAndExport => "Tour.Chapter.PreviewAndExport",
        Chapter.Editor => "Tour.Chapter.Editor",
        Chapter.Armory => "Tour.Chapter.Armory",
        _ => "Tour.Chapter.Files",
    });

    /// <summary>What a chapter covers, in one line, as the Help tab's Tutorials list gives it in its tooltip.</summary>
    public static string AboutOf(Chapter chapter) => Texts.Get(chapter switch
    {
        Chapter.ModsAndRules => "Tour.About.ModsAndRules",
        Chapter.Rules => "Tour.About.Rules",
        Chapter.PreviewAndExport => "Tour.About.PreviewAndExport",
        Chapter.Editor => "Tour.About.Editor",
        Chapter.Armory => "Tour.About.Armory",
        _ => "Tour.About.Files",
    });

    /// <summary>Starts the tutorial at a chapter; it runs on through the chapters after it.</summary>
    public void Start(Chapter chapter = Chapter.Files)
    {
        view = MainView.Find(this);
        if (view is null) return;
        // not over a rule left open: its window would cover what the tutorial points at
        if (view.RefusedForOpenRule()) return;
        // the tutorial points at the tabs and their pages in the main window
        view.ReturnAllTabs();
        steps = AllSteps(view);
        // nothing is exported during a tutorial: the Export window only shows what would be written
        view.ExportWindow.TourOnly = true;
        if (Window.GetWindow(this) is Window window)
        {
            window.PreviewKeyDown -= KeyPressed;
            window.PreviewKeyDown += KeyPressed;
        }
        holeTo = Rect.Empty;
        cardTo = new Point(double.NaN, double.NaN);
        Visibility = Visibility.Visible;
        timer.Start();
        Go(Math.Max(0, steps.FindIndex(step => step.Chapter == chapter)));
    }

    /// <summary>Stops the tutorial; the app is left as the tour left it.</summary>
    public void End()
    {
        timer.Stop();
        Visibility = Visibility.Collapsed;
        if (view is not null) view.ExportWindow.TourOnly = false;
        if (Window.GetWindow(this) is Window window) window.PreviewKeyDown -= KeyPressed;
    }

    /// <param name="byUser">Back pressed: the step waits for Next even when what it asks is done already.</param>
    private void Go(int step, bool byUser = false)
    {
        if (step >= steps.Count)
        {
            End();
            return;
        }
        targetSeen = false;
        at = Math.Max(0, step);
        // steps whose work is done already are passed over, on the way forward and back; going back, so are the steps
        // inside a window that has closed since (a question, the rule's windows, the model viewer): back to the step
        // that opens it
        int direction = byUser ? -1 : 1;
        while ((steps[at].SkipIf?.Invoke() == true || byUser && steps[at].Show is null && steps[at].Undone?.Invoke() == true) && at + direction >= 0 && at + direction < steps.Count)
            at += direction;
        Step shown = steps[at];
        if (shown.Tab is TabItem tab && view is not null) view.ShowTab(tab);
        shown.Show?.Invoke();
        // gone back to a step that is done already: it waits for Next (or for its work to be undone, and done again)
        held = byUser && shown.Done?.Invoke() == true;
        // "Chapter 2 of 5: Mods and Rules · Step 3 of 21", the steps counted within the chapter
        // steps passed over now are not counted, so the count has no gap
        List<Step> inChapter = [.. steps.Where(each => each.Chapter == shown.Chapter && (each == shown || each.SkipIf?.Invoke() != true))];
        counter.Text = Texts.Get("Tour.Counter", ("chapter", (int)shown.Chapter + 1), ("chapters", Enum.GetValues<Chapter>().Length),
            ("name", NameOf(shown.Chapter)), ("step", inChapter.IndexOf(shown) + 1), ("count", inChapter.Count));
        // a part only to look at takes no clicks: the card says so, or a click there seems not to work
        if (shown.Done is null && shown.LookOnly && shown.Target() is not null) counter.Text += " · " + Texts.Get("Tour.LookOnly");
        if (!byUser) shown.OnEnter?.Invoke();
        ShowText(shown.Text);
        backButton.Content = Texts.Get("Welcome.Tour.Back");
        backButton.IsEnabled = at > 0;
        bool last = at == steps.Count - 1;
        nextButton.Content = Texts.Get(last ? "Welcome.Tour.Finish" : shown.Done is null || held ? "Welcome.Tour.Next" : "Welcome.Tour.Skip");
        ShowCanGoOn(shown);
        endButton.Content = Texts.Get("Welcome.Tour.End");
        endButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        outline.StrokeDashArray = shown.Done is null && shown.LookOnly ? new DoubleCollection([3, 2]) : null;
        Follow();
    }

    /// <summary>
    /// Several times a second: goes on when the step is done (or back when what it waited on was cancelled), steps aside
    /// for a window the step is not about and for a run, and keeps the outline and the card on the part outlined, which
    /// may move, appear late, or be in one of the app's windows or menus.
    /// </summary>
    private void Follow()
    {
        if (view is null || steps.Count == 0) return;
        Step shown = steps[at];
        bool done = shown.Done?.Invoke() == true;
        if (held && shown.Done is not null && !done)
        {
            // what the step asks for was undone (its Show put the view back): doing it again goes on, as the first time
            held = false;
            nextButton.Content = Texts.Get(at == steps.Count - 1 ? "Welcome.Tour.Finish" : "Welcome.Tour.Skip");
        }
        if (!held && done)
        {
            Go(at + 1);
            return;
        }
        ShowCanGoOn(shown);
        if (targetSeen && shown.Undone?.Invoke() == true && at > 0)
        {
            // the window the step is in was closed: back to the step that opens it, before the others in that window
            int back = at - 1;
            while (back > 0 && steps[back].Undone is not null) back--;
            Go(back, byUser: true);
            return;
        }
        FrameworkElement? target = shown.Target();
        if (target is not null && (!target.IsVisible || target.ActualWidth <= 0)) target = null;
        if (target is not null) targetSeen = true;
        // a run of Preview or Export: the card alone, at the top, and the app usable under it (its progress card and Cancel)
        if (view.FilesTab.Model.IsBusy || view.FilesTab.Model.IsPreparing)
        {
            Visibility = Visibility.Visible;
            ShowWaiting();
            return;
        }
        if (Covered(target))
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        if (!ReferenceEquals(text.Tag, shown)) ShowText(shown.Text);
        text.Tag = shown;
        dim.Visibility = Visibility.Visible;
        Rect part = target is null ? Rect.Empty : PlaceOf(target);
        if (!part.IsEmpty && shown.Also?.Invoke() is FrameworkElement also && also.IsVisible && PlaceOf(also) is { IsEmpty: false } second) part.Union(second);
        if (!part.IsEmpty) part.Inflate(Margin_, Margin_);
        outline.Visibility = part.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        lookOnly.Visibility = !part.IsEmpty && shown.Done is null && shown.LookOnly ? Visibility.Visible : Visibility.Collapsed;
        // nothing outlined: the hole closes to a point in the middle, so the next one opens from there
        SlideHole(part.IsEmpty ? new Rect(ActualWidth / 2, ActualHeight / 2, 0, 0) : part);
        SlideCard(CardPlace(part, WindowAround(target)));
    }

    /// <summary>While a run goes: "Wait while the rules run" at the top, nothing dimmed, the app usable.</summary>
    private void ShowWaiting()
    {
        dim.Visibility = outline.Visibility = lookOnly.Visibility = Visibility.Collapsed;
        ShowText(Texts.Get("Tour.Wait"));
        text.Tag = null;
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SlideCard(new Point(Math.Round((ActualWidth - card.DesiredSize.Width) / 2), 52));
    }

    /// <summary>
    /// Whether one of the app's windows is open and the part outlined is not in it: a question the step is not about,
    /// which the tour steps aside for. A part in a menu (its own window, above the app) is never covered.
    /// </summary>
    private bool Covered(FrameworkElement? target)
    {
        if (Parent is not Panel root) return false;
        List<FrameworkElement> open = [.. root.Children.OfType<FrameworkElement>().Where(IsOpenWindow)];
        if (open.Count == 0) return false;
        if (target is null) return true;
        if (!SameWindow(target)) return false;
        return !open.Any(window => window.Holds(target));
    }

    /// <summary>One of the app's windows (a dialog, the 3D viewer), shown; not the tour, the notices or the side panel.</summary>
    private bool IsOpenWindow(FrameworkElement element) =>
        !ReferenceEquals(element, this) && element.Visibility == Visibility.Visible
        && element is UserControl && element.GetType().Namespace is "EasyNdf.App.Dialogs" or "EasyNdf.App.Models"
        && element is not NoticeLayer;

    /// <summary>Whether an element is in the main window, not in a menu or a dropdown's list (windows of their own).</summary>
    private bool SameWindow(Visual element) =>
        ReferenceEquals(PresentationSource.FromVisual(element)?.RootVisual, PresentationSource.FromVisual(this)?.RootVisual);

    /// <summary>
    /// Where an element is over the tour. An item of a menu gives the whole menu: the menu is drawn above the app, so an
    /// outline round the item alone would be hidden under it, and the card would cover the items under it.
    /// </summary>
    private Rect PlaceOf(FrameworkElement target)
    {
        try
        {
            if (SameWindow(target)) return target.TransformToVisual(this).TransformBounds(new Rect(target.RenderSize));
            Visual shown = PresentationSource.FromVisual(target)?.RootVisual is FrameworkElement menu && menu.ActualWidth > 0 ? menu : target;
            var size = shown is FrameworkElement element ? new Size(element.ActualWidth, element.ActualHeight) : target.RenderSize;
            Point topLeft = PointFromScreen(shown.PointToScreen(new Point(0, 0)));
            Point bottomRight = PointFromScreen(shown.PointToScreen(new Point(size.Width, size.Height)));
            return new Rect(topLeft, bottomRight);
        }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    /// <summary>The hole slides to its new place; the first time, it is there at once.</summary>
    private void SlideHole(Rect to)
    {
        if (Near(to, holeTo)) return;
        bool first = holeTo.IsEmpty;
        holeTo = to;
        if (first)
        {
            hole.BeginAnimation(RectangleGeometry.RectProperty, null);
            hole.Rect = to;
            return;
        }
        hole.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(hole.Rect, to, SlideTime) { EasingFunction = SlideEase });
    }

    /// <summary>The card slides to its new place; the first time, it is there at once.</summary>
    private void SlideCard(Point to)
    {
        if (Math.Abs(to.X - cardTo.X) < 1 && Math.Abs(to.Y - cardTo.Y) < 1) return;
        bool first = double.IsNaN(cardTo.X);
        cardTo = to;
        foreach ((DependencyProperty side, double value) in new[] { (Canvas.LeftProperty, to.X), (Canvas.TopProperty, to.Y) })
        {
            if (first)
            {
                card.BeginAnimation(side, null);
                card.SetValue(side, value);
                continue;
            }
            double from = (double)card.GetValue(side);
            card.BeginAnimation(side, new DoubleAnimation(double.IsNaN(from) ? value : from, value, SlideTime) { EasingFunction = SlideEase });
        }
    }

    private static bool Near(Rect a, Rect b) =>
        !a.IsEmpty && !b.IsEmpty && Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;

    /// <summary>The box of the app's window (a dialog, the 3D viewer) a part is in, over the tour; empty when it is in none.</summary>
    private Rect WindowAround(FrameworkElement? target)
    {
        if (target is null || Parent is not Panel root || !SameWindow(target)) return Rect.Empty;
        foreach (FrameworkElement window in root.Children.OfType<FrameworkElement>().Where(IsOpenWindow))
            if (window.Holds(target) && window.FindName("Box") is FrameworkElement { IsVisible: true } box)
                return PlaceOf(box);
        return Rect.Empty;
    }

    /// <summary>
    /// Where the card goes: beside a window the part is in, so it covers none of the window's fields; beside a part as
    /// tall as much of the screen (a list, a pane); else under the part, or above it when there is no room. Kept inside
    /// the window; in the middle when nothing is outlined.
    /// </summary>
    private Point CardPlace(Rect part, Rect window)
    {
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = card.DesiredSize.Width, height = card.DesiredSize.Height;
        double x, y;
        Rect beside = !window.IsEmpty ? window : part.Height > ActualHeight * 0.4 ? part : Rect.Empty;
        if (!part.IsEmpty && !beside.IsEmpty && (beside.Right + 12 + width < ActualWidth - 8 || beside.Left - 12 - width > 8))
        {
            x = beside.Right + 12 + width < ActualWidth - 8 ? beside.Right + 12 : beside.Left - 12 - width;
            y = Math.Clamp(part.Top, 8, Math.Max(8, ActualHeight - height - 8));
            return new Point(Math.Round(x), Math.Round(y));
        }
        if (part.IsEmpty)
        {
            x = (ActualWidth - width) / 2;
            y = (ActualHeight - height) / 2;
        }
        else
        {
            x = Math.Clamp(part.Left, 8, Math.Max(8, ActualWidth - width - 8));
            y = part.Bottom + 10;
            if (y + height > ActualHeight - 8) y = part.Top - 10 - height;
            // a part as tall as the window (a list): the card sits at its side, or over its middle
            if (y < 8)
            {
                y = Math.Max(8, (ActualHeight - height) / 2);
                if (part.Right + 10 + width < ActualWidth - 8) x = part.Right + 10;
                else if (part.Left - 10 - width > 8) x = part.Left - 10 - width;
            }
        }
        return new Point(Math.Round(x), Math.Round(y));
    }

    /// <summary>
    /// The card's line: **bold** and `code` as elsewhere; [[Label]] as the app's button of that name and [[?]] as the ?
    /// button, drawn as they are but not clickable; [[key:Ctrl+Z]] as a key, as the Help tab draws them. A new user sees
    /// what to look for, rather than a name or a sign that means nothing yet.
    /// </summary>
    private void ShowText(string markup)
    {
        text.Inlines.Clear();
        string[] parts = System.Text.RegularExpressions.Regex.Split(markup, @"\[\[(.+?)\]\]");
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0)
            {
                if (parts[i] == "") continue;
                text.Inlines.AddRange(RichText.Pieces(parts[i], bold => new Bold(new Run(bold)), code => new Run(code), plain => new Run(plain)));
                continue;
            }
            text.Inlines.Add(new InlineUIContainer(parts[i].StartsWith("key:", StringComparison.Ordinal) ? KeyFace(parts[i][4..]) : ButtonFace(parts[i]))
            {
                BaselineAlignment = BaselineAlignment.Center,
            });
        }
    }

    /// <summary>A button as the app draws it, only to look at.</summary>
    private static FrameworkElement ButtonFace(string label)
    {
        var button = new Button { Focusable = false, IsHitTestVisible = false, Margin = new Thickness(1, 0, 1, 0) };
        if (label == "?") button.Style = (Style)Application.Current.FindResource("HelpButton");
        else button.Content = label;
        return button;
    }

    /// <summary>A key, in the code font on a small raised key, as the Help tab draws one.</summary>
    private static FrameworkElement KeyFace(string name)
    {
        var face = new TextBlock { Text = name, FontSize = 11.5 };
        face.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        face.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        var key = new Bevel { Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(1, 0, 1, 0), Child = face };
        key.SetResourceReference(Bevel.TopLeftProperty, "hi");
        key.SetResourceReference(Bevel.BottomRightProperty, "lo");
        key.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return key;
    }

    /// <summary>Left and Right go back and on, outside a box being typed in.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (!IsRunning || Visibility != Visibility.Visible || Keyboard.FocusedElement is TextBoxBase or ComboBox) return;
        if (e.Key == Key.Left && at > 0) { Go(at - 1, byUser: true); e.Handled = true; }
        else if (e.Key == Key.Right) { Skip(); e.Handled = true; }
    }
}
