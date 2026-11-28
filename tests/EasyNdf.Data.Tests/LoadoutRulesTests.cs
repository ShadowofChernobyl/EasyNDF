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
/// A unit's changes to its weapons (swap, take off, add), as rules: run on a made-up loadout laid out as the game's,
/// the unit must get a loadout of its own with the changes, as the Armory shows it, and every other unit keep its own.
/// </summary>
public class LoadoutRulesTests
{
    private const string Tank = "Descriptor_Unit_Tank_US", OtherTank = "Descriptor_Unit_Other_Tank_US";
    private const string Loadout = "WeaponDescriptor_Tank_US";

    // a second tank with the same loadout, as a few of the game's units share one
    private const string OtherTankText = """

        export Descriptor_Unit_Other_Tank_US is TEntityDescriptor
        (
            ModulesDescriptors = [
                TTypeUnitModuleDescriptor( Coalition = TWargameCoalition/NATO MotherCountry = 'US' ),
                $/GFX/Weapon/WeaponDescriptor_Tank_US,
                TProductionModuleDescriptor( FactoryType = Factory/Tanks ),
            ]
        )
        """;

    // two turrets, the main gun's two rounds sharing an ammo box, as the game's tanks have them
    private const string Loadouts = """
        export WeaponDescriptor_Tank_US is TWeaponManagerModuleDescriptor
        (
            Salves = [ 40, 65 ]
            TurretDescriptorList = [
                TTurretTwoAxisDescriptor
                (
                    MountedWeaponDescriptorList = [
                        TMountedWeaponDescriptor
                        (
                            AmmoBoxIndex = 0
                            Ammunition = $/GFX/Weapon/Ammo_Canon_AP_120mm
                            EffectTag = "FireEffect_Canon_AP_120mm"
                            HandheldEquipmentKey = "WeaponAlternative_1"
                            NbWeapons = 1
                            WeaponShootDataPropertyName = [ "WeaponShootData_0_1" ]
                        ),
                        TMountedWeaponDescriptor
                        (
                            AmmoBoxIndex = 0
                            Ammunition = $/GFX/Weapon/Ammo_Canon_HE_120mm
                            EffectTag = "FireEffect_Canon_HE_120mm"
                            HandheldEquipmentKey = "WeaponAlternative_2"
                            NbWeapons = 1
                            WeaponShootDataPropertyName = [ "WeaponShootData_0_2" ]
                        ),
                    ]
                    Tag = "tourelle1"
                ),
                TTurretTwoAxisDescriptor
                (
                    MountedWeaponDescriptorList = [
                        TMountedWeaponDescriptor
                        (
                            AmmoBoxIndex = 1
                            Ammunition = $/GFX/Weapon/Ammo_HMG_M2HB
                            EffectTag = "FireEffect_HMG_M2HB"
                            HandheldEquipmentKey = "WeaponAlternative_3"
                            NbWeapons = 1
                            WeaponShootDataPropertyName = [ "WeaponShootData_0_3" ]
                        ),
                    ]
                    Tag = "tourelle2"
                ),
            ]
        )
        """;

    private const string Ammunition = """
        Ammo_Canon_AP_120mm is TAmmunitionDescriptor
        (
            Arme = TDamageTypeRTTI(Family=DamageFamily_ap Index=32)
            MaximumRangeGRU = 2275
            ShotsCountPerSalvo = 1
        )
        Ammo_Canon_HE_120mm is TAmmunitionDescriptor
        (
            Arme = TDamageTypeRTTI(Family=DamageFamily_he Index=1)
            MaximumRangeGRU = 2275
            ShotsCountPerSalvo = 1
        )
        Ammo_HMG_M2HB is TAmmunitionDescriptor
        (
            Arme = TDamageTypeRTTI(Family=DamageFamily_mmgballe Index=1)
            MaximumRangeGRU = 1400
            ShotsCountPerSalvo = 5
        )
        Ammo_Autocanon_M230 is TAmmunitionDescriptor
        (
            Arme = TDamageTypeRTTI(Family=DamageFamily_he_autocanon Index=1)
            MaximumRangeGRU = 1750
            ShotsCountPerSalvo = 5
        )
        """;

    private static Dictionary<string, string> Files()
    {
        Dictionary<string, string> files = UnitReaderTests.WarnoFiles();
        files["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] += OtherTankText;
        files["Generated/Gameplay/Gfx/WeaponDescriptor.ndf"] = Loadouts;
        files["Generated/Gameplay/Gfx/Ammunition.ndf"] = Ammunition;
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
            Assert.All(result.Stats.Where(rule => rule.Kind != "Group"), rule => Assert.True(rule.Edits > 0 || rule.Kind == "Clone", rule.Name + " changed nothing"));
            changed.Add(new ChangedFile("C:/Mod/GameData/" + path, result.Text!));
        }
        return changed;
    }

    /// <summary>A unit's weapons, as the Armory lists them.</summary>
    private static string WeaponsOf(UnitCatalog catalog, string unit) =>
        string.Join(", ", catalog.Units.Single(each => each.Descriptor == unit).Weapons.Select(weapon => weapon.Ammunition + "×" + weapon.Count));

    private static Dictionary<string, Weapon> WeaponsByDescriptor(UnitCatalog catalog) => catalog.Weapons!.ToDictionary(weapon => weapon.Descriptor);

    private static string? MountText(int turret, int mount) => LoadoutRules.MountText(new StringReader(Loadouts), Loadout, turret, mount);

    [Fact]
    public void The_loadout_is_read_turret_by_turret_with_its_ammo_boxes()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        Loadout loadout = Read(zip).Loadouts![Loadout];

        Assert.Equal([40, 65], loadout.AmmoBoxes);
        Assert.Equal(["tourelle1", "tourelle2"], loadout.Turrets.Select(turret => turret.Tag));
        Assert.Equal(["Ammo_Canon_AP_120mm", "Ammo_Canon_HE_120mm", "Ammo_HMG_M2HB"], loadout.Mounts.Select(mount => mount.Ammunition));
        Assert.Equal(3, loadout.HighestWeaponNumber);
        Assert.Equal((0, 1), (loadout.Turrets[0].Mounts[1].AmmoBox, loadout.Turrets[1].Mounts[0].AmmoBox));
    }

    [Fact]
    public void A_game_units_weapon_changes_go_into_a_loadout_of_its_own_as_the_Armory_shows_them_and_the_other_unit_keeps_the_games()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        var edit = new UnitEdit { Descriptor = Tank };
        // the HE round taken off, the machine gun swapped for the autocannon, and an autocannon added to the main turret
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Remove, 0, 1, MountText: MountText(0, 1)!));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Swap, 1, 0, "Ammo_Autocanon_M230"));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Add, 0, 2, "Ammo_Autocanon_M230", LoadoutRules.NewMountText("Ammo_Autocanon_M230", "FireEffect_Autocanon_M230"), Number: 300));
        mod.UnitEdits.Add(edit);

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog(), game.Loadouts);
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Unit tank = game.Units.Single(unit => unit.Descriptor == Tank);
        Unit shown = edit.WithLoadoutChanges(edit.Apply(tank), Tank, game.Loadouts, WeaponsByDescriptor(game));
        Assert.Equal("WeaponDescriptor_EZ_Tank_US", shown.WeaponSet);
        Assert.Equal(shown.WeaponSet, exported.Units.Single(unit => unit.Descriptor == Tank).WeaponSet);
        Assert.Equal("Ammo_Canon_AP_120mm×1, Ammo_Autocanon_M230×1, Ammo_Autocanon_M230×1", WeaponsOf(exported, Tank));
        Assert.Equal(WeaponsOf(exported, Tank), string.Join(", ", shown.Weapons.Select(weapon => weapon.Ammunition + "×" + weapon.Count)));
        // the added weapon has an ammo box of its own and the next weapon number
        Loadout own = exported.Loadouts!["WeaponDescriptor_EZ_Tank_US"];
        Assert.Equal([40, 65, 300], own.AmmoBoxes);
        Assert.Equal((2, 4), (own.Turrets[0].Mounts[1].AmmoBox, own.Turrets[0].Mounts[1].WeaponNumber));
        // the game's loadout, and the other tank that shares it, are as they were
        Assert.Equal(WeaponsOf(game, OtherTank), WeaponsOf(exported, OtherTank));
        Assert.Equal(game.Loadouts![Loadout].Mounts.Select(mount => mount.Ammunition), exported.Loadouts[Loadout].Mounts.Select(mount => mount.Ammunition));
    }

    [Fact]
    public void A_new_unit_with_changed_weapons_gets_its_own_loadout_and_its_model_keeps_the_games()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        var hunter = new ModUnit { Descriptor = "Descriptor_Unit_EZ_Hunter_US", MadeFrom = Tank, Name = "HUNTER", DeckNumber = 5 };
        hunter.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Swap, 0, 0, "Ammo_Canon_HE_120mm"));
        mod.Units.Add(hunter);

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog(), game.Loadouts);
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Assert.Equal("WeaponDescriptor_EZ_Hunter_US", exported.Units.Single(unit => unit.Descriptor == hunter.Descriptor).WeaponSet);
        Assert.Equal("Ammo_Canon_HE_120mm×1, Ammo_Canon_HE_120mm×1, Ammo_HMG_M2HB×1", WeaponsOf(exported, hunter.Descriptor));
        Assert.Equal(WeaponsOf(game, Tank), WeaponsOf(exported, Tank));
    }

    [Fact]
    public void A_boxs_salvos_and_a_weapons_number_are_changed_in_the_units_own_loadout_after_a_weapon_taken_off_before_it()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        var edit = new UnitEdit { Descriptor = Tank };
        // the AP round taken off, so the HE round is the turret's first in the unit's own loadout
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Remove, 0, 0, MountText: MountText(0, 0)!));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Count, 0, 1, Number: 2));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Salvos, 0, 1, Number: 90));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Add, 1, 1, "Ammo_Autocanon_M230", LoadoutRules.NewMountText("Ammo_Autocanon_M230", "FireEffect_Autocanon_M230", 3), Number: 150));
        mod.UnitEdits.Add(edit);

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog(), game.Loadouts);
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Loadout own = exported.Loadouts!["WeaponDescriptor_EZ_Tank_US"];
        Assert.Equal([40, 90, 150], own.AmmoBoxes);
        Assert.Equal("Ammo_Canon_HE_120mm×2, Ammo_HMG_M2HB×1, Ammo_Autocanon_M230×3", WeaponsOf(exported, Tank));
        // the Armory shows the same as the export makes
        Loadout shown = LoadoutRules.Changed(game.Loadouts![Loadout], edit.LoadoutChanges, own.Descriptor);
        Assert.Equal(own.AmmoBoxes, shown.AmmoBoxes);
        Assert.Equal(own.Mounts.Select(mount => (mount.Ammunition, mount.Count)), shown.Mounts.Select(mount => (mount.Ammunition, mount.Count)));
        Assert.Equal(WeaponsOf(game, OtherTank), WeaponsOf(exported, OtherTank));
    }

    [Fact]
    public void The_Armory_presets_carry_a_new_units_weapon_rules_and_the_export_gives_it_its_own_loadout()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        var hunter = new ModUnit { Descriptor = "Descriptor_Unit_EZ_Hunter_US", MadeFrom = Tank, Name = "HUNTER", DeckNumber = 5, LoadoutFrom = Loadout };
        hunter.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Salvos, 0, 0, Number: 90));
        mod.Units.Add(hunter);

        // as the Armory makes its presets, with the game's loadouts: without them, no weapon rule was made at all
        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: game.Units, gameWeapons: game.Weapons, gameLoadouts: game.Loadouts);
        JsonArray rules = ArmoryPresets.RulesOf(mod) is var all ? new JsonArray([.. all.Select(rule => rule?.DeepClone())]) : [];
        Assert.Contains(UnitChangeRules.LoadoutsFile, DivisionChangeRules.FilesOf(rules));
        UnitCatalog exported = Read(zip, RunOnEachFile(rules, Files()));

        Assert.Equal("WeaponDescriptor_EZ_Hunter_US", exported.Units.Single(unit => unit.Descriptor == hunter.Descriptor).WeaponSet);
        Assert.Equal([90, 65], exported.Loadouts!["WeaponDescriptor_EZ_Hunter_US"].AmmoBoxes);
    }

    [Fact]
    public void A_change_put_in_the_place_of_another_keeps_its_place_in_the_list()
    {
        var first = new LoadoutChange(LoadoutChange.Add, 0, 2, "Ammo_A", Number: 10);
        var second = new LoadoutChange(LoadoutChange.Add, 0, 3, "Ammo_B", Number: 20);
        var changes = new List<LoadoutChange> { first, second };

        LoadoutRules.Replace(changes, first, first with { Number = 30 });
        LoadoutRules.Replace(changes, null, new LoadoutChange(LoadoutChange.Count, 0, 0, Number: 2));
        LoadoutRules.Replace(changes, second, null);

        Assert.Equal(["Ammo_A", ""], changes.Select(change => change.Ammunition));
        Assert.Equal([30, 2], changes.Select(change => change.Number));
        Assert.Equal(3, LoadoutRules.AddedCount(new LoadoutChange(LoadoutChange.Add, 0, 0, "Ammo_A", LoadoutRules.NewMountText("Ammo_A", "Fire", 3))));
    }

    [Fact]
    public void A_swap_is_refused_where_the_game_could_not_take_it()
    {
        var weapons = new Dictionary<string, Weapon>
        {
            ["Ammo_Canon_AP_120mm"] = new() { Descriptor = "Ammo_Canon_AP_120mm", File = "Ammunition.ndf", Name = "AP", Kind = WeaponKind.AntiArmor, ShotsPerSalvo = 1 },
            ["Ammo_Canon_HE_120mm"] = new() { Descriptor = "Ammo_Canon_HE_120mm", File = "Ammunition.ndf", Name = "HE", Kind = WeaponKind.HighExplosive, ShotsPerSalvo = 1 },
            ["Ammo_HMG_M2HB"] = new() { Descriptor = "Ammo_HMG_M2HB", File = "Ammunition.ndf", Name = "HMG", Kind = WeaponKind.SmallArms, ShotsPerSalvo = 5 },
            ["Ammo_TOW"] = new() { Descriptor = "Ammo_TOW", File = "AmmunitionMissiles.ndf", Name = "TOW", Kind = WeaponKind.Missile, ShotsPerSalvo = 1, Missile = "Descriptor_Missile_TOW" },
            ["Ammo_Howitzer"] = new() { Descriptor = "Ammo_Howitzer", File = "Ammunition.ndf", Name = "Howitzer", Kind = WeaponKind.HighExplosive, ShotsPerSalvo = 1, MustDeploy = true },
        };
        var mounts = new List<LoadoutMount>
        {
            new(0, 0, "Ammo_Canon_AP_120mm", 0, 1, "", 1),
            new(0, 1, "Ammo_Canon_HE_120mm", 0, 1, "", 2),
        };
        var loadout = new Loadout(Loadout, [40], [new LoadoutTurret(0, "tourelle1", "TTurretTwoAxisDescriptor", mounts)]);

        Assert.Equal(LoadoutRules.Fit.Fits, LoadoutRules.FitOf(loadout, mounts[0], weapons["Ammo_Canon_AP_120mm"], weapons).Fit);
        Assert.Equal(LoadoutRules.Fit.OtherKind, LoadoutRules.FitOf(loadout, mounts[0], weapons["Ammo_Canon_HE_120mm"], weapons).Fit);
        Assert.Equal("Weapons.Swap.NeedsLauncher", LoadoutRules.FitOf(loadout, mounts[0], weapons["Ammo_TOW"], weapons).Reason);
        Assert.Equal("Weapons.Swap.MustDeploy", LoadoutRules.FitOf(loadout, mounts[0], weapons["Ammo_Howitzer"], weapons).Reason);
        // the HE round shares box 0 and fires 1 a salvo: a machine gun firing 5 cannot go there
        Assert.Equal(("Weapons.Swap.SharedBox", "HE"), LoadoutRules.FitOf(loadout, mounts[0], weapons["Ammo_HMG_M2HB"], weapons) is var fit ? (fit.Reason, fit.Other) : default);
        Assert.Equal("Weapons.Add.NeedsLauncher", LoadoutRules.WhyNotAdd(loadout.Turrets[0], weapons["Ammo_TOW"], weapons));
        Assert.Null(LoadoutRules.WhyNotAdd(loadout.Turrets[0], weapons["Ammo_HMG_M2HB"], weapons));
    }

    [Fact]
    public void A_units_weapon_changes_are_kept_in_the_mod_file()
    {
        var mod = new Mod { Name = "My mod" };
        var edit = new UnitEdit { Descriptor = Tank };
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Remove, 0, 1, MountText: "TMountedWeaponDescriptor(X = 1)"));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Add, 1, 1, "Ammo_X", "TMountedWeaponDescriptor()", Number: 12));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Salvos, 0, 1, Number: 90));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Count, 0, 0, Number: 2));
        mod.UnitEdits.Add(edit);

        Mod read = ModFiles.Read(ModFiles.ExportText(mod), "My mod.json").Single();

        Assert.Equal(edit.LoadoutChanges, read.UnitEdits.Single().LoadoutChanges);
        Assert.False(read.UnitEdits.Single().ChangesNothing);
    }
}
