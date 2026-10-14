using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Editor;
using Microsoft.Win32;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The Filter Descriptors window (the HTML app's <c>dfOpen</c>, <c>dfDraw</c> and the controls around them). Each
/// condition tests a property of the descriptors (or their name or type); the tree under the editor keeps to the
/// descriptors that pass all of them, or any. A filter can be saved under a name, and saved filters exported to a file
/// and imported again. Every change is used at once.
/// </summary>
public partial class FilterWindow : UserControl
{
    /// <summary>The viewer the filter is for, while the window is open.</summary>
    private IDescriptorFilterOwner? owner;

    /// <summary>The condition chosen, by its place in the list; -1 for none.</summary>
    private int chosen = -1;

    /// <summary>The properties the Property list offers.</summary>
    private List<string> properties = ["(Name)", "(Type)"];

    /// <summary>The saved filters, by name.</summary>
    private SortedDictionary<string, DescriptorFilterState> saved = [];

    /// <summary>A condition copied while the clipboard could not be used, for Paste.</summary>
    private FilterRule? copied;

    /// <summary>Whether the window is filling its boxes, which is not a change.</summary>
    private bool showing;

    /// <summary>Builds the window, closed.</summary>
    public FilterWindow()
    {
        InitializeComponent();
        TestChoice.ItemsSource = DescriptorFilterState.Tests;
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    private DescriptorFilterState Filter => owner!.Filter;

    /// <summary>Opens the window for a viewer's filter; the properties of what it views fill the Property list once read.</summary>
    public async Task Open(IDescriptorFilterOwner viewer)
    {
        owner = viewer;
        saved = DescriptorFilterState.LoadSaved();
        chosen = Filter.Rules.Count > 0 ? 0 : -1;
        CentreBesidePageScrollbar();
        Visibility = Visibility.Visible;
        Draw();
        CountNote.Text = "Reading the properties...";
        properties = await viewer.FilterProperties();
        if (owner != viewer || !IsOpen) return;
        Draw();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => RuleList.Focus());
    }

    /// <summary>Closes the window; the filter stays as it is.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        owner = null;
    }

    /// <summary>The filter changed: the viewer is told, and the window shows it.</summary>
    private void Changed()
    {
        owner?.FilterChanged();
        Draw();
    }

    // ---- showing it ----

    /// <summary>Shows the conditions, the chosen one's boxes, the saved filters and the count (the HTML app's <c>dfDraw</c>).</summary>
    private void Draw()
    {
        if (owner is null) return;
        List<FilterRule> rules = Filter.Rules;
        if (chosen >= rules.Count) chosen = rules.Count - 1;
        if (chosen < 0 && rules.Count > 0) chosen = 0;
        showing = true;
        try
        {
            DrawList(rules);
            FilterRule? rule = chosen >= 0 ? rules[chosen] : null;
            EditBox.Opacity = rule is null ? 0.5 : 1;
            EditBox.IsHitTestVisible = rule is not null;
            List<string> offered = [.. properties];
            if (rule is { Property.Length: > 0 } && !offered.Contains(rule.Property)) offered.Add(rule.Property);
            PropertyChoice.ItemsSource = offered;
            PropertyChoice.SelectedItem = rule?.Property;
            TestChoice.SelectedItem = rule?.Test ?? "contains";
            NameBox.Text = rule?.Name ?? "";
            ValueBox.Text = rule?.Value ?? "";
            ValueBox.IsEnabled = rule is not null && !rule.NeedsNoValue;
            GateChoice.SelectedIndex = Filter.Gate == "Any" ? 1 : 0;

            var names = new List<string> { "(none)" };
            names.AddRange(saved.Keys);
            SavedChoice.ItemsSource = names;
            SavedChoice.SelectedItem = saved.ContainsKey(Filter.Name) ? Filter.Name : "(none)";
            SaveButton.IsEnabled = DeleteButton.IsEnabled = saved.ContainsKey(Filter.Name);
            ExportButton.IsEnabled = saved.Count > 0 || rules.Count > 0;
        }
        finally { showing = false; }
        ShowCount();
    }

    /// <summary>The conditions, each with its switch, its sentence and ×; the chosen one marked.</summary>
    private void DrawList(List<FilterRule> rules)
    {
        RuleList.Children.Clear();
        if (rules.Count == 0)
        {
            RuleList.Children.Add(new TextBlock { Text = "No conditions. Press Add condition.", Margin = new Thickness(6), Foreground = (System.Windows.Media.Brush)FindResource("muted") });
            return;
        }
        for (int i = 0; i < rules.Count; i++)
        {
            FilterRule rule = rules[i];
            int index = i;
            var row = new DockPanel { Margin = new Thickness(0), Background = i == chosen ? (System.Windows.Media.Brush)FindResource("sel") : System.Windows.Media.Brushes.Transparent, Tag = index };
            var on = new CheckBox { Style = (Style)FindResource("Tick"), IsChecked = rule.On, ToolTip = "Use this condition", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 2, 4, 2) };
            on.Click += (_, _) =>
            {
                rule.On = on.IsChecked == true;
                chosen = index;
                Changed();
            };
            var delete = new Button { Style = (Style)FindResource("IconButton"), Content = "×", ToolTip = "Delete", Margin = new Thickness(4, 2, 4, 2) };
            delete.Click += (_, _) =>
            {
                chosen = index;
                DeleteChosen();
            };
            DockPanel.SetDock(on, Dock.Left);
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(on);
            row.Children.Add(delete);
            row.Children.Add(Sentence(rule));
            RuleList.Children.Add(row);
        }
    }

    /// <summary>
    /// A condition as the list reads it (the HTML app's <c>dfLine</c>): "Damage is greater than 10", the property and
    /// value in the code font and the test dimmed; a named one shows its name, then the sentence dimmed.
    /// </summary>
    private TextBlock Sentence(FilterRule rule)
    {
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 4, 2, 4) };
        var muted = (System.Windows.Media.Brush)FindResource("muted");
        bool allDim = !rule.On;
        Run Part(string words, bool mono, bool dim)
        {
            var run = new Run(words);
            if (mono) run.FontFamily = (System.Windows.Media.FontFamily)FindResource("mono");
            if (dim || allDim) run.Foreground = muted;
            return run;
        }
        bool named = rule.Name != "";
        if (named)
        {
            text.Inlines.Add(Part(rule.Name, mono: false, dim: false));
            text.Inlines.Add(new Run(" "));
        }
        // the spaces between the parts are in the plain font, as on the page, where they sit between the parts
        text.Inlines.Add(Part(rule.Property != "" ? rule.Property : "(no property)", mono: true, dim: named));
        text.Inlines.Add(Part(" " + rule.Test, mono: false, dim: true));
        if (rule.NeedsNoValue) return text;
        text.Inlines.Add(new Run(" "));
        text.Inlines.Add(Part(rule.Value == "" ? "\"\"" : rule.Value, mono: true, dim: named));
        return text;
    }

    /// <summary>How many descriptors are shown (the HTML app's <c>dfCount</c>); the viewer calls it after filtering again.</summary>
    public void ShowCount()
    {
        if (owner is null || !IsOpen) return;
        CountNote.Inlines.Clear();
        if (!Filter.IsOn)
        {
            CountNote.Inlines.Add(new Run(Filter.Rules.Count > 0 ? "No complete condition is on: every descriptor is shown." : "No conditions: every descriptor is shown."));
            return;
        }
        (string before, string bold, string after) = owner.FilterCount();
        CountNote.Inlines.Add(new Run(before));
        var number = new Run(bold);
        number.SetResourceReference(TextElement.ForegroundProperty, "ink");
        CountNote.Inlines.Add(number);
        CountNote.Inlines.Add(new Run(after));
    }

    /// <summary>Says something in the count's place, such as "Saved".</summary>
    private void SayInCount(string text)
    {
        CountNote.Inlines.Clear();
        CountNote.Inlines.Add(new Run(text));
    }

    // ---- the chosen condition's boxes ----

    private FilterRule? ChosenRule => owner is not null && chosen >= 0 && chosen < Filter.Rules.Count ? Filter.Rules[chosen] : null;

    private void NameTyped(object sender, TextChangedEventArgs e)
    {
        if (showing || ChosenRule is not FilterRule rule) return;
        // a name does not change what passes, so the tree is left alone
        rule.Name = NameBox.Text;
        Filter.Save();
        RedrawChosenSentence(rule);
    }

    private void ValueTyped(object sender, TextChangedEventArgs e)
    {
        if (showing || ChosenRule is not FilterRule rule) return;
        rule.Value = ValueBox.Text;
        owner!.FilterChanged();
        RedrawChosenSentence(rule);
        ShowCount();
    }

    /// <summary>The chosen condition's sentence follows its boxes as they are typed in, without drawing the list again.</summary>
    private void RedrawChosenSentence(FilterRule rule)
    {
        if (chosen < 0 || chosen >= RuleList.Children.Count || RuleList.Children[chosen] is not DockPanel row) return;
        row.Children.RemoveAt(row.Children.Count - 1);
        row.Children.Add(Sentence(rule));
    }

    private void PropertyChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showing || ChosenRule is not FilterRule rule || PropertyChoice.SelectedItem is not string property) return;
        rule.Property = property;
        Changed();
    }

    private void TestChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showing || ChosenRule is not FilterRule rule || TestChoice.SelectedItem is not string test) return;
        rule.Test = test;
        Changed();
    }

    private void GateChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showing || owner is null || GateChoice.SelectedItem is not ComboBoxItem { Tag: string gate }) return;
        Filter.Gate = gate;
        owner.FilterChanged();
        ShowCount();
    }

    // ---- the list of conditions ----

    /// <summary>Adds a condition, on the first real property and testing "contains", after <paramref name="at"/> or at the end.</summary>
    private void Add(int? at = null)
    {
        if (owner is null) return;
        var rule = new FilterRule { Property = properties.Count > 2 ? properties[2] : "(Name)" };
        int index = at ?? Filter.Rules.Count;
        Filter.Rules.Insert(index, rule);
        chosen = index;
        Changed();
    }

    private void AddClicked(object sender, RoutedEventArgs e)
    {
        Add();
        PropertyChoice.Focus();
    }

    /// <summary>Moves the chosen condition up (-1) or down (1).</summary>
    private void MoveChosen(int by)
    {
        List<FilterRule> rules = Filter.Rules;
        int to = chosen + by;
        if (chosen < 0 || to < 0 || to >= rules.Count) return;
        (rules[chosen], rules[to]) = (rules[to], rules[chosen]);
        chosen = to;
        Changed();
        RuleList.Focus();
    }

    private void DeleteChosen()
    {
        if (ChosenRule is null) return;
        Filter.Rules.RemoveAt(chosen);
        Changed();
        RuleList.Focus();
    }

    private void ToggleChosen()
    {
        if (ChosenRule is not FilterRule rule) return;
        rule.On = !rule.On;
        Changed();
        RuleList.Focus();
    }

    private void DuplicateChosen()
    {
        if (ChosenRule is not FilterRule rule) return;
        Filter.Rules.Insert(chosen + 1, rule.Copy());
        chosen++;
        Changed();
    }

    /// <summary>Copies the chosen condition to the clipboard, as JSON; kept here too, should the clipboard fail.</summary>
    private void CopyChosen()
    {
        if (ChosenRule is not FilterRule rule) return;
        copied = rule.Copy();
        try { Clipboard.SetText(DescriptorFilterState.ClipboardText(rule)); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    /// <summary>Pastes a condition from the clipboard (or the one copied here) after the chosen one.</summary>
    private void Paste()
    {
        FilterRule? rule = null;
        try { if (Clipboard.ContainsText()) rule = DescriptorFilterState.FromClipboard(Clipboard.GetText()); }
        catch (System.Runtime.InteropServices.COMException) { }
        rule ??= copied?.Copy();
        if (rule is null || owner is null) return;
        Filter.Rules.Insert(chosen + 1, rule);
        chosen++;
        Changed();
        RuleList.Focus();
    }

    /// <summary>The row of the list under the mouse, by its place; -1 for none.</summary>
    private static int RowUnder(MouseEventArgs e)
    {
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null; at = at is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is DockPanel { Tag: int index }) return index;
        return -1;
    }

    /// <summary>A click on a condition chooses it; a double-click below the conditions adds one.</summary>
    private void ListPressed(object sender, MouseButtonEventArgs e)
    {
        int index = RowUnder(e);
        if (index < 0)
        {
            if (e.ClickCount == 2)
            {
                Add();
                PropertyChoice.Focus();
            }
            return;
        }
        if (e.OriginalSource is DependencyObject source && IsInside<ButtonBase>(source)) return;
        chosen = index;
        Draw();
        RuleList.Focus();
    }

    private static bool IsInside<T>(DependencyObject element) where T : DependencyObject
    {
        for (DependencyObject? at = element; at is not null; at = at is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is T) return true;
        return false;
    }

    /// <summary>The list's right-click menu.</summary>
    private void ListRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        int index = RowUnder(e);
        if (index >= 0)
        {
            chosen = index;
            Draw();
        }
        FilterRule? rule = index >= 0 ? ChosenRule : null;
        bool has = rule is not null;
        Menus.Show(RuleList,
        [
            has ? new MenuChoice(rule!.On ? "Disable" : "Enable", ToggleChosen) : null,
            has ? new MenuChoice("Duplicate", DuplicateChosen) : null,
            has ? Menus.Line : null,
            has ? new MenuChoice("Move up", () => MoveChosen(-1), chosen > 0, "Ctrl+Up") : null,
            has ? new MenuChoice("Move down", () => MoveChosen(1), chosen < Filter.Rules.Count - 1, "Ctrl+Down") : null,
            has ? Menus.Line : null,
            has ? new MenuChoice("Cut", () => { CopyChosen(); DeleteChosen(); }, Key: "Ctrl+X") : null,
            has ? new MenuChoice("Copy", CopyChosen, Key: "Ctrl+C") : null,
            new MenuChoice("Paste", Paste, Key: "Ctrl+V"),
            Menus.Line,
            new MenuChoice("Add condition", () => Add(has ? chosen + 1 : null)),
            has ? new MenuChoice("Delete", DeleteChosen, Key: "Delete") : null,
        ]);
    }

    /// <summary>
    /// The list's keys: Up and Down choose, Ctrl+Up and Ctrl+Down move, Delete deletes, Space switches the condition on or
    /// off, and Ctrl+X, Ctrl+C and Ctrl+V cut, copy and paste.
    /// </summary>
    private void ListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox or ComboBox) return;
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        int count = Filter.Rules.Count;
        switch (e.Key)
        {
            case Key.Up when control: MoveChosen(-1); break;
            case Key.Down when control: MoveChosen(1); break;
            case Key.Up when chosen > 0: chosen--; Draw(); break;
            case Key.Down when chosen < count - 1: chosen++; Draw(); break;
            case Key.Delete: DeleteChosen(); break;
            case Key.Space: ToggleChosen(); break;
            case Key.C when control: CopyChosen(); break;
            case Key.X when control: CopyChosen(); DeleteChosen(); break;
            case Key.V when control: Paste(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void ClearClicked(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        owner.Filter = new DescriptorFilterState();
        chosen = -1;
        Changed();
    }

    // ---- saved filters: a filter works without one ----

    private void SavedChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showing || owner is null || SavedChoice.SelectedItem is not string name) return;
        if (name == "(none)")
        {
            Filter.Name = "";
            Filter.Save();
            Draw();
            return;
        }
        if (!saved.TryGetValue(name, out DescriptorFilterState? filter)) return;
        DescriptorFilterState used = filter.Copy();
        used.Name = name;
        owner.Filter = used;
        chosen = 0;
        Changed();
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        if (!saved.ContainsKey(Filter.Name))
        {
            await SaveAs();
            return;
        }
        saved[Filter.Name] = Filter.Copy();
        DescriptorFilterState.StoreSaved(saved);
        SayInCount("Saved \"" + Filter.Name + "\".");
    }

    private async void SaveAsClicked(object sender, RoutedEventArgs e) => await SaveAs();

    /// <summary>Saves the filter under a name asked for; an existing one of that name is replaced only when the user says so.</summary>
    private async Task SaveAs()
    {
        if (owner is null) return;
        MessageOverlay messages = MainView.Of(this).Messages;
        DialogAnswer reply = await messages.Ask("Name for this filter:", new DialogOptions { Input = Filter.Name, Ok = "Save" });
        string name = reply.Text.Trim();
        if (reply.Choice != DialogChoice.Ok || name == "" || owner is null) return;
        if (saved.ContainsKey(name) && name != Filter.Name && !await messages.Confirm("A filter named \"" + name + "\" already exists. Replace it?", "Replace")) return;
        saved[name] = Filter.Copy();
        Filter.Name = name;
        DescriptorFilterState.StoreSaved(saved);
        Filter.Save();
        Draw();
    }

    private async void DeleteClicked(object sender, RoutedEventArgs e)
    {
        if (owner is null || !saved.ContainsKey(Filter.Name)) return;
        string name = Filter.Name;
        if (!await MainView.Of(this).Messages.Confirm("Delete the saved filter \"" + name + "\"? The conditions shown stay as they are.", "Delete")) return;
        saved.Remove(name);
        Filter.Name = "";
        DescriptorFilterState.StoreSaved(saved);
        Filter.Save();
        Draw();
    }

    /// <summary>"Export": the saved filters (or, with none saved, this one, as "Filter") as a filter file.</summary>
    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        IReadOnlyDictionary<string, DescriptorFilterState> filters = saved.Count > 0 ? saved : new Dictionary<string, DescriptorFilterState> { ["Filter"] = Filter.Copy() };
        var dialog = new SaveFileDialog { Title = "Export filters", FileName = "EasyNDF filters.json", Filter = "Filter files (*.json)|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, DescriptorFilterState.FileText(filters)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _ = MainView.Of(this).Messages.Tell("The filters could not be saved. " + error.Message);
        }
    }

    /// <summary>"Import...": the filters of a filter file are added; one whose name is taken gets " (2)", " (3)"...</summary>
    private async void ImportClicked(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        var dialog = new OpenFileDialog { Title = "Import filters", Filter = "Filter files (*.json)|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string text;
        try { text = await File.ReadAllTextAsync(dialog.FileName); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await MainView.Of(this).Messages.Tell("\"" + Path.GetFileName(dialog.FileName) + "\" could not be read. " + error.Message);
            return;
        }
        if (DescriptorFilterState.FromFile(text) is not List<(string Name, DescriptorFilterState Filter)> filters)
        {
            await MainView.Of(this).Messages.Tell("\"" + Path.GetFileName(dialog.FileName) + "\" is not a filter file.");
            return;
        }
        foreach ((string name, DescriptorFilterState filter) in filters)
        {
            string key = name;
            for (int n = 2; saved.ContainsKey(key); n++) key = name + " (" + n + ")";
            saved[key] = filter;
        }
        DescriptorFilterState.StoreSaved(saved);
        Draw();
        SayInCount(filters.Count > 0 ? "Imported " + filters.Count + (filters.Count == 1 ? " filter." : " filters.") : "No filters found in that file.");
    }

    // ---- closing ----

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e) => Close();

    /// <summary>A click inside the window does not reach the backdrop behind it.</summary>
    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>
    /// The window is centred in the room beside the page's scrollbar, when the page has one, as a browser centres a
    /// window fixed to its view.
    /// </summary>
    private void CentreBesidePageScrollbar()
    {
        double scrollbar = 0;
        if (MainView.Find(this) is MainView view && FindPageScroller(view.Tabs) is ScrollViewer page && page.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            scrollbar = page.ActualWidth - page.ViewportWidth;
        Box.Margin = new Thickness(0, 0, scrollbar, 0);
    }

    /// <summary>The scrolling box the tabs' pages sit in.</summary>
    private static ScrollViewer? FindPageScroller(DependencyObject element)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
            if (child is ScrollViewer { TemplatedParent: TabControl } page) return page;
            if (FindPageScroller(child) is ScrollViewer inner) return inner;
        }
        return null;
    }

    /// <summary>The window is min(40rem, 94%) wide and at most 90% of the app tall.</summary>
    private void BackdropResized(object sender, SizeChangedEventArgs e)
    {
        double border = RuleWindow.PageBorder;
        Box.Width = Math.Min(640, (e.NewSize.Width + 2 * border) * 0.94);
        Box.MaxHeight = (e.NewSize.Height + 2 * border) * 0.9;
    }
}
