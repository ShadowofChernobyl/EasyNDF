using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// The mod's division changes as rules: run on made-up division files, the files they give must read back as the
/// Armory shows the divisions.
/// </summary>
public class DivisionChangeRulesTests
{
    private const string Armored = "Descriptor_Deck_Division_US_Armored_multi";

    private const string DeckNumbers = """
        unnamed TDeckSerializerEntries
        (
            DivisionIds = MAP
            [
                (Descriptor_Deck_Division_US_Armored_multi, 12),
            ]
            UnitIds = MAP
            [
                ($/GFX/Unit/Descriptor_Unit_Tank_US, 3),
            ]
        )
        """;

    private static Dictionary<string, string> Files() => new()
    {
        ["Generated/Gameplay/Decks/Divisions.ndf"] = DivisionReaderTests.WarnoDivisions,
        ["Generated/Gameplay/Decks/DivisionRules.ndf"] = DivisionReaderTests.WarnoDivisionRules,
        ["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"] = DivisionReaderTests.WarnoCosts,
        ["Generated/Gameplay/Decks/DeckSerializer.ndf"] = DeckNumbers,
        ["Generated/UserInterface/Textures/DivisionTextures.ndf"] = DivisionReaderTests.WarnoEmblems,
        ["Generated/Gameplay/Decks/Decks.ndf"] = BattlegroupTests.Decks,
        ["Generated/Gameplay/Decks/DeckPacks.ndf"] = BattlegroupTests.DeckPacks,
    };

    private static List<Division> Read(string zip, IReadOnlyList<ChangedFile>? changed = null)
    {
        using var files = new GameDataFiles(zip, null, changed);
        return DivisionReader.Read(files, GameTexts.FromPairs([]), []);
    }

    /// <summary>Runs the Armory presets' rules on each file, as the export does, and gives back the files it changed.</summary>
    private static List<ChangedFile> RunOnEachFile(JsonArray rules, Dictionary<string, string> files)
    {
        var changed = new List<ChangedFile>();
        foreach ((string path, string text) in files)
        {
            JsonArray forFile = DivisionChangeRules.ForFile(rules, Path.GetFileName(path));
            if (forFile.Count == 0) continue;
            var engine = new NdfEngine();
            engine.Load(text);
            RunResult result = engine.Run(RuleLoading.LoadForEngine(forFile), new RunOptions());
            Assert.Equal("", result.ParseError);
            Assert.All(result.Stats, rule => Assert.Equal(0, rule.Skipped));
            changed.Add(new ChangedFile("C:/Mod/GameData/" + path, result.Text!));
        }
        return changed;
    }

    private static string Describe(Division division) =>
        division.Descriptor + " " + division.ActivationPoints + " " + division.Country + " " + division.Side + " [" + string.Join(" ", division.Tags) + "] "
        + string.Join("; ", division.Costs.OrderBy(cost => cost.Key).Select(cost => cost.Key + "=" + string.Join(",", cost.Value))) + " | "
        + string.Join("; ", division.Units.Select(unit => unit.Unit + " " + unit.Cards + "x" + unit.UnitsPerCard + " " + unit.AvailableWithoutTransport
            + " [" + string.Join(",", unit.VeterancyMultipliers) + "] (" + string.Join(",", unit.Transports) + ")"));

    [Fact]
    public void Every_change_made_in_the_Armory_reaches_the_files_as_the_Armory_shows_it()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        List<Division> game = Read(zip);
        var mod = new Mod { Name = "Test" };

        // a game division changed in every way
        DivisionChanges.AddUnits(mod, game, Armored, ["Descriptor_Unit_Jeep_US"]);
        DivisionChanges.RemoveUnit(mod, game, Armored, "Descriptor_Unit_Rifles_US");
        DivisionUnit tank = game[0].Units[0];
        DivisionChanges.SetRule(mod, game, Armored, tank with
        {
            Cards = 8, VeterancyMultipliers = [1.0, 0.5, 0.25, 0.0], AvailableWithoutTransport = false, Transports = ["Descriptor_Unit_Truck_US"],
        });
        DivisionChanges.ChangeDetails(mod, game, Armored, change =>
        {
            change.ActivationPoints = 55;
            change.Costs = new() { ["Art"] = [1, 2, 2, 3, 3], ["Defense"] = [], ["Tanks"] = [1, 2] };
            change.Country = "UK";
            change.ListOrder = 7;
        });
        DivisionChanges.Rename(mod, game, Armored, "Iron Fist");
        // and a new one
        string added = DivisionChanges.AddNew(mod, game, "Test Brigade", game[0], ["Descriptor_Unit_Tank_US"], GameTitle.Warno);

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        List<Division> exported = Read(zip, RunOnEachFile(rules, Files()));
        List<Division> shown = DivisionChanges.Apply(game, mod);

        Division armored = exported.First(division => division.Descriptor == Armored);
        Assert.Equal(Describe(shown[0]), Describe(armored));
        Assert.Equal(DivisionChangeRules.NameToken(Armored), armored.NameToken);
        Assert.Equal(7.0, armored.ListOrder);

        Division brigade = exported.First(division => division.Descriptor == added);
        Assert.Equal(Describe(shown.First(division => division.Descriptor == added)), Describe(brigade));
        Assert.Equal(13, brigade.DeckNumber);
        Assert.Equal((NewDivisionStart.WarnoPlaceholderType, ""), (brigade.TypeToken, brigade.EmblemTexture));
        Assert.Equal(DivisionChangeRules.NameToken(added), brigade.NameToken);
        // the game's division keeps its own unit list
        Assert.Contains(armored.Units, unit => unit.Unit == "Descriptor_Unit_Jeep_US");
    }

    [Fact]
    public void An_official_battlegroup_s_cards_order_and_name_reach_the_files_with_the_cards_the_game_lacks()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        List<Division> game = Read(zip);
        var mod = new Mod { Name = "Test" };
        var veteranTank = new BattlegroupCard("Descriptor_Unit_Tank_US", 2, null);
        var rifles = new BattlegroupCard("Descriptor_Unit_Rifles_US", 0, "Descriptor_Unit_Humvee_US");
        var tank = new BattlegroupCard("Descriptor_Unit_Tank_US", 1, null);
        DivisionChanges.ChangeDetails(mod, game, Armored, change =>
        {
            change.BattlegroupCards = [veteranTank, rifles, tank, veteranTank];
            change.BattlegroupName = "Iron Fist";
        });

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        List<ChangedFile> changed = RunOnEachFile(rules, Files());
        Battlegroup exported = Read(zip, changed).First(division => division.Descriptor == Armored).Battlegroup!;

        Assert.Equal([veteranTank, rifles, tank, veteranTank], exported.Cards);
        string token = DivisionChangeRules.TextToken(Armored, "battlegroup");
        Assert.Equal(token, exported.NameToken);
        // the one card the game lacks is added once, though the battlegroup has it twice
        Assert.Single(rules, rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.FileKey] == "DeckPacks.ndf");
        // the name goes into UNITS.csv under the deck's token
        Assert.Contains("\"" + token + "\";\"Iron Fist\"", DivisionExtras.WithNames("", rules).Text);
    }

    [Fact]
    public void A_new_division_gets_an_official_battlegroup_once_it_has_cards()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        List<Division> game = Read(zip);
        var mod = new Mod { Name = "Test" };
        string added = DivisionChanges.AddNew(mod, game, "Test Brigade", game[0], ["Descriptor_Unit_Tank_US"], GameTitle.Warno);
        Assert.DoesNotContain(DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit),
            rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.FileKey] == "Decks.ndf");

        var card = new BattlegroupCard("Descriptor_Unit_Tank_US", 0, null);
        DivisionChanges.ChangeDetails(mod, game, added, change => change.BattlegroupCards = [card, card]);
        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Division brigade = Read(zip, RunOnEachFile(rules, Files())).First(division => division.Descriptor == added);

        Assert.NotNull(brigade.Battlegroup);
        Assert.Equal(ModDivision.DeckDescriptorOf(added), brigade.Battlegroup.Descriptor);
        Assert.Equal([card, card], brigade.Battlegroup.Cards);
        // its name, made from the division's, goes into UNITS.csv under the deck's token
        Assert.Contains("\"" + DivisionChangeRules.TextToken(added, "battlegroup") + "\";", DivisionExtras.WithNames("", rules).Text);
    }

    [Fact]
    public void A_battlegroup_change_is_kept_in_the_mod_file_and_let_go_of_as_one()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        List<Division> game = Read(zip);
        var mod = new Mod { Name = "Test" };
        var card = new BattlegroupCard("Descriptor_Unit_Rifles_US", 2, "Descriptor_Unit_Bradley_US");
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.BattlegroupCards = [card]);

        ModDivision read = ModDivision.FromJson(mod.Divisions[0].ToJson());
        Assert.Equal([card], read.BattlegroupCards);
        Assert.Equal("", read.BattlegroupName);

        read.LetGoOf("Battlegroup", game[0]);
        Assert.True(read.ChangesNothing);
        // the game's own cards given back let the change go
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.BattlegroupCards = [.. game[0].Battlegroup!.Cards]);
        Assert.Empty(mod.Divisions);
    }

    [Fact]
    public void A_division_taken_out_stays_out_when_its_nation_and_type_change_too()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        List<Division> game = Read(zip);
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change =>
        {
            change.Country = "UK";
            change.TypeToken = "MECHANIZED";
        });
        DivisionChanges.Delete(mod, Armored);

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Division armored = Read(zip, RunOnEachFile(rules, Files())).First(division => division.Descriptor == Armored);

        Assert.DoesNotContain("DEFAULT", armored.Tags);
        Assert.Contains("MECHANIZED", armored.Tags);
        Assert.DoesNotContain("ARMORED", armored.Tags);
    }

    [Fact]
    public void Every_rule_of_a_new_division_with_its_own_picture_changes_something()
    {
        List<Division> game = Read(WriteZip(out TemporaryFolder temp));
        using (temp)
        {
            var mod = new Mod { Name = "Test" };
            string added = DivisionChanges.AddNew(mod, game, "Test Brigade", game[0], ["Descriptor_Unit_Tank_US"], GameTitle.Warno);
            DivisionChanges.ChangeDetails(mod, game, added, change => change.EmblemFile = "C:/Pictures/Brigade.png");

            JsonArray rules = DivisionChangeRules.ForFile(DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit), "Divisions.ndf");
            var engine = new NdfEngine();
            engine.Load(Files()["Generated/Gameplay/Decks/Divisions.ndf"]);
            RunResult result = engine.Run(RuleLoading.LoadForEngine(rules), new RunOptions());

            // a rule that changes nothing is listed as one that may not have run
            Assert.All(result.Stats.Where(rule => rule.Kind != "Group"), rule => Assert.True(rule.Edits > 0, rule.Name + " changed nothing"));
        }
    }

    private static string WriteZip(out TemporaryFolder temp)
    {
        temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        return zip;
    }

    [Fact]
    public void A_type_changed_alone_changes_the_type_tag_too()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        List<Division> game = Read(zip);
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.TypeToken = "MECHANIZED");

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Division armored = Read(zip, RunOnEachFile(rules, Files())).First(division => division.Descriptor == Armored);

        Assert.Equal(["DEFAULT", "US", "NATO", "MECHANIZED"], armored.Tags);
    }

    [Fact]
    public void Each_rule_names_its_file_and_is_locked_to_the_Armory()
    {
        var game = new List<Division>
        {
            new() { Descriptor = Armored, Name = "Armored", RuleName = Armored + "_Rule", IsForMultiplayer = true, ActivationPoints = 50 },
        };
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.ActivationPoints = 60);

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);

        Assert.Equal(["Group", "Edit"], rules.Select(rule => (string?)rule!["Kind"]));
        Assert.All(rules, rule => Assert.True(DivisionChangeRules.IsFromArmory(rule)));
        Assert.Equal("Divisions.ndf", (string?)rules[1]![DivisionChangeRules.ArmoryKey]![DivisionChangeRules.FileKey]);
        Assert.Empty(DivisionChangeRules.ForFile(rules, "DivisionRules.ndf"));
    }

    [Fact]
    public void Names_go_into_a_new_UNITS_csv_beside_its_other_lines_as_the_game_writes_them()
    {
        var game = new List<Division>
        {
            new() { Descriptor = Armored, Name = "Armored", RuleName = Armored + "_Rule", IsForMultiplayer = true, EmblemTexture = "T" },
        };
        var mod = new Mod { Name = "Test" };
        DivisionChanges.Rename(mod, game, Armored, "Say \"Hi\"; now");
        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        string line = "\"" + DivisionChangeRules.NameToken(Armored) + "\";\"Say ''Hi'', now\"";

        (string text, int count) = DivisionExtras.WithNames("\"TOKEN\";\"REFTEXT\"\r\n\"OTHER\";\"Kept\"\r\n" + line.Replace("now", "then") + "\r\n", rules);

        // the line of the same token is replaced, the others kept
        Assert.Equal((1, "\"TOKEN\";\"REFTEXT\"\r\n\"OTHER\";\"Kept\"\r\n" + line + "\r\n"), (count, text));
        // an empty file gets the game's heading line
        Assert.Equal("\"TOKEN\";\"REFTEXT\"\r\n" + line + "\r\n", DivisionExtras.WithNames("", rules).Text);
        Assert.Contains(rules, DivisionExtras.NeedsNamesFile);
    }

    [Fact]
    public void Summary_and_history_texts_go_into_UNITS_csv_under_tokens_of_their_own()
    {
        var game = new List<Division>
        {
            new() { Descriptor = Armored, Name = "Armored", RuleName = Armored + "_Rule", IsForMultiplayer = true, EmblemTexture = "T", SummaryToken = "SUMUS3AD", Summary = "The game's" },
        };
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.Summary = "Mine");
        string added = DivisionChanges.AddNew(mod, game, "Test Brigade", game[0], [], GameTitle.Warno);

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        string csv = DivisionExtras.WithNames("", rules).Text;

        // the game division's summary under a token of its own, the game's left as it is
        Assert.Contains("\"" + DivisionChangeRules.TextToken(Armored, "summary") + "\";\"Mine\"", csv);
        Assert.DoesNotContain("SUMUS3AD", csv);
        // a new division's summary is its name, its history empty, and neither is the division's it was made from
        Assert.Contains("\"" + DivisionChangeRules.TextToken(added, "summary") + "\";\"Test Brigade\"", csv);
        Assert.Contains("\"" + DivisionChangeRules.TextToken(added, "history") + "\";\"\"", csv);
        Assert.Equal("", DivisionChanges.Apply(game, mod).First(division => division.Descriptor == added).History);
        // the game's text given again lets the change go
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.Summary = "The game's");
        Assert.Null(DivisionChanges.EntryOf(mod, Armored));
    }

    [Fact]
    public void Pictures_are_copied_into_the_export_folders_GameData()
    {
        using var temp = new TemporaryFolder();
        string gameData = Path.Combine(temp.Path, "Export", "GameData");
        string picture = Path.Combine(temp.Path, "mine.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        var game = new List<Division>
        {
            new() { Descriptor = Armored, Name = "Armored", RuleName = Armored + "_Rule", IsForMultiplayer = true, EmblemTexture = "T" },
        };
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.EmblemFile = picture);
        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);

        string divisionsFile = Path.Combine(gameData, "Generated", "Gameplay", "Decks", "Divisions.ndf");
        DivisionExtras.Outcome outcome = DivisionExtras.CopyPictures(rules, divisionsFile, [temp.Path], "no {path}", "missing {path}");

        Assert.Equal((1, "", 1), (outcome.Pictures, outcome.Problem, outcome.Emblems));
        Assert.True(File.Exists(Path.Combine(gameData, "Assets", "2D", "Interface", "UseOutGame", "Division", "Emblem", "US_Armored.png")));
        // outside a GameData folder, nothing is copied, and the problem says where
        Assert.Equal("no C:/Elsewhere/Divisions.ndf", DivisionExtras.CopyPictures(rules, "C:/Elsewhere/Divisions.ndf", [temp.Path], "no {path}", "missing {path}").Problem);
    }

    [Theory]
    [InlineData(@"E:\Mods\Test Mod\GameData\Generated\Gameplay\Decks\DivisionCostMatrix.ndf", true)]
    [InlineData(@"E:\Mods\Test Mod\GameData\Gameplay\Decks\DivisionCostMatrix.ndf", false)]
    [InlineData(@"C:\Mods\Mod.zip > GameData/Gameplay/Decks/DivisionCostMatrix.ndf", false)]
    [InlineData(@"C:\Desktop\DivisionCostMatrix.ndf", true)]
    public void The_Armory_writes_only_to_a_game_folders_Generated_files(string source, bool writes)
    {
        // Steel Division 2 has a short DivisionCostMatrix.ndf outside Generated: a new division's costs added to it too were declared twice
        var entry = new EasyNdf.Data.FileList.FileEntry { Name = "DivisionCostMatrix.ndf", Source = source };
        Assert.Equal(writes, DivisionChangeRules.WritesTo(entry));
    }

    [Fact]
    public void No_unit_is_added_to_a_Steel_Division_2_DLC_division()
    {
        // the game stops on a pack a mod adds to a DLC division: its DLC is the base game's, not the division's
        Assert.True(SteelDivisionDivisions.IsInDlc("Descriptor_Deck_Division_US_101st_Airborne_multi"));
        Assert.False(SteelDivisionDivisions.IsInDlc("Descriptor_Deck_Division_SOV_2CCharGd_multi"));
        var mod = new Mod();
        Assert.Equal(0, DivisionChanges.AddUnits(mod, [], "Descriptor_Deck_Division_US_101st_Airborne_multi", ["Descriptor_Unit_AB_Commander_UK"]));
        Assert.Empty(mod.Divisions);
    }

    [Fact]
    public void A_unit_added_to_a_division_comes_as_the_game_most_often_has_it_and_a_copy_as_its_model()
    {
        DivisionUnit Rule(string unit, int perCard, params string[] transports) =>
            new() { Unit = unit, Cards = 1, UnitsPerCardByPhase = [perCard, 0, 0], VeterancyMultipliers = [0, 0, 1.0], AvailableWithoutTransport = true, Transports = transports };
        Division Of(string name, params DivisionUnit[] units) => new() { Descriptor = name, Name = name, IsForMultiplayer = true, Units = units };
        List<Division> game =
        [
            Of("A", Rule("Descriptor_Unit_Commander_UK", 1, "Descriptor_Unit_Jeep_UK", "Descriptor_Unit_Truck_CMP")),
            Of("B", Rule("Descriptor_Unit_Commander_UK", 1, "Descriptor_Unit_Jeep_UK")),
            Of("C", Rule("Descriptor_Unit_Engineer_Ldr_US", 3, "Descriptor_Unit_Jeep_US")),
        ];
        // as common either way: the one with fewer transports
        Assert.Equal(["Descriptor_Unit_Jeep_UK"], DivisionChanges.RuleFor("Descriptor_Unit_Commander_UK", game).Transports);
        var mod = new Mod { Units = [new() { Descriptor = "Descriptor_Unit_EZ_Test_US", MadeFrom = "Descriptor_Unit_Engineer_Ldr_US" }] };
        DivisionUnit copy = DivisionChanges.RuleFor("Descriptor_Unit_EZ_Test_US", game, mod);
        Assert.Equal(("Descriptor_Unit_EZ_Test_US", 3), (copy.Unit, copy.UnitsPerCardByPhase[0]));
        // in no division, and no model: one unit in the first phase, as the game's commonest
        Assert.Equal([1, 0, 0], DivisionChanges.RuleFor("Descriptor_Unit_Nobody", game).UnitsPerCardByPhase);
    }

    [Fact]
    public void The_export_folder_column_shows_the_mod_and_the_last_folder()
    {
        Assert.Equal("Test Mod › Decks", EasyNdf.Data.FileList.FileListView.FolderName(@"E:\Steel Division 2\Mods\Test Mod\GameData\Generated\Gameplay\Decks\"));
        Assert.Equal("Test Mod", EasyNdf.Data.FileList.FileListView.FolderName(@"E:\Steel Division 2\Mods\Test Mod\GameData"));
        Assert.Equal("Test Mod › Gfx", EasyNdf.Data.FileList.FileListView.FolderName(@"E:\Steel Division 2\Mods\Test Mod\CommonData\Gameplay\Gfx"));
        Assert.Equal("Out", EasyNdf.Data.FileList.FileListView.FolderName(@"C:\Out"));
        Assert.Equal(@"C:\", EasyNdf.Data.FileList.FileListView.FolderName(@"C:\"));
        // the folder the file goes into: its export folder and the folders it was added from
        var row = new EasyNdf.Data.FileList.FileEntry { Name = "Divisions.ndf", Folder = "Gameplay/Decks", ExportFolder = @"E:\Mods\Test Mod\GameData\Generated" };
        Assert.Equal(@"E:\Mods\Test Mod\GameData\Generated\Gameplay\Decks", EasyNdf.Data.FileList.FileListView.FileFolderOf(row));
    }
}