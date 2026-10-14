using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EasyNdf.App.Windowing;

/// <summary>
/// Gives a window the app's own title bar while Windows keeps doing everything else, the way Windows Terminal and the
/// browsers do it. Windows keeps its invisible resize borders on the left, right and bottom, its shadow, snapping and
/// animations; only the title bar it would draw is taken away, and the app's title row takes its place.
/// </summary>
/// <remarks>
/// WPF's own WindowChrome hands the whole window, resize borders too, to the app. While such a window is resized, Windows
/// shows the old picture pinned to the top left until WPF draws the new one, so everything on the right and bottom edges
/// jitters. Keeping the borders as Windows' own avoids that.
/// </remarks>
public sealed class CustomTitleBar
{
    private readonly Window window;
    private readonly Func<Point, bool> isTitleBarAt;

    /// <summary>Takes over the window's title bar once the window exists.</summary>
    /// <param name="window">The window.</param>
    /// <param name="isTitleBarAt">
    /// Whether a point of the window (in WPF units, from its top left) is part of the title bar that drags the window,
    /// rather than a control on it, such as a button.
    /// </param>
    public CustomTitleBar(Window window, Func<Point, bool> isTitleBarAt)
    {
        this.window = window;
        this.isTitleBarAt = isTitleBarAt;
        window.SourceInitialized += (_, _) => Attach();
    }

    private void Attach()
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle).AddHook(WindowMessage);
        // Windows 11 rounds the corners of a window with its own frame, and draws a thin grey border around it. The app's
        // look is square, and its own raised edge is the outermost line, as in the HTML app. Older Windows ignore both.
        int square = DwmCornerDoNotRound, noBorder = DwmColorNone;
        DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref square, sizeof(int));
        DwmSetWindowAttribute(handle, DwmBorderColor, ref noBorder, sizeof(int));
        // Windows works the window's frame out again, now with our answer to WM_NCCALCSIZE.
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
    }

    private IntPtr WindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcCalcSize && wParam != IntPtr.Zero)
        {
            handled = true;
            return LeaveOutTitleBar(handle, wParam, lParam);
        }
        if (message == WmNcHitTest)
        {
            handled = true;
            return WhatIsAt(handle, wParam, lParam);
        }
        // while the window is resized by its edges, the panes are laid out again on every move of the mouse, as when a
        // pane's bar is dragged: whole-pixel edges are checked once, when it ends (moving the window starts this too)
        if (message == WmEnterSizeMove) Controls.PixelSnapping.PauseForDrag();
        if (message == WmExitSizeMove) Controls.PixelSnapping.ResumeAfterDrag();
        return IntPtr.Zero;
    }

    /// <summary>
    /// WM_NCCALCSIZE: Windows works out the usual frame, then the window's content is made to start at the window's top,
    /// where the title bar would be. A maximized window reaches past the screen's top by the frame's thickness, so then
    /// its content starts that much lower.
    /// </summary>
    private static IntPtr LeaveOutTitleBar(IntPtr handle, IntPtr wParam, IntPtr lParam)
    {
        var sizes = Marshal.PtrToStructure<NcCalcSizeParams>(lParam);
        int windowTop = sizes.NewWindow.Top;
        DefWindowProc(handle, WmNcCalcSize, wParam, lParam);

        sizes = Marshal.PtrToStructure<NcCalcSizeParams>(lParam);
        sizes.NewWindow.Top = windowTop + (IsZoomed(handle) ? FrameThickness(handle) : 0);
        Marshal.StructureToPtr(sizes, lParam, false);
        return IntPtr.Zero;
    }

    /// <summary>
    /// WM_NCHITTEST: what is under the mouse. Windows answers for its own resize borders; near the top edge the window can
    /// be resized too; the rest of the title row drags the window (and double-clicking it maximizes), except over its
    /// controls, which the app handles.
    /// </summary>
    private IntPtr WhatIsAt(IntPtr handle, IntPtr wParam, IntPtr lParam)
    {
        IntPtr windowsAnswer = DefWindowProc(handle, WmNcHitTest, wParam, lParam);
        if (windowsAnswer != HtClient) return windowsAnswer;

        int screenX = unchecked((short)(long)lParam), screenY = unchecked((short)((long)lParam >> 16));
        GetWindowRect(handle, out Rect32 bounds);
        if (!IsZoomed(handle) && screenY < bounds.Top + FrameThickness(handle)) return HtTop;

        Point inWindow = window.PointFromScreen(new Point(screenX, screenY));
        return isTitleBarAt(inWindow) ? HtCaption : HtClient;
    }

    /// <summary>The thickness of Windows' resize border, in pixels, at the window's DPI.</summary>
    private static int FrameThickness(IntPtr handle)
    {
        uint dpi = GetDpiForWindow(handle);
        return GetSystemMetricsForDpi(SmCyFrame, dpi) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpi);
    }

    // ---- Windows functions and values used above ----

    private const int WmNcCalcSize = 0x0083, WmNcHitTest = 0x0084, WmEnterSizeMove = 0x0231, WmExitSizeMove = 0x0232;
    private static readonly IntPtr HtClient = 1, HtCaption = 2, HtTop = 12;
    private const int SmCyFrame = 33, SmCxPaddedBorder = 92;
    private const int DwmWindowCornerPreference = 33, DwmCornerDoNotRound = 1, DwmBorderColor = 34;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpFrameChanged = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }

    /// <summary>The first rectangle is the window's new size; Windows replaces it with the new content area.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NcCalcSizeParams { public Rect32 NewWindow, OldWindow, OldContent; public IntPtr Position; }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect32 bounds);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
