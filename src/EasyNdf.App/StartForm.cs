using System.Runtime.InteropServices;

namespace EasyNdf.App;

/// <summary>
/// The small form shown from the app's first moment until its window is there: the icon, the name with the version
/// beside it, a line of what the app is, "Starting…" and the app's block loading bar (as <see cref="Controls.BlockBar"/>:
/// flat blocks in a sunken track). In the default theme's colours, as no theme is read yet.
/// <para>
/// The bar fills block by block at the pace a start-up usually takes, up to where the step being done ends; there it
/// waits for the step (<see cref="Progress"/>), and a step that ends early brings the bar forward to it. So it neither
/// stands still through a long step nor runs ahead of what is done.
/// </para>
/// </summary>
/// <remarks>
/// Tuned for speed, as felt: the window takes a few seconds to come, so this is drawn by Windows' own calls on a thread
/// of its own, before anything of WPF's is loaded, and it keeps drawing while the app's thread is busy starting. Keep it
/// free of WPF and of the app's other classes when tidying.
/// </remarks>
internal static class StartForm
{
    /// <summary>The form's size and the bar's block count, at 96 DPI.</summary>
    private const int Width = 440, Height = 150, Blocks = 32;

    /// <summary>How long a start-up usually takes, which sets the bar's pace; and how often the bar is brought up to date.</summary>
    private const int UsualMilliseconds = 3000;
    private const uint TickMilliseconds = 50;

    // the default theme's colours, as Windows takes them (blue, green, red)
    private const uint Panel = 0x1e221c, Well = 0x151814, Hi = 0x424a3f, Lo = 0x0a0b09, Accent = 0x46a9b7, Ink = 0xe2e8e3, Muted = 0x9ba398;

    private static readonly object gate = new();
    private static readonly WindowProcedure procedure = Handle;   // kept, so it lives as long as the window
    private static IntPtr window;
    private static bool closed;
    private static volatile int filled;
    // the blocks the steps done stand for, and where the step being done ends: at first, reading the saved data and theme
    private static volatile int done, stepEnds = 12;
    private static long shownAt;
    private static double scale = 1;
    private static string version = "";

    /// <summary>Shows the form, in the middle of the main screen. It takes neither the keyboard nor a place on the taskbar.</summary>
    public static void Show()
    {
        Version? number = typeof(StartForm).Assembly.GetName().Version;
        version = number is null ? "" : "v" + number.Major + "." + number.Minor + "." + number.Build;
        new Thread(Run) { IsBackground = true, Name = "Start form" }.Start();
    }

    /// <summary>
    /// Says how far the start-up is, from 0 to 1, and where the step now starting ends: the bar comes forward to the
    /// first and may fill on to just before the second.
    /// </summary>
    public static void Progress(double reached, double nextStepEnds)
    {
        done = (int)Math.Floor(Math.Clamp(reached, 0, 1) * Blocks);
        stepEnds = (int)Math.Floor(Math.Clamp(nextStepEnds, 0, 1) * Blocks);
    }

    /// <summary>Brings the bar up to date: whole blocks, never fewer than before.</summary>
    private static void Tick(IntPtr at)
    {
        long byPace = (Environment.TickCount64 - shownAt) * Blocks / UsualMilliseconds;
        int blocks = (int)Math.Min(Blocks, Math.Max(done, Math.Min(byPace, stepEnds - 1)));
        if (blocks <= filled) return;
        filled = blocks;
        var bar = new Rect { Left = Px(26), Top = Px(112), Right = Px(26 + 388), Bottom = Px(112 + 18) };
        InvalidateRect(at, ref bar, false);
    }

    /// <summary>Closes the form: the app's window is there, or the start-up failed.</summary>
    public static void Close()
    {
        lock (gate)
        {
            closed = true;
            if (window != IntPtr.Zero) PostMessageW(window, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static void Run()
    {
        try
        {
            IntPtr module = GetModuleHandleW(null);
            scale = Math.Max(1, GetDpiForSystem() / 96.0);
            var kind = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Style = 0x00020000,   // a shadow under it
                Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
                Instance = module,
                Cursor = LoadCursorW(IntPtr.Zero, (IntPtr)32650),   // the arrow with the busy ring
                Name = "EasyNdfStartForm",
            };
            if (RegisterClassExW(ref kind) == 0) return;
            int width = Px(Width), height = Px(Height);
            IntPtr made = CreateWindowExW(0x00000080 /* off the taskbar */, kind.Name, "EasyNDF", 0x80000000 /* no frame */,
                (GetSystemMetrics(0) - width) / 2, (GetSystemMetrics(1) - height) / 2, width, height, IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
            if (made == IntPtr.Zero) return;
            lock (gate)
            {
                window = made;
                if (closed) PostMessageW(made, 0x0010, IntPtr.Zero, IntPtr.Zero);
            }
            shownAt = Environment.TickCount64;
            ShowWindow(made, 4 /* shown, the keyboard left where it is */);
            SetTimer(made, 1, TickMilliseconds, IntPtr.Zero);
            while (GetMessageW(out Message message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception notShown) when (notShown is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            // the app starts without the form
        }
    }

    private static IntPtr Handle(IntPtr at, uint message, IntPtr wide, IntPtr large)
    {
        switch (message)
        {
            case 0x000F:   // WM_PAINT
                IntPtr paint = BeginPaint(at, out PaintData data);
                Draw(paint);
                EndPaint(at, ref data);
                return IntPtr.Zero;
            case 0x0113:   // WM_TIMER
                Tick(at);
                return IntPtr.Zero;
            case 0x0014:   // WM_ERASEBKGND: Draw fills it
                return 1;
            case 0x0010:   // WM_CLOSE
                lock (gate) window = IntPtr.Zero;
                DestroyWindow(at);
                return IntPtr.Zero;
            case 0x0002:   // WM_DESTROY
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(at, message, wide, large);
    }

    private static void Draw(IntPtr paint)
    {
        int width = Px(Width), height = Px(Height);
        Fill(paint, Panel, 0, 0, width, height);
        // the bevel: light top and left, dark bottom and right, as the app's panels
        Fill(paint, Hi, 0, 0, width, 1);
        Fill(paint, Hi, 0, 0, 1, height);
        Fill(paint, Lo, 0, height - 1, width, 1);
        Fill(paint, Lo, width - 1, 0, 1, height);

        // the app's icon, from the program file, at 64 pixels
        if (Environment.ProcessPath is string program && PrivateExtractIconsW(program, 0, Px(64), Px(64), out IntPtr icon, out _, 1, 0) > 0 && icon != IntPtr.Zero)
        {
            DrawIconEx(paint, Px(30), Px(28), icon, Px(64), Px(64), 0, IntPtr.Zero, 3);
            DestroyIcon(icon);
        }

        SetBkMode(paint, 1 /* see-through */);
        SetTextAlign(paint, 24 /* placed by the text's baseline */);
        int nameWidth = Write(paint, "EasyNDF", 29, bold: true, Accent, 110, 48);
        Write(paint, version, 12, bold: false, Muted, 110 + (int)Math.Round(nameWidth / scale) + 10, 48);
        Write(paint, "Rule-based .ndf editing", 13, bold: false, Ink, 112, 75);
        Write(paint, "Starting…", 12, bold: false, Muted, 112, 97);

        // the bar, as Controls.BlockBar: a sunken track (1 pixel of edge, 2 inside it), blocks 10 by 12 with 2 between
        int left = Px(26), top = Px(112), barWidth = Px(388), barHeight = Px(18), edge = Px(1);
        Fill(paint, Well, left, top, barWidth, barHeight);
        Fill(paint, Lo, left, top, barWidth, edge);
        Fill(paint, Lo, left, top, edge, barHeight);
        Fill(paint, Hi, left, top + barHeight - edge, barWidth, edge);
        Fill(paint, Hi, left + barWidth - edge, top, edge, barHeight);
        for (int block = 0, shown = filled; block < shown; block++)
            Fill(paint, Accent, Px(26 + 3 + block * 12), Px(112 + 3), Px(10), Px(12));
    }

    /// <summary>Writes one line in Tahoma, its baseline at the place given (at 96 DPI); gives back its width in pixels.</summary>
    private static int Write(IntPtr paint, string text, int size, bool bold, uint colour, int left, int baseline)
    {
        IntPtr font = CreateFontW(-Px(size), 0, 0, 0, bold ? 700 : 400, 0, 0, 0, 1, 0, 0, 5 /* ClearType */, 0, "Tahoma");
        IntPtr before = SelectObject(paint, font);
        SetTextColor(paint, colour);
        TextOutW(paint, Px(left), Px(baseline), text, text.Length);
        GetTextExtentPoint32W(paint, text, text.Length, out Extent extent);
        SelectObject(paint, before);
        DeleteObject(font);
        return extent.Width;
    }

    private static void Fill(IntPtr paint, uint colour, int left, int top, int width, int height)
    {
        var area = new Rect { Left = left, Top = top, Right = left + width, Bottom = top + height };
        IntPtr brush = CreateSolidBrush(colour);
        FillRect(paint, ref area, brush);
        DeleteObject(brush);
    }

    /// <summary>A length at 96 DPI, in the screen's pixels.</summary>
    private static int Px(int length) => (int)Math.Round(length * scale);

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wide, IntPtr large);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public IntPtr Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? Menu;
        public string Name;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Extent { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr Wide, Large;
        public uint Time;
        public int X, Y;
        public uint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintData
    {
        public IntPtr Paint;
        public int Erase;
        public Rect Area;
        public int Restore, Update;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int which);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr module, IntPtr name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WindowClass kind);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint moreStyle, string kind, string title, uint style, int left, int top, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr module, IntPtr data);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int how);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr window, IntPtr id, uint milliseconds, IntPtr procedure);
    [DllImport("user32.dll")] private static extern int GetMessageW(out Message message, IntPtr window, uint from, uint to);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wide, IntPtr large);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wide, IntPtr large);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr window, out PaintData data);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr window, ref PaintData data);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr window, ref Rect area, bool erase);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr paint, ref Rect area, IntPtr brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIconsW(string file, int index, int width, int height, out IntPtr icon, out uint id, uint count, uint flags);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr paint, int left, int top, IntPtr icon, int width, int height, uint step, IntPtr brush, uint how);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint colour);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr thing);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr paint, IntPtr thing);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr paint, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextAlign(IntPtr paint, uint how);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr paint, uint colour);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int tilt, int turn, int weight, uint italic, uint underline, uint strike, uint letters,
        uint precision, uint clip, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool TextOutW(IntPtr paint, int left, int top, string text, int length);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool GetTextExtentPoint32W(IntPtr paint, string text, int length, out Extent extent);
}
