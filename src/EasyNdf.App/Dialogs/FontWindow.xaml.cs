using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Theming;
using EasyNdf.App.Text;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// A font in one of the Fonts window's lists.
/// </summary>
/// <param name="Name">The font's name as the setting keeps it; "" for the theme's font.</param>
/// <param name="Label">The name as the list shows it.</param>
/// <param name="Face">The font, for the sample beside the name.</param>
public sealed record FontChoice(string Name, string Label, FontFamily Face);

/// <summary>
/// The Fonts window (Settings, Appearance, "Fonts..."). <see cref="Edit"/> opens it on the fonts in use; OK saves them in
/// the settings "uiFont" and "codeFont" ("" for the theme's own) and applies them to every theme.
/// </summary>
public partial class FontWindow : UserControl
{
    /// <summary>The fonts on this computer, by name, read once.</summary>
    private static List<FontFamily>? installed;

    /// <summary>Whether each font is fixed-width, worked out once per font.</summary>
    private static readonly Dictionary<string, bool> fixedWidth = new(StringComparer.OrdinalIgnoreCase);

    private string chosenUi = "", chosenCode = "";
    private bool filling;
    private TaskCompletionSource<bool>? answer;

    /// <summary>Builds the window, closed.</summary>
    public FontWindow()
    {
        InitializeComponent();
        PreviewKeyDown += KeyPressed;
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>The interface font's list.</summary>
    internal IEnumerable<FontChoice> UiChoices => UiList.Items.OfType<FontChoice>();

    /// <summary>The code font's list.</summary>
    internal IEnumerable<FontChoice> CodeChoices => CodeList.Items.OfType<FontChoice>();

    /// <summary>Opens the window on the fonts chosen now. Answers whether OK saved a choice.</summary>
    public Task<bool> Edit()
    {
        answer = new TaskCompletionSource<bool>();
        chosenUi = ThemeManager.ChosenFont(isCodeFont: false);
        chosenCode = ThemeManager.ChosenFont(isCodeFont: true);
        filling = true;
        UiSearch.Text = CodeSearch.Text = "";
        FixedOnly.IsChecked = true;
        filling = false;
        ShowList(UiList);
        ShowList(CodeList);
        ShowSample();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => UiList.Focus());
        return answer.Task;
    }

    // ---- the lists ----

    private bool IsCodeList(ListBox list) => list == CodeList;

    /// <summary>
    /// Fills a list: the theme's font first, then each font on this computer whose name holds what its search box holds
    /// (and, for the code list, only fixed-width ones unless that is switched off). The chosen font is selected.
    /// </summary>
    private void ShowList(ListBox list)
    {
        bool isCode = IsCodeList(list);
        string search = (isCode ? CodeSearch : UiSearch).Text.Trim();
        bool fixedOnly = isCode && FixedOnly.IsChecked == true;
        string themeFont = ThemeManager.ThemeFontName(isCode);
        var choices = new List<FontChoice> { new("", Texts.Get("Settings.Fonts.ThemeFont", ("font", themeFont)), ThemeManager.FontFamilyFor(themeFont, isCode)) };
        foreach (FontFamily family in Installed())
        {
            string name = family.Source;
            if (search != "" && !name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            if (fixedOnly && !IsFixedWidth(family)) continue;
            choices.Add(new FontChoice(name, name, family));
        }

        filling = true;
        list.ItemsSource = choices;
        string chosen = isCode ? chosenCode : chosenUi;
        list.SelectedItem = choices.FirstOrDefault(c => string.Equals(c.Name, chosen, StringComparison.OrdinalIgnoreCase));
        filling = false;
        if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
    }

    /// <summary>The fonts on this computer, sorted by name.</summary>
    private static List<FontFamily> Installed() =>
        installed ??= [.. Fonts.SystemFontFamilies.OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Whether a font is fixed-width: its "i" as wide as its "W". A font that cannot be read counts as not.</summary>
    private static bool IsFixedWidth(FontFamily family)
    {
        if (fixedWidth.TryGetValue(family.Source, out bool known)) return known;
        bool isFixed = false;
        try
        {
            foreach (Typeface face in family.GetTypefaces())
            {
                if (!face.TryGetGlyphTypeface(out GlyphTypeface glyphs)) continue;
                isFixed = glyphs.CharacterToGlyphMap.TryGetValue('i', out ushort narrow) && glyphs.CharacterToGlyphMap.TryGetValue('W', out ushort wide)
                    && Math.Abs(glyphs.AdvanceWidths[narrow] - glyphs.AdvanceWidths[wide]) < 0.0001;
                break;
            }
        }
        catch (Exception problem) when (problem is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NullReferenceException) { }
        fixedWidth[family.Source] = isFixed;
        return isFixed;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!filling) ShowList(sender == CodeSearch ? CodeList : UiList);
    }

    private void FixedOnlyClicked(object sender, RoutedEventArgs e) => ShowList(CodeList);

    /// <summary>A font clicked in a list becomes the choice; a list emptied by its search keeps the choice it had.</summary>
    private void FontPicked(object sender, SelectionChangedEventArgs e)
    {
        if (filling || sender is not ListBox { SelectedItem: FontChoice picked } list) return;
        if (IsCodeList(list)) chosenCode = picked.Name;
        else chosenUi = picked.Name;
        ShowSample();
    }

    /// <summary>The sample lines in the fonts chosen.</summary>
    private void ShowSample()
    {
        UiSample.FontFamily = ThemeManager.FontFamilyFor(chosenUi != "" ? chosenUi : ThemeManager.ThemeFontName(isCodeFont: false), isCodeFont: false);
        CodeSample.FontFamily = ThemeManager.FontFamilyFor(chosenCode != "" ? chosenCode : ThemeManager.ThemeFontName(isCodeFont: true), isCodeFont: true);
    }

    // ---- the buttons ----

    private void ThemeFontsClicked(object sender, RoutedEventArgs e)
    {
        chosenUi = chosenCode = "";
        ShowList(UiList);
        ShowList(CodeList);
        ShowSample();
    }

    private void OkClicked(object sender, RoutedEventArgs e)
    {
        var settings = AppData.Current.Settings;
        settings["uiFont"] = chosenUi;
        settings["codeFont"] = chosenCode;
        AppData.Current.SaveSettings();
        ThemeManager.ApplySaved();
        Close(saved: true);
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => Close(saved: false);

    private void Close(bool saved)
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        answer?.TrySetResult(saved);
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(saved: false); }
        else if (e.Key == Key.Enter) { e.Handled = true; OkClicked(this, e); }
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close(saved: false);
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
