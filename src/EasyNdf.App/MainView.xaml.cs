using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Text;
using EasyNdf.App.Windowing;

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
        Notifications.RecentChanged += ShowNoticeCount;
        PreviewMouseDown += CloseNoticesOnClickElsewhere;
        // the notifications stay above the usage readout
        Readout.SizeChanged += (_, _) => Notifications.KeepAbove(Readout.ActualHeight);
        Readout.IsVisibleChanged += (_, _) => Notifications.KeepAbove(Readout.IsVisible ? Readout.ActualHeight : 0);
        // the rule set list says how many listed files each rule set applies to
        RulesTab.FilesUsing = ruleSet => FilesTab.Model.Entries.Count(entry => Files.FileRunner.AppliesTo(ruleSet, entry));
        // a mod that is on whose game has no file on the Files tab is dimmed on the Mods tab
        RulesTab.FileGames = () => FilesTab.Model.Entries.Select(entry => entry.Game).ToHashSet();
        FilesTab.Model.FilesChanged += RulesTab.FilesChanged;
        History = new UndoHistory(AppData.Current, FilesTab.Model);
        FilesTab.Model.PropertyChanged += (_, _) => History.MarkSoon();
        AppData.Current.ModsChanged += History.ModsChanged;
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
        // "Show mods' changes" lists only the mods of the source's game
        Units.ModChangesSwitch.SourceGame = () => Sources.Chosen?.Game;
        Sources.Changed += Units.ModChangesSwitch.ShowSourceChanged;
        // the Editor tab's list of files follows the Files tab
        Tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, Tabs)) return;
            SessionLog.Write("Tab", WhereTheUserIs());
            if (Tabs.SelectedItem == EditorTabItem) { if (!IsPoppedOut(EditorTabItem)) EditorTab.Refresh(); }
            // a large file in the editor is let go of a while after its tab is left (EditorPage.LetGo.cs); in a window
            // of its own, the editor is still shown
            else if (e.RemovedItems.Contains(EditorTabItem) && !IsPoppedOut(EditorTabItem)) EditorTab.TabLeft();
        };
        Memory.WhenIdle = PackUnusedRuleSets;
        Watermark.AppVersion = AppVersion;
        RowWindow.OpenChanged += RuleWindow.ShowBeside;
        RuleWindow.ListedFiles = () => FilesTab.Model.Entries;
        RuleWindow.FilesModel = () => FilesTab.Model;
        RuleWindow.ReadFile = FilesTab.ReadInBackground;
        FilesTab.Model.FilesChanged += RuleWindow.FilesChanged;
        // the Editor in a window of its own is shown beside the Files tab: files added or taken out reach its list at
        // once, as one refresh however many changes come together
        FilesTab.Model.FilesChanged += () =>
        {
            if (!IsPoppedOut(EditorTabItem) || editorRefreshWaits) return;
            editorRefreshWaits = true;
            Dispatcher.BeginInvoke(() => { editorRefreshWaits = false; if (IsPoppedOut(EditorTabItem)) EditorTab.Refresh(); }, System.Windows.Threading.DispatcherPriority.Background);
        };
        FilesTab.Model.RowReplaced += entry => _ = EditorTab.ReadAgain(entry);
        // a file added, removed, switched on or off, or given another preset changes the values the Armory shows
        FilesTab.Model.PropertyChanged += (_, _) => UnitsTab.ModValuesMayHaveChanged();
        VersionTag.Click += (_, _) => ShowLatestChanges();
        HelpTab.WelcomeRequested += Welcome.Open;
        // the window's size can put the Files and Editor tabs' second panes beside or below
        SizeChanged += (_, _) => Controls.PaneArrangement.WindowResized(WindowSize.Width, WindowSize.Height);
        SettingChanged += key => { if (key is "panesAuto" or "panesSide" or "") Controls.PaneArrangement.Update(); };
        SettingChanged += key => { if (key == "panesScale") Controls.PaneArrangement.Update(alwaysTell: true); };
        // the scope lines are drawn behind the text, which is not redrawn by itself
        SettingChanged += key => { if (key is "indentGuides" or "") EditorTab.RedrawIndentGuides(); };
        SettingChanged += key => { if (key is "lineNumbers" or "wordWrap" or "") EditorTab.ApplyViewSettings(); };
        SettingChanged += key => { if (key is "treeShowsAll" or "") _ = EditorTab.RebuildTree(); };
        SettingChanged += key => { if (key is "uiScale" or "") { ApplyScale(); ScaleTabWindows(); } };
        SettingChanged += key => { if (key is "armoryMarks" or "armoryFileNames" or "") UnitsTab.MarksSettingChanged(); };
        // the usage readout measures only while it is switched on (Settings, Debug)
        SettingChanged += key => { if (key is "usageReadout" or "usageEvery" or "") Readout.Apply(); };
        // the session log (Settings, Debug); every setting changed is a line in it
        SettingChanged += key => { if (key is "logOn" or "logKeep" or "") SessionLog.Apply(); };
        SettingChanged += key => { if (key != "") SessionLog.Write("Settings", key + " = " + AppData.Current.Settings[key]?.ToJsonString()); };
        Readout.Where = WhereTheUserIs;
        Readout.MemoryParts = MemoryParts;
        ApplyScale();
        Readout.Apply();
        SessionLog.Apply();
        Controls.PaneArrangement.Changed += () => SettingsTab.Show();
        HelpPopup.ShowPage += ShowHelpPage;
        PreviewMouseDown += CloseHelpPopupOnClickElsewhere;
        PreviewKeyDown += CloseHelpPopupOnEscape;
        WatchTabsForPopOut();
        WatchMouseButtons();
        WatchRuleLayer();
        HoldBackWhileAskedElsewhere();
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
    /// Shows a page on the Help tab (the ? popup's See more, the HTML app's <c>qMore</c>): the Watermark window in the
    /// way closes first, as its × closes it. An open rule stays as it is, on the Mods tab.
    /// </summary>
    public void ShowHelpPage(string id)
    {
        Watermark.Cancel();
        ShowTab(HelpTabItem);
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
    /// Tuned for memory: whenever the app is idle, every rule set of every mod put away (<see cref="Data.ModPreset.PackRules"/>),
    /// switched on or off and whatever its size, but those shown or selected on the Rules tab, and none while a rule
    /// window is open, as a rule held there would lose a change made to it. With large mods it took rule sets from
    /// about 246 MB to a few, the mod files they were read from let go of with them. Keep it that way when tidying.
    /// </summary>
    private void PackUnusedRuleSets()
    {
        if (RowWindow.IsOpen || RuleWindow.IsOpen) return;
        HashSet<Data.ModPreset> inUse = [.. RulesTab.Model.SelectedPresets];
        if (RulesTab.Model.OpenPreset is Data.ModPreset open) inUse.Add(open);
        foreach (Data.Mod mod in AppData.Current.Mods)
            foreach (Data.ModPreset preset in mod.Presets)
                if (!inUse.Contains(preset)) preset.PackRules();
    }

    /// <summary>
    /// The version button: what changed in the newest version only, read from the change log (from its first version
    /// heading to the next), so it follows the change log without being written twice.
    /// </summary>
    public void ShowLatestChanges() => ShowChangesSince(null);

    /// <summary>
    /// What's New with every version newer than <paramref name="since"/> (the version the user had before updating),
    /// newest first; only the newest when that version is not known.
    /// </summary>
    public void ShowChangesSince(string? since)
    {
        string log = ChangeLogText();
        int first = log.IndexOf("<h2>", StringComparison.Ordinal);
        if (first < 0) { ShowChangeLog(); return; }
        int next = log.IndexOf("<h2>", first + 4, StringComparison.Ordinal);
        string latest = next < 0 ? log[first..] : log[first..next];
        if (Version.TryParse(since, out Version? before))
        {
            var newer = new System.Text.StringBuilder();
            foreach (string piece in log[first..].Split("<h2>", StringSplitOptions.RemoveEmptyEntries))
            {
                int end = piece.IndexOf("</h2>", StringComparison.Ordinal);
                if (end < 0 || !Version.TryParse(piece[..end].Trim(), out Version? version) || version <= before) continue;
                newer.Append("<h2>").Append(piece);
            }
            if (newer.Length > 0) latest = newer.ToString();
        }
        (FrameworkElement text, _, _) = new Help.WikiRenderer(Help.PageLook.ChangeLog).Render(latest);
        TextWindow.Open(Texts.Get("Main.WhatsNew.Title"), text);
    }

    /// <summary>
    /// What's New, shown by itself once after each update: when the version running is not the one What's New was last
    /// shown for. A first start shows the Welcome window instead, which counts as having seen that version.
    /// </summary>
    public void ShowLatestChangesAfterUpdate()
    {
        string? before = AppData.Current.State[WhatsNewKey] is System.Text.Json.Nodes.JsonValue seen && seen.TryGetValue(out string? shown) ? shown
            : AppData.Current.State["welcomed"] is System.Text.Json.Nodes.JsonValue welcomed && welcomed.TryGetValue(out string? first) ? first : null;
        if (!WhatsNewIsDue(AppData.Current.State, AppVersion))
        {
            if (BundledModAskedLater()) _ = OfferBundledModUpdate();
            else RememberBundledMod();
            return;
        }
        AppData.Current.State[WhatsNewKey] = AppVersion;
        AppData.Current.SaveState();
        ShowLatestChangesThenOfferBundledMod(before);
    }

    /// <summary>The question about the mod the app comes with first, then What's New: one popup at a time.</summary>
    private async void ShowLatestChangesThenOfferBundledMod(string? before)
    {
        await OfferBundledModUpdate();
        ShowChangesSince(before);
    }

    /// <summary>Where the saved state keeps the version What's New was last shown for.</summary>
    public const string WhatsNewKey = "whatsNew";

    /// <summary>
    /// Whether What's New has yet to be shown for a version: the app was started before (its Welcome window was closed,
    /// which keeps the version then running), and neither then nor since was this the version.
    /// </summary>
    public static bool WhatsNewIsDue(System.Text.Json.Nodes.JsonObject state, string version)
    {
        static string? Text(System.Text.Json.Nodes.JsonNode? kept) =>
            kept is System.Text.Json.Nodes.JsonValue value && value.TryGetValue(out string? text) ? text : null;
        if (state["welcomed"] is null || version == "") return false;
        return (Text(state[WhatsNewKey]) ?? Text(state["welcomed"])) != version;
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

    /// <summary>Gives a notification of what Undo or Redo did.</summary>
    private static void Tell(UndoResult result) => Notices.Say(result.Message, result.IsDone ? NoticeKind.Done : NoticeKind.Caution);

    /// <summary>The button on the tab row opens or closes the recent notifications, under it at the right.</summary>
    private void NoticesClicked(object sender, RoutedEventArgs e)
    {
        if (Notifications.IsRecentOpen) { Notifications.CloseRecent(); return; }
        Point corner = NoticesButton.TranslatePoint(new Point(NoticesButton.ActualWidth, NoticesButton.ActualHeight), Notifications);
        Notifications.OpenRecent(corner.Y + 2, Notifications.ActualWidth - corner.X);
    }

    /// <summary>The button shows how many notifications there are to look back on.</summary>
    private void ShowNoticeCount()
    {
        int count = Notices.Recent.Count;
        NoticeCount.Text = count == 0 ? "" : Text.Texts.Number(count);
        NoticeCount.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A click anywhere else closes the recent notifications.</summary>
    private void CloseNoticesOnClickElsewhere(object sender, MouseButtonEventArgs e)
    {
        if (!Notifications.IsRecentOpen || Notifications.IsOnRecent(e.GetPosition(Notifications)) || NoticesButton.IsMouseOver) return;
        Notifications.CloseRecent();
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
        {
            // a tab in a window of its own, and the windows shown over it there, belong to the view the tab came from
            if (at is Windowing.PopOutWindow own) return own.View;
            at = (at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : null) ?? LogicalTreeHelper.GetParent(at);
        }
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
        // full screen (F11) shows Restore too: the button leaves it
        bool maximized = state == WindowState.Maximized || (Window.GetWindow(this) is Window window && FullScreen.IsOn(window));
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
        // what is there can be a piece of a text, which is not a Visual
        for (DependencyObject? hit = InputHitTest(point) as DependencyObject; hit is not null; hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit))
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
        return await Messages.Confirm(message, Texts.Get("Main.Close.Anyway"), DialogIcon.Warning);
    }

    /// <summary>
    /// A key the focused control did not take: on the Files tab, Ctrl+A selects every row shown and Delete removes the
    /// selected rows. Nothing happens while the message window is open.
    /// </summary>
    /// <param name="e">The key.</param>
    /// <param name="ownWindow">The tab whose own window the key was pressed in; null in the main window.</param>
    public void HandleShortcut(KeyEventArgs e, TabItem? ownWindow = null)
    {
        // the tab the key is for: none on the main window's note about a tab that is in a window of its own
        TabItem? on = ownWindow ?? (Tabs.SelectedItem is TabItem shown && !IsPoppedOut(shown) ? shown : null);
        // the Watermark window takes its own keys
        bool onArmory = on == UnitsTabItem;
        bool onEditor = on == EditorTabItem;
        // not on Settings, whose changes Undo never takes back, nor on Help and About, which change nothing
        if (Messages.IsOpen || Watermark.IsOpen || (on != FilesTabItem && on != RulesTabItem && !onArmory && !onEditor)) return;
        // nor under the Add Units window or a unit's pop-up: they act on the division as it was when they opened, so
        // an Undo beneath them would be overwritten by their Done, or leave them adding to a division gone
        if (AddUnits.IsOpen || NewUnit.IsOpen || Floating.IsOpen || GenerateDivisions.IsOpen || GenerationSettings.IsOpen || ExportWindow.IsOpen) return;
        // Undo and redo work on the Files, Rules and Editor tabs and the Armory outside a text box (the File Editor's text
        // too), which keeps its own, and not while the rule window is open: its changes are not saved yet, and its × is
        // the way to take them back.
        ModifierKeys keys = Keyboard.Modifiers;
        bool shift = keys.HasFlag(ModifierKeys.Shift), redoKey = e.Key == Key.Y || (e.Key == Key.Z && shift);
        // a text box takes the keys only while it has typing of its own to undo or redo (the Armory's find box keeps the
        // keyboard after a search, and would otherwise swallow every Ctrl+Z); the File Editor's text keeps its own
        bool inTextBox = Keyboard.FocusedElement switch
        {
            TextBox box => redoKey ? box.CanRedo : box.CanUndo,
            ICSharpCode.AvalonEdit.Editing.TextArea => true,
            _ => false,
        };
        if (!inTextBox && keys.HasFlag(ModifierKeys.Control) && !keys.HasFlag(ModifierKeys.Alt) && (e.Key == Key.Z || e.Key == Key.Y))
        {
            // under the open rule nothing happens; in another of the app's windows, a note says why
            if (IsRuleOpen)
            {
                if (on != RulesTabItem) { e.Handled = true; RefusedForOpenRule(show: false); return; }
            }
            else
            {
                e.Handled = true;
                if (redoKey) Redo();
                else Undo();
                return;
            }
        }
        // the File Editor's own keys, when the keys are not in its page (a click on its empty part leaves them on the
        // tab row): in the page, it has seen the key already
        if (onEditor && !e.Handled) EditorTab.HandleKeysFromOutside(e);
        if (onArmory || onEditor) return;
        // the rule window takes the keys of the window it is over, not those of the Files tab in a window of its own
        if (RuleWindow.IsOpen && on == RulesTabItem) RuleWindow.HandleShortcut(e, RulesTab.Paste);
        else if (on == FilesTabItem) FilesTab.HandleShortcut(e);
        else RulesTab.HandleShortcut(e);
    }

    // ---- files dragged onto the app from outside it ----

    /// <summary>Files dragged over the app: the Files tab comes to the front, since that is where they will land.</summary>
    internal void FilesDraggedOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        lastDragOver = Environment.TickCount64;
        if (Messages.IsOpen) return;
        ShowTab(FilesTabItem, activate: false);
        FilesTab.ShowDropHint(true);
    }

    /// <summary>When files were last dragged over the app: Windows says so again and again while they are over it.</summary>
    private long lastDragOver;

    /// <summary>Hides the drop outline once no drag has been over the app for a moment.</summary>
    private System.Windows.Threading.DispatcherTimer? dragGone;

    /// <summary>The drag left the app: the drop outline goes.</summary>
    internal void FilesDragLeft(object sender, DragEventArgs e)
    {
        // the main view, or the root of a tab's own window
        FrameworkElement over = sender as FrameworkElement ?? this;
        Point at = e.GetPosition(over);
        if (at.X <= 0 || at.Y <= 0 || at.X >= over.ActualWidth || at.Y >= over.ActualHeight) FilesTab.ShowDropHint(false);
        // a drag cancelled over the app (Escape, or let go outside every window) says it left from where it last was,
        // inside: the outline stayed. Windows keeps telling the app while a drag is over it, so once it has said
        // nothing for a moment, the drag is gone
        dragGone ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250), System.Windows.Threading.DispatcherPriority.Background, (_, _) =>
        {
            if (Environment.TickCount64 - lastDragOver < 200) return;
            dragGone!.Stop();
            FilesTab.ShowDropHint(false);
        }, Dispatcher);
        dragGone.Stop();
        dragGone.Start();
    }

    internal async void FilesDropped(object sender, DragEventArgs e)
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
        if (FullScreen.IsOn(window)) { FullScreen.Leave(window); ShowWindowState(window.WindowState); return; }
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this).Close();
}
