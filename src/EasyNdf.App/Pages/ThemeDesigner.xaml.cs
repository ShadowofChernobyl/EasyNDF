using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Theming;
using EasyNdf.Data;
using EasyNdf.Data.Themes;
using Microsoft.Win32;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Theme Designer (the HTML app's <c>renderThemeEditor</c> and the handlers of its buttons). The built-in themes are
/// fixed; "New from this theme" copies the theme in use, and the copy can be edited. Every change is saved and applied
/// at once.
/// </summary>
public partial class ThemeDesigner : UserControl
{
    /// <summary>Raised after a theme is added, renamed or deleted, so the Settings tab can show it in its Theme dropdown.</summary>
    public event Action? ThemesChanged;

    /// <summary>Builds the designer.</summary>
    public ThemeDesigner()
    {
        InitializeComponent();
    }

    private MainView View => MainView.Of(this);

    /// <summary>The designer's window, when it is in one (it always is, except in the workbench's pictures).</summary>
    private ThemeDesignerWindow? OwnWindow => Window.GetWindow(this) as ThemeDesignerWindow;

    /// <summary>The colour window: the designer window's own, which opens over the designer.</summary>
    private ColourWindow ColourPicker => OwnWindow?.ColourWindow ?? View.ColourWindow;

    /// <summary>The messages: the designer window's own, so a question appears where the button was clicked.</summary>
    private MessageOverlay Messages => OwnWindow?.Messages ?? View.Messages;

    private static JsonObject Settings => AppData.Current.Settings;

    /// <summary>The user's own theme in use, which can be edited; null for a built-in theme.</summary>
    private static CustomTheme? EditedTheme => ThemeManager.CustomThemeOf((string?)Settings["theme"] ?? "");

    /// <summary>Shows the buttons, the note and the rows for the theme in use (the HTML app's <c>renderThemeEditor</c>).</summary>
    public void Show()
    {
        filling = true;
        try { FillThemeChoice(ThemeChoice); }
        finally { filling = false; }
        CustomTheme? theme = EditedTheme;
        RenameButton.IsEnabled = DeleteButton.IsEnabled = ResetButton.IsEnabled = ExportButton.IsEnabled = theme is not null;
        ShowNote(theme is null
            ? Texts.Get("Settings.Designer.BuiltInNote")
            : Texts.Get("Settings.Designer.EditingNote", ("name", NameOf(theme))));
        // a built-in theme's colours are shown too, but locked: "New from This Theme" makes the copy that can be changed
        Colours.IsEnabled = theme is not null;
        ShowRows(theme);
    }

    /// <summary>Whether the dropdown is being filled, which is not a choice.</summary>
    private bool filling;

    /// <summary>
    /// Fills a Theme dropdown (the Settings tab's and the designer's): Automatic, the built-in themes, then the user's own,
    /// with the theme in use chosen.
    /// </summary>
    public static void FillThemeChoice(ComboBox box)
    {
        var options = new List<Rules.ChoiceOption> { new("system", Texts.Get("Settings.ThemeChoice.Automatic")) };
        options.AddRange(BuiltInThemes.Order.Select(id => new Rules.ChoiceOption(id, BuiltInThemes.ById[id].Name)));
        foreach (CustomTheme own in AppData.Current.Themes.Themes)
            options.Add(new Rules.ChoiceOption(ThemeManager.CustomKey(own), own.Name != "" ? own.Name : Texts.Get("Settings.ThemeChoice.Untitled"), Texts.Get("Settings.ThemeChoice.YourTheme")));
        box.SetResourceReference(ItemsControl.ItemTemplateProperty, "DimmedChoice");
        box.ItemsSource = options;
        string current = (string?)AppData.Current.Settings["theme"] ?? BuiltInThemes.DefaultId;
        box.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options.First(o => o.Value == BuiltInThemes.DefaultId);
    }

    /// <summary>A theme chosen in the designer's dropdown: the app switches to it, and Settings shows it too.</summary>
    private void ThemeChosen(object sender, SelectionChangedEventArgs e)
    {
        if (filling || ThemeChoice.SelectedItem is not Rules.ChoiceOption { Value: string id }) return;
        Settings["theme"] = id;
        AppData.Current.SaveSettings();
        ThemeManager.ApplySaved();
        Show();
        ThemesChanged?.Invoke();
    }

    private void ShowNote(string text, bool isWarning = false)
    {
        Note.Text = text;
        Note.SetResourceReference(TextBlock.ForegroundProperty, isWarning ? "warn" : "muted");
    }

    private static string NameOf(CustomTheme theme) => theme.Name != "" ? theme.Name : Texts.Get("Settings.Designer.Untitled");

    // ---- the rows ----

    /// <summary>
    /// The colour rows, in two groups: the colours of the app itself, then (with its scrollbars between) the
    /// colours of the File Editor's text.
    /// </summary>
    private void ShowRows(CustomTheme? edited)
    {
        Dictionary<string, string> colours = edited is not null
            ? ThemeColors.For(ThemeManager.CustomThemeFor(ThemeManager.CustomKey(edited))!, bevelSetting: null)
            : ThemeColors.For(ThemeManager.Current, bevelSetting: null);
        // the rows of a built-in theme are locked, so the theme they would change is never used
        CustomTheme theme = edited ?? new CustomTheme();
        var interfaceRows = new RowGrid(InterfaceRows);
        var editorRows = new RowGrid(EditorRows);
        foreach ((string key, _) in BuiltInThemes.Keys)
        {
            bool isEditorColour = key.StartsWith("s-", StringComparison.Ordinal) || key.StartsWith("d-", StringComparison.Ordinal);
            (isEditorColour ? editorRows : interfaceRows).AddColour(ThemeColourNames.Row(key), ColourRow(theme, key, ThemeColourNames.Of(key), colours[key]));
        }
    }

    /// <summary>A colour's swatch, which opens the colour window, and its hex text box.</summary>
    private (FrameworkElement Swatch, TextBox Hex) ColourRow(CustomTheme theme, string key, string label, string colour)
    {
        var hex = new TextBox { Text = colour, MaxLength = 7, Style = (Style)FindResource("MonoBox"), ToolTip = Texts.Get("Settings.Designer.Hex.Tip", ("colour", label)) };
        hex.LostKeyboardFocus += (_, _) => TakeHex(theme, key, hex);
        hex.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            TakeHex(theme, key, hex);
        };
        // input[type=color]: a sunken box with the colour in it, inside a thin grey line, as Chrome draws it
        var swatch = new Bevel
        {
            Padding = new Thickness(3, 6, 3, 6),
            Cursor = Cursors.Hand,
            ToolTip = label,
            Child = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)), BorderThickness = new Thickness(1), Background = BrushOf(colour) },
        };
        swatch.SetResourceReference(Bevel.TopLeftProperty, "lo");
        swatch.SetResourceReference(Bevel.BottomRightProperty, "hi");
        swatch.SetResourceReference(Bevel.BackgroundProperty, "well");
        swatch.MouseLeftButtonUp += async (_, _) =>
        {
            string before = theme.Colors.GetValueOrDefault(key, hex.Text);
            // the theme follows the window as the colour changes, without saving; OK saves it, Cancel puts it back
            string? picked = await ColourPicker.Pick(ThemeColourNames.Row(key), before, shown => PreviewColour(theme, key, shown));
            if (picked is not null) SetColour(theme, key, picked);
        };
        return (swatch, hex);
    }

    private static SolidColorBrush BrushOf(string colour)
    {
        int[] rgb = ThemeColors.Channels(colour);
        return new SolidColorBrush(Color.FromRgb((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]));
    }

    /// <summary>A hex box's colour, once typed: "#abc" and "#AABBCC" are taken; anything else is refused with a warning.</summary>
    private void TakeHex(CustomTheme theme, string key, TextBox hex)
    {
        string current = ThemeColors.For(ThemeManager.CustomThemeFor(ThemeManager.CustomKey(theme))!, null)[key];
        if (hex.Text == current) return;
        string valid = ThemeColors.ValidColor(hex.Text);
        if (valid == "")
        {
            hex.Text = current;
            ShowNote(Texts.Get("Settings.Designer.NotAColour"), isWarning: true);
            return;
        }
        SetColour(theme, key, valid);
    }

    /// <summary>A colour shown in the theme while it is being picked; it is saved only once it is kept.</summary>
    private static void PreviewColour(CustomTheme theme, string key, string colour)
    {
        theme.Colors[key] = colour;
        ThemeManager.ApplySaved();
    }

    private void SetColour(CustomTheme theme, string key, string colour)
    {
        theme.Colors[key] = colour;
        SaveAndApply(theme);
        // drawn again after the box that lost the focus has finished with it
        Dispatcher.BeginInvoke(Show);
    }

    /// <summary>Saves the theme, and applies it.</summary>
    private void SaveAndApply(CustomTheme theme)
    {
        try { AppData.Current.Themes.Save(theme); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _ = Messages.Tell(Texts.Get("Settings.Designer.CouldNotSave", ("problem", error.Message)), DialogIcon.Error);
        }
        ThemeManager.ApplySaved();
    }

    // ---- the buttons ----

    /// <summary>"New from this theme": an editable copy of the theme in use, named by the user.</summary>
    private async void NewClicked(object sender, RoutedEventArgs e)
    {
        DialogAnswer answer = await Messages.Ask(Texts.Get("Settings.Designer.NameAsk"), new DialogOptions { Input = FreeName(Texts.Get("Settings.Designer.MyTheme", ("theme", ThemeManager.Current.Name))) });
        string name = answer.Choice == DialogChoice.Ok ? answer.Text.Trim() : "";
        if (name != "") AddCopyOfThemeInUse(name);
    }

    /// <summary>
    /// Adds a copy of the theme in use under a name, and chooses it (the HTML app's <c>addTheme</c>). It remembers the
    /// built-in theme it comes from, for "Reset colors".
    /// </summary>
    public void AddCopyOfThemeInUse(string name)
    {
        Theme copied = ThemeManager.Current;
        string chosen = (string?)Settings["theme"] ?? "";
        string baseId = BuiltInThemes.ById.ContainsKey(chosen) ? chosen : EditedTheme?.BaseId ?? "";
        AddTheme(new CustomTheme
        {
            Name = FreeName(name),
            Scheme = copied.Scheme,
            Font = CustomTheme.CleanFontName(copied.Font),
            CodeFont = CustomTheme.CleanFontName(copied.CodeFont),
            Colors = ThemeColors.Complete(copied.Colors, baseId),
            BaseId = baseId,
        });
    }

    private async void RenameClicked(object sender, RoutedEventArgs e)
    {
        if (EditedTheme is not CustomTheme theme) return;
        DialogAnswer answer = await Messages.Ask(Texts.Get("Settings.Designer.RenameAsk"), new DialogOptions { Input = theme.Name });
        string name = answer.Choice == DialogChoice.Ok ? answer.Text.Trim() : "";
        if (name == "" || name == theme.Name) return;
        theme.Name = FreeName(ClipName(name));
        // the theme is chosen by its name, so the setting follows it
        Settings["theme"] = ThemeManager.CustomKey(theme);
        AppData.Current.SaveSettings();
        SaveAndApply(theme);
        ThemesChanged?.Invoke();
    }

    private async void DeleteClicked(object sender, RoutedEventArgs e)
    {
        if (EditedTheme is not CustomTheme theme) return;
        string olivine = BuiltInThemes.ById[BuiltInThemes.DefaultId].Name;
        if (!await Messages.Confirm(Texts.Get("Settings.Designer.DeleteAsk", ("name", theme.Name), ("theme", olivine)), Texts.Get("Settings.Designer.Delete"), DialogIcon.Warning)) return;
        try { AppData.Current.Themes.Delete(theme); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await Messages.Tell(Texts.Get("Settings.Designer.CouldNotDelete", ("problem", error.Message)), DialogIcon.Error);
            return;
        }
        Settings["theme"] = BuiltInThemes.DefaultId;
        AppData.Current.SaveSettings();
        ThemeManager.ApplySaved();
        ThemesChanged?.Invoke();
    }

    /// <summary>"Reset colors": every colour and the fonts as in the built-in theme it was made from.</summary>
    private async void ResetClicked(object sender, RoutedEventArgs e)
    {
        if (EditedTheme is not CustomTheme theme) return;
        Theme from = BuiltInThemes.ById.GetValueOrDefault(theme.BaseId) ?? BuiltInThemes.ById[BuiltInThemes.DefaultId];
        if (!await Messages.Confirm(Texts.Get("Settings.Designer.ResetAsk", ("name", theme.Name), ("theme", from.Name)), Texts.Get("Settings.Designer.ResetColors"), DialogIcon.Warning)) return;
        theme.Colors = new Dictionary<string, string>(from.Colors);
        theme.Scheme = from.Scheme;
        theme.Font = CustomTheme.CleanFontName(from.Font);
        theme.CodeFont = CustomTheme.CleanFontName(from.CodeFont);
        SaveAndApply(theme);
        ThemesChanged?.Invoke();
    }

    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (EditedTheme is not CustomTheme theme) return;
        var dialog = new SaveFileDialog { Title = Texts.Get("Settings.Designer.ExportTheme"), FileName = FileNames.ForName(theme.Name) + ".theme.json", Filter = Texts.Get("Settings.Designer.ThemeFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, ThemeStore.ExportText(theme)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _ = Messages.Tell(Texts.Get("Settings.Designer.CouldNotSave", ("problem", error.Message)), DialogIcon.Error);
        }
    }

    /// <summary>"Import theme...": a theme file becomes one of the user's themes; colours it lacks are Olivine's.</summary>
    private async void ImportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Texts.Get("Settings.Designer.ImportTitle"), Filter = Texts.Get("Settings.Designer.AnyThemeFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        CustomTheme imported;
        try { imported = ReadThemeFile(dialog.FileName); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            await Messages.Tell(Texts.Get("Settings.Designer.CouldNotImport", ("problem", error.Message)), DialogIcon.Error);
            return;
        }
        AddTheme(imported);
        ShowNote(Texts.Get("Settings.Designer.Imported", ("theme", BuiltInThemes.ById[BuiltInThemes.DefaultId].Name)));
    }

    /// <summary>Reads a theme file as "Import theme..." takes it; throws when it has no colour the app knows.</summary>
    private static CustomTheme ReadThemeFile(string path)
    {
        string text = File.ReadAllText(path).TrimStart('﻿');
        if (JsonNode.Parse(text) is not JsonObject json || ThemeStore.Read(json) is not CustomTheme read
            || !BuiltInThemes.Keys.Any(key => ThemeColors.ValidColor(read.Colors.GetValueOrDefault(key.Key)) != ""))
            throw new InvalidDataException(Texts.Get("Settings.Designer.NoColours"));
        string fileName = Path.GetFileName(path);
        string nameFromFile = fileName.EndsWith(".theme.json", StringComparison.OrdinalIgnoreCase) ? fileName[..^".theme.json".Length]
            : fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? fileName[..^".json".Length] : fileName;
        read.Name = FreeName(read.Name != "" ? read.Name : nameFromFile);
        read.Font = CustomTheme.CleanFontName(read.Font);
        read.CodeFont = CustomTheme.CleanFontName(read.CodeFont);
        read.Colors = ThemeColors.Complete(read.Colors, "");
        read.BaseId = "";
        return read;
    }

    /// <summary>Adds a theme, and chooses it.</summary>
    private void AddTheme(CustomTheme theme)
    {
        theme.Name = ClipName(theme.Name != "" ? theme.Name : Texts.Get("Settings.Designer.DefaultName"));
        Settings["theme"] = ThemeManager.CustomKey(theme);
        AppData.Current.SaveSettings();
        SaveAndApply(theme);
        ThemesChanged?.Invoke();
    }

    /// <summary>A theme's name is at most 60 characters.</summary>
    private static string ClipName(string name) => name.Length > 60 ? name[..60] : name;

    /// <summary>A name no theme has yet: the name itself, else with " 2", " 3"... after it (the HTML app's <c>freeThemeName</c>).</summary>
    private static string FreeName(string name)
    {
        HashSet<string> used = [.. BuiltInThemes.Order.Select(id => BuiltInThemes.ById[id].Name), .. AppData.Current.Themes.Themes.Select(t => t.Name)];
        if (!used.Contains(name)) return name;
        int number = 2;
        while (used.Contains(name + " " + number)) number++;
        return name + " " + number;
    }

    /// <summary>
    /// The rows of one box of colours (the HTML app's <c>.trows</c>): the label, a 51-pixel swatch and a 112-pixel hex box,
    /// 10 pixels apart and 6 pixels down; a font box or a dropdown takes a row of its own under its label.
    /// </summary>
    private sealed class RowGrid
    {
        private readonly Grid grid;

        public RowGrid(Grid grid)
        {
            this.grid = grid;
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            foreach (double width in new[] { -1, 10, 51.2, 10, 112 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        }

        public void AddColour(string label, (FrameworkElement Swatch, TextBox Hex) controls)
        {
            int row = NextRow();
            controls.Swatch.Height = 27.2;
            Put(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, row, 0, 1);
            Put(controls.Swatch, row, 2, 1);
            controls.Hex.VerticalAlignment = VerticalAlignment.Center;
            Put(controls.Hex, row, 4, 1);
        }

        public void AddOwnRow(string label, FrameworkElement control)
        {
            Put(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 0) }, NextRow(), 0, 5);
            Put(control, NextRow(), 0, 5);
        }

        /// <summary>A new row, 6 pixels under the one before.</summary>
        private int NextRow()
        {
            if (grid.RowDefinitions.Count > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return grid.RowDefinitions.Count - 1;
        }

        private void Put(FrameworkElement element, int row, int column, int span)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            if (element is TextBlock text) text.SetResourceReference(TextBlock.ForegroundProperty, "ink");
            grid.Children.Add(element);
        }
    }
}
