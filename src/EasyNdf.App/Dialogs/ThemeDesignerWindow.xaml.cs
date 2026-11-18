using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EasyNdf.App.Windowing;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The Theme Designer's window. There is one at most: the button opens it, or brings it forward if it is open. It belongs
/// to the main window, so it stays above it and minimizes with it, but the app can be used while it is open. It keeps its
/// place while the app runs.
/// </summary>
public partial class ThemeDesignerWindow : Window
{
    /// <summary>The window while it is open; null when closed.</summary>
    public static ThemeDesignerWindow? Current { get; private set; }

    /// <summary>Where the window was when last closed, so it opens there again while the app runs.</summary>
    private static Rect? lastPlace;

    private ThemeDesignerWindow(Window owner, bool nativeFrame)
    {
        InitializeComponent();
        Owner = owner;
        if (nativeFrame) CaptionButtons.Visibility = Visibility.Collapsed;
        else _ = new CustomTitleBar(this, IsTitleBarAt);
        ApplyScale();
        // a new Interface size applies here too, at once
        if (owner is MainWindow main)
        {
            Action<string> rescale = key => { if (key is "uiScale" or "") ApplyScale(); };
            main.View.SettingChanged += rescale;
            Closed += (_, _) => main.View.SettingChanged -= rescale;
        }
        Place(owner);
        Closed += (_, _) =>
        {
            lastPlace = new Rect(Left, Top, Width, Height);
            Current = null;
        };
    }

    /// <summary>Opens the window, or brings it forward when it is already open.</summary>
    /// <param name="owner">The main window.</param>
    /// <param name="nativeFrame">Whether the main window uses Windows' own title bar; the designer then does too.</param>
    public static ThemeDesignerWindow Open(Window owner, bool nativeFrame)
    {
        if (Current is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return open;
        }
        Current = new ThemeDesignerWindow(owner, nativeFrame);
        Current.Designer.Show();
        Current.Show();
        return Current;
    }

    /// <summary>"Interface size" applies here too, as in the main window.</summary>
    public void ApplyScale()
    {
        double percent = double.TryParse(AppData.Current.Settings["uiScale"]?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double chosen) ? chosen : 100;
        double scale = Math.Clamp(percent, 50, 200) / 100;
        Page.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
    }

    /// <summary>
    /// Where it was last time; the first time, just right of the main window if the screen has room there, otherwise
    /// inside its right edge.
    /// </summary>
    private void Place(Window owner)
    {
        if (lastPlace is Rect place)
        {
            (Left, Top, Width, Height) = (place.Left, place.Top, place.Width, place.Height);
            return;
        }
        Rect screen = SystemParameters.WorkArea;
        double ownerLeft = owner.WindowState == WindowState.Maximized ? screen.Left : owner.Left;
        double ownerWidth = owner.WindowState == WindowState.Maximized ? screen.Width : owner.ActualWidth;
        double ownerTop = owner.WindowState == WindowState.Maximized ? screen.Top : owner.Top;
        Height = Math.Min(Height, screen.Height - 40);
        Left = ownerLeft + ownerWidth + 8 + Width <= screen.Right ? ownerLeft + ownerWidth + 8 : Math.Max(screen.Left, ownerLeft + ownerWidth - Width - 24);
        Top = Math.Max(screen.Top, Math.Min(ownerTop + 60, screen.Bottom - Height));
    }

    /// <summary>Whether a point of the window is title bar that drags it: inside the title row, but not on its button.</summary>
    private bool IsTitleBarAt(Point point)
    {
        double bottom = TitleRow.TranslatePoint(new Point(0, TitleRow.ActualHeight), this).Y;
        if (point.Y < 0 || point.Y >= bottom) return false;
        for (DependencyObject? hit = InputHitTest(point) as DependencyObject; hit is not null; hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit))
            if (hit is ButtonBase) return false;
        return true;
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
