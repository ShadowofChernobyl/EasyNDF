using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.Data.Tests;

/// <summary>
/// WARNO v210942's layout: the units in Generated/Gameplay/TacticalUnits.ndf, and a weapon's name, text keys, picture
/// and traits in each mounted weapon that fires it rather than in its ammunition.
/// </summary>
public class WarnoV210942Tests
{
    private const string Ammunition = "Ammo_Canon_AP_120mm";
    private const string Loadout = "WeaponDescriptor_Tank_US";

    private const string Weapons = """
        export WeaponDescriptor_Tank_US is TWeaponManagerModuleDescriptor
        (
            Salves =
            [
                1,
                2,
            ]
            TurretDescriptorList =
            [
                TTurretTwoAxisDescriptor
                (
                    MountedWeaponDescriptorList =
                    [
                        TMountedWeaponDescriptor
                        (
                            AmmoBoxIndex = 0
                            Ammunition = $/GFX/Weapon/Ammo_Canon_AP_120mm
                            Caliber = "CALIBRE"
                            InterfaceWeaponTexture = "Texture_Interface_Weapon_M68_120"
                            Name = "GUNNAME"
                            NbWeapons = 1
                            TraitsToken =
                            [
                                "MOTION",
                                "KINETIC",
                            ]
                            TypeCategoryName = "CATEGORY"
                            WeaponDescriptionToken = "DESCRIPTION"
                        ),
                        TMountedWeaponDescriptor
                        (
                            AmmoBoxIndex = 1
                            Ammunition = $/GFX/Weapon/Ammo_SAM_Stinger
                            Name = "STINGER"
                            NbWeapons = 2
                            TraitsToken = []
                        ),
                    ]
                ),
            ]
        )
        """;

    /// <summary>The made-up game of <see cref="UnitReaderTests"/>, laid out as v210942 lays it out.</summary>
    private static Dictionary<string, string> Files()
    {
        Dictionary<string, string> files = UnitReaderTests.WarnoFiles();
        files["Generated/Gameplay/TacticalUnits.ndf"] = files["Generated/Gameplay/Gfx/UniteDescriptor.ndf"];
        files.Remove("Generated/Gameplay/Gfx/UniteDescriptor.ndf");
        files["Generated/Gameplay/Gfx/WeaponDescriptor.ndf"] = Weapons;
        // the ammunition without what v210942 moved into its mounted weapons
        files["Generated/Gameplay/Gfx/Ammunition.ndf"] = Regex.Replace(files["Generated/Gameplay/Gfx/Ammunition.ndf"],
            @"(?m)^\s*(TraitsToken|InterfaceWeaponTexture)\s*=.*\r?\n", "");
        return files;
    }

    private static UnitCatalog Read()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        using var gameData = new GameDataFiles(zip);
        return UnitReader.Read(gameData, GameTitle.Warno, UnitReaderTests.WarnoTexts);
    }

    [Fact]
    public void The_units_are_read_from_TacticalUnits_ndf()
    {
        UnitCatalog catalog = Read();

        Unit tank = catalog.Units.Single(unit => unit.Descriptor == "Descriptor_Unit_Tank_US");
        Assert.Equal(UnitReader.TacticalUnitsFile, tank.File);
        Assert.Empty(catalog.Problems);
        Assert.Equal(UnitReader.TacticalUnitsFile, UnitChangeRules.UnitsFileOf(catalog.Units));
    }

    /// <summary>
    /// A mod's units on the updated game: v210942's files in base.zip, and the mod's own copy of the units file under the
    /// name and in the place of the version it was made for (<paramref name="ownFile"/>).
    /// </summary>
    private static UnitCatalog ReadMod(string ownFile, string descriptor)
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        string mod = Path.Combine(temp.Path, "Mod");
        string own = Path.Combine([mod, "GameData", .. ownFile.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllText(own, UnitReaderTests.WarnoFiles()["Generated/Gameplay/Gfx/UniteDescriptor.ndf"].Replace("Descriptor_Unit_Tank_US", descriptor, StringComparison.Ordinal));
        using var gameData = new GameDataFiles(zip, mod);
        return UnitReader.Read(gameData, GameTitle.Warno, UnitReaderTests.WarnoTexts);
    }

    [Fact]
    public void A_mod_made_before_v210942_is_read_from_its_own_UniteDescriptor_ndf_on_the_updated_game()
    {
        // the updated game's base.zip has no UniteDescriptor.ndf: the mod's own is still where its units are
        UnitCatalog catalog = ReadMod("Generated/Gameplay/Gfx/UniteDescriptor.ndf", "Descriptor_Unit_Tank_US_Older");

        Unit tank = catalog.Units.Single(unit => unit.Descriptor == "Descriptor_Unit_Tank_US_Older");
        Assert.Equal(UnitReader.UnitsFile, tank.File);
        Assert.DoesNotContain(catalog.Units, unit => unit.Descriptor == "Descriptor_Unit_Tank_US");
        Assert.Equal(UnitReader.UnitsFile, UnitChangeRules.UnitsFileOf(catalog.Units));
    }

    [Fact]
    public void A_mod_made_on_v210942_is_read_from_its_own_TacticalUnits_ndf()
    {
        UnitCatalog catalog = ReadMod("Generated/Gameplay/TacticalUnits.ndf", "Descriptor_Unit_Tank_US_Newer");

        Unit tank = catalog.Units.Single(unit => unit.Descriptor == "Descriptor_Unit_Tank_US_Newer");
        Assert.Equal(UnitReader.TacticalUnitsFile, tank.File);
        Assert.Equal(UnitReader.TacticalUnitsFile, UnitChangeRules.UnitsFileOf(catalog.Units));
    }

    [Fact]
    public void A_weapon_s_name_picture_and_traits_are_those_of_its_mounted_weapon()
    {
        Weapon gun = Read().Weapons.Single(weapon => weapon.Descriptor == Ammunition);

        Assert.True(gun.ShownOnMount);
        Assert.Equal("Texture_Interface_Weapon_M68_120", gun.PictureTexture);
        Assert.Equal(["MOTION", "KINETIC"], gun.Traits);
        Assert.Equal("\"GUNNAME\"", gun.FileValue("Name"));
        Assert.Equal("\"CALIBRE\"", gun.FileValue("Caliber"));
        Assert.Contains(("Name", "\"GUNNAME\""), gun.MountValues());
        Assert.Contains((Weapon.TraitsProperty, "[\"MOTION\", \"KINETIC\"]"), gun.MountValues());
    }

    [Fact]
    public void A_weapon_added_to_a_loadout_or_put_in_a_place_is_given_its_name_picture_and_traits_there()
    {
        UnitCatalog catalog = Read();
        Loadout loadout = catalog.Loadouts[Loadout];
        Func<string, IReadOnlyList<(string Property, string Value)>> mountValues = WeaponChangeRules.MountValuesFor(new Mod { Game = GameTitle.Warno }, catalog.Weapons);
        LoadoutChange add = new(LoadoutChange.Add, 0, 0, Ammunition, LoadoutRules.NewMountText(Ammunition, "FireEffect_X"), 3);
        LoadoutChange swap = new(LoadoutChange.Swap, 0, 1, Ammunition);

        List<LoadoutRules.LoadoutAction> actions = LoadoutRules.ActionsFor(loadout, [swap, add], GameTitle.Warno, mountValues);

        LoadoutRules.LoadoutAction added = actions.Single(action => action.Operator == "Insert [array]" && action.Target.EndsWith("MountedWeaponDescriptorList", StringComparison.Ordinal));
        Assert.Contains("Name = \"GUNNAME\"", added.Value, StringComparison.Ordinal);
        Assert.Contains("InterfaceWeaponTexture = \"Texture_Interface_Weapon_M68_120\"", added.Value, StringComparison.Ordinal);
        Assert.Contains(actions, action => action.Target.EndsWith("Index1" + Engine.Rules.ActionApplier.PathSeparator + "Name", StringComparison.Ordinal) && action.Value == "\"GUNNAME\"");
    }

    [Fact]
    public void A_change_to_a_weapon_s_picture_and_traits_is_written_in_its_mounted_weapons()
    {
        UnitCatalog catalog = Read();
        var mod = new Mod { Name = "My mod", Game = GameTitle.Warno };
        mod.WeaponEdits.Add(new WeaponEdit { Descriptor = Ammunition, Picture = "Texture_Interface_Weapon_New", Traits = ["MOTION"] });

        JsonArray rules = WeaponChangeRules.For(mod, catalog.Weapons, new TextCatalog(), _ => "Tank", catalog.Loadouts);

        JsonNode mountRule = rules.Single(rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.FileKey] == WeaponChangeRules.LoadoutsFile)!;
        Assert.Contains(Loadout, mountRule["Conditions"]!.ToJsonString(), StringComparison.Ordinal);
        List<(string? Target, string? Value)> actions = [.. mountRule["Actions"]!.AsArray().Select(action => ((string?)action!["Target"], (string?)action["Value"]))];
        Assert.Contains(("TurretDescriptorList" + Engine.Rules.ActionApplier.PathSeparator + "Index0" + Engine.Rules.ActionApplier.PathSeparator + "MountedWeaponDescriptorList"
            + Engine.Rules.ActionApplier.PathSeparator + "Index0" + Engine.Rules.ActionApplier.PathSeparator + Weapon.PictureProperty, "\"Texture_Interface_Weapon_New\""), actions);
        Assert.Contains(actions, action => action.Target!.EndsWith(Weapon.TraitsProperty, StringComparison.Ordinal) && action.Value == "[\"MOTION\"]");
        // nothing is written in the ammunition, which no longer has them
        Assert.DoesNotContain(rules, rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.FileKey] == "Ammunition.ndf");
    }

    [Fact]
    public void Rules_made_for_UniteDescriptor_ndf_run_on_TacticalUnits_ndf()
    {
        var rule = new JsonObject { ["Kind"] = "Edit", [DivisionChangeRules.ArmoryKey] = new JsonObject { [DivisionChangeRules.FileKey] = UnitReader.UnitsFile } };

        Assert.NotEmpty(DivisionChangeRules.ForFile([rule], UnitReader.TacticalUnitsFile));
        Assert.Empty(DivisionChangeRules.ForFile([rule], "Divisions.ndf"));
        Assert.True(UnitReader.SameFile(UnitReader.UnitsFile, UnitReader.TacticalUnitsFile));
    }

    [Fact]
    public void A_rule_set_named_for_UniteDescriptor_ndf_applies_to_TacticalUnits_ndf()
    {
        var byName = new ModPreset { Name = "WTO (UniteDescriptor.ndf)" };
        var listed = new ModPreset { Name = "Units", AppliesTo = ["Gfx/UniteDescriptor.ndf"] };
        var other = new ModPreset { Name = "WTO (Divisions.ndf)" };

        List<ModPreset> applying = RuleSetFiles.For(UnitReader.TacticalUnitsFile, "GameData/Generated/Gameplay", [byName, listed, other], byName: true);

        Assert.Equal([byName, listed], applying);
    }
}
