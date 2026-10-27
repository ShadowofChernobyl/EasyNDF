using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Files;
using EasyNdf.App.Rules;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.FileList;
using EasyNdf.App.Text;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The rule window (the HTML app's <c>#ruleDlg</c>): opens on the rule selected on the Rules tab, shows what
/// <see cref="RuleEditor"/> builds for it, and stays on it while the tab's data changes; when the rule is gone (deleted,
/// or undone) the window closes.
/// </summary>
public partial class RuleWindow : UserControl
{
    /// <summary>
    /// The page border drawn around the view when it is drawn without a window, on each side. A web page's sizes in parts of the window
    /// (92vh, 50vw) count it, as a browser's window would include it.
    /// </summary>
    internal static double PageBorder { get; set; }

    /// <summary>
    /// How far from the side of the app the rule window and the row window sit while both are open: as far from the
    /// middle as 31rem, or 10 pixels from the edge (the HTML app's <c>max(10px, calc(50vw - 31rem))</c>).
    /// </summary>
    public static double BesideGap(double viewWidth) => Math.Max(10, (viewWidth + 2 * PageBorder) / 2 - 496) - PageBorder;

    /// <summary>The condition or action window, which closes first, and beside which this window moves left.</summary>
    public RowWindow? Rows { get; set; }

    /// <summary>The files on the Files tab (set by the main view): they fill the Property scan list.</summary>
    public Func<IReadOnlyList<FileEntry>> ListedFiles { get; set; } = () => [];

    /// <summary>Reads a file on the Files tab, which scans it (set by the main view).</summary>
    public Func<FileEntry, Task> ReadFile { get; set; } = _ => Task.CompletedTask;

    private RulesModel? model;
    private UndoHistory? history;
    private RuleEditor? editor;
    private JsonObject? shownRule;

    /// <summary>Builds the window, closed.</summary>
    public RuleWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => FitHeight();
        ScanChoice.SetResourceReference(ItemsControl.ItemTemplateProperty, "DimmedChoice");
        ScanChoice.SelectionChanged += ScanChosen;
        AppData.Current.ScansChanged += ScansChanged;
        Scroller.ScrollChanged += (_, _) => Editor.MinHeight = Math.Max(0, Scroller.ViewportHeight - 20);
        ShowScan();
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Raised when the window closes, so the Rules tab's rule list takes the keys again.</summary>
    public event Action? Closed;

    /// <summary>Opens the window on the Rules tab's selected rule (the HTML app's <c>ruleOpen</c>).</summary>
    public void Open(RulesModel rules, UndoHistory undo)
    {
        if (rules.OpenRule is null) return;
        if (!ReferenceEquals(model, rules))
        {
            if (model is not null) { model.PropertyChanged -= ModelChanged; model.Said -= ShowMessage; }
            model = rules;
            model.PropertyChanged += ModelChanged;
            model.Said += ShowMessage;
        }
        history = undo;
        Rows?.Close();
        model.ActiveRowList = null;
        ShowMessage("");
        Build();
        // the changes are kept until Done, and nothing is filed for Undo meanwhile: Cancel is the window's undo
        model.StartEditingRule();
        history.IsPaused = true;
        Visibility = Visibility.Visible;
        Scroller.ScrollToTop();
        // the name takes the keys, with the caret ready to type a name when it has none
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (FindName() is TextBox name) { name.Focus(); if (name.Text == "") name.SelectAll(); }
        });
    }

    /// <summary>Done: closes the window and saves its changes, as one step to undo.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        Rows?.Close();
        Visibility = Visibility.Collapsed;
        ShowMessage("");
        shownRule = null;
        if (history is not null) history.IsPaused = false;
        model?.FinishEditingRule();
        Closed?.Invoke();
    }

    /// <summary>×, Escape or a click outside: closes the window and throws its changes away, asking first when there are any.</summary>
    public async Task Cancel()
    {
        if (!IsOpen || model is null) return;
        if (model.HasUnsavedRuleChanges && !await MainView.Of(this).Messages.Confirm(Texts.Get("Rules.DiscardChanges"), Texts.Get("Rules.Discard")))
            return;
        model.CancelEditingRule();
        Close();
        // a file list change made while the window was open is filed now
        history?.MarkSoon();
    }

    /// <summary>The rule's name box, the first text box of the window.</summary>
    private TextBox? FindName() =>
        Editor.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBox>().FirstOrDefault();

    /// <summary>Builds the window again for the open rule (the HTML app's <c>renderEditor</c>).</summary>
    private void Build()
    {
        if (model?.OpenRule is not JsonObject rule) { Close(); return; }
        shownRule = rule;
        ForgetScans();
        string kind = RuleEditor.KindOf(rule);
        Title.Text = RuleEditor.TitleOf(kind);
        // the Add Descriptor window is wider, for the descriptor's text: 64rem, or 96% of the app
        Box.Width = kind == "Add" ? Math.Min(1024, (ActualWidth > 0 ? ActualWidth + 2 * PageBorder : 1280) * 0.96) : 480;
        ShowScan();
        editor = new RuleEditor(model, ScanListsFor(rule), Changed, () => Dispatcher.BeginInvoke(Build));
        Editor.Children.Clear();
        bool locked = model.IsOpenRuleLocked;
        if (locked) Editor.Children.Add(LockedBanner(rule));
        foreach (UIElement part in editor.Build(rule))
        {
            // a rule the Armory made is only shown here: it is changed there
            if (locked) part.IsEnabled = false;
            Editor.Children.Add(part);
        }
        foreach (RowListView list in editor.Lists)
        {
            list.MenuAsked += ShowRowMenu;
            list.RowOpenAsked += OpenRow;
        }
    }

    /// <summary>
    /// The band at the top of a rule the Armory made: what it is, with Show in Armory, and Make Editable for keeping
    /// it by hand from now on.
    /// </summary>
    private Bevel LockedBanner(JsonObject rule)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        var bold = new System.Windows.Documents.Run("\U0001F512 " + Texts.Get("Rules.Locked.Title") + " ") { FontWeight = FontWeights.Bold };
        text.Inlines.Add(bold);
        text.Inlines.Add(new System.Windows.Documents.Run(Texts.Get("Rules.Locked.Text")));
        text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        bold.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "ink");

        var show = new Button { Content = Texts.Get("Rules.Locked.ShowInArmory"), ToolTip = Texts.Get("Rules.Locked.ShowInArmory.Tip") };
        show.Click += (_, _) => ShowInArmory(rule);
        var editable = new Button { Content = Texts.Get("Rules.Locked.MakeEditable"), ToolTip = Texts.Get("Rules.Locked.MakeEditable.Tip") };
        editable.Click += (_, _) => _ = MakeEditable(rule);
        var band = new Bevel
        {
            Padding = new Thickness(8, 6, 8, 6),
            Child = new StackPanel { Children = { text, new WrapRow { Children = { show, editable } } } },
        };
        band.SetResourceReference(Bevel.TopLeftProperty, "line");
        band.SetResourceReference(Bevel.BottomRightProperty, "line");
        band.SetResourceReference(Bevel.BackgroundProperty, "well");
        return band;
    }

    /// <summary>Show in Armory: the window closes, and the Armory shows the division or unit the rule is for.</summary>
    private void ShowInArmory(JsonObject rule)
    {
        Close();
        MainView view = MainView.Of(this);
        view.Tabs.SelectedItem = view.UnitsTabItem;
        view.UnitsTab.ShowWhatRuleChanges(rule);
    }

    /// <summary>
    /// Make Editable, once the user says so: the rule (and the others of the same change) is kept by hand from now on,
    /// and the Armory lets go of the change. The window opens again on the rule, now editable.
    /// </summary>
    private async Task MakeEditable(JsonObject rule)
    {
        MainView view = MainView.Of(this);
        if (!await view.Messages.Confirm(Texts.Get("Rules.Locked.MakeEditable.Ask"), Texts.Get("Rules.Locked.MakeEditable"))) return;
        if (!view.UnitsTab.CanMakeEditable)
        {
            await view.Messages.Tell(view.UnitsTab.ArmoryRulesBlockedReason ?? Texts.Get("Rules.Locked.ArmoryNotRead"));
            return;
        }
        JsonNode? armory = rule[DivisionChangeRules.ArmoryKey]?.DeepClone();
        Close();
        view.UnitsTab.MakeEditable(rule);
        if (model is null || history is null) return;
        // the rule is now among those kept by hand, at the end of the preset
        int index = model.Rules.ToList().FindIndex(each =>
            (string?)each?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.DivisionKey] == (string?)armory?[DivisionChangeRules.DivisionKey]
            && (string?)each?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.ChangeKey] == (string?)armory?[DivisionChangeRules.ChangeKey]
            && (string?)each?["Name"] == (string?)rule["Name"]);
        if (index < 0) return;
        model.SelectOnly(index);
        Open(model, history);
    }

    /// <summary>Opens the condition or action window on a row (the HTML app's <c>rowOpen</c>).</summary>
    internal void OpenRow(string list, int index)
    {
        if (model is null || Rows is null) return;
        Rows.Open(model, list, index, NamesFor, model.OpenRule is { } rule ? ScanListsFor(rule) : null);
    }

    /// <summary>
    /// Moves the window to the left while the row window is open beside it (the HTML app's <c>#ruleDlg.side</c>), or back
    /// to the middle.
    /// </summary>
    public void ShowBeside(bool beside)
    {
        Box.HorizontalAlignment = beside ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        Box.Margin = beside ? new Thickness(BesideGap(ActualWidth), 0, 0, 0) : new Thickness();
    }

    /// <summary>A change in the window: it shows in the window's lists, and is saved on Done.</summary>
    private void Changed() => model?.SaveRules();

    /// <summary>
    /// The Rules tab's data changed: the window follows. Another rule (Undo brought back a copy of this one) builds it
    /// again; the same rule only draws its lists again, so the box being typed in keeps the keys.
    /// </summary>
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsOpen || model is null) return;
        if (model.OpenRule is not JsonObject rule) { Close(); return; }
        if (!ReferenceEquals(rule, shownRule)) { Build(); return; }
        foreach (RowListView list in editor?.Lists ?? []) list.Refresh();
    }

    /// <summary>#ruleMsg: what the last action did, while the window is open.</summary>
    private void ShowMessage(string message)
    {
        RuleMessage.Text = message;
        RuleMessage.Visibility = message == "" ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- the property scan (the HTML app's renderScan): which scanned file fills the dropdowns ----

    /// <summary>
    /// The scans and the file they give the open preset, worked out once each time the window is built rather than for
    /// each list that wants them: with a game's 4,000 files that took a noticeable moment each time.
    /// </summary>
    private (PropertyScans Scans, string CurrentFile)? scansNow;

    /// <summary>The scans, with the Files tab's files that are not scanned yet.</summary>
    private PropertyScans Scans() => ScansNow().Scans;

    /// <summary>The scans and the open preset's scanned file, made again after <see cref="ForgetScans"/>.</summary>
    private (PropertyScans Scans, string CurrentFile) ScansNow()
    {
        if (scansNow is { } made) return made;
        var scans = new PropertyScans(AppData.Current.Scans.Scans, [.. ListedFiles().Where(f => f.Source is not null).Select(FileListModel.ScanKey)]);
        scansNow = (scans, WorkOutScanFile(scans));
        return scansNow.Value;
    }

    /// <summary>The scans or the files changed, or the window is built again: they are worked out again when next wanted.</summary>
    private void ForgetScans() => scansNow = null;

    /// <summary>The scanned file whose names fill the open preset's dropdowns, or "".</summary>
    private string CurrentScanFile(PropertyScans scans) => ReferenceEquals(scans, scansNow?.Scans) ? scansNow.Value.CurrentFile : WorkOutScanFile(scans);

    private string WorkOutScanFile(PropertyScans scans)
    {
        if (model?.OpenPreset is not ModPreset preset) return "";
        Mod mod = model.OpenMod;
        IEnumerable<string> withPreset = ListedFiles().Where(f => f.Source is not null && FileRunner.AppliesTo(preset, f)).Select(FileListModel.ScanKey);
        return scans.ForPreset(preset, mod.Presets, AppData.Current.ScanPickOf(mod, preset), withPreset);
    }

    /// <summary>What fills a rule's "Pick from scan..." dropdowns and a Lookup rule's other file.</summary>
    private ScanLists ScanListsFor(JsonObject rule)
    {
        PropertyScans scans = Scans();
        ScanInfo current = scans.Of(CurrentScanFile(scans));
        string sourceFile = JsonValues.AsText(rule["Params"]?["SourceFile"]);
        string sourceKey = scans.Find(sourceFile);
        // only a Lookup rule lists the files, for its other file: a game's 4,000 of them take a moment to name
        if (RuleEditor.KindOf(rule) != "Lookup") return new ScanLists(current.Names, current.Titles, current.Types, [], sourceKey != "", scans.Of(sourceFile).Names);
        List<ChoiceOption> files = [.. scans.Keys.Select(key => new ChoiceOption(
            key == sourceKey ? sourceFile : scans.ShortName(key), PropertyScans.BaseName(key),
            scans.Folder(key) is { Length: > 0 } folder ? PropertyScans.FolderTag(folder) : ""))];
        // the files on the Files tab not read yet are offered too, as the Property scan offers them; the one chosen is read
        foreach (string key in scans.Unread)
        {
            // named as a scanned file is: its name, with the folders that set it apart from another file of that name
            string folder = scans.Folder(key);
            string reference = folder != "" ? folder + "/" + PropertyScans.BaseName(key) : PropertyScans.BaseName(key);
            string dim = (folder != "" ? PropertyScans.FolderTag(folder) + " " : "") + Texts.Get("Files.Status.NotReadYet");
            files.Add(new ChoiceOption(reference, PropertyScans.BaseName(key), dim));
        }
        if (sourceKey == "" && sourceFile != "") ReadForLookup(sourceFile);
        return new ScanLists(current.Names, current.Titles, current.Types, files, sourceKey != "", scans.Of(sourceFile).Names);
    }

    /// <summary>The files being read for a Lookup rule's other file, so each is read once.</summary>
    private readonly HashSet<FileEntry> readingForLookup = [];

    /// <summary>
    /// A Lookup rule names a file on the Files tab that is not read yet: it is read in the background, and once scanned
    /// the window builds itself again with its names (see <see cref="ScansChanged"/>).
    /// </summary>
    private async void ReadForLookup(string file)
    {
        // the rule names the file as a scanned file is named: by its name, or with folders in front
        string wanted = file.ToLowerInvariant();
        FileEntry? entry = ListedFiles().FirstOrDefault(each => each.Source is not null && NamesFile(FileListModel.ScanKey(each).ToLowerInvariant(), wanted));
        if (entry is null || !readingForLookup.Add(entry)) return;
        try
        {
            await ReadFile(entry);
        }
        finally
        {
            readingForLookup.Remove(entry);
        }
    }

    /// <summary>Whether a rule's reference to a file (in lower case) names this file's key (in lower case).</summary>
    private static bool NamesFile(string key, string reference) =>
        key == reference || key.EndsWith("/" + reference, StringComparison.Ordinal);

    /// <summary>The property names for a list's Operand or Target: a Lookup rule's other file's for its conditions there.</summary>
    private IReadOnlyList<string> NamesFor(string list)
    {
        PropertyScans scans = Scans();
        if (list == RulesModel.SourceConditions) return scans.Of(JsonValues.AsText(model?.OpenRule?["Params"]?["SourceFile"])).Names;
        return scans.Of(CurrentScanFile(scans)).Names;
    }

    private bool showingScan;

    /// <summary>The Property scan's dropdown and note.</summary>
    private void ShowScan()
    {
        PropertyScans scans = Scans();
        string current = CurrentScanFile(scans);
        var options = new List<ChoiceOption>();
        if (scans.Keys.Count == 0 && scans.Unread.Count == 0) options.Add(new ChoiceOption("", Texts.Get("Rules.Scan.NoFile")));
        else
        {
            if (current == "") options.Add(new ChoiceOption("", Texts.Get("Rules.Editor.Lookup.ChooseFile")));
            options.AddRange(scans.Keys.Select(key => new ChoiceOption(key, PropertyScans.BaseName(key), DimFolder(scans, key))));
            options.AddRange(scans.Unread.Select(key => new ChoiceOption(key, PropertyScans.BaseName(key),
                (DimFolder(scans, key) is { Length: > 0 } folder ? folder + " " : "") + Texts.Get("Files.Status.NotReadYet"))));
        }
        showingScan = true;
        ScanChoice.ItemsSource = options;
        ScanChoice.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options[0];
        showingScan = false;
        ScanNote.Text = current != ""
            ? Texts.Get("Rules.Scan.Properties", ("count", scans.Of(current).Names.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)), ("file", scans.Label(current)))
            : Texts.Get("Rules.Scan.Hint");
    }

    private static string DimFolder(PropertyScans scans, string key) => scans.Folder(key) is { Length: > 0 } folder ? PropertyScans.FolderTag(folder) : "";

    /// <summary>
    /// A file picked in the Property scan: it fills this preset's dropdowns from now on. A file on the Files tab that is
    /// not read yet is read first.
    /// </summary>
    private async void ScanChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showingScan || model?.OpenPreset is not ModPreset preset || ScanChoice.SelectedItem is not ChoiceOption { Value: { Length: > 0 } file }) return;
        Mod mod = model.OpenMod;
        if (!AppData.Current.Scans.Scans.ContainsKey(file))
        {
            if (ListedFiles().FirstOrDefault(f => f.Source is not null && FileListModel.ScanKey(f) == file) is not FileEntry entry) return;
            ScanNote.Text = Texts.Get("Editor.Viewer.ReadingFile", ("file", entry.Name));
            await ReadFile(entry);
            if (!AppData.Current.Scans.Scans.ContainsKey(file))
            {
                ShowScan();
                await MainView.Of(this).Messages.Tell(Texts.Get("Rules.Scan.CouldNotRead", ("file", entry.Name), ("reason", entry.Message)));
                return;
            }
        }
        AppData.Current.RememberScanPick(mod, preset, file);
        Build();
    }

    /// <summary>"...": scans a file without adding it to the Files tab; it fills this preset's dropdowns.</summary>
    private async void ScanFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Texts.Get("Rules.Scan.Title"), Filter = Texts.Get("Files.NdfFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || model is null) return;
        string name = System.IO.Path.GetFileName(dialog.FileName);
        ScanNote.Text = Texts.Get("Rules.Scan.Scanning", ("file", name));
        try
        {
            string path = dialog.FileName;
            EasyNdf.Engine.LoadSummary summary = await Task.Run(() => new EasyNdf.Engine.NdfEngine().Load(System.IO.File.ReadAllText(path)));
            if (model.OpenPreset is ModPreset preset) AppData.Current.RememberScanPick(model.OpenMod, preset, name);
            AppData.Current.SetScan(name, summary);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or EasyNdf.Engine.Ndf.NdfParseException)
        {
            ShowScan();
            await MainView.Of(this).Messages.Tell(Texts.Get("Rules.Scan.Failed", ("file", name), ("reason", error.Message)));
        }
    }

    /// <summary>
    /// Files were added on the Files tab or taken off it: the Property scan offers them, and an open rule (a Lookup
    /// rule's other file among them) is built again.
    /// </summary>
    public void FilesChanged()
    {
        ForgetScans();
        // a closed window shows them when it next opens: adding a game's 4,000 files calls this often
        if (IsOpen && shownRule is not null) Build();
    }

    /// <summary>A file was scanned: the window shows it, building itself again when the names it shows changed.</summary>
    private void ScansChanged(string file)
    {
        ForgetScans();
        if (!IsOpen || shownRule is null) return;
        // a session's thousands of files are read just after the app starts: building the window again for each one
        // stalled it until all were read. Only the file that fills the dropdowns, or a Lookup rule's other file, changes
        // what it shows; any other only joins the Property scan's list
        if (MayFillWindow(file)) Build();
        else ShowScanSoon();
    }

    /// <summary>
    /// Whether a file just scanned may be the one whose names fill the open preset's dropdowns (see
    /// <see cref="PropertyScans.ForPreset"/>: the file picked for it, a file that has it on the Files tab, or one whose
    /// name the preset's name holds), or the open Lookup rule's other file.
    /// </summary>
    private bool MayFillWindow(string file)
    {
        if (model?.OpenPreset is not ModPreset preset) return true;
        if (AppData.Current.ScanPickOf(model.OpenMod, preset) == file) return true;
        if (preset.Name.Replace('\\', '/').Contains(PropertyScans.BaseName(file), StringComparison.Ordinal)) return true;
        if (ListedFiles().Any(entry => FileRunner.AppliesTo(preset, entry) && FileListModel.ScanKey(entry) == file)) return true;
        string sourceFile = JsonValues.AsText(shownRule?["Params"]?["SourceFile"]).ToLowerInvariant();
        return sourceFile != "" && NamesFile(file.ToLowerInvariant(), sourceFile);
    }

    /// <summary>Shows the Property scan's list again soon, at most twice a second while files are read one after another.</summary>
    private System.Windows.Threading.DispatcherTimer? scanListLater;

    private void ShowScanSoon()
    {
        if (scanListLater is null)
        {
            scanListLater = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            scanListLater.Tick += (_, _) =>
            {
                scanListLater.Stop();
                if (IsOpen) ShowScan();
            };
        }
        if (!scanListLater.IsEnabled) scanListLater.Start();
    }

    /// <summary>The window is as tall as 92% of the app, or 52rem; it stays beside the row window as the app is resized.</summary>
    private void FitHeight()
    {
        if (ActualHeight > 0) Box.Height = Math.Min(832, (ActualHeight + 2 * PageBorder) * 0.92);
        if (shownRule is not null && RuleEditor.KindOf(shownRule) == "Add") Box.Width = Math.Min(1024, (ActualWidth + 2 * PageBorder) * 0.96);
        if (Box.HorizontalAlignment == HorizontalAlignment.Left) ShowBeside(true);
    }

    private void DoneClicked(object sender, RoutedEventArgs e) => Close();

    private async void CancelClicked(object sender, RoutedEventArgs e) => await Cancel();

    /// <summary>A press outside the box cancels the window.</summary>
    private async void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Backdrop)) return;
        await CancelTopWindow();
    }

    /// <summary>Cancels the row's window if it is open, else this one.</summary>
    private async Task CancelTopWindow()
    {
        if (Rows?.IsOpen == true) await Rows.Cancel();
        else await Cancel();
    }

    /// <summary>A press in the box stays there.</summary>
    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>A key the focused control did not take, while the window is open (see <see cref="MainView.HandleShortcut"/>).</summary>
    public void HandleShortcut(KeyEventArgs e, Func<string, bool> paste)
    {
        if (model is null) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _ = CancelTopWindow();
            return;
        }
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool plain = Keyboard.Modifiers == ModifierKeys.None;
        if (control && e.Key == Key.V)
        {
            e.Handled = true;
            if (ClipboardText() is { Length: > 0 } text) paste(text);
            return;
        }
        if (model.ActiveRowList is not string list) return;
        if (control && e.Key == Key.Up) { e.Handled = true; model.MoveRows(list, -1); }
        else if (control && e.Key == Key.Down) { e.Handled = true; model.MoveRows(list, +1); }
        else if (control && e.Key == Key.A) { e.Handled = true; model.SelectAllRows(list); }
        else if (control && e.Key == Key.C) { e.Handled = true; CopyRows(list); }
        else if (control && e.Key == Key.X) { e.Handled = true; CutRows(list); }
        else if (plain && e.Key == Key.Delete) { e.Handled = true; model.DeleteRows(list); }
    }

    private void CopyRows(string list)
    {
        if (model?.RowsAsClipboardText(list) is not string text || !CopyToClipboard(text)) return;
        model.Say(Texts.Get("Rules.Copied", ("what", RulesModel.CountRows(list, model.SelectedRowsOf(list).Count))));
    }

    /// <summary>Puts text on the clipboard; false, and the status says so, when another program has it open.</summary>
    private bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            model?.Say(Texts.Get("Rules.ClipboardBusy"));
            return false;
        }
    }

    /// <summary>The text on the clipboard; empty when it holds none, or another program has it open.</summary>
    private static string ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return "";
        }
    }

    /// <summary>Ctrl+X: copies the selected rows, then deletes them.</summary>
    private void CutRows(string list)
    {
        if (model?.RowsAsClipboardText(list) is not string text) return;
        int count = model.SelectedRowsOf(list).Count;
        // nothing is deleted when the copy failed, as when another program has the clipboard open
        if (!CopyToClipboard(text)) return;
        model.DeleteRows(list);
        model.Say(Texts.Get("Rules.Cut", ("what", RulesModel.CountRows(list, count))));
    }

    /// <summary>A list's right-click menu: for the row under the mouse, or for the list.</summary>
    private void ShowRowMenu(RowListView view, int index)
    {
        if (model is null) return;
        string list = view.List;
        string create = list == RulesModel.Actions ? Texts.Get("Rules.Rows.NewAction") : Texts.Get("Rules.Rows.NewCondition");
        IEnumerable<Controls.MenuChoice?> tail =
        [
            new Controls.MenuChoice(create, () => OpenRow(list, model.AddRow(list))),
            new Controls.MenuChoice(Texts.Get("Common.Menu.Paste"), () => MainView.Of(this).PasteFromClipboard(), Key: "Ctrl+V"),
        ];
        if (index < 0) { Controls.Menus.Show(view, tail); return; }
        int count = model.SelectedRowsOf(list).Count;
        string several = RulesModel.CountRows(list, count);
        Controls.Menus.Show(view,
        [
            new Controls.MenuChoice(Texts.Get("Common.Menu.Configure"), () => OpenRow(list, index), IsEnabled: count == 1, Key: "Enter"),
            new Controls.MenuChoice(Texts.Get("Common.Menu.Duplicate"), () => model.DuplicateRows(list)),
            new Controls.MenuChoice(Texts.Get("Common.Menu.MoveUp"), () => model.MoveRows(list, -1), Key: "Ctrl+Up"),
            new Controls.MenuChoice(Texts.Get("Common.Menu.MoveDown"), () => model.MoveRows(list, +1), Key: "Ctrl+Down"),
            new Controls.MenuChoice(count > 1 ? Texts.Get("Common.Menu.CutSeveral", ("what", several)) : Texts.Get("Common.Menu.Cut"), () => CutRows(list), Key: "Ctrl+X"),
            new Controls.MenuChoice(count > 1 ? Texts.Get("Common.Menu.CopySeveral", ("what", several)) : Texts.Get("Common.Menu.Copy"), () => CopyRows(list), Key: "Ctrl+C"),
            new Controls.MenuChoice(count > 1 ? Texts.Get("Common.Menu.DeleteSeveral", ("what", several)) : Texts.Get("Common.Menu.Delete"), () => model.DeleteRows(list), Key: "Delete"),
            Controls.Menus.Line,
            .. tail,
        ]);
    }
}
