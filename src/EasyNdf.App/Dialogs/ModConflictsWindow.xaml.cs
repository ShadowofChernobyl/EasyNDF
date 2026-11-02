using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Files;
using EasyNdf.App.Mods;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// A line of the Mod Conflicts window's list: a file, a descriptor in it, or a value in conflict. A file or descriptor
/// folds and unfolds what is under it; a value is picked to see it at the right.
/// </summary>
public sealed class ConflictRow : INotifyPropertyChanged
{
    /// <summary>"File", "Descriptor" or "Value", for the look the list gives each.</summary>
    public required string Kind { get; init; }

    /// <summary>The file's name, the descriptor's name, or the value's property.</summary>
    public required string Text { get; init; }

    /// <summary>▾ when open, ▸ when folded; empty for a value.</summary>
    public string Arrow { get; init; } = "";

    /// <summary>✖ for a name clash, ⚠ for a value overwritten; empty for a file or descriptor.</summary>
    public string Mark { get; init; } = "";

    /// <summary>How many values are under a file or descriptor; empty for a value.</summary>
    public string CountText { get; init; } = "";

    /// <summary>How far the line is set in, by its depth.</summary>
    public Thickness Indent { get; init; }

    /// <summary>Whether the value is a name clash, marked in the warning colour.</summary>
    public bool IsClash { get; init; }

    /// <summary>What the line folds or unfolds: the file's key, or the file's key and the descriptor.</summary>
    public string FoldKey { get; init; } = "";

    /// <summary>The value, for a value's line.</summary>
    public (string FileKey, ConflictedValue Value)? Value { get; init; }

    private bool isPicked;

    /// <summary>Whether it is the value shown at the right.</summary>
    public bool IsPicked
    {
        get => isPicked;
        set
        {
            if (isPicked == value) return;
            isPicked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPicked)));
        }
    }

    private bool isCursor;

    /// <summary>Whether the arrow keys are on this line, outlined while the list has the keyboard.</summary>
    public bool IsCursor
    {
        get => isCursor;
        set
        {
            if (isCursor == value) return;
            isCursor = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCursor)));
        }
    }

    /// <summary>What tells the line apart from the others when the list is made again: its fold key, or its value's place.</summary>
    public string LineKey => Value is { } value ? value.FileKey + "\n" + value.Value.Descriptor + "\n" + value.Value.Property + "\n" + value.Value.IsNameClash : FoldKey;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The Mod Conflicts window: every value more than one mod that is on changes, from what the last Preview or export
/// found (<see cref="ModConflictList"/>). Read-only: it shows the conflicts, and takes the user to a rule or to the
/// File Editor, but changes nothing. ×, Close, Escape or a click outside close it.
/// </summary>
public partial class ModConflictsWindow : UserControl
{
    /// <summary>Two or more spaces in a row.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("  +")]
    private static partial System.Text.RegularExpressions.Regex MultipleSpaces();

    /// <summary>Every value in conflict, in the order of the files, then descriptors, as the run found them.</summary>
    private List<(string FileKey, ConflictedValue Value)> all = [];

    /// <summary>The values the filters let through, in the list's order: what previous and next go through.</summary>
    private List<(string FileKey, ConflictedValue Value)> shown = [];

    /// <summary>The files and descriptors unfolded, by their fold keys.</summary>
    private readonly HashSet<string> unfolded = new(StringComparer.Ordinal);

    /// <summary>The value shown at the right; null when none is.</summary>
    private (string FileKey, ConflictedValue Value)? picked;

    /// <summary>The mods that are on, in the order they run, for the order line and each mod's colour.</summary>
    private List<string> modOrder = [];

    /// <summary>The value of "Any mod" in the mod box.</summary>
    private static string AnyMod => Texts.Get("Files.Conflicts.AnyMod");

    /// <summary>Set while the window fills its own boxes, so their events do not refill the list.</summary>
    private bool isFilling;

    /// <summary>Builds the window, closed; it follows what is found while it is open.</summary>
    public ModConflictsWindow()
    {
        InitializeComponent();
        // a size from the start: measured with no size, the list would make a line for every value at once
        Inside.Width = Inside.MinWidth;
        Inside.Height = Inside.MinHeight;
        ModConflictList.Changed += () => { if (IsOpen) Fill(keepPicked: true); };
        copiedPause.Tick += (_, _) =>
        {
            copiedPause.Stop();
            CopyButton.Content = Texts.Get("Files.Conflicts.Copy");
        };
    }

    /// <summary>Whether Find again follows the Files tab's runs yet; it starts to when the window first opens.</summary>
    private bool followsRuns;

    /// <summary>Find again is off while a Preview or export runs: a second run cannot start until the first is done.</summary>
    private void ShowRunState()
    {
        if (MainView.Find(this) is MainView view) FindAgainButton.IsEnabled = !view.FilesTab.Model.IsBusy;
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Opens the window on every conflict found, or only those of one mod (from a ⚠ on the Mods tab).</summary>
    /// <param name="mod">The mod to show the conflicts of; null for every mod.</param>
    public void Open(string? mod = null)
    {
        isFilling = true;
        FindBox.Text = "";
        ClashToggle.IsChecked = true;
        OverToggle.IsChecked = true;
        isFilling = false;
        unfolded.Clear();
        picked = null;
        cursorKey = null;
        if (!followsRuns && MainView.Find(this) is MainView view)
        {
            followsRuns = true;
            view.FilesTab.Model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is null or "" or nameof(FileListModel.IsBusy)) ShowRunState();
            };
        }
        ShowRunState();
        Fill(keepPicked: false, mod);
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => FindBox.Focus());
    }

    // ---- the list ----

    /// <summary>
    /// Reads what is found again, and fills the mod box, the counts and the list. The value picked stays picked when it
    /// is still in conflict; otherwise the first value listed is picked.
    /// </summary>
    private void Fill(bool keepPicked, string? mod = null)
    {
        all = [.. ModConflictList.All.OrderBy(found => found.Value.File, StringComparer.OrdinalIgnoreCase).ThenBy(found => found.FileKey, StringComparer.Ordinal)];
        modOrder = [.. ModStack.Running.Select(running => running.Name)];
        isFilling = true;
        string chosen = mod ?? ModChoice.SelectedItem as string ?? AnyMod;
        List<string> inConflict = [.. modOrder.Where(name => all.Any(found => found.Value.Changes.Any(change => change.Mod == name)))];
        ModChoice.ItemsSource = new List<string> { AnyMod }.Concat(inConflict).ToList();
        ModChoice.SelectedItem = inConflict.Contains(chosen) ? chosen : AnyMod;
        isFilling = false;
        // a few mods are named; more would run off the line, so they are counted, and the tooltip lists them in order
        string values = Texts.CountWide("Files.Conflicts.Values", all.Count);
        Summary.Text = modOrder.Count <= NamedMods
            ? Texts.Get("Files.Conflicts.Summary", ("values", values), ("mods", string.Join("  ›  ", modOrder)))
            : Texts.Get("Files.Conflicts.SummaryCounted", ("values", values), ("mods", Texts.CountWide("Files.Conflicts.Mods", modOrder.Count)));
        Summary.ToolTip = modOrder.Count == 0 ? null : Texts.Get("Files.Conflicts.Order.Tip") + "\n" + NumberedList(modOrder);
        if (!keepPicked || picked is not { } was || !all.Any(found => SameValue(found, was))) picked = null;
        else picked = all.First(found => SameValue(found, was));
        Refill();
    }

    /// <summary>How many mods the summary line and the verdict name before they count the rest.</summary>
    private const int NamedMods = 3;

    /// <summary>How many lines a tooltip's list of mods has before it says how many more there are.</summary>
    private const int ListedMods = 30;

    /// <summary>Mods one a line, each with its place in the run order: "3. WTO"; past <see cref="ListedMods"/>, "and 12 more".</summary>
    private string NumberedList(IEnumerable<string> mods)
    {
        List<string> names = [.. mods];
        IEnumerable<string> lines = names.Take(ListedMods).Select(mod => Texts.Number(RunPlace(mod)) + ". " + mod);
        if (names.Count > ListedMods) lines = lines.Append(Texts.Get("Files.Conflicts.AndMore", ("count", Texts.Number(names.Count - ListedMods))));
        return string.Join("\n", lines);
    }

    /// <summary>A mod's place in the run order, from 1; 0 when it no longer runs.</summary>
    private int RunPlace(string mod) => modOrder.IndexOf(mod) + 1;

    /// <summary>Whether two found values are the same value: the same file, descriptor and property.</summary>
    private static bool SameValue((string FileKey, ConflictedValue Value) a, (string FileKey, ConflictedValue Value) b) =>
        a.FileKey == b.FileKey && a.Value.Descriptor == b.Value.Descriptor && a.Value.Property == b.Value.Property;

    /// <summary>Lists the values the filters let through, folded by file and descriptor, and shows the one picked.</summary>
    private void Refill()
    {
        string mod = ModChoice.SelectedItem as string ?? AnyMod;
        bool clashes = ClashToggle.IsChecked == true, overwritten = OverToggle.IsChecked == true;
        string find = FindBox.Text.Trim();
        bool OfMod((string FileKey, ConflictedValue Value) found) => mod == AnyMod || found.Value.Changes.Any(change => change.Mod == mod);
        ClashToggle.Content = "✖ " + Texts.Number(all.Count(found => found.Value.IsNameClash && OfMod(found)));
        OverToggle.Content = "⚠ " + Texts.Number(all.Count(found => !found.Value.IsNameClash && OfMod(found)));
        shown = [.. all.Where(found => (found.Value.IsNameClash ? clashes : overwritten) && OfMod(found) && Matches(found, find))];
        // the value shown stays while it is listed; Find or a filter that leaves it out shows the first one listed
        // a value picked here is unfolded to; one picked already stays folded away if the user folded it
        if (picked is not { } current || !shown.Any(found => SameValue(found, current)))
        {
            picked = shown.Count > 0 ? shown[0] : null;
            if (picked is { } open)
            {
                unfolded.Add(open.FileKey);
                unfolded.Add(open.FileKey + "\n" + open.Value.Descriptor);
            }
        }

        // the lines: each file, its descriptors when unfolded, their values when unfolded; all open while finding
        var rows = new List<ConflictRow>();
        bool allOpen = find != "";
        foreach (IGrouping<string, (string FileKey, ConflictedValue Value)> file in shown.GroupBy(found => found.FileKey))
        {
            bool fileOpen = allOpen || unfolded.Contains(file.Key);
            rows.Add(new ConflictRow
            {
                Kind = "File", Text = file.First().Value.File, Arrow = fileOpen ? "▾" : "▸", CountText = Texts.Number(file.Count()),
                Indent = new Thickness(4, 3, 0, 3), FoldKey = file.Key,
            });
            if (!fileOpen) continue;
            foreach (IGrouping<string, (string FileKey, ConflictedValue Value)> descriptor in file.GroupBy(found => found.Value.Descriptor))
            {
                string key = file.Key + "\n" + descriptor.Key;
                bool descriptorOpen = allOpen || unfolded.Contains(key);
                rows.Add(new ConflictRow
                {
                    Kind = "Descriptor", Text = descriptor.Key, Arrow = descriptorOpen ? "▾" : "▸", CountText = Texts.Number(descriptor.Count()),
                    Indent = new Thickness(14, 3, 0, 3), FoldKey = key,
                });
                if (!descriptorOpen) continue;
                foreach ((string FileKey, ConflictedValue Value) found in descriptor)
                    rows.Add(new ConflictRow
                    {
                        Kind = "Value", Text = found.Value.IsNameClash ? Texts.Get("Files.Conflicts.NameClash.Row") : found.Value.Property,
                        Mark = found.Value.IsNameClash ? "✖" : "⚠", IsClash = found.Value.IsNameClash,
                        Indent = new Thickness(28, 3, 0, 3), Value = found, IsPicked = picked is { } p && SameValue(p, found),
                    });
            }
        }
        List.ItemsSource = rows;
        ShowCursor();
        // with no conflicts at all, the filters are not why the list is empty, so it says how to find them
        NothingFound.Text = all.Count == 0 ? Texts.Get("Files.Conflicts.NoConflicts") : Texts.Get("Files.Conflicts.NothingMatches");
        NothingFound.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowPicked();
    }

    // ---- the list by keyboard ----

    /// <summary>The line the arrow keys are on, by its <see cref="ConflictRow.LineKey"/>; null for the value picked.</summary>
    private string? cursorKey;

    /// <summary>The lines listed now.</summary>
    private List<ConflictRow> Lines => List.ItemsSource as List<ConflictRow> ?? [];

    /// <summary>The line the arrow keys are on: the one moved to last, or else the value picked, or else the first.</summary>
    private int CursorAt()
    {
        List<ConflictRow> lines = Lines;
        int at = cursorKey is null ? -1 : lines.FindIndex(line => line.LineKey == cursorKey);
        if (at < 0) at = lines.FindIndex(line => line.IsPicked);
        return lines.Count == 0 ? -1 : Math.Max(0, at);
    }

    /// <summary>Outlines the line the arrow keys are on, only while the list has the keyboard, and keeps it in sight.</summary>
    private void ShowCursor()
    {
        bool focused = List.IsKeyboardFocusWithin && listKeysInUse;
        List<ConflictRow> lines = Lines;
        int at = CursorAt();
        for (int index = 0; index < lines.Count; index++) lines[index].IsCursor = focused && index == at;
        if (focused && at >= 0) List.ScrollIntoView(lines[at]);
    }

    /// <summary>Whether the keyboard is in use on the list: the outline shows only then, not after a click with the mouse.</summary>
    private bool listKeysInUse;

    /// <summary>The list got or lost the keyboard: reached with Tab, the outline shows where the keys are; clicked, it waits for a key.</summary>
    private void ListFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (List.IsKeyboardFocusWithin && e.NewFocus == List) listKeysInUse = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        ShowCursor();
    }

    /// <summary>
    /// A key while the list has the keyboard. Up and Down move along the lines, picking each value moved onto; on a file
    /// or descriptor, Enter folds or unfolds it, Left folds it and Right unfolds it. Home and End go to the first and
    /// last line. True when the key was the list's.
    /// </summary>
    private bool ListKey(Key key)
    {
        List<ConflictRow> lines = Lines;
        int at = CursorAt();
        if (at < 0) return false;
        ConflictRow line = lines[at];
        listKeysInUse = true;
        switch (key)
        {
            case Key.Up or Key.Down or Key.Home or Key.End:
                int to = key switch { Key.Up => at - 1, Key.Down => at + 1, Key.Home => 0, _ => lines.Count - 1 };
                to = Math.Clamp(to, 0, lines.Count - 1);
                cursorKey = lines[to].LineKey;
                if (lines[to].Value is { } value) Pick(value);
                ShowCursor();
                return true;
            case Key.Enter or Key.Space or Key.Left or Key.Right when line.Value is null:
                bool open = unfolded.Contains(line.FoldKey) || FindBox.Text.Trim() != "";
                bool wanted = key switch { Key.Left => false, Key.Right => true, _ => !open };
                if (wanted == open) return true;
                if (wanted) unfolded.Add(line.FoldKey);
                else unfolded.Remove(line.FoldKey);
                cursorKey = line.LineKey;
                Refill();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Whether a value holds the text found, in its file, descriptor, property, a mod, a rule or a value; true when nothing is typed.</summary>
    private static bool Matches((string FileKey, ConflictedValue Value) found, string find)
    {
        if (find == "") return true;
        bool Has(string text) => text.Contains(find, StringComparison.OrdinalIgnoreCase);
        ConflictedValue value = found.Value;
        return Has(value.File) || Has(value.Descriptor) || Has(value.Property)
            || value.Changes.Any(change => Has(change.Mod) || Has(change.Rule) || Has(change.New));
    }

    /// <summary>A click on a line: a file or descriptor folds or unfolds, a value is picked.</summary>
    private void ListClicked(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ConflictRow row) return;
        // the arrow keys go on from the line clicked
        cursorKey = row.LineKey;
        listKeysInUse = false;
        List.Focus();
        if (row.Value is { } value)
        {
            Pick(value);
            ShowCursor();
            return;
        }
        if (!unfolded.Remove(row.FoldKey)) unfolded.Add(row.FoldKey);
        Refill();
    }

    /// <summary>Shows a value at the right, and marks its line.</summary>
    private void Pick((string FileKey, ConflictedValue Value) value)
    {
        picked = value;
        foreach (ConflictRow row in List.ItemsSource as List<ConflictRow> ?? [])
            row.IsPicked = row.Value is { } each && SameValue(each, value);
        ShowPicked();
    }

    /// <summary>Previous or next: the value before or after the one picked, among those listed, going round at the ends.</summary>
    private void Step(int by)
    {
        if (shown.Count == 0) return;
        int at = picked is { } now ? shown.FindIndex(found => SameValue(found, now)) : -1;
        (string FileKey, ConflictedValue Value) next = shown[((at < 0 ? 0 : at + by) % shown.Count + shown.Count) % shown.Count];
        picked = next;
        cursorKey = null;
        unfolded.Add(next.FileKey);
        unfolded.Add(next.FileKey + "\n" + next.Value.Descriptor);
        Refill();
        if ((List.ItemsSource as List<ConflictRow>)?.FirstOrDefault(row => row.IsPicked) is ConflictRow line) List.ScrollIntoView(line);
    }

    private void PreviousClicked(object sender, RoutedEventArgs e) => Step(-1);

    private void NextClicked(object sender, RoutedEventArgs e) => Step(1);

    private void FindChanged(object sender, TextChangedEventArgs e)
    {
        if (!isFilling && IsOpen) Refill();
    }

    private void FilterClicked(object sender, RoutedEventArgs e) => Refill();

    private void ModChosen(object sender, SelectionChangedEventArgs e)
    {
        if (!isFilling && IsOpen) Refill();
    }

    // ---- the value picked ----

    /// <summary>
    /// How wide each panel is: as the user last dragged it, kept with the pane sizes; until then, the room shared
    /// between the panels, each at least <see cref="FirstWidest"/> wide.
    /// </summary>
    private double PanelWidth
    {
        get => (double?)AppData.Current.State["sizes"]?["conflictPanels"] ?? SharedWidth(Panels.Children.Count);
        set
        {
            if (AppData.Current.State["sizes"] is not JsonObject sizes) AppData.Current.State["sizes"] = sizes = [];
            sizes["conflictPanels"] = Math.Round(value);
        }
    }

    /// <summary>The narrowest and widest a panel can be dragged.</summary>
    private const double NarrowestPanel = 180, WidestPanel = 1200;

    /// <summary>The narrowest a panel is made before the user drags one: more panels than fit then scroll sideways.</summary>
    private const double FirstWidest = 300;

    /// <summary>The width each of so many panels takes when they share the room, with the gap after each.</summary>
    private double SharedWidth(int panels) =>
        panels == 0 ? FirstWidest : Math.Max(FirstWidest, Math.Floor((PanelScroller.ActualWidth - 6 * panels) / panels));

    /// <summary>The room for the panels changed: until the user drags a panel, they share it again.</summary>
    private void PanelRoomChanged(object sender, SizeChangedEventArgs e)
    {
        if (AppData.Current.State["sizes"]?["conflictPanels"] is not null) return;
        double width = SharedWidth(Panels.Children.Count);
        foreach (Grid panel in Panels.Children.OfType<Grid>()) panel.Width = width;
    }

    /// <summary>
    /// Shows the value picked: its name, its place in the list, the game's panel and each mod's, in the order they
    /// ran, and the line saying who wins and why.
    /// </summary>
    private void ShowPicked()
    {
        Panels.Children.Clear();
        bool has = picked is not null;
        ShowInEditorButton.IsEnabled = has;
        PreviousButton.IsEnabled = NextButton.IsEnabled = shown.Count > 1;
        if (picked is not { } value)
        {
            ValueName.Text = "";
            Place.Text = Texts.Get("Files.Conflicts.Place", ("at", 0), ("count", Texts.Number(shown.Count)));
            Verdict.Text = all.Count == 0 ? Texts.Get("Files.Conflicts.NoneFound") : Texts.Get("Files.Conflicts.PickOne");
            Verdict.ToolTip = null;
            return;
        }
        ConflictedValue conflict = value.Value;
        ValueName.Text = conflict.IsNameClash ? conflict.Descriptor : conflict.Descriptor + " › " + conflict.Property;
        int at = shown.FindIndex(found => SameValue(found, value));
        Place.Text = Texts.Get("Files.Conflicts.Place", ("at", at < 0 ? "–" : Texts.Number(at + 1)), ("count", Texts.Number(shown.Count)));

        var scrollers = new List<ScrollViewer>();
        panelCount = conflict.Changes.Count + (conflict.IsNameClash ? 0 : 1);
        if (!conflict.IsNameClash)
            Panels.Children.Add(MakePanel(Texts.Get("Files.Conflicts.TheGame"), null, null, "", conflict.GameText, LineLook.Game, Texts.Get("Files.Conflicts.NotFound"), scrollers));
        foreach (ModChange change in conflict.Changes)
        {
            bool kept = ReferenceEquals(change, conflict.Kept);
            Panels.Children.Add(MakePanel(change.Mod, change, value.FileKey, kept ? "✓ " + Texts.Get("Files.Conflicts.InExport") : Texts.Get("Files.Conflicts.Lost"),
                change.Text, kept ? LineLook.Kept : LineLook.Lost, change.New, scrollers));
        }
        // the panels show the same lines, so they scroll up and down together
        foreach (ScrollViewer scroller in scrollers)
            scroller.ScrollChanged += (_, e) =>
            {
                if (e.VerticalChange == 0) return;
                foreach (ScrollViewer other in scrollers.Where(other => other != scroller)) other.ScrollToVerticalOffset(scroller.VerticalOffset);
            };
        PanelScroller.ScrollToHorizontalOffset(0);

        // the mods that lost: a few named, the rest counted, so the line stays short with fifty mods; all of them in the tooltip
        List<string> lost = [.. conflict.Changes.Where(change => !ReferenceEquals(change, conflict.Kept)).Select(change => change.Mod).Distinct()];
        string others = string.Join(", ", lost.Take(NamedMods));
        if (lost.Count > NamedMods) others = Texts.Get("Files.Conflicts.NamesAndMore", ("names", others), ("count", Texts.Number(lost.Count - NamedMods)));
        Verdict.Text = conflict.IsNameClash
            ? Texts.Get("Files.Conflicts.Verdict.Clash", ("kept", conflict.Kept.Mod), ("others", others))
            : Texts.Get("Files.Conflicts.Verdict.Overwritten", ("kept", conflict.Kept.Mod), ("others", others));
        Verdict.ToolTip = Verdict.Text + "\n\n" + Texts.Get("Files.Conflicts.Lost.Tip", ("kept", conflict.Kept.Mod)) + "\n" + NumberedList(lost);
    }

    /// <summary>How a panel marks the value's line: the game's as it was, the kept one's as in the export, a lost one's struck through.</summary>
    private enum LineLook { Game, Kept, Lost }

    /// <summary>
    /// One panel: its heading (the mod's name with "in export" or "lost", and its rule, a link to it; or "The Game"),
    /// then the lines around the value with the value's line marked, scrolling both ways; or, when the lines were not
    /// found, what the edit log says. Its right edge is dragged to make every panel wider or narrower.
    /// </summary>
    private Grid MakePanel(string title, ModChange? change, string? fileKey, string state, ValueText? text, LineLook look, string instead, List<ScrollViewer> scrollers)
    {
        var panel = new Grid { Width = (double?)AppData.Current.State["sizes"]?["conflictPanels"] ?? SharedWidth(panelCount), Margin = new Thickness(0, 0, 6, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition());

        // the heading: the name and its state on the first line, the rule (or "no mods") on the second
        var heading = new Border { BorderThickness = new Thickness(1, 1, 1, 0), Padding = new Thickness(7, 3, 7, 4) };
        heading.SetResourceReference(Border.BorderBrushProperty, "line");
        heading.SetResourceReference(Border.BackgroundProperty, "panel");
        var lines = new StackPanel();
        var first = new DockPanel();
        if (state != "")
        {
            var stateText = new TextBlock { Text = state, FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            stateText.SetResourceReference(TextBlock.ForegroundProperty, look == LineLook.Kept ? "accent" : "muted");
            DockPanel.SetDock(stateText, Dock.Right);
            first.Children.Add(stateText);
        }
        if (change is not null) first.Children.Add(PlaceBadge(change.Mod));
        first.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = title });
        lines.Children.Add(first);
        var second = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        if (change is not null)
        {
            // a button, so Tab reaches it and Enter or Space follows it; its tooltip names the whole rule, which may be cut
            string rule = Texts.Get("Files.Conflicts.Rule", ("rule", change.Rule));
            second.Text = rule;
            second.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            var link = new Button { Content = second, Style = (Style)FindResource("RuleLink"), ToolTip = rule + "\n" + Texts.Get("Files.Conflicts.Rule.Tip") };
            System.Windows.Automation.AutomationProperties.SetName(link, rule);
            // the tooltip is the link's, which holds the whole name, so the cut text gives none of its own
            second.ToolTip = link.ToolTip;
            link.MouseEnter += (_, _) => second.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (_, _) => second.TextDecorations = null;
            link.Click += (_, _) => GoToRule(fileKey!, change);
            lines.Children.Add(link);
        }
        else
        {
            second.Text = Texts.Get("Files.Conflicts.NoMods");
            second.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            lines.Children.Add(second);
        }
        heading.Child = lines;
        panel.Children.Add(heading);

        // the lines, in the code font, scrolling both ways
        var body = new Controls.Bevel { Padding = new Thickness(0, 4, 0, 4) };
        body.SetResourceReference(Controls.Bevel.TopLeftProperty, "lo");
        body.SetResourceReference(Controls.Bevel.BottomRightProperty, "hi");
        body.SetResourceReference(Controls.Bevel.BackgroundProperty, "well");
        var scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var column = new StackPanel();
        if (text is null)
        {
            var note = new TextBlock { Text = instead, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 2, 8, 2) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            column.Children.Add(note);
        }
        else
            for (int at = 0; at < text.Lines.Count; at++) column.Children.Add(Line(text.Lines[at], at == text.At ? look : null));
        scroller.Content = column;
        scrollers.Add(scroller);
        body.Child = scroller;
        Grid.SetRow(body, 1);
        panel.Children.Add(body);

        // the right edge: dragged, every panel takes the new width
        var edge = new Thumb { Width = 6, Style = (Style)FindResource("PanelEdge"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, -6, 0), ToolTip = Texts.Get("Files.Conflicts.Resize.Tip") };
        Grid.SetRowSpan(edge, 2);
        edge.DragDelta += (_, e) =>
        {
            double width = Math.Clamp(panel.Width + e.HorizontalChange, NarrowestPanel, WidestPanel);
            foreach (Grid each in Panels.Children.OfType<Grid>()) each.Width = width;
        };
        edge.DragCompleted += (_, _) => PanelWidth = panel.Width;
        panel.Children.Add(edge);
        return panel;
    }

    /// <summary>How many panels the value picked has, to share the room between them.</summary>
    private int panelCount;

    /// <summary>
    /// A line of a panel: as in the file, or the value's line marked by how it came out. The spaces the game's files
    /// line their "=" up with are shown as one, so the value is in sight in a narrow panel; the indent is kept.
    /// </summary>
    private static UIElement Line(string text, LineLook? look)
    {
        string indent = text[..(text.Length - text.TrimStart().Length)];
        string shown = indent + MultipleSpaces().Replace(text.TrimStart(), " ");
        var line = new TextBlock { Text = shown.Length == 0 ? " " : shown, FontSize = 12, Padding = new Thickness(8, 0, 8, 0) };
        line.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        if (look is not LineLook marked) return line;
        // the mark: a tint of the colour under the line, the text in full colour over it
        var mark = new Grid();
        var tint = new Border { Opacity = 0.22 };
        tint.SetResourceReference(Border.BackgroundProperty, marked switch { LineLook.Kept => "ok", LineLook.Lost => "warn", _ => "caution" });
        mark.Children.Add(tint);
        if (marked == LineLook.Lost)
        {
            line.TextDecorations = TextDecorations.Strikethrough;
            line.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        }
        mark.Children.Add(line);
        return mark;
    }

    /// <summary>
    /// A mod's place in the run order, in a small badge before its name: numbers tell any number of mods apart, where
    /// colours ran out and repeated after a few.
    /// </summary>
    private FrameworkElement PlaceBadge(string mod)
    {
        int place = RunPlace(mod);
        var number = new TextBlock { Text = place > 0 ? Texts.Number(place) : "–", FontSize = 10.5, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        number.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var badge = new Border
        {
            Child = number, BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0), MinWidth = 18, Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = place > 0 ? Texts.Get("Files.Conflicts.Place.Tip", ("place", Texts.Number(place)), ("count", Texts.Number(modOrder.Count))) : null,
        };
        number.HorizontalAlignment = HorizontalAlignment.Center;
        badge.SetResourceReference(Border.BorderBrushProperty, "line");
        badge.SetResourceReference(Border.BackgroundProperty, "well");
        return badge;
    }

    // ---- going elsewhere ----

    /// <summary>The file a value is in, on the Files tab; null when it was removed since.</summary>
    private FileEntry? EntryOf(string fileKey) => MainView.Of(this).FilesTab.Model.Entries.FirstOrDefault(entry => entry.Key == fileKey);

    /// <summary>
    /// A rule's link: the Mods tab, with the rule's mod open, its rule set open and the rule picked. A rule of the mod's
    /// Armory changes has no rule set of its own; the mod is opened.
    /// </summary>
    private void GoToRule(string fileKey, ModChange change)
    {
        MainView view = MainView.Of(this);
        FileEntry? entry = EntryOf(fileKey);
        Close();
        view.Tabs.SelectedItem = view.RulesTabItem;
        Rules.RulesModel model = view.RulesTab.Model;
        model.OpenModNamed(change.Mod);
        if (entry is null) return;
        // the rule's place among the mod's rules for the file is its rule set's, then its place in it
        int index = change.RuleIndex;
        foreach (ModPreset ruleSet in FileRunner.StackFor(entry).FirstOrDefault(part => part.Mod.Name == change.Mod)?.RuleSets ?? [])
        {
            if (index < ruleSet.Rules.Count)
            {
                model.ClickPreset(ruleSet, control: false, shift: false);
                model.SelectOnly(index);
                model.Refresh();
                return;
            }
            index -= ruleSet.Rules.Count;
        }
    }

    /// <summary>Show in File Editor: the File Editor, with the value's file open and its descriptor shown, as exported.</summary>
    private void ShowInEditorClicked(object sender, RoutedEventArgs e)
    {
        if (picked is not { } value || EntryOf(value.FileKey) is not FileEntry entry) return;
        MainView view = MainView.Of(this);
        Close();
        _ = view.EditorTab.ShowDescriptor(entry, value.Value.Descriptor);
    }

    /// <summary>Find again: a Preview of the files, which finds the conflicts again without saving anything; the window follows.</summary>
    private void FindAgainClicked(object sender, RoutedEventArgs e)
    {
        MainView view = MainView.Of(this);
        if (!view.FilesTab.Model.IsBusy) _ = view.FilesTab.Preview();
    }

    /// <summary>Copy as text: every value listed, one line each, for a mod's notes or a message.</summary>
    private void CopyClicked(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder();
        foreach ((string _, ConflictedValue value) in shown)
        {
            string what = value.IsNameClash ? value.Descriptor : value.Descriptor + " › " + value.Property;
            string mods = string.Join(", ", value.Changes.Where(change => !ReferenceEquals(change, value.Kept)).Select(change => change.Mod + " (" + change.New + ")"));
            text.AppendLine(Texts.Get("Files.Conflicts.Copy.Line", ("file", value.File), ("value", what), ("kept", value.Kept.Mod + " (" + value.Kept.New + ")"), ("others", mods)));
        }
        // the button says for a moment what it did, as copying shows nothing else
        try
        {
            Clipboard.SetText(text.ToString());
            CopyButton.Content = Texts.CountWide("Files.Conflicts.Copied", shown.Count);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // another program holds the clipboard: nothing was copied
            CopyButton.Content = Texts.Get("Files.Conflicts.ClipboardBusy");
        }
        copiedPause.Stop();
        copiedPause.Start();
    }

    /// <summary>How long Copy as text says what it did before it reads as before.</summary>
    private readonly System.Windows.Threading.DispatcherTimer copiedPause = new() { Interval = TimeSpan.FromSeconds(2.5) };

    // ---- closing ----

    private void Close()
    {
        Visibility = Visibility.Collapsed;
        List.ItemsSource = null;
        Panels.Children.Clear();
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Escape closes, unless the mod box's list is open: then it closes only that. In the list, the arrow keys, Enter
    /// and Home and End move and fold (<see cref="ListKey"/>); elsewhere Left and Right go to the previous or next
    /// value, unless typing in Find.
    /// </summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ModChoice.IsDropDownOpen) return;
            e.Handled = true;
            Close();
        }
        else if (List.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None && ListKey(e.Key)) e.Handled = true;
        else if (e.Key is Key.Left or Key.Right && e.OriginalSource is not TextBox && e.OriginalSource is not ComboBox)
        {
            e.Handled = true;
            Step(e.Key == Key.Left ? -1 : 1);
        }
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>In a small window the box keeps only a thin margin round it, so its parts have what room there is.</summary>
    private void BackdropSized(object sender, SizeChangedEventArgs e) =>
        Box.Margin = Backdrop.ActualWidth < 760 || Backdrop.ActualHeight < 560 ? new Thickness(6) : new Thickness(40, 30, 40, 30);

    /// <summary>
    /// The window's parts fill the room in the box, but no less than their smallest size: below it, they keep that size
    /// and the box scrolls, so nothing is cut off.
    /// </summary>
    private void RoomChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0) return;
        Inside.Width = Math.Max(Inside.MinWidth, Room.ViewportWidth);
        Inside.Height = Math.Max(Inside.MinHeight, Room.ViewportHeight);
    }
}
