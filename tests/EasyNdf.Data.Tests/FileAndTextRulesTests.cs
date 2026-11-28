using System.Text;
using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Engine.Mods;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Text rules, which give dictionary files such as UNITS.csv their lines, and File rules, which carry files of the
/// mod's own into it: the store they are kept in, their export, and how a mod file carries them.
/// </summary>
public class FileAndTextRulesTests
{
    private static JsonObject TextRule(string lines, bool enabled = true)
    {
        JsonObject rule = RuleLoading.NewRule("Text");
        rule["Params"]!["Text"] = lines;
        rule["Enabled"] = enabled;
        return rule;
    }

    private static JsonObject FileRule(string destination, string stored, bool enabled = true)
    {
        JsonObject rule = RuleLoading.NewRule("File");
        rule["Params"]!["Destination"] = destination;
        rule["Params"]!["Stored"] = stored;
        rule["Params"]!["ChosenName"] = "Logo.png";
        rule["Enabled"] = enabled;
        return rule;
    }

    // ---- Text rules ----

    [Fact]
    public void Text_lines_are_read_with_or_without_quotes()
    {
        List<DictionaryText> texts = FileAndTextRules.LinesOf("\"TOKEN\";\"REFTEXT\"\nYSM_A;First\r\n\"YSM_B\";\"Second; with a semicolon\"\n\nno semicolon\n");

        Assert.Equal([new DictionaryText("YSM_A", "First"), new DictionaryText("YSM_B", "Second; with a semicolon")], texts);
    }

    [Fact]
    public void Text_rules_that_are_off_give_nothing()
    {
        JsonObject group = RuleLoading.NewRule("Group");
        group["Enabled"] = false;
        List<DictionaryText> texts = FileAndTextRules.TextsOf([TextRule("A;1"), TextRule("B;2", enabled: false), group, TextRule("C;3")]);

        Assert.Equal([new DictionaryText("A", "1")], texts);
    }

    [Fact]
    public void A_dictionary_file_gets_the_texts_and_a_later_text_wins()
    {
        string csv = "\"TOKEN\";\"REFTEXT\"\r\n\"OLD\";\"Old\"\r\n\"A\";\"Game text\"\r\n";

        (string text, int count) = DivisionExtras.WithTexts(csv, [new DictionaryText("A", "First"), new DictionaryText("B", "Bee"), new DictionaryText("A", "Second")]);

        Assert.Equal("\"TOKEN\";\"REFTEXT\"\r\n\"OLD\";\"Old\"\r\n\"A\";\"Second\"\r\n\"B\";\"Bee\"\r\n", text);
        Assert.Equal(2, count);
    }

    // ---- the store ----

    [Fact]
    public void The_store_keeps_a_file_once_by_its_content()
    {
        using var folder = new TemporaryFolder();
        var store = new ModFileStore(folder.Path);
        byte[] content = Encoding.UTF8.GetBytes("picture");

        string name = store.Keep(content, @"C:\Pictures\Logo.PNG");
        string again = store.Keep(content, "other.png");

        Assert.Equal(name, again);
        Assert.Matches("^[0-9a-f]{32}\\.png\\z", name);
        Assert.Equal(content, store.Read(name));
        Assert.Single(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public void The_store_reads_only_names_it_gives()
    {
        using var folder = new TemporaryFolder();
        var store = new ModFileStore(Path.Combine(folder.Path, "Mod Files"));
        File.WriteAllText(Path.Combine(folder.Path, "secret.txt"), "secret");

        Assert.Null(store.PathOf("../secret.txt"));
        Assert.Null(store.PathOf("secret.txt"));
    }

    [Fact]
    public void A_carried_file_whose_content_does_not_give_its_name_is_left_out()
    {
        using var folder = new TemporaryFolder();
        var store = new ModFileStore(folder.Path);
        byte[] content = Encoding.UTF8.GetBytes("video");
        string name = ModFileStore.NameFor(content, "Intro.webm");
        string wrongName = ModFileStore.NameFor(Encoding.UTF8.GetBytes("other"), "Intro.webm");

        int kept = store.KeepCarried(new Dictionary<string, byte[]> { [name] = content, [wrongName] = content, ["../x.png"] = content });

        Assert.Equal(1, kept);
        Assert.NotNull(store.PathOf(name));
        Assert.Null(store.PathOf(wrongName));
    }

    // ---- File rules on export ----

    [Fact]
    public void File_rules_write_into_the_folder_that_holds_GameData()
    {
        using var folder = new TemporaryFolder();
        var store = new ModFileStore(Path.Combine(folder.Path, "store"));
        string stored = store.Keep(Encoding.UTF8.GetBytes("logo"), "Logo.png");
        string exported = Path.Combine(folder.Path, "Mods", "YSM", "GameData", "Generated", "Gameplay", "Gfx", "Ammunition.ndf");
        string modFolder = FileAndTextRules.ModFolderOf(exported)!;

        FileAndTextRules.Outcome outcome = FileAndTextRules.WriteFiles(
            FileAndTextRules.FilesOf([FileRule("GameData/Assets/2D/Logo.png", stored), FileRule("../outside.png", stored)]),
            modFolder, store, "bad {path}", "missing {path}");

        Assert.Equal(Path.Combine(folder.Path, "Mods", "YSM"), modFolder);
        Assert.Equal(1, outcome.Written);
        Assert.Equal(["bad ../outside.png"], outcome.Problems);
        Assert.Equal("logo", File.ReadAllText(Path.Combine(modFolder, "GameData", "Assets", "2D", "Logo.png")));
    }

    [Fact]
    public void The_last_File_rule_to_name_a_place_wins()
    {
        List<FileAndTextRules.CarriedFile> files = FileAndTextRules.FilesOf([FileRule("GameData/A.png", "first"), FileRule("GameData\\a.png", "second")]);

        Assert.Equal("second", Assert.Single(files).StoredName);
    }

    [Fact]
    public void A_file_already_in_the_mod_stays_when_its_rule_is_switched_off()
    {
        using var folder = new TemporaryFolder();
        var store = new ModFileStore(Path.Combine(folder.Path, "store"));
        string logo = store.Keep(Encoding.UTF8.GetBytes("logo"), "Logo.png");
        string mod = Path.Combine(folder.Path, "Mod");
        FileAndTextRules.WriteFiles(FileAndTextRules.FilesOf([FileRule("GameData/Logo.png", logo)]), mod, store, "", "");

        List<FileAndTextRules.CarriedFile> files = FileAndTextRules.FilesOf([FileRule("GameData/Logo.png", logo, enabled: false)]);
        FileAndTextRules.WriteFiles(files, mod, store, "", "");

        Assert.Empty(files);
        Assert.Equal("logo", File.ReadAllText(Path.Combine(mod, "GameData", "Logo.png")));
    }

    [Fact]
    public void A_group_switched_off_at_the_end_of_one_rule_set_does_not_leave_out_the_next_ones_files()
    {
        using var folder = new TemporaryFolder();
        var store = new ModFileStore(Path.Combine(folder.Path, "store"));
        string logo = store.Keep(Encoding.UTF8.GetBytes("logo"), "Logo.png");
        string mod = Path.Combine(folder.Path, "Mod");
        JsonObject offGroup = RuleLoading.NewRule("Group");
        offGroup["Enabled"] = false;
        // rule set X ends with a group switched off; rule set Y has a File rule of its own, under no group
        JsonNode?[] first = [offGroup, TextRule("A;1")];
        JsonNode?[] second = [FileRule("GameData/Logo.png", logo)];
        List<JsonNode?> rules = [.. FileAndTextRules.EachInItsOwnRuleSet([first, second])];
        FileAndTextRules.WriteFiles(FileAndTextRules.FilesOf(rules), mod, store, "", "");

        Assert.True(File.Exists(Path.Combine(mod, "GameData", "Logo.png")));
        // the rules under the group are still off: as copies switched off, without the heading
        Assert.DoesNotContain(rules, rule => (string?)rule?["Kind"] == "Group");
        Assert.False((bool)rules.Single(rule => (string?)rule?["Kind"] == "Text")!["Enabled"]!);
    }

    // ---- mod files ----

    [Fact]
    public void An_exported_mod_carries_its_files_and_an_import_gets_them_back()
    {
        using var folder = new TemporaryFolder();
        var store = new ModFileStore(Path.Combine(folder.Path, "store"));
        string stored = store.Keep(Encoding.UTF8.GetBytes("logo"), "Logo.png");
        var mod = new Mod { Name = "YSM" };
        mod.Presets.Add(new ModPreset { Name = "YSM Files", Rules = [FileRule("GameData/Logo.png", stored), TextRule("A;1")] });

        string text = ModFiles.ExportText(mod, isOn: true, store);
        Mod read = ModFiles.Read(text, "YSM.mod.json")[0];
        var otherStore = new ModFileStore(Path.Combine(folder.Path, "other"));
        otherStore.KeepCarried(read.CarriedFiles);

        Assert.Equal("logo", Encoding.UTF8.GetString(otherStore.Read(stored)!));
        Assert.Equal("Text", (string?)read.Presets[0].Rules[1]!["Kind"]);
        Assert.True((bool)read.Presets[0].Rules[1]!["Enabled"]!);
    }

    [Fact]
    public void The_mod_as_stored_leaves_the_files_in_the_store()
    {
        var mod = new Mod { Name = "YSM" };
        mod.Presets.Add(new ModPreset { Name = "YSM Files", Rules = [FileRule("GameData/Logo.png", "0123456789abcdef0123456789abcdef.png")] });

        Assert.DoesNotContain("\"Files\"", ModFiles.StoredText(mod));
    }
}
