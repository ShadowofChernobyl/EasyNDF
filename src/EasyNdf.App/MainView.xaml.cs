using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Text;

namespace EasyNdf.App;

/// <summary>The title row, the tabs and their pages: everything inside the main window.</summary>
public partial class MainView : UserControl
{
    /// <summary>Watches every tooltip in the app, once, for the "Tooltips" setting.</summary>
    static MainView() => StopTooltipsWhenOff();

    /// <summary>Builds the view with the Files tab showing.</summary>
    public MainView()
    {
        InitializeComponent();
        VersionTag.Content = "v" + AppVersion;
        // the rule set list says how many listed files each rule set applies to
        RulesTab.FilesUsing = ruleSet => FilesTab.Model.Entries.Count(entry => Files.FileRunner.AppliesTo(ruleSet, entry));
        History = new UndoHistory(AppData.Current, FilesTab.Model);
        FilesTab.Model.PropertyChanged += (_, _) => History.MarkSoon();
        AppData.Current.ModsChanged += _ => History.ModsChanged();
        AppData.Current.ModFoldersChanged += History.MarkSoon;
        RulesTab.History = History;
        RuleWindow.Closed += RulesTab.RuleWindowClosed;
        RuleWindow.Rows = RowWindow;
        EditorTab.Files = FilesTab.Model;
        EditorTab.ReadFile = FilesTab.ReadInBackground;
        EditorTab.AddFile = async added =>
        {
            await FilesTab.AddAndRead([added], fromFolder: false);
            return FilesTab.Model.Entries.LastOrDefault(entry => entry.Name == added.Name);
        };
        FilesTab.OpenInEditor = entry => _ = EditorTab.Open(entry);
        // one source for the Files and Units tabs: choosing it on either chooses it on both
        UnitsTab.UseSources(Sources);
        FilesTab.UseSources(Sources);
        // the Editor tab's list of files follows the Files tab
        Tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, Tabs) && Tabs.SelectedItem == EditorTabItem) EditorTab.Refresh(); };
        Watermark.AppVersion = AppVersion;
        RowWindow.OpenChanged += RuleWindow.ShowBeside;
        RuleWindow.ListedFiles = () => FilesTab.Model.Entries;
        RuleWindow.ReadFile = FilesTab.ReadInBackground;
        FilesTab.Model.FilesChanged += RuleWindow.FilesChanged;
        // a file added, removed, switched on or off, or given another preset changes the values the Armory shows
        FilesTab.Model.PropertyChanged += (_, _) => UnitsTab.ModValuesMayHaveChanged();
        VersionTag.Click += (_, _) => ShowLatestChanges();
        HelpTab.WelcomeRequested += Welcome.Open;
        // the window's size can put the Files and Editor tabs' second panes beside or below
        SizeChanged += (_, _) => Controls.PaneArrangement.WindowResized(WindowSize.Width, WindowSize.Height);
        SettingChanged += key => { if (key is "panesAuto" or "panesSide" or "") Controls.PaneArrangement.Update(); };
        SettingChanged += key => { if (key == "panesScale") Controls.PaneArrangement.Update(alwaysTell: true); };
        // the scope lines are drawn behind the text, which is not redrawn by itself
        SettingChanged += key => { if (key is "indentGuides" or "") EditorTab.Text.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background); };
        SettingChanged += key => { if (key is "lineNumbers" or "wordWrap" or "") EditorTab.ApplyViewSettings(); };
        SettingChanged += key => { if (key is "treeShowsAll" or "") _ = EditorTab.RebuildTree(); };
        SettingChanged += key => { if (key is "uiScale" or "") ApplyScale(); };
        SettingChanged += key => { if (key is "armoryMarks" or "armoryFileNames" or "") UnitsTab.MarksSettingChanged(); };
        ApplyScale();
        Controls.PaneArrangement.Changed += () => SettingsTab.Show();
        HelpPopup.ShowPage += ShowHelpPage;
        PreviewMouseDown += CloseHelpPopupOnClickElsewhere;
        PreviewKeyDown += CloseHelpPopupOnEscape;
    }

    /// <summary>"Interface size": everything in the window is drawn larger or smaller, as a browser's zoom does.</summary>
    private void ApplyScale()
    {
        double percent = double.TryParse(AppData.Current.Settings["uiScale"]?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double chosen) ? chosen : 100;
        double scale = Math.Clamp(percent, 50, 200) / 100;
        Page.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
    }

    /// <summary>
    /// "Tooltips" off: no tooltip opens anywhere in the app. Each one is stopped as it is about to open, so the setting
    /// works at once and every control keeps its tooltip for when it is on again.
    /// </summary>
    private static void StopTooltipsWhenOff()
    {
        ToolTipEventHandler stop = (_, e) => { if (AppData.Current.Settings["tooltips"]?.GetValue<bool>() == false) e.Handled = true; };
        EventManager.RegisterClassHandler(typeof(FrameworkElement), ToolTipService.ToolTipOpeningEvent, stop);
        EventManager.RegisterClassHandler(typeof(FrameworkContentElement), ToolTipService.ToolTipOpeningEvent, stop);
    }

    /// <summary>
    /// Shows a page on the Help tab (the ? popup's See more, the HTML app's <c>qMore</c>): the windows in the way close
    /// first, as their × closes them.
    /// </summary>
    public void ShowHelpPage(string id)
    {
        RowWindow.Close();
        Watermark.Cancel();
        RuleWindow.Close();
        Tabs.SelectedItem = HelpTabItem;
        HelpTab.Go(id);
    }

    /// <summary>A click anywhere but on the ? popup or a ? button closes the popup, and still does what it does.</summary>
    private void CloseHelpPopupOnClickElsewhere(object sender, MouseButtonEventArgs e)
    {
        if (!HelpPopup.IsOpen || HelpPopup.Contains(e.GetPosition(HelpPopup))) return;
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is Button button && Controls.QuickHelp.GetPage(button) is not null) return;
        HelpPopup.Close();
    }

    /// <summary>Escape closes the ? popup first, before anything else it would close.</summary>
    private void CloseHelpPopupOnEscape(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !HelpPopup.IsOpen) return;
        e.Handled = true;
        HelpPopup.Close();
    }

    /// <summary>The change log: what changed in each version, newest first (Help/changelog.html), in the language in use.</summary>
    private static string ChangeLogText() => Help.WikiPages.ChangeLogHtml;

    /// <summary>
    /// The version button: what changed in the newest version only, read from the change log (from its first version
    /// heading to the next), so it follows the change log without being written twice.
    /// </summary>
    public void ShowLatestChanges()
    {
        string log = ChangeLogText();
        int first = log.IndexOf("<h2>", StringComparison.Ordinal);
        if (first < 0) { ShowChangeLog(); return; }
        int next = log.IndexOf("<h2>", first + 4, StringComparison.Ordinal);
        string latest = next < 0 ? log[first..] : log[first..next];
        (FrameworkElement text, _, _) = new Help.WikiRenderer(Help.PageLook.ChangeLog).Render(latest);
        TextWindow.Open("What's New", text);
    }

    /// <summary>The change log window: what changed in each version, newest first.</summary>
    public void ShowChangeLog()
    {
        (FrameworkElement log, double top, _) = new Help.WikiRenderer(Help.PageLook.ChangeLog).Render(ChangeLogText());
        // the scrolling box keeps the first paragraph's margin inside it, as a browser does
        log.Margin = new Thickness(0, top, 0, 0);
        TextWindow.Open(Texts.Get("About.ChangeLog.Title"), log);
    }

    /// <summary>"Paste" in a menu of the rule window: what is on the clipboard goes where the Rules tab's Paste puts it.</summary>
    public void PasteFromClipboard() => RulesTab.PasteFromClipboard();

    /// <summary>The game or mod the app works with, chosen in the Source list of the Files or Units tab.</summary>
    public Units.SourceSelection Sources { get; } = new();

    /// <summary>Undo and Redo, for the changes on the Files, Rules and Editor tabs and the Armory's.</summary>
    public UndoHistory History { get; }

    /// <summary>The Undo button and Ctrl+Z.</summary>
    public void Undo() => Tell(History.Undo());

    /// <summary>The Redo button, Ctrl+Y and Ctrl+Shift+Z.</summary>
    public void Redo() => Tell(History.Redo());

    /// <summary>
    /// Says what Undo or Redo did under the Rules tab's buttons, above the file list too when something changed or
    /// the Files tab is showing, and at the foot of the Armory when it is showing.
    /// </summary>
    private void Tell(UndoResult result)
    {
        RulesTab.Model.Say(result.Message);
        if (result.IsDone || Tabs.SelectedItem == FilesTabItem) FilesTab.Model.Say(result.Message);
        if (Tabs.SelectedItem == UnitsTabItem) UnitsTab.SayForAMoment(result.Message);
    }

    /// <summary>
    /// A setting was changed on the Settings tab (its name; "" for all of them, after Reset settings): the tabs that use
    /// it follow.
    /// </summary>
    public void SettingsChanged(string key) => SettingChanged?.Invoke(key);

    /// <summary>The window's size, in Windows' scaled pixels; the view's own with its border while it has no window.</summary>
    private Size WindowSize => Window.GetWindow(this) is Window window && window.ActualWidth > 0
        ? new Size(window.ActualWidth, window.ActualHeight)
        : new Size(ActualWidth + 20, ActualHeight + 20);

    /// <summary>Raised when a setting changed on the Settings tab, with its name ("" for all).</summary>
    public event Action<string>? SettingChanged;

    /// <summary>The main view an element is in, found through its parents.</summary>
    public static MainView Of(DependencyObject element) =>
        Find(element) ?? throw new InvalidOperationException(element.GetType().Name + " is not inside the main view.");

    /// <summary>
    /// The main view an element is in, or null when it is not in one (yet). The parents on screen are followed first, as
    /// they always lead to the window; where there is none, such as for a page that is not on screen, the parent it was
    /// written in.
    /// </summary>
    public static MainView? Find(DependencyObject element)
    {
        DependencyObject? at = element;
        while (at is not null and not MainView)
            at = (at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : null) ?? LogicalTreeHelper.GetParent(at);
        return at as MainView;
    }

    /// <summary>The app's version, such as "0.1.0", from the project file.</summary>
    public static string AppVersion
    {
        get
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "" : version.Major + "." + version.Minor + "." + version.Build;
        }
    }

    /// <summary>Shows whether the window is maximized: the maximize button becomes a restore button.</summary>
    public void ShowWindowState(WindowState state)
    {
        bool maximized = state == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? Texts.Get("Main.Restore.Tip") : Texts.Get("Main.Maximize.Tip");
    }

    /// <summary>
    /// Whether a point of the view is title bar that drags the window: inside the title row, but not on one of its
    /// controls (the version label and the window buttons).
    /// </summary>
    public bool IsTitleBarAt(Point point)
    {
        if (point.Y < 0 || point.Y >= TitleRow.ActualHeight) return false;
        for (DependencyObject? hit = InputHitTest(point) as DependencyObject; hit is not null; hit = VisualTreeHelper.GetParent(hit))
            if (hit is ButtonBase) return false;
        return true;
    }

    /// <summary>
    /// What closing the app now would lose, one line each: edits in the File Editor not yet saved, changes to the rule
    /// open in its window not yet confirmed with Done, and an export still running. Mods, rules and settings are saved
    /// as they change, so they are never on the list.
    /// </summary>
    public List<string> WhatClosingLoses()
    {
        var lost = new List<string>();
        if (EditorTab.EditedFileName is string file) lost.Add(Texts.Get("Main.Close.EditorEdits", ("file", file)));
        if (RuleWindow.IsOpen && RulesTab.Model.HasUnsavedRuleChanges) lost.Add(Texts.Get("Main.Close.RuleChanges"));
        if (FilesTab.Model.IsBusy && FilesTab.Model.IsExporting) lost.Add(Texts.Get("Main.Close.Export"));
        return lost;
    }

    /// <summary>Asks whether to close although <see cref="WhatClosingLoses"/> names something. True to close.</summary>
    public async Task<bool> AskBeforeClosing()
    {
        List<string> lost = WhatClosingLoses();
        if (lost.Count == 0) return true;
        string message = Texts.Get("Main.Close.Ask") + Environment.NewLine + string.Join(Environment.NewLine, lost.Select(line => "• " + line));
        return await Messages.Confirm(message, Texts.Get("Main.Close.Anyway"));
    }

    /// <summary>
    /// A key the focused control did not take: on the Files tab, Ctrl+A selects every row shown and Delete removes the
    /// selected rows. Nothing happens while the message window is open.
    /// </summary>
    public void HandleShortcut(KeyEventArgs e)
    {
        // the Watermark window takes its own keys
        bool onArmory = Tabs.SelectedItem == UnitsTabItem;
        bool onEditor = Tabs.SelectedItem == EditorTabItem;
        // not on Settings, whose changes Undo never takes back, nor on Help and About, which change nothing
        if (Messages.IsOpen || Watermark.IsOpen || (Tabs.SelectedItem != FilesTabItem && Tabs.SelectedItem != RulesTabItem && !onArmory && !onEditor)) return;
        // nor under the Add Units window or a unit's pop-up: they act on the division as it was when they opened, so
        // an Undo beneath them would be overwritten by their Done, or leave them adding to a division gone
        if (AddUnits.IsOpen || NewUnit.IsOpen || Floating.IsOpen || GenerateDivisions.IsOpen || GenerationSettings.IsOpen) return;
        // Undo and redo work on the Files, Rules and Editor tabs and the Armory outside a text box (the File Editor's text
        // too), which keeps its own, and not while the rule window is open: its changes are not saved yet, and its × is
        // the way to take them back.
        bool inTextBox = Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem or ICSharpCode.AvalonEdit.Editing.TextArea;
        ModifierKeys keys = Keyboard.Modifiers;
        if (!inTextBox && !RuleWindow.IsOpen && keys.HasFlag(ModifierKeys.Control) && !keys.HasFlag(ModifierKeys.Alt))
        {
            bool shift = keys.HasFlag(ModifierKeys.Shift);
            if (e.Key == Key.Z && !shift) { e.Handled = true; Undo(); return; }
            if (e.Key == Key.Y || (e.Key == Key.Z && shift)) { e.Handled = true; Redo(); return; }
        }
        if (onArmory || onEditor) return;
        if (RuleWindow.IsOpen) RuleWindow.HandleShortcut(e, RulesTab.Paste);
        else if (Tabs.SelectedItem == FilesTabItem) FilesTab.HandleShortcut(e);
        else RulesTab.HandleShortcut(e);
    }

    // ---- files dragged onto the app from outside it ----

    /// <summary>Files dragged over the app: the Files tab comes to the front, since that is where they will land.</summary>
    private void FilesDraggedOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        if (Messages.IsOpen) return;
        Tabs.SelectedItem = FilesTabItem;
        FilesTab.ShowDropHint(true);
    }

    /// <summary>The drag left the app: the drop outline goes.</summary>
    private void FilesDragLeft(object sender, DragEventArgs e)
    {
        Point at = e.GetPosition(this);
        if (at.X <= 0 || at.Y <= 0 || at.X >= ActualWidth || at.Y >= ActualHeight) FilesTab.ShowDropHint(false);
    }

    private async void FilesDropped(object sender, DragEventArgs e)
    {
        e.Handled = true;
        FilesTab.ShowDropHint(false);
        if (Messages.IsOpen || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        await FilesTab.AddDropped(paths);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this).WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        Window window = Window.GetWindow(this);
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this).Close();
}
