using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// The units a mod adds, as rules: run on made-up game files, the files they give must read back as the Armory shows
/// the units (<see cref="ModUnit.Apply"/>).
/// </summary>
public class UnitChangeRulesTests
{
    private const string Tank = "Descriptor_Unit_Tank_US";

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
                ($/GFX/Unit/Descriptor_Unit_Truck_SOV, 41),
            ]
        )
        """;

    // laid out as the game's own: one texture bank, its pictures in a MAP
    private const string ButtonTextures = """
        UnitButtonTextureAdditionalBank is TBUCKToolAdditionalTextureBank
        (
            Textures = MAP
            [
                ("Texture_Button_Unit_Tank_US", MAP [( ~/ComponentState/Normal, TUIResourceTexture( FileName = "GameData:/Assets/2D/Interface/Common/UnitsIcons/NATO/Tank_US.png"))]),
            ]
        )
        """;

    private static Dictionary<string, string> Files()
    {
        Dictionary<string, string> files = UnitReaderTests.WarnoFiles();
        files["Generated/Gameplay/Decks/DeckSerializer.ndf"] = DeckNumbers;
        files["UserInterface/Textures/ButtonTexturesUnites.ndf"] = ButtonTextures;
        return files;
    }

    private static UnitCatalog Read(string zip, IReadOnlyList<ChangedFile>? changed = null)
    {
        using var files = new GameDataFiles(zip, null, changed);
        return UnitReader.Read(files, GameTitle.Warno, UnitReaderTests.WarnoTexts);
    }

    /// <summary>Runs the rules on each file, as the export does, and gives back the files they changed.</summary>
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

    /// <summary>What the Armory shows of a unit, but its name (the game's texts give it) and its picture.</summary>
    private static string Describe(Unit unit) =>
        string.Join(" ", unit.Descriptor, unit.Side, unit.Nation, unit.Factory, unit.Role, unit.Cost, unit.Strength, unit.Armor, unit.Speed, unit.RoadSpeed,
            unit.Optics, unit.Vision, unit.Stealth, unit.Fuel, unit.WeaponSet, unit.DeckNumber, "[" + string.Join(",", unit.Specialties) + "]",
            "[" + string.Join(",", unit.Weapons.Select(weapon => weapon.Name)) + "]");

    private static ModUnit NewTank(Mod mod, UnitCatalog game) => new()
    {
        Descriptor = ModUnit.NewDescriptor("Hunter tank", "UK", [.. game.Units.Select(unit => unit.Descriptor)]),
        MadeFrom = Tank,
        Name = "HUNTER",
        Nation = "UK",
        Side = "NATO",
        Role = "reco",
        DeckNumber = ModUnit.NextDeckNumber(game.Units, mod),
        Cost = 120, Strength = 7, FrontArmor = 4, SideArmor = 2, RearArmor = 1, TopArmor = 0,
        Speed = 80, RoadSpeed = 110, Fuel = 600, Optics = 1500, Vision = 3000.5, Stealth = 1.5,
    };

    [Fact]
    public void A_taken_descriptor_puts_the_number_on_the_EZ_and_its_picture_and_loadout_keep_it()
    {
        string first = ModUnit.NewDescriptor("Test Unit", "US", []);
        string second = ModUnit.NewDescriptor("Test Unit", "US", [first]);
        string third = ModUnit.NewDescriptor("Test Unit", "US", [first, second]);

        Assert.Equal("Descriptor_Unit_EZ_Test_Unit_US", first);
        Assert.Equal("Descriptor_Unit_EZ2_Test_Unit_US", second);
        Assert.Equal("Descriptor_Unit_EZ3_Test_Unit_US", third);
        Assert.Equal("Descriptor_Unit_EZ_M4_105_US", ModUnit.NewDescriptor("M4 105", "US", []));
        Assert.Equal(["Test_Unit_US", "EZ2_Test_Unit_US"], [ModUnit.ShortName(first), ModUnit.ShortName(second)]);
        Assert.Equal(["WeaponDescriptor_EZ_Test_Unit_US", "WeaponDescriptor_EZ2_Test_Unit_US"],
            [LoadoutRules.OwnLoadoutName(first), LoadoutRules.OwnLoadoutName(second)]);
    }

    [Fact]
    public void A_new_unit_reaches_the_files_as_the_Armory_shows_it_a_copy_of_its_model_unit_with_the_values_given()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        ModUnit hunter = NewTank(mod, game);
        mod.Units.Add(hunter);

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Unit tank = game.Units.First(unit => unit.Descriptor == Tank);
        Unit shown = hunter.Apply(tank);
        Unit written = exported.Units.Single(unit => unit.Descriptor == hunter.Descriptor);
        Assert.Equal("Descriptor_Unit_EZ_Hunter_tank_UK", hunter.Descriptor);
        Assert.Equal(42, hunter.DeckNumber);
        Assert.Equal(Describe(shown), Describe(written));
        // nothing of the copy is taken away: it keeps the weapons and specialties its model expects
        Assert.Equal(tank.Weapons.Select(weapon => weapon.Name), written.Weapons.Select(weapon => weapon.Name));
        Assert.Equal(tank.Specialties, written.Specialties);
        Assert.Equal(Describe(tank), Describe(exported.Units.First(unit => unit.Descriptor == Tank)));
        Assert.Equal(tank.PictureSource, written.PictureSource);
    }

    [Fact]
    public void A_duplicate_keeps_everything_of_the_unit_it_is_made_from_but_its_name()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        var copy = new ModUnit { Descriptor = ModUnit.NewDescriptor("Tank two", "US", []), MadeFrom = Tank, IsDuplicate = true, Name = "TANK TWO" };
        mod.Units.Add(copy);

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Unit written = exported.Units.Single(unit => unit.Descriptor == copy.Descriptor);
        Assert.Equal(Describe(copy.Apply(game.Units[0])), Describe(written));
        Assert.Equal(game.Units[0].Weapons.Count, written.Weapons.Count);
        Assert.Equal(game.Units[0].Specialties, written.Specialties);
        // its deck number was given when the rules were made
        Assert.Equal(42, copy.DeckNumber);
    }

    [Fact]
    public void A_card_picture_of_the_users_own_is_added_to_the_games_list_of_unit_pictures_and_copied_by_the_export()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        string picture = Path.Combine(temp.Path, "hunter.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        ModUnit hunter = NewTank(mod, game);
        hunter.PictureFile = picture;
        mod.Units.Add(hunter);

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Assert.Equal("GameData:/Assets/2D/Interface/Common/UnitsIcons/NATO/Hunter_tank_UK.png", exported.Units.Single(unit => unit.Descriptor == hunter.Descriptor).PictureSource);
        Assert.Equal(picture, hunter.Apply(game.Units[0]).PictureSource);
        string exportedFile = Path.Combine(temp.Path, "Export", "GameData", "UserInterface", "Textures", "ButtonTexturesUnites.ndf");
        Assert.Equal([UnitChangeRules.PicturesFile], DivisionExtras.FilesWithPictures(rules));
        DivisionExtras.Outcome outcome = DivisionExtras.CopyPictures(rules, exportedFile, [temp.Path], "no {path}", "missing {path}");
        Assert.Equal((1, "", 0), (outcome.Pictures, outcome.Problem, outcome.Emblems));
        Assert.True(File.Exists(Path.Combine(temp.Path, "Export", "GameData", "Assets", "2D", "Interface", "Common", "UnitsIcons", "NATO", "Hunter_tank_UK.png")));
    }

    [Fact]
    public void Values_not_given_stay_those_of_the_unit_it_is_made_from()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        var faster = new ModUnit { Descriptor = ModUnit.NewDescriptor("Fast tank", "US", []), MadeFrom = Tank, Name = "FAST TANK", Speed = 90 };
        mod.Units.Add(faster);

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Unit tank = game.Units.First(unit => unit.Descriptor == Tank);
        Unit written = exported.Units.Single(unit => unit.Descriptor == faster.Descriptor);
        Assert.Equal(Describe(faster.Apply(tank)), Describe(written));
        Assert.Equal((90.0, tank.Cost, tank.Armor, tank.Nation, tank.Role), (written.Speed, written.Cost, written.Armor, written.Nation, written.Role));
        // the copy's own name, its deck number and the speed: nothing else is set
        Assert.Equal(["ClassNameForDebug", "NameToken", "MaxSpeedInKmph"], rules[1]!["Actions"]!.AsArray().Select(action => (string?)action!["Target"]));
    }

    [Fact]
    public void A_new_units_name_goes_into_UNITS_csv_under_its_own_token()
    {
        var mod = new Mod { Name = "My mod" };
        var tank = new Unit { Descriptor = Tank, Name = "TANK", Side = "NATO", Nation = "US" };
        mod.Units.Add(new ModUnit { Descriptor = "Descriptor_Unit_EZ_Hunter_US", MadeFrom = Tank, Name = "HUNTER", DeckNumber = 5 });

        JsonArray rules = UnitChangeRules.For(mod, [tank], new TextCatalog());

        Assert.Contains(rules, DivisionExtras.NeedsNamesFile);
        Assert.Contains("\"" + UnitChangeRules.NameToken("Descriptor_Unit_EZ_Hunter_US") + "\";\"HUNTER\"", DivisionExtras.WithNames("", rules).Text);
    }

    [Fact]
    public void The_units_rules_go_in_the_Units_preset_and_deleting_one_takes_the_unit_out_of_the_mod_and_its_divisions()
    {
        var mod = new Mod { Name = "My mod" };
        var tank = new Unit { Descriptor = Tank, Name = "TANK", Side = "NATO", Nation = "US" };
        const string hunter = "Descriptor_Unit_EZ_Hunter_US";
        mod.Units.Add(new ModUnit { Descriptor = hunter, MadeFrom = Tank, Name = "HUNTER", DeckNumber = 5 });
        var division = new ModDivision { Descriptor = "Descriptor_Deck_Division_US_Armored_multi", StartsFrom = "Descriptor_Deck_Division_US_Armored_multi" };
        division.Added.Add(new DivisionUnit { Unit = hunter, Cards = 2 });
        mod.Divisions.Add(division);

        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: [tank]);

        ModPreset units = ArmoryPresets.Of(mod, ArmoryPart.Units)!;
        Assert.Equal(["HUNTER", "Clone", "Edit"], units.Rules.Select(rule => (string?)rule?["Kind"] == "Group" ? (string?)rule?["Name"] : (string?)rule?["Kind"]));
        Assert.All(units.Rules.Skip(1), rule => Assert.Equal(ArmoryPart.Units, DivisionChangeRules.PartOf(rule)));

        ArmoryPresets.TakeBack(mod, [], [units.Rules[1]]);

        Assert.Empty(mod.Units);
        Assert.Empty(mod.Divisions);
    }

    [Fact]
    public void Make_editable_keeps_every_rule_of_the_unit_by_hand_and_the_Armory_lets_go_of_it()
    {
        var mod = new Mod { Name = "My mod" };
        var tank = new Unit { Descriptor = Tank, Name = "TANK", Side = "NATO", Nation = "US" };
        mod.Units.Add(new ModUnit { Descriptor = "Descriptor_Unit_EZ_Hunter_US", MadeFrom = Tank, Name = "HUNTER", DeckNumber = 5 });
        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: [tank]);

        ArmoryPresets.MakeEditable(mod, [], ArmoryPresets.Of(mod, ArmoryPart.Units)!.Rules[2]!);
        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: [tank]);

        Assert.Empty(mod.Units);
        JsonArray kept = ArmoryPresets.Of(mod, ArmoryPart.Units)!.Rules;
        Assert.Equal(["Clone", "Edit"], kept.Select(rule => (string?)rule?["Kind"]));
        Assert.DoesNotContain(kept, ArmoryPresets.IsLocked);
    }

    [Fact]
    public void A_unit_without_a_number_for_deck_codes_is_given_the_one_the_mod_keeps_and_reads_back_with_it()
    {
        const string truck = "Descriptor_Unit_Truck_SOV";
        Dictionary<string, string> files = Files();
        // the truck as a unit a mod's rules add: in no DeckSerializer.ndf
        files["Generated/Gameplay/Decks/DeckSerializer.ndf"] = DeckNumbers.Replace("        ($/GFX/Unit/" + truck + ", 41),\n", "").Replace("        ($/GFX/Unit/" + truck + ", 41),\r\n", "");
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, files);
        UnitCatalog game = Read(zip);
        Assert.False(game.Units.Single(unit => unit.Descriptor == truck).HasDeckNumber);
        var mod = new Mod { Name = "My mod" };
        mod.UnitDeckNumbers[truck] = 16000;

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog(), unitName: _ => "TRUCK");
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, files));

        Assert.Equal(["TRUCK", "Edit"], rules.Select(rule => (string?)rule?["Kind"] == "Group" ? (string?)rule?["Name"] : (string?)rule?["Kind"]));
        Assert.Equal(16000, exported.Units.Single(unit => unit.Descriptor == truck).DeckNumber);
        // the game's own numbers are left as they are
        Assert.Equal(3, exported.Units.Single(unit => unit.Descriptor == Tank).DeckNumber);
    }

    [Fact]
    public void Deleting_a_number_for_deck_codes_or_making_it_editable_lets_go_of_it_and_nothing_else()
    {
        const string building = "Descriptor_Unit_YS_Doomsday";
        var mod = new Mod { Name = "My mod" };
        mod.Units.Add(new ModUnit { Descriptor = "Descriptor_Unit_EZ_Hunter_US", MadeFrom = Tank, Name = "HUNTER", DeckNumber = 5 });
        mod.UnitDeckNumbers[building] = 16004;
        var tank = new Unit { Descriptor = Tank, Name = "TANK", Side = "NATO", Nation = "US" };
        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: [tank]);
        JsonNode NumberRule() => ArmoryPresets.Of(mod, ArmoryPart.Units)!.Rules
            .Single(rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[UnitChangeRules.UnitKey] == building && (string?)rule?["Kind"] == "Edit")!;

        ArmoryPresets.TakeBack(mod, [], [NumberRule()]);
        Assert.Empty(mod.UnitDeckNumbers);
        Assert.Single(mod.Units);

        mod.UnitDeckNumbers[building] = 16004;
        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: [tank]);
        ArmoryPresets.MakeEditable(mod, [], NumberRule());
        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: [tank]);
        Assert.Empty(mod.UnitDeckNumbers);
        Assert.Single(mod.Units);
        Assert.Equal(building, (string?)NumberRule()[DivisionChangeRules.ArmoryKey]![UnitChangeRules.UnitKey]);
        Assert.False(ArmoryPresets.IsLocked(NumberRule()));
    }

    [Fact]
    public void Numbers_given_at_export_get_the_rules_the_Armory_would_make_once_and_leave_the_other_presets_alone()
    {
        const string building = "Descriptor_Unit_YS_Doomsday";
        var mod = new Mod { Name = "My mod" };
        mod.Presets.Add(new ModPreset { Name = "By hand", Rules = [new JsonObject { ["Name"] = "kept", ["Kind"] = "Edit" }] });
        mod.UnitDeckNumbers[building] = 16004;

        ArmoryPresets.AddDeckNumberRules(mod, new TextCatalog(), _ => "DOOMSDAY", "Units");
        ArmoryPresets.AddDeckNumberRules(mod, new TextCatalog(), _ => "DOOMSDAY", "Units");
        JsonArray added = ArmoryPresets.Of(mod, ArmoryPart.Units)!.Rules;
        ArmoryPresets.Update(mod, [], new TextCatalog(), _ => "DOOMSDAY", part => part.ToString());

        Assert.Equal("kept", (string?)mod.Presets[0].Rules.Single()?["Name"]);
        Assert.Equal(["Group", "Edit"], added.Select(rule => (string?)rule?["Kind"]));
        Assert.Contains("16004", added[1]!["Actions"]![0]!["Value"]!.ToString());
        Assert.Equal(added.ToJsonString(), ArmoryPresets.Of(mod, ArmoryPart.Units)!.Rules.ToJsonString());
    }

    [Fact]
    public void The_numbers_for_deck_codes_a_mod_gives_are_kept_in_its_file_its_backup_and_its_copies()
    {
        var mod = new Mod { Name = "My mod" };
        mod.Presets.Add(new ModPreset { Name = "Rules" });
        mod.UnitDeckNumbers["Descriptor_Unit_YS_Doomsday"] = 16004;
        mod.UnitDeckNumbers["Descriptor_Unit_YC_Oracle"] = 16001;

        Mod read = ModFiles.Read(ModFiles.ExportText(mod), "My mod.json").Single();
        Mod restored = ModFiles.Read(ModFiles.BackupText([mod]), "backup.json").Single();
        Mod copy = mod.DeepCopy();
        copy.UnitDeckNumbers.Clear();

        Assert.Equal(mod.UnitDeckNumbers, read.UnitDeckNumbers);
        Assert.Equal(mod.UnitDeckNumbers, restored.UnitDeckNumbers);
        Assert.Equal(2, mod.UnitDeckNumbers.Count);
    }

    [Fact]
    public void A_mods_units_are_kept_in_its_file_and_its_copies()
    {
        var mod = new Mod { Name = "My mod" };
        mod.Units.Add(new ModUnit { Descriptor = "Descriptor_Unit_EZ_Hunter_US", MadeFrom = Tank, Name = "HUNTER", Nation = "US", DeckNumber = 5, Cost = 120, Stealth = 1.5 });

        Mod read = ModFiles.Read(ModFiles.ExportText(mod), "My mod.json").Single();
        Mod copy = mod.DeepCopy();
        copy.Units[0].Cost = 10;

        Assert.Equal(mod.Units[0].ToJson().ToJsonString(), read.Units[0].ToJson().ToJsonString());
        Assert.Equal(120, mod.Units[0].Cost);
        Assert.Null(read.Units[0].Strength);
    }

    [Fact]
    public void A_mod_file_cannot_make_an_export_copy_write_files_outside_its_places()
    {
        using var temp = new TemporaryFolder();
        string pictures = Path.Combine(temp.Path, "Pictures");
        Directory.CreateDirectory(pictures);
        string picture = Path.Combine(pictures, "own.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        string secret = Path.Combine(temp.Path, "secret.txt");
        File.WriteAllText(secret, "not for a mod");
        var mod = new Mod { Name = "My mod" };
        var tank = new Unit { Descriptor = Tank, Name = "TANK", Side = "NATO", Nation = "US" };
        mod.Units.Add(new ModUnit { Descriptor = "Descriptor_Unit_EZ_Hunter_US", MadeFrom = Tank, Name = "HUNTER", DeckNumber = 5, PictureFile = picture });
        JsonArray rules = UnitChangeRules.For(mod, [tank], new TextCatalog());
        string gameData = Path.Combine(temp.Path, "Export", "GameData");
        string exportedFile = Path.Combine(gameData, "UserInterface", "Textures", "ButtonTexturesUnites.ndf");
        JsonNode pictureRule = rules.First(rule => rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.PictureFileKey] is not null)!;
        JsonObject armory = (JsonObject)pictureRule[DivisionChangeRules.ArmoryKey]!;

        // a picture outside the app's picture folders is not copied into the mod
        armory[DivisionChangeRules.PictureFileKey] = secret;
        DivisionExtras.Outcome outcome = DivisionExtras.CopyPictures(rules, exportedFile, [pictures], "no {path}", "missing {path}");
        Assert.Equal((0, "missing " + secret), (outcome.Pictures, outcome.Problem));
        Assert.Empty(DivisionExtras.PictureFiles(rules, [pictures]));

        // a path leading out of GameData is not written
        armory[DivisionChangeRules.PictureFileKey] = picture;
        armory[DivisionChangeRules.PicturePathKey] = "../../secret.txt";
        Assert.Equal(0, DivisionExtras.CopyPictures(rules, exportedFile, [pictures], "no {path}", "missing {path}").Pictures);
        Assert.Equal("not for a mod", File.ReadAllText(secret));
    }

    // ---- changes to the game's own units ----

    [Fact]
    public void A_change_to_a_game_unit_reaches_the_files_as_the_Armory_shows_it_and_leaves_the_rest_of_the_unit_alone()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        string picture = Path.Combine(temp.Path, "own.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        var edit = new UnitEdit { Descriptor = Tank, Name = "BIG TANK", Nation = "UK", Side = "NATO", Cost = 150, SideArmor = 3, Vision = 2500.5, PictureFile = picture };
        mod.UnitEdits.Add(edit);

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Unit tank = game.Units.First(unit => unit.Descriptor == Tank);
        Unit written = exported.Units.Single(unit => unit.Descriptor == Tank);
        Assert.Equal(Describe(edit.Apply(tank)), Describe(written));
        Assert.Equal((150.0, "UK", tank.Strength, tank.Armor with { Sides = 3 }), (written.Cost, written.Nation, written.Strength, written.Armor));
        Assert.Equal(tank.Weapons.Select(weapon => weapon.Name), written.Weapons.Select(weapon => weapon.Name));
        // its own picture under a texture name of its own, never the game's picture's
        Assert.Equal("GameData:/Assets/2D/Interface/Common/UnitsIcons/NATO/EZ_Unit_Tank_US.png", written.PictureSource);
        Assert.Contains("\"" + UnitChangeRules.NameToken(Tank) + "\";\"BIG TANK\"", DivisionExtras.WithNames("", rules).Text);
        // the other units are as the game has them
        Assert.All(game.Units.Where(unit => unit.Descriptor != Tank),
            unit => Assert.Equal(Describe(unit), Describe(exported.Units.Single(each => each.Descriptor == unit.Descriptor))));
    }

    [Fact]
    public void A_unit_added_as_a_copy_of_a_changed_one_is_copied_with_the_changes()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        var edit = new UnitEdit { Descriptor = Tank, Strength = 12 };
        mod.UnitEdits.Add(edit);
        var copy = new ModUnit { Descriptor = ModUnit.NewDescriptor("Tank two", "US", []), MadeFrom = Tank, IsDuplicate = true, Name = "TANK TWO" };
        mod.Units.Add(copy);

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Unit changedTank = edit.Apply(game.Units.First(unit => unit.Descriptor == Tank));
        Unit written = exported.Units.Single(unit => unit.Descriptor == copy.Descriptor);
        Assert.Equal(Describe(copy.Apply(changedTank)), Describe(written));
        Assert.Equal(12.0, written.Strength);
    }

    [Fact]
    public void A_change_set_back_to_the_games_values_changes_nothing_and_makes_no_rule()
    {
        var mod = new Mod { Name = "My mod" };
        var tank = new Unit { Descriptor = Tank, Name = "TANK", Side = "NATO", Nation = "US" };
        UnitEdit.For(mod, Tank).Give(UnitField.Cost, 90);
        Assert.Single(mod.UnitEdits);

        UnitEdit.For(mod, Tank).Give(UnitField.Cost, null);
        Assert.Empty(UnitChangeRules.For(mod, [tank], new TextCatalog()));
        UnitEdit.LetGoOfIfEmpty(mod, Tank);

        Assert.Empty(mod.UnitEdits);
    }

    [Fact]
    public void Deleting_a_changes_rule_gives_the_unit_back_as_the_game_has_it_and_make_editable_keeps_it_by_hand()
    {
        var tank = new Unit { Descriptor = Tank, Name = "TANK", Side = "NATO", Nation = "US" };
        Mod Changed()
        {
            var mod = new Mod { Name = "My mod" };
            mod.UnitEdits.Add(new UnitEdit { Descriptor = Tank, Cost = 90 });
            ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: [tank]);
            return mod;
        }

        Mod deleted = Changed();
        JsonArray rules = ArmoryPresets.Of(deleted, ArmoryPart.Units)!.Rules;
        Assert.Equal(["TANK", "Edit"], rules.Select(rule => (string?)rule?["Kind"] == "Group" ? (string?)rule?["Name"] : (string?)rule?["Kind"]));
        Assert.Equal("TANK: Units.Rule.Changed", (string?)rules[1]!["Name"]);
        ArmoryPresets.TakeBack(deleted, [], [rules[1]]);
        Assert.Empty(deleted.UnitEdits);

        Mod editable = Changed();
        ArmoryPresets.MakeEditable(editable, [], ArmoryPresets.Of(editable, ArmoryPart.Units)!.Rules[1]!);
        ArmoryPresets.Update(editable, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: [tank]);
        Assert.Empty(editable.UnitEdits);
        Assert.DoesNotContain(ArmoryPresets.Of(editable, ArmoryPart.Units)!.Rules, ArmoryPresets.IsLocked);
    }

    [Fact]
    public void A_mods_changes_to_game_units_are_kept_in_its_file_and_its_copies()
    {
        var mod = new Mod { Name = "My mod" };
        mod.UnitEdits.Add(new UnitEdit { Descriptor = Tank, Name = "BIG TANK", Cost = 120, Stealth = 1.5 });

        Mod read = ModFiles.Read(ModFiles.ExportText(mod), "My mod.json").Single();
        Mod copy = mod.DeepCopy();
        copy.UnitEdits[0].Cost = 10;

        Assert.Equal(mod.UnitEdits[0].ToJson().ToJsonString(), read.UnitEdits[0].ToJson().ToJsonString());
        Assert.Equal(120, mod.UnitEdits[0].Cost);
        Assert.Null(read.UnitEdits[0].Strength);
    }

    [Fact]
    public void A_changed_value_is_marked_with_the_changes_rule_and_a_value_other_rules_changed_keeps_theirs()
    {
        var game = new Unit { Descriptor = Tank, Name = "TANK", Cost = 100, Strength = 10 };
        var byRules = new Unit { Descriptor = Tank, Name = "TANK", Cost = 100, Strength = 14 };
        var edit = new UnitEdit { Descriptor = Tank, Cost = 150 };
        Unit shown = edit.Apply(byRules);

        UnitChanges changes = UnitChanges.None.WithEdited("My mod", [(game, shown, [.. edit.FieldsChanged()], "TANK: changed")]);

        UnitChange change = changes.Of(shown)!;
        Assert.Equal([new RuleOfMod("My mod", "TANK: changed")], change.RulesFor(UnitField.Cost));
        Assert.True(change.Changed(UnitField.Strength));
        Assert.False(change.Changed(UnitField.Speed));
        Assert.Equal(100.0, change.Game!.Cost);
    }
}
