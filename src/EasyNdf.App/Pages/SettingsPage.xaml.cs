using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Text;
using EasyNdf.App.Theming;
using EasyNdf.Data;
using EasyNdf.Data.Themes;
using Microsoft.Win32;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Settings tab (the HTML app's <c>renderSettings</c> and the handlers of its boxes). Each setting is saved as soon
/// as it is changed, under the HTML app's own names, so the app reads it where it is used.
/// </summary>
public partial class SettingsPage : UserControl
{
    /// <summary>Whether the page is filling its boxes, which is not a change.</summary>
    private bool showing;

    private static JsonObject Settings => AppData.Current.Settings;

    /// <summary>Builds the tab, showing the settings as they are.</summary>
    public SettingsPage()
    {
        InitializeComponent();
        Designer.ThemesChanged += Show;
        ListCategories();
        MarkCategory("Results");
        Show();
    }

    private MainView View => MainView.Of(this);

    // ---- the categories ----

    /// <summary>
    /// The cards, each by its name: the English name in its Tag, which stays the same whatever language the headings
    /// are shown in.
    /// </summary>
    private IEnumerable<(string Name, FrameworkElement Card)> CardsByName =>
        Cards.Children.OfType<FrameworkElement>().Select(card => ((string)card.Tag, card));

    /// <summary>A card's heading: the text in the heading strip at its top.</summary>
    private static TextBlock HeadingOf(FrameworkElement card) =>
        card is Controls.Bevel { Child: StackPanel { Children: [Controls.Bevel { Child: TextBlock heading }, ..] } } ? heading : new TextBlock();

    /// <summary>The list on the left: a line for each card, in their order, showing the card's heading.</summary>
    private void ListCategories()
    {
        foreach ((string name, FrameworkElement card) in CardsByName)
        {
            // the line shows the heading's text, so it changes with the language
            var title = new TextBlock();
            title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = HeadingOf(card) });
            var line = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = (Style)FindResource("WikiNavItem"),
                Content = title,
                Tag = name,
            };
            line.Click += (_, _) => ShowCategory(name);
            // as tall as its rows, across the whole width
            card.VerticalAlignment = VerticalAlignment.Top;
            Categories.Children.Add(line);
        }
    }

    /// <summary>Scrolls a category's card to the top of the page.</summary>
    public void ShowCategory(string name)
    {
        if (CardsByName.FirstOrDefault(c => c.Name == name).Card is not FrameworkElement card) return;
        UpdateLayout();
        CardScroller.ScrollToVerticalOffset(TopOf(card));
        MarkCategory(name);
    }

    /// <summary>Where a card starts on the page, from the page's top.</summary>
    private double TopOf(FrameworkElement card) => card.TranslatePoint(new Point(0, 0), Cards).Y;

    /// <summary>The page scrolled: the list marks the category at the top of what is in view.</summary>
    private void CardsScrolled(object sender, ScrollChangedEventArgs e)
    {
        // a card counts as in view once its heading is within 40 pixels of the top; the last card, once the page is at its end
        var shown = CardsByName.Where(c => c.Card.Visibility == Visibility.Visible).ToList();
        if (shown.Count == 0) return;
        string inView = shown[0].Name;
        foreach ((string name, FrameworkElement card) in shown)
            if (TopOf(card) - CardScroller.VerticalOffset <= 40) inView = name;
        if (CardScroller.VerticalOffset >= CardScroller.ScrollableHeight - 1 && CardScroller.ScrollableHeight > 0) inView = shown[^1].Name;
        MarkCategory(inView);
    }

    // ---- the search box ----

    /// <summary>
    /// The search box: only the settings whose name, note or section holds every word typed are shown, with the sections
    /// that have any, in the page and in the list. The Theme Designer counts as one setting of Appearance.
    /// </summary>
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        string[] words = SettingsSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Matches(string text) => words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
        bool anyShown = false;
        foreach ((string name, FrameworkElement card) in CardsByName)
        {
            bool cardShown = false;
            foreach (Controls.SettingRow row in RowsOf(card))
            {
                bool rowShown = Matches(HeadingOf(card).Text + " " + row.Label + " " + row.Note);
                row.Visibility = rowShown ? Visibility.Visible : Visibility.Collapsed;
                cardShown |= rowShown;
            }
            if (card.IsAncestorOf(DesignerBlock))
            {
                bool designerShown = Matches(HeadingOf(card).Text + " " + Text.Texts.Get("Settings.Designer.SearchWords"));
                DesignerBlock.Visibility = designerShown ? Visibility.Visible : Visibility.Collapsed;
                cardShown |= designerShown;
            }
            card.Visibility = cardShown ? Visibility.Visible : Visibility.Collapsed;
            foreach (var line in Categories.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Where(l => (string)l.Tag == name))
                line.Visibility = card.Visibility;
            anyShown |= cardShown;
        }
        NoMatch.Visibility = anyShown ? Visibility.Collapsed : Visibility.Visible;
        CardScroller.ScrollToTop();
    }

    /// <summary>Escape in the search box empties it.</summary>
    private void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || SettingsSearch.Text == "") return;
        e.Handled = true;
        SettingsSearch.Text = "";
    }

    /// <summary>The setting rows of a card, in each of its setting grids.</summary>
    private static IEnumerable<Controls.SettingRow> RowsOf(FrameworkElement card) =>
        card is Controls.Bevel { Child: StackPanel parts }
            ? parts.Children.OfType<Controls.SettingGrid>().SelectMany(grid => grid.Children.OfType<Controls.SettingRow>())
            : [];

    /// <summary>Marks one category in the list on the left.</summary>
    private void MarkCategory(string name)
    {
        foreach (var line in Categories.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>())
            line.IsChecked = (string)line.Tag == name;
    }

    /// <summary>Shows every setting as it is (the HTML app's <c>renderSettings</c>).</summary>
    public void Show()
    {
        showing = true;
        try
        {
            CapBox.Text = Number("logCap", 500).ToString(System.Globalization.CultureInfo.InvariantCulture);
            CapRow.Note = Texts.Get("Settings.CapRow.Note");
            ShowFonts();
            CapRow.NoteIsWarning = false;
            DecimalsBox.Text = Number("decimals", 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            DecimalsRow.Note = Texts.Get("Settings.DecimalsRow.Note");
            DecimalsRow.NoteIsWarning = false;
            foreach (CheckBox toggle in new[] { CommentsSwitch, StampSwitch, WrapSwitch, FitSwitch, AssignSwitch, ConfirmSwitch, SessionSwitch, PanesScaleSwitch, GuidesSwitch, PairsSwitch,
                ConfirmExportSwitch, BackupSwitch, OpenFolderSwitch, LineNumbersSwitch, WordWrapSwitch, ReopenFileSwitch, TooltipsSwitch, WindowSwitch, TreeAllSwitch })
                toggle.IsChecked = IsOn((string)toggle.Tag);
            foreach (ComboBox choice in new[] { LineEndChoice, ScopeChoice, FolderChoice, DuplicateChoice, UndoChoice, ScaleChoice })
            {
                string current = (string?)Settings[(string)choice.Tag] ?? "";
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? choice.Items[0];
            }
            ShowTextSizes();
            ShowThemes();
            ShowLanguages();
            Designer.Show();
            BevelSwitch.IsChecked = Settings["bevel"]?.GetValue<bool>() ?? true;
            TreadSwitch.IsChecked = Settings["treadScrollbars"]?.GetValue<bool>() ?? true;
            DataNote.Text = AppData.Current.Folders is { } folders
                ? Texts.Get("Settings.DataNote.Folder", ("folder", folders.Root))
                : Texts.Get("Settings.DataNote.InMemory");
        }
        finally { showing = false; }
    }

    /// <summary>
    /// Whether a switch is on. The HTML app's settings that start off are off unless set; the others are on unless set
    /// off (as its <c>settings.x !== false</c>).
    /// </summary>
    /// <summary>Whether a switch's setting is on; a setting not saved yet takes its default.</summary>
    private static bool IsOn(string key) => (Settings[key] ?? SettingsStore.DefaultSettings()[key])?.GetValue<bool>() == true;

    private static int Number(string key, int otherwise) => Settings[key] is JsonValue value && value.TryGetValue(out int number) ? number : otherwise;

    // ---- switches and dropdowns ----

    /// <summary>A switch: its setting (named in its Tag) is saved; what depends on it follows.</summary>
    private void SwitchClicked(object sender, RoutedEventArgs e)
    {
        if (showing || sender is not CheckBox { Tag: string key } toggle) return;
        Settings[key] = toggle.IsChecked == true;
        AppData.Current.SaveSettings();
        if (key == "wrapRows") Controls.ListText.ApplySaved();
        View.SettingsChanged(key);
    }

    /// <summary>The File Editor's text sizes, with the one in use chosen (Ctrl+scroll can make it any size from 7 to 36).</summary>
    private void ShowTextSizes()
    {
        // the saved size, as the page is filled before it is inside the main view
        int current = (int)(AppData.Current.Settings["edZoom"]?.GetValue<double>() ?? 12);
        var sizes = new SortedSet<int> { 10, 11, 12, 13, 14, 16, 18, 20, current };
        TextSizeChoice.ItemsSource = sizes.ToList();
        TextSizeChoice.SelectedItem = current;
    }

    private void TextSizeChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showing || TextSizeChoice.SelectedItem is not int size) return;
        View.EditorTab.ChangeTextSize(size);
    }

    /// <summary>A dropdown: the value of its choice (in the choice's Tag) is saved under its setting (in its own Tag).</summary>
    private void ChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (showing || sender is not ComboBox { Tag: string key, SelectedItem: ComboBoxItem { Tag: string value } }) return;
        Settings[key] = value;
        AppData.Current.SaveSettings();
        View.SettingsChanged(key);
    }

    // ---- the two number boxes ----

    /// <summary>Enter in a number box takes the number, as leaving the box does.</summary>
    private void BoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (sender == CapBox) TakeCap();
        else TakeDecimals();
    }

    private void CapLeft(object sender, KeyboardFocusChangedEventArgs e) => TakeCap();

    private void DecimalsLeft(object sender, KeyboardFocusChangedEventArgs e) => TakeDecimals();

    /// <summary>"Edits listed per rule": a whole number from 0 to 20000; anything else is refused and the old number kept.</summary>
    private void TakeCap()
    {
        string typed = CapBox.Text.Trim();
        int before = Number("logCap", 500);
        if (typed == before.ToString(System.Globalization.CultureInfo.InvariantCulture)) return;
        if (typed == "" || !typed.All(char.IsAsciiDigit) || !int.TryParse(typed, out int cap) || cap > 20000)
        {
            CapBox.Text = before.ToString(System.Globalization.CultureInfo.InvariantCulture);
            CapRow.Note = Texts.Get("Settings.CapRow.Refused", ("before", before.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
            CapRow.NoteIsWarning = true;
            return;
        }
        Settings["logCap"] = cap;
        AppData.Current.SaveSettings();
        Show();
    }

    /// <summary>"Decimal places": a whole number of at most two digits, at most 12; empty means 0, no limit.</summary>
    private void TakeDecimals()
    {
        string typed = DecimalsBox.Text.Trim();
        int before = Number("decimals", 0);
        if (typed == before.ToString(System.Globalization.CultureInfo.InvariantCulture)) return;
        if (typed != "" && (typed.Length > 2 || !typed.All(char.IsAsciiDigit)))
        {
            DecimalsRow.Note = Texts.Get("Settings.DecimalsRow.Refused");
            DecimalsRow.NoteIsWarning = true;
            DecimalsBox.Text = before.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }
        Settings["decimals"] = Math.Min(12, typed == "" ? 0 : int.Parse(typed, System.Globalization.CultureInfo.InvariantCulture));
        AppData.Current.SaveSettings();
        Show();
    }

    // ---- appearance ----

    /// <summary>The theme dropdown: Automatic, the built-in themes in the HTML app's order, then the user's own (the HTML app's "Your themes").</summary>
    private void ShowThemes()
    {
        var options = new List<Rules.ChoiceOption> { new("system", Texts.Get("Settings.ThemeChoice.Automatic")) };
        options.AddRange(BuiltInThemes.Order.Select(id => new Rules.ChoiceOption(id, BuiltInThemes.ById[id].Name)));
        foreach (CustomTheme own in AppData.Current.Themes.Themes)
            options.Add(new Rules.ChoiceOption(ThemeManager.CustomKey(own), own.Name != "" ? own.Name : Texts.Get("Settings.ThemeChoice.Untitled"), Texts.Get("Settings.ThemeChoice.YourTheme")));
        ThemeChoice.SetResourceReference(ItemsControl.ItemTemplateProperty, "DimmedChoice");
        ThemeChoice.ItemsSource = options;
        string current = (string?)Settings["theme"] ?? BuiltInThemes.DefaultId;
        ThemeChoice.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options.First(o => o.Value == BuiltInThemes.DefaultId);
    }

    private void ThemeChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showing || ThemeChoice.SelectedItem is not Rules.ChoiceOption { Value: string id }) return;
        Settings["theme"] = id;
        AppData.Current.SaveSettings();
        ThemeManager.ApplySaved();
        Show();
    }

    /// <summary>"Fonts...": the Fonts window; the note under the row names the fonts in use afterwards.</summary>
    private async void FontsClicked(object sender, RoutedEventArgs e)
    {
        if (await View.FontWindow.Edit()) ShowFonts();
    }

    /// <summary>The Fonts row's note: the two fonts in use, and whether they are the theme's.</summary>
    private void ShowFonts()
    {
        static string Named(bool isCode)
        {
            string chosen = ThemeManager.ChosenFont(isCode);
            return chosen != "" ? chosen : Texts.Get("Settings.FontsRow.ThemeFont", ("font", ThemeManager.ThemeFontName(isCode)));
        }
        FontsRow.Note = Texts.Get("Settings.FontsRow.Note", ("interface", Named(false)), ("code", Named(true)));
    }

    private void BevelClicked(object sender, RoutedEventArgs e)
    {
        if (showing) return;
        Settings["bevel"] = BevelSwitch.IsChecked == true;
        AppData.Current.SaveSettings();
        ThemeManager.ApplySaved();
    }

    private void TreadClicked(object sender, RoutedEventArgs e)
    {
        if (showing) return;
        Settings["treadScrollbars"] = TreadSwitch.IsChecked == true;
        AppData.Current.SaveSettings();
        ThemeManager.ApplySaved();
    }

    /// <summary>"Reset column widths": every column fits itself again.</summary>
    private void ResetColumnsClicked(object sender, RoutedEventArgs e)
    {
        Files.FileListModel files = View.FilesTab.Model;
        bool had = files.HasColumnWidthsByHand;
        files.FitColumnsAgain();
        ColumnsNote.Text = had ? Texts.Get("Settings.ColumnsReset") : Texts.Get("Settings.NothingToReset");
        DataNote.Text = Texts.Get("Settings.ColumnsReset");
    }

    // ---- data ----

    /// <summary>"Back up all mods": every preset of every mod in one file.</summary>
    private void BackupClicked(object sender, RoutedEventArgs e)
    {
        int presets = AppData.Current.Mods.Sum(m => m.Presets.Count);
        if (presets == 0)
        {
            DataNote.Text = Texts.Get("Settings.Backup.NoPresets");
            return;
        }
        var dialog = new SaveFileDialog { Title = Texts.Get("Settings.BackUpAllMods"), FileName = "EasyNDF mods backup.json", Filter = Texts.Get("Settings.ModFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, ModFiles.BackupText(AppData.Current.Mods)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _ = View.Messages.Tell(Texts.Get("Settings.Backup.NotSaved", ("reason", error.Message)));
            return;
        }
        DataNote.Text = Texts.Get("Settings.Backup.Done", ("presets", Rules.RulesModel.Count(presets)), ("file", Path.GetFileName(dialog.FileName)));
    }

    /// <summary>"Restore backup...": the mods in it are added on the Rules tab, as Import mod adds them.</summary>
    private async void RestoreClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Texts.Get("Settings.Restore.Title"), Filter = Texts.Get("Settings.ModFiles"), Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        View.Tabs.SelectedItem = View.RulesTabItem;
        await View.RulesTab.ImportModFiles(dialog.FileNames);
    }

    /// <summary>"Reset settings": every setting as at first, the theme too. Mods, rules and files are kept.</summary>
    private async void ResetClicked(object sender, RoutedEventArgs e)
    {
        if (!await View.Messages.Confirm(Texts.Get("Settings.Reset.Ask"), Texts.Get("Settings.ResetSettings"))) return;
        AppData.Current.ResetSettings();
        ThemeManager.ApplySaved();
        Text.Texts.UseSaved();
        View.SettingsChanged("");
        Show();
        DataNote.Text = Texts.Get("Settings.Reset.Done");
    }

    /// <summary>
    /// "Clear all data": everything the app has kept is deleted, and the app starts again with the mods it comes with.
    /// Files elsewhere on the disk are not touched.
    /// </summary>
    private async void ClearClicked(object sender, RoutedEventArgs e)
    {
        if (!await View.Messages.Confirm(Texts.Get("Settings.Clear.Ask"), Texts.Get("Common.Ok"))) return;
        try { AppData.Current.ClearAll(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await View.Messages.Tell(Texts.Get("Settings.Clear.NotAll", ("reason", error.Message)));
        }
        // the app starts again, as the HTML app reloads its page
        if (Environment.ProcessPath is string app) Process.Start(app);
        Application.Current.Shutdown();
    }
}
