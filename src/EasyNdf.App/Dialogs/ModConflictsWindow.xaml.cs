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
        ModConflictList.Changed += () => { if (IsOpen) Fill(keepPicked: true); };
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
        Summary.Text = Texts.Get("Files.Conflicts.Summary", ("values", Texts.Count("Files.Conflicts.Values", all.Count)), ("mods", string.Join("  ›  ", modOrder)));
        if (!keepPicked || picked is not { } was || !all.Any(found => SameValue(found, was))) picked = null;
        else picked = all.First(found => SameValue(found, was));
        Refill();
    }

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
        if (picked is not { } current || !shown.Any(found => SameValue(found, current))) picked = shown.Count > 0 ? shown[0] : null;
        if (picked is { } open)
        {
            unfolded.Add(open.FileKey);
            unfolded.Add(open.FileKey + "\n" + open.Value.Descriptor);
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
        NothingFound.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowPicked();
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
        if (row.Value is { } value)
        {
            Pick(value);
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

        List<string> lost = [.. conflict.Changes.Where(change => !ReferenceEquals(change, conflict.Kept)).Select(change => change.Mod)];
        Verdict.Text = conflict.IsNameClash
            ? Texts.Get("Files.Conflicts.Verdict.Clash", ("kept", conflict.Kept.Mod), ("others", string.Join(", ", lost)))
            : Texts.Get("Files.Conflicts.Verdict.Overwritten", ("kept", conflict.Kept.Mod), ("others", string.Join(", ", lost)));
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
        if (change is not null) first.Children.Add(Dot(change.Mod));
        first.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = title });
        lines.Children.Add(first);
        var second = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        if (change is not null)
        {
            second.Text = Texts.Get("Files.Conflicts.Rule", ("rule", change.Rule));
            second.ToolTip = Texts.Get("Files.Conflicts.Rule.Tip");
            second.Cursor = Cursors.Hand;
            second.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            second.MouseEnter += (_, _) => second.TextDecorations = TextDecorations.Underline;
            second.MouseLeave += (_, _) => second.TextDecorations = null;
            second.MouseLeftButtonUp += (_, _) => GoToRule(fileKey!, change);
        }
        else
        {
            second.Text = Texts.Get("Files.Conflicts.NoMods");
            second.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        }
        lines.Children.Add(second);
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
        var edge = new Thumb { Width = 6, Cursor = Cursors.SizeWE, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, -6, 0), Opacity = 0, ToolTip = Texts.Get("Files.Conflicts.Resize.Tip") };
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

    /// <summary>Colours for the mods' dots, by their place in the order: told apart in every theme, light or dark.</summary>
    private static readonly Color[] DotColours =
    [
        Color.FromRgb(0x6F, 0x9F, 0xC8), Color.FromRgb(0xC9, 0x93, 0x5F), Color.FromRgb(0xA0, 0x84, 0xC9), Color.FromRgb(0x6F, 0xB5, 0x9E),
        Color.FromRgb(0xC8, 0x84, 0x9A), Color.FromRgb(0xB5, 0xA9, 0x5A), Color.FromRgb(0x8C, 0x9A, 0xA8), Color.FromRgb(0x9E, 0xB8, 0x6A),
    ];

    /// <summary>A mod's dot, in its colour.</summary>
    private FrameworkElement Dot(string mod)
    {
        int at = Math.Max(0, modOrder.IndexOf(mod));
        return new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            Fill = new SolidColorBrush(DotColours[at % DotColours.Length]),
        };
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
        try { Clipboard.SetText(text.ToString()); }
        catch (System.Runtime.InteropServices.ExternalException) { } // the clipboard is busy: nothing is copied
    }

    // ---- closing ----

    private void Close()
    {
        Visibility = Visibility.Collapsed;
        List.ItemsSource = null;
        Panels.Children.Clear();
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>Escape closes; the arrow keys go to the previous or next value, unless typing in Find.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
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
}
