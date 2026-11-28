using EasyNdf.Data.Languages;

namespace EasyNdf.Data.Tests;

public class LanguageTests
{
    private static TextCatalog Catalog()
    {
        var catalog = new TextCatalog();
        catalog.AddEnglish("""{"Files.AddFolder":"Add folder...","Files.Edits":"{count} edit|{count} edits","Rules.DeleteAsk":"Delete \"{name}\"?"}""", "files.json");
        return catalog;
    }

    private static readonly Dictionary<string, string> English = new() { ["Files.AddFolder"] = "Add folder...", ["Files.Edits"] = "{count} edit|{count} edits", ["Ok"] = "OK" };

    [Fact]
    public void English_texts_with_blanks_and_plurals()
    {
        TextCatalog catalog = Catalog();

        Assert.Equal("Add folder...", catalog.Get("Files.AddFolder"));
        Assert.Equal("Delete \"WTO\"?", catalog.Get("Rules.DeleteAsk", ("name", "WTO")));
        Assert.Equal("1 edit", catalog.Count("Files.Edits", 1));
        Assert.Equal("5 edits", catalog.Count("Files.Edits", 5));
        Assert.Equal("0 edits", catalog.Count("Files.Edits", 0));
        // a name with no English shows as itself, so a typo stands out
        Assert.Equal("Files.AddFolfer", catalog.Get("Files.AddFolfer"));
    }

    [Fact]
    public void A_name_used_twice_is_refused()
    {
        TextCatalog catalog = Catalog();
        var error = Assert.Throws<FormatException>(() => catalog.AddEnglish("""{"Files.AddFolder":"Again"}""", "other.json"));
        Assert.Contains("already used", error.Message);
    }

    [Fact]
    public void A_language_falls_back_to_english_for_what_it_lacks()
    {
        TextCatalog catalog = Catalog();
        var french = new LanguagePack { Code = "fr", Name = "Français" };
        french.Texts["Files.Edits"] = new PackText("{count} modification|{count} modifications", "{count} edit|{count} edits");
        catalog.Use(french);

        Assert.Equal("Add folder...", catalog.Get("Files.AddFolder"));
        // French uses the singular for 0 and 1
        Assert.Equal("0 modification", catalog.Count("Files.Edits", 0));
        Assert.Equal("2 modifications", catalog.Count("Files.Edits", 2));
    }

    [Theory]
    [InlineData("ru", 1, 0)]
    [InlineData("ru", 3, 1)]
    [InlineData("ru", 5, 2)]
    [InlineData("ru", 11, 2)]
    [InlineData("ru", 21, 0)]
    [InlineData("ru", 22, 1)]
    [InlineData("pl", 1, 0)]
    [InlineData("pl", 21, 2)]
    [InlineData("pl", 24, 1)]
    [InlineData("zh", 5, 0)]
    [InlineData("de", 1, 0)]
    [InlineData("pt-br", 0, 0)]
    public void Plural_forms_follow_each_language(string code, long count, int form) =>
        Assert.Equal(form, PluralForms.FormOf(code, count));

    [Fact]
    public void Packs_import_by_name_and_count_what_needs_review()
    {
        using var folder = new TemporaryFolder();
        var store = new LanguageStore(folder.Path);

        LanguagePack pack = store.Import("""
            {"code":"FR!","name":"Français","texts":{
              "Files.AddFolder":{"english":"Add folder...","text":"Ajouter un dossier..."},
              "Files.Edits":{"english":"{count} change|{count} changes","text":"{count} modification|{count} modifications"},
              "Ok":{"english":"OK","text":"OK"},
              "Help":{"english":"Help","text":""}}}
            """, English);

        Assert.Equal("fr", pack.Code);
        Assert.Equal(3, pack.Texts.Count);
        Assert.Equal("Français", new LanguageStore(folder.Path).Packs["fr"].Name);
        // the English of Files.Edits has changed since it was translated
        Assert.Equal(new PackProgress(3, 1, 3), pack.ProgressAgainst(English));
    }

    [Fact]
    public void Html_app_packs_are_read_by_their_english()
    {
        var store = new LanguageStore();

        LanguagePack pack = store.Import("""{"code":"de","sections":[{"strings":{"Add folder...":"Ordner hinzufügen...","OK":"OK"}}]}""", English);

        Assert.Equal("Ordner hinzufügen...", pack.Texts["Files.AddFolder"].Text);
        Assert.False(pack.Texts.ContainsKey("Ok"));
        var error = Assert.Throws<ModFileException>(() => store.Import("""{"code":"en","texts":{"a":{"english":"b","text":"c"}}}""", English));
        Assert.StartsWith("Its \"code\"", error.Message);
    }

    [Fact]
    public void Export_lists_every_name_with_the_current_english()
    {
        var pack = new LanguagePack { Code = "fr", Name = "Français" };
        pack.Texts["Files.AddFolder"] = new PackText("Ajouter un dossier...", "Add a folder...");

        var json = (System.Text.Json.Nodes.JsonObject)JsonFiles.Parse(LanguageStore.Export(pack, English, "1.2.3"))!;

        Assert.Equal("1.2.3", (string?)json["madeFor"]);
        Assert.Equal(["Files.AddFolder", "Files.Edits", "Ok"], ((System.Text.Json.Nodes.JsonObject)json["texts"]!).Select(t => t.Key));
        Assert.Equal("Add folder...", (string?)json["texts"]!["Files.AddFolder"]!["english"]);
        Assert.Equal("", (string?)json["texts"]!["Ok"]!["text"]);
    }

    [Fact]
    public void An_imported_pack_is_laid_over_the_built_in_one()
    {
        LanguagePack builtIn = LanguageStore.ReadPack("""{"code":"fr","name":"Français","texts":{"Files.AddFolder":{"english":"Add folder...","text":"Ajouter un dossier...","machine":true},"Ok":{"english":"OK","text":"OK"}}}""");
        var own = new LanguagePack { Code = "fr", Name = "Français (mine)" };
        own.Texts["Files.AddFolder"] = new PackText("Ajouter le dossier...", "Add folder...");

        LanguagePack layered = LanguageStore.Layered(builtIn, own)!;

        Assert.Equal("Ajouter le dossier...", layered.Texts["Files.AddFolder"].Text);
        Assert.Equal("OK", layered.Texts["Ok"].Text);
        Assert.Equal("Français (mine)", layered.Name);
        Assert.Same(builtIn, LanguageStore.Layered(builtIn, null));
        Assert.True(builtIn.Texts["Files.AddFolder"].Machine);
    }
}
