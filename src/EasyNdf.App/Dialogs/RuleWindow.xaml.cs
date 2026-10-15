using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Files;
using EasyNdf.App.Rules;
using EasyNdf.Data;
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
        Visibility = Visibility.Visible;
        Scroller.ScrollToTop();
        // the name takes the keys, with the caret ready to type a name when it has none
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (FindName() is TextBox name) { name.Focus(); if (name.Text == "") name.SelectAll(); }
        });
    }

    /// <summary>Closes the window (the HTML app's <c>ruleClose</c>).</summary>
    public void Close()
    {
        if (!IsOpen) return;
        Rows?.Close();
        Visibility = Visibility.Collapsed;
        ShowMessage("");
        shownRule = null;
        Closed?.Invoke();
    }

    /// <summary>The rule's name box, the first text box of the window.</summary>
    private TextBox? FindName() =>
        Editor.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBox>().FirstOrDefault();

    /// <summary>Builds the window again for the open rule (the HTML app's <c>renderEditor</c>).</summary>
    private void Build()
    {
        if (model?.OpenRule is not JsonObject rule) { Close(); return; }
        shownRule = rule;
        string kind = RuleEditor.KindOf(rule);
        Title.Text = RuleEditor.TitleOf(kind);
        // the Add Descriptor window is wider, for the descriptor's text: 64rem, or 96% of the app
        Box.Width = kind == "Add" ? Math.Min(1024, (ActualWidth > 0 ? ActualWidth + 2 * PageBorder : 1280) * 0.96) : 480;
        ShowScan();
        editor = new RuleEditor(model, ScanListsFor(rule), Saved, () => Dispatcher.BeginInvoke(Build));
        Editor.Children.Clear();
        foreach (UIElement part in editor.Build(rule)) Editor.Children.Add(part);
        foreach (RowListView list in editor.Lists)
        {
            list.MenuAsked += ShowRowMenu;
            list.RowOpenAsked += OpenRow;
        }
    }

    /// <summary>Opens the condition or action window on a row (the HTML app's <c>rowOpen</c>).</summary>
    internal void OpenRow(string list, int index)
    {
        if (model is null || history is null || Rows is null) return;
        Rows.Open(model, history, list, index, NamesFor, model.OpenRule is { } rule ? ScanListsFor(rule) : null);
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

    /// <summary>A change in the window is saved; typing in one box is one step to undo.</summary>
    private void Saved(string typingKey)
    {
        if (model is null) return;
        if (typingKey != "") history?.TypingIn(typingKey);
        model.SaveRules();
    }

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

    /// <summary>The scans, with the Files tab's files that are not scanned yet.</summary>
    private PropertyScans Scans() =>
        new(AppData.Current.Scans.Scans, [.. ListedFiles().Where(f => f.Source is not null).Select(FileListModel.ScanKey)]);

    /// <summary>The scanned file whose names fill the open preset's dropdowns, or "".</summary>
    private string CurrentScanFile(PropertyScans scans)
    {
        if (model?.OpenPreset is not ModPreset preset) return "";
        Mod mod = model.OpenMod;
        IEnumerable<string> withPreset = ListedFiles().Where(f => f.Source is not null && ReferenceEquals(f.Preset, preset)).Select(FileListModel.ScanKey);
        return scans.ForPreset(preset, mod.Presets, AppData.Current.ScanPickOf(mod, preset), withPreset);
    }

    /// <summary>What fills a rule's "Pick from scan..." dropdowns and a Lookup rule's other file.</summary>
    private ScanLists ScanListsFor(JsonObject rule)
    {
        PropertyScans scans = Scans();
        ScanInfo current = scans.Of(CurrentScanFile(scans));
        string sourceFile = JsonValues.AsText(rule["Params"]?["SourceFile"]);
        string sourceKey = scans.Find(sourceFile);
        List<ChoiceOption> files = [.. scans.Keys.Select(key => new ChoiceOption(
            key == sourceKey ? sourceFile : scans.ShortName(key), PropertyScans.BaseName(key),
            scans.Folder(key) is { Length: > 0 } folder ? PropertyScans.FolderTag(folder) : ""))];
        return new ScanLists(current.Names, current.Titles, current.Types, files, sourceKey != "", scans.Of(sourceFile).Names);
    }

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
            ScanNote.Text = "Reading " + entry.Name + "...";
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
        ScanNote.Text = "Scanning " + name + "...";
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

    /// <summary>A file was scanned: the window shows it, building itself again when the names it shows changed.</summary>
    private void ScansChanged()
    {
        if (!IsOpen || shownRule is null) return;
        Build();
    }

    /// <summary>The window is as tall as 92% of the app, or 52rem; it stays beside the row window as the app is resized.</summary>
    private void FitHeight()
    {
        if (ActualHeight > 0) Box.Height = Math.Min(832, (ActualHeight + 2 * PageBorder) * 0.92);
        if (shownRule is not null && RuleEditor.KindOf(shownRule) == "Add") Box.Width = Math.Min(1024, (ActualWidth + 2 * PageBorder) * 0.96);
        if (Box.HorizontalAlignment == HorizontalAlignment.Left) ShowBeside(true);
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>A press outside the box closes the window.</summary>
    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Backdrop)) return;
        // a click outside closes the row's window first, then the rule's
        if (Rows?.IsOpen == true) Rows.Close();
        else Close();
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
            if (Rows?.IsOpen == true) Rows.Close();
            else Close();
            return;
        }
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool plain = Keyboard.Modifiers == ModifierKeys.None;
        if (control && e.Key == Key.V)
        {
            e.Handled = true;
            if (Clipboard.ContainsText() && Clipboard.GetText() is { Length: > 0 } text) paste(text);
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
        if (model?.RowsAsClipboardText(list) is not string text) return;
        Clipboard.SetText(text);
        model.Say(Texts.Get("Rules.Copied", ("what", RulesModel.CountRows(list, model.SelectedRowsOf(list).Count))));
    }

    /// <summary>Ctrl+X: copies the selected rows, then deletes them.</summary>
    private void CutRows(string list)
    {
        if (model?.RowsAsClipboardText(list) is not string text) return;
        int count = model.SelectedRowsOf(list).Count;
        Clipboard.SetText(text);
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
