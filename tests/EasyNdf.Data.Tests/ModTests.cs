using System.Text.Json.Nodes;

namespace EasyNdf.Data.Tests;

public class ModTests
{
    private const string SampleMod = """
        {
          "ModName": "Test mod",
          "FormatVersion": 1,
          "Watermark": { "on": true, "author": "Mod\nder", "ver": "1.4", "game": "9" },
          "Presets": [
            { "PresetName": "Test (Ammunition.ndf)", "Rules": [
              { "Name": "Double", "Kind": "Edit", "Enabled": true, "LogicGate": "All", "Conditions": [],
                "Actions": [ { "Target": "Damage", "Operator": "Multiply [int, float]", "Value": "2", "ManualOverride": false } ] } ] },
            { "PresetName": "", "Rules": [] },
            { "PresetName": "Test (Ammunition.ndf)", "Rules": [] }
          ]
        }
        """;

    [Fact]
    public void Reading_a_mod_file_names_presets_and_reads_the_watermark()
    {
        Mod mod = ModFiles.Read(SampleMod, "whatever.mod.json").Single();

        Assert.Equal("Test mod", mod.Name);
        Assert.Equal(["Test (Ammunition.ndf)", "Test mod 2", "Test (Ammunition.ndf) (2)"], mod.Presets.Select(p => p.Name));
        Assert.Equal(new ModWatermark(true, "Mod der", "1.4", "9", Ask: true), mod.Watermark);
    }

    [Fact]
    public void A_mod_file_without_a_name_is_named_after_the_file()
    {
        Mod mod = ModFiles.Read("""{ "Presets": [] }""", "My Stuff.mod.json").Single();
        Assert.Equal("My Stuff", mod.Name);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("""{ "Presets": [ {} ] }""", "rule set 1 in it has no \"Rules\" list")]
    [InlineData("""{ "Rules": [] }""", "a rule set, not a mod")]
    [InlineData("""{ "Something": 1 }""", "not a mod: a mod file holds a \"Presets\" list")]
    public void Files_that_are_not_mods_say_why(string text, string reason)
    {
        var error = Assert.Throws<ModFileException>(() => ModFiles.Read(text, "x.json"));
        Assert.StartsWith(reason, error.Message);
    }

    [Fact]
    public void Export_names_unnamed_rows_and_writes_like_the_html_app()
    {
        Mod mod = ModFiles.Read(SampleMod, "x.json").Single();
        JsonNode exported = JsonNode.Parse(ModFiles.ExportText(mod))!;

        Assert.Equal("Damage Multiply 2", (string?)exported["Presets"]![0]!["Rules"]![0]!["Actions"]![0]!["Name"]);
        Assert.Equal("", (string?)JsonNode.Parse(ModFiles.StoredText(mod))!["Presets"]![0]!["Rules"]![0]!["Actions"]![0]!["Name"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_exported_mod_keeps_whether_it_was_on(bool isOn)
    {
        Mod mod = ModFiles.Read(SampleMod, "x.json").Single();
        Mod imported = ModFiles.Read(ModFiles.ExportText(mod, isOn), "x.json").Single();
        Assert.Equal(isOn, imported.WasOn);
    }

    [Fact]
    public void A_mod_file_without_its_switch_says_nothing_about_it()
    {
        Assert.Null(ModFiles.Read(SampleMod, "x.json").Single().WasOn);
    }

    [Fact]
    public void Json_is_written_like_javascripts_json_stringify()
    {
        var json = JsonNode.Parse("""{"ModName":"Mod é 中","FormatVersion":1,"Presets":[{"PresetName":"P <1> & \"q\"","Rules":[]}],"Empty":{},"Tab":"a\tb"}""");

        // The text below is what JSON.stringify(value, null, 2) gives in the HTML app.
        Assert.Equal("{\n  \"ModName\": \"Mod é 中\",\n  \"FormatVersion\": 1,\n  \"Presets\": [\n    {\n      \"PresetName\": \"P <1> & \\\"q\\\"\",\n      \"Rules\": []\n    }\n  ],\n  \"Empty\": {},\n  \"Tab\": \"a\\tb\"\n}",
            JsonFiles.ToText(json).Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_backup_restores_every_mod_with_its_watermark()
    {
        Mod first = ModFiles.Read(SampleMod, "x.json").Single();
        var second = new Mod { Name = "Other", Presets = [new ModPreset { Name = "Only" }] };

        List<Mod> restored = ModFiles.Read(ModFiles.BackupText([first, second]), "EasyNDF mods backup.json");

        Assert.Equal(["Test mod", "Other"], restored.Select(m => m.Name));
        Assert.Equal(first.Watermark, restored[0].Watermark);
        Assert.Null(restored[1].Watermark);
        Assert.Equal(first.Presets.Select(p => p.Name), restored[0].Presets.Select(p => p.Name));
    }

    /// <summary>Every mod file under a Mods folder, by its path in it ("WTO\WTO.mod.json").</summary>
    private static List<string> ModFilesIn(string folder) =>
        [.. Directory.GetFiles(folder, "*.mod.json", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(folder, path)).Order(StringComparer.Ordinal)];

    [Fact]
    public void A_mod_file_kept_loose_moves_into_a_folder_of_its_own()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(Path.Combine(folder.Path, "Test mod.mod.json"), SampleMod);

        var store = new ModStore(folder.Path);

        Assert.Equal(["Test mod"], store.Mods.Select(m => m.Name));
        Assert.Equal([@"Test mod\Test mod.mod.json"], ModFilesIn(folder.Path));
    }

    [Fact]
    public void A_renamed_mods_folder_moves_with_the_files_in_it()
    {
        using var folder = new TemporaryFolder();
        var store = new ModStore(folder.Path);
        store.Add(new Mod { Name = "WTO" });
        File.WriteAllText(Path.Combine(folder.Path, "WTO", "Carried.png"), "png");

        Mod mod = store.Find("WTO")!;
        mod.Name = "WTO Dev";
        store.Save(mod);

        Assert.Equal([@"WTO Dev\WTO Dev.mod.json"], ModFilesIn(folder.Path));
        Assert.True(File.Exists(Path.Combine(folder.Path, "WTO Dev", "Carried.png")));
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "WTO")));
    }

    [Fact]
    public void The_store_keeps_mods_between_sessions_and_follows_renames()
    {
        using var folder = new TemporaryFolder();
        var store = new ModStore(folder.Path);
        store.AddDefaultMods(SampleMod);
        Mod mod = store.Find("Test mod")!;
        mod.Name = "Renamed: mod?";
        store.Save(mod);

        var again = new ModStore(folder.Path);

        Assert.Equal(["My mod", "Renamed: mod?"], again.Mods.Select(m => m.Name));
        Assert.Equal([@"My mod\My mod.mod.json", @"Renamed_ mod_\Renamed_ mod_.mod.json"], ModFilesIn(folder.Path));
        Assert.Equal(3, again.Find("Renamed: mod?")!.Presets.Count);
        Assert.False(again.AddDefaultMods(SampleMod.Replace("Test mod", "Renamed: mod?")));
    }

    [Fact]
    public void Importing_a_mod_that_exists_asks_and_can_keep_both()
    {
        using var folder = new TemporaryFolder();
        var store = new ModStore(folder.Path);
        store.Add(ModFiles.Read(SampleMod, "x.json").Single());

        ImportResult kept = store.Import(ModFiles.Read(SampleMod, "x.json"), (_, _) => ImportChoice.KeepBoth);
        ImportResult skipped = store.Import(ModFiles.Read(SampleMod, "x.json"), (_, _) => ImportChoice.Skip);

        Assert.Equal(["Test mod", "Test mod (2)"], store.Mods.Select(m => m.Name));
        Assert.Equal(3, kept.Added);
        Assert.Equal(3, skipped.Skipped);
    }

    [Fact]
    public void Two_names_that_give_the_same_file_name_get_two_files()
    {
        using var folder = new TemporaryFolder();
        var store = new ModStore(folder.Path);
        store.Add(new Mod { Name = "A?" });
        store.Add(new Mod { Name = "A*" });

        Assert.Equal([@"A_ (2)\A_ (2).mod.json", @"A_\A_.mod.json"], ModFilesIn(folder.Path));
        Assert.Equal(["A*", "A?"], new ModStore(folder.Path).Mods.Select(m => m.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_mod_renamed_from_a_second_copys_name_leaves_the_numbered_file_for_its_own_name()
    {
        using var folder = new TemporaryFolder();
        var store = new ModStore(folder.Path);
        store.Add(new Mod { Name = "WTO" });
        store.Add(new Mod { Name = "WTO (2)" });
        store.Delete(store.Find("WTO")!);

        Mod second = store.Find("WTO (2)")!;
        second.Name = "WTO";
        store.Save(second);
        Assert.Equal([@"WTO\WTO.mod.json"], ModFilesIn(folder.Path));
        // only the case changes: still the same file, renamed
        second.Name = "wto";
        store.Save(second);

        Assert.Equal([@"wto\wto.mod.json"], ModFilesIn(folder.Path));
        Assert.Equal(["wto"], new ModStore(folder.Path).Mods.Select(m => m.Name));
    }

    [Fact]
    public void A_mod_whose_file_name_another_file_has_keeps_its_numbered_file()
    {
        using var folder = new TemporaryFolder();
        var store = new ModStore(folder.Path);
        store.Add(new Mod { Name = "A?" });
        store.Add(new Mod { Name = "A*" });

        store.Save(store.Find("A*")!);
        store.Save(store.Find("A?")!);

        Assert.Equal([@"A_ (2)\A_ (2).mod.json", @"A_\A_.mod.json"], ModFilesIn(folder.Path));
        Assert.Equal(["A*", "A?"], new ModStore(folder.Path).Mods.Select(m => m.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Undo_puts_mods_back_deleting_the_ones_that_were_not_there()
    {
        using var folder = new TemporaryFolder();
        var store = new ModStore(folder.Path);
        store.Add(new Mod { Name = "Kept", Presets = [new ModPreset { Name = "P" }] });
        List<Mod> before = [.. store.Mods.Select(m => m.DeepCopy())];
        // a copy shares nothing with its mod
        before[0].Presets[0].Rules.Add(1);
        Assert.Empty(store.Find("Kept")!.Presets[0].Rules);
        before[0].Presets[0].Rules.Clear();

        store.Find("Kept")!.Presets.Add(new ModPreset { Name = "Added later" });
        store.Save(store.Find("Kept")!);
        store.Add(new Mod { Name = "New" });
        store.Restore(before);

        Assert.Equal(["Kept"], store.Mods.Select(m => m.Name));
        Assert.Equal(["P"], store.Find("Kept")!.Presets.Select(p => p.Name));
        Assert.Equal([@"Kept\Kept.mod.json"], ModFilesIn(folder.Path));
        Assert.Equal(["P"], new ModStore(folder.Path).Mods.Single().Presets.Select(p => p.Name));
    }

    [Fact]
    public void Json_written_without_shared_buffers_is_written_as_before()
    {
        JsonNode value = new JsonArray(new JsonObject { ["Name"] = "É \"one\" <b> 中", ["Value"] = 1.50, ["List"] = new JsonArray(1, "two", null, true) });
        Assert.Equal(value.ToJsonString(), JsonFiles.TextOf(value));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(value.ToJsonString()), JsonFiles.Utf8Of(value));
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IndentSize = 2, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        Assert.Equal(value.ToJsonString(options), JsonFiles.ToText(value));
    }

    [Fact]
    public void A_rule_set_put_away_keeps_its_counts_and_text_and_is_read_without_being_unpacked()
    {
        var preset = new ModPreset
        {
            Name = "Small",
            Rules = [new JsonObject { ["Kind"] = "Group", ["Name"] = "G" }, new JsonObject { ["Kind"] = "Edit", ["Name"] = "É \"one\"" }],
        };
        string text = preset.Rules.ToJsonString();
        int version = preset.RulesVersion;

        // put away whatever its size
        preset.PackRules();
        Assert.True(preset.IsPacked);
        Assert.Equal(2, preset.RuleCount);
        Assert.Equal(1, preset.RuleCountWithoutGroups);
        Assert.Equal(text, preset.RulesText());
        Assert.Equal("G", (string?)preset.RulesToRead()[0]!["Name"]);
        Assert.True(preset.IsPacked);

        // unpacked, the same text and the same version: the rules were not swapped
        Assert.Equal(2, preset.Rules.Count);
        Assert.Equal(text, preset.RulesText());
        Assert.Equal(version, preset.RulesVersion);
        preset.Rules = [];
        Assert.NotEqual(version, preset.RulesVersion);
    }
}
