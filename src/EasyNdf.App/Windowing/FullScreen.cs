using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace EasyNdf.App.Windowing;

/// <summary>
/// F11: a window of the app (the main window or a tab's own window) covers its whole screen, taskbar included, and F11
/// again puts it back as it was, maximized or not. The title row stays, with its buttons; Esc never leaves full screen,
/// since it closes the app's popups. F11 does nothing while a popup, one of the app's windows or questions, the rule popup
/// or the tutorial is open. Full screen is never kept: the app always opens without it.
/// </summary>
/// <remarks>
/// The window loses Windows' title bar and resize borders while full screen, as the browsers do it, so Windows sees a
/// window the size of the screen and puts it above the taskbar. Where the window was is kept as Windows keeps it
/// (its placement), so leaving gives back the maximized state and the size to restore to.
/// </remarks>
public static class FullScreen
{
    /// <summary>What a full-screen window comes back to.</summary>
    private sealed class Before
    {
        public WindowPlacement Placement;
        public int Style;
    }

    private static readonly ConditionalWeakTable<Window, Before> windows = new();

    /// <summary>Whether a window is full screen.</summary>
    public static bool IsOn(Window window) => windows.TryGetValue(window, out _);


    /// <summary>F11 toggles full screen on this window, unless something is open over the app (see the class summary).</summary>
    /// <param name="window">The window.</param>
    /// <param name="view">The main view, which knows the app's windows, questions and rule popup.</param>
    /// <param name="showState">Shows the state on the window's maximize button: full screen shows Restore.</param>
    public static void Watch(Window window, MainView view, Action showState)
    {
        // before any control: a text box or the File Editor would take the key otherwise
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F11 || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            if (e.IsRepeat || view.BlocksFullScreen || AnyPopupOpen()) return;
            Toggle(window);
            showState();
        };
        // Win+Up while full screen: Windows maximizes the window, which is then no longer full screen (minimizing keeps it)
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Maximized && windows.TryGetValue(window, out Before? before))
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                windows.Remove(window);
                SetWindowLong(handle, GwlStyle, before.Style);
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                showState();
            }
        };
    }

    /// <summary>Goes full screen, or leaves it.</summary>
    public static void Toggle(Window window)
    {
        if (IsOn(window)) Leave(window);
        else Enter(window);
    }

    private static void Enter(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || window.WindowState == WindowState.Minimized) return;
        var before = new Before { Placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() }, Style = GetWindowLong(handle, GwlStyle) };
        if (!GetWindowPlacement(handle, ref before.Placement)) return;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref monitor)) return;
        windows.AddOrUpdate(window, before);
        SetWindowLong(handle, GwlStyle, before.Style & ~(WsCaption | WsThickFrame));
        Rect32 screen = monitor.Screen;
        // a maximized window is first made a normal one the size of the screen, so Windows no longer holds it to the
        // work area (the placement's normal place counts from the work area's corner)
        if (IsZoomed(handle))
        {
            WindowPlacement normal = before.Placement;
            normal.ShowCommand = SwShowNormal;
            Rect32 work = monitor.WorkArea;
            normal.Normal = new Rect32 { Left = screen.Left - work.Left, Top = screen.Top - work.Top, Right = screen.Right - work.Left, Bottom = screen.Bottom - work.Top };
            SetWindowPlacement(handle, ref normal);
        }
        SetWindowPos(handle, IntPtr.Zero, screen.Left, screen.Top, screen.Right - screen.Left, screen.Bottom - screen.Top, SwpNoZOrder | SwpFrameChanged);
    }

    /// <summary>Leaves full screen, back to where and how the window was. Does nothing when it is not full screen.</summary>
    public static void Leave(Window window)
    {
        if (!windows.TryGetValue(window, out Before? before)) return;
        windows.Remove(window);
        IntPtr handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, GwlStyle, before.Style);
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        // a window minimized while full screen comes back where it was, not minimized
        WindowPlacement placement = before.Placement;
        if (placement.ShowCommand == SwShowMinimized) placement.ShowCommand = SwShowNormal;
        SetWindowPlacement(handle, ref placement);
    }

    /// <summary>
    /// Whether one of the app's popups is open: a menu, a list of suggestions, a pop-up box. Tooltips do not count. Each
    /// popup is a small window of its own, so the ones shown are found among the app's windows.
    /// </summary>
    private static bool AnyPopupOpen()
    {
        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (source is not HwndSource hwnd || hwnd.RootVisual is not Visual root || root.GetType().Name != "PopupRoot") continue;
            if (!IsWindowVisible(hwnd.Handle) || HoldsToolTip(root, 4)) continue;
            return true;
        }
        return false;
    }

    private static bool HoldsToolTip(DependencyObject at, int depth)
    {
        if (at is ToolTip) return true;
        if (depth == 0) return false;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(at); i++)
            if (HoldsToolTip(VisualTreeHelper.GetChild(at, i), depth - 1)) return true;
        return false;
    }

    // ---- Windows functions and values used above ----

    private const int GwlStyle = -16, WsCaption = 0x00C00000, WsThickFrame = 0x00040000;
    private const uint MonitorDefaultToNearest = 2;
    private const int SwShowNormal = 1, SwShowMinimized = 2;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32 { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement { public int Length, Flags, ShowCommand; public Point32 MinPosition, MaxPosition; public Rect32 Normal; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect32 Screen, WorkArea; public int Flags; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr handle, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr handle, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr handle, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr handle);
}
