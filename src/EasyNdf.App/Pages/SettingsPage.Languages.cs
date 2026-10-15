using System.IO;
using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.Languages;
using Microsoft.Win32;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Settings tab's Language card: the Language dropdown, and Import, Export and Remove for language packs.
/// </summary>
public partial class SettingsPage
{
    /// <summary>
    /// The Language dropdown: English, then each imported pack by its name, with how much of the app it covers dimmed
    /// beside it. Remove is only on for an imported language.
    /// </summary>
    private void ShowLanguages()
    {
        var options = new List<Rules.ChoiceOption> { new(LanguagePack.EnglishCode, Texts.Get("Settings.LanguageChoice.English")) };
        foreach (LanguagePack pack in Texts.Languages().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            options.Add(new Rules.ChoiceOption(pack.Code, pack.Name, ProgressText(pack)));
        LanguageChoice.SetResourceReference(ItemsControl.ItemTemplateProperty, "DimmedChoice");
        LanguageChoice.ItemsSource = options;
        LanguageChoice.SelectedItem = options.FirstOrDefault(o => o.Value == Texts.Catalog.LanguageCode) ?? options[0];
        // only a pack the user imported can be removed; the languages built into the app stay
        RemoveLanguageButton.IsEnabled = AppData.Current.Languages.Packs.ContainsKey(Texts.Catalog.LanguageCode);
    }

    /// <summary>How much of the app a pack covers: "96%", or "96%, 4 to check" when some texts need a look.</summary>
    private static string ProgressText(LanguagePack pack)
    {
        PackProgress progress = pack.ProgressAgainst(Texts.Catalog.English);
        return progress.NeedsReview == 0
            ? Texts.Get("Settings.LanguageChoice.Progress", ("percent", progress.Percent))
            : Texts.Get("Settings.LanguageChoice.ProgressReview", ("percent", progress.Percent), ("review", progress.NeedsReview));
    }

    /// <summary>A language was chosen: it is saved, and every screen shows it at once.</summary>
    private void LanguageChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showing || LanguageChoice.SelectedItem is not Rules.ChoiceOption { Value: string code }) return;
        UseLanguage(code);
    }

    /// <summary>Switches the app to a language and saves the choice.</summary>
    private void UseLanguage(string code)
    {
        Settings["lang"] = code;
        AppData.Current.SaveSettings();
        Texts.Use(code);
        View.SettingsChanged("lang");
        Show();
    }

    /// <summary>"Import...": reads a language pack, keeps it, and switches to it.</summary>
    private void ImportLanguageClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Texts.Get("Settings.ImportLanguage.Title"), Filter = Texts.Get("Settings.LanguagePackFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        LanguagePack pack;
        try { pack = AppData.Current.Languages.Import(File.ReadAllText(dialog.FileName), Texts.Catalog.English); }
        catch (Exception error) when (error is ModFileException or IOException or UnauthorizedAccessException)
        {
            _ = View.Messages.Tell(Texts.Get("Settings.LanguageNotImported", ("reason", error.Message)));
            return;
        }
        UseLanguage(pack.Code);
        PackProgress progress = pack.ProgressAgainst(Texts.Catalog.English);
        ShowLanguageNote(Texts.Get("Settings.LanguageImported", ("name", pack.Name), ("translated", progress.Translated), ("total", progress.Total)));
    }

    /// <summary>
    /// "Export": saves the language in use as a pack file with every text of the app, ready to translate; for English,
    /// a blank pack.
    /// </summary>
    private void ExportLanguageClicked(object sender, RoutedEventArgs e)
    {
        LanguagePack? pack = Texts.Catalog.Language;
        string name = pack?.Name ?? Texts.Get("Settings.ExportLanguage.BlankName");
        var dialog = new SaveFileDialog
        {
            Title = Texts.Get("Settings.ExportLanguage.Title"),
            FileName = "EasyNDF language - " + name + ".json",
            Filter = Texts.Get("Settings.LanguagePackFiles"),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, LanguageStore.Export(pack, Texts.Catalog.English, MainView.AppVersion)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _ = View.Messages.Tell(Texts.Get("Settings.LanguageNotExported", ("reason", error.Message)));
            return;
        }
        ShowLanguageNote(Texts.Get("Settings.LanguageExported", ("file", Path.GetFileName(dialog.FileName))));
    }

    /// <summary>
    /// "Remove": deletes the imported pack of the language in use, after asking. A language built into the app is then
    /// shown as it comes; any other goes back to English.
    /// </summary>
    private async void RemoveLanguageClicked(object sender, RoutedEventArgs e)
    {
        if (!AppData.Current.Languages.Packs.TryGetValue(Texts.Catalog.LanguageCode, out LanguagePack? pack)) return;
        if (!await View.Messages.Confirm(Texts.Get("Settings.RemoveLanguage.Ask", ("name", pack.Name)), Texts.Get("Settings.RemoveLanguage"))) return;
        try { AppData.Current.Languages.Delete(pack.Code); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        UseLanguage(Texts.BuiltIn.ContainsKey(pack.Code) ? pack.Code : LanguagePack.EnglishCode);
        ShowLanguageNote(Texts.Get("Settings.LanguageRemoved", ("name", pack.Name)));
    }

    /// <summary>A note under the Language card, such as what was imported.</summary>
    private void ShowLanguageNote(string text)
    {
        LanguageNote.Text = text;
        LanguageNote.Visibility = Visibility.Visible;
    }
}
