using System.Runtime.InteropServices;
using System.Text;

namespace EasyNdf.App.Windowing;

/// <summary>
/// Puts every Windows file and folder dialog the app opens (Open, Save, pick a folder) in the middle of the screen the
/// app is on. Windows opens them where one was last moved to, often a corner far from the app. Done once for the
/// whole app, as there are many such dialogs: a watch on windows shown by this process finds each one as it appears
/// and moves it before it is drawn there.
/// </summary>
public static class FileDialogPlacement
{
    private const uint EventObjectShow = 0x8002, OutOfContext = 0x0000, SkipOwnThreadNone = 0;
    private const int ObjectIdWindow = 0;
    private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;
    private const uint OwnerOf = 4, RootOf = 2, MonitorNearest = 2;

    /// <summary>Kept for as long as the app runs: Windows calls it, and a collected delegate would close the app.</summary>
    private static WinEventProc? watch;

    /// <summary>The dialogs moved already: each is moved once, as it first shows, so the user can then move it.</summary>
    private static readonly HashSet<IntPtr> moved = [];

    /// <summary>Starts watching for file dialogs; called once as the app starts, on its screen's thread.</summary>
    public static void Start()
    {
        if (watch is not null) return;
        watch = Shown;
        SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, watch, (uint)Environment.ProcessId, SkipOwnThreadNone, OutOfContext);
    }

    private static void Shown(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (objectId != ObjectIdWindow || window == IntPtr.Zero || GetAncestor(window, RootOf) != window || !IsFileDialog(window)) return;
        if (!moved.Add(window)) return;
        moved.RemoveWhere(each => !IsWindow(each));
        Centre(window);
    }

    /// <summary>
    /// Whether a window is one of Windows' file or folder dialogs: a dialog box ("#32770") holding the file view
    /// ("DUIViewWndClassName"). The app's own message boxes are WPF windows and are left alone.
    /// </summary>
    private static bool IsFileDialog(IntPtr window) =>
        ClassOf(window) == "#32770" && FindWindowEx(window, IntPtr.Zero, "DUIViewWndClassName", null) != IntPtr.Zero;

    /// <summary>Moves a dialog to the middle of the work area of the screen its owner (the app's window) is on.</summary>
    private static void Centre(IntPtr window)
    {
        IntPtr owner = GetWindow(window, OwnerOf);
        IntPtr monitor = MonitorFromWindow(owner != IntPtr.Zero ? owner : window, MonitorNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(window, out Rect box)) return;
        int width = box.Right - box.Left, height = box.Bottom - box.Top;
        Rect area = info.Work;
        int x = area.Left + Math.Max(0, (area.Right - area.Left - width) / 2);
        int y = area.Top + Math.Max(0, (area.Bottom - area.Top - height) / 2);
        SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(64);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private delegate void WinEventProc(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect box);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
