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
/// The weapons a mod changes and adds, as rules: run on made-up game files, the files they give must read back as the
/// Armory shows them (<see cref="ModWeaponView.Apply"/>), and a copy must reach only the units it is given to.
/// </summary>
public class WeaponChangeRulesTests
{
    [Fact]
    public void A_new_weapons_name_taken_already_puts_its_number_on_the_EZ_as_a_new_units_does()
    {
        Assert.Equal("Ammo_EZ_M256_DU", ModWeapon.NewDescriptor("M256 (DU)", []));
        Assert.Equal("Ammo_EZ2_M256_DU", ModWeapon.NewDescriptor("M256 (DU)", ["Ammo_EZ_M256_DU"]));
        Assert.Equal("Ammo_EZ3_M256_DU", ModWeapon.NewDescriptor("M256 (DU)", ["Ammo_EZ_M256_DU", "Ammo_EZ2_M256_DU"]));
    }

    private const string Gun = "Ammo_Canon_AP_120mm";
    private const string Tank = "Descriptor_Unit_Tank_US", OtherTank = "Descriptor_Unit_Other_Tank_US";

    // a second tank carrying the same gun, with a loadout of its own
    private const string OtherTankText = """

        export Descriptor_Unit_Other_Tank_US is TEntityDescriptor
        (
            ModulesDescriptors = [
                TTypeUnitModuleDescriptor( Coalition = TWargameCoalition/NATO MotherCountry = 'US' ),
                $/GFX/Weapon/WeaponDescriptor_Other_Tank_US,
                TProductionModuleDescriptor( FactoryType = Factory/Tanks ),
            ]
        )
        """;

    private const string OtherLoadout = """

        export WeaponDescriptor_Other_Tank_US is TWeaponManagerModuleDescriptor
        (
            TurretDescriptorList = [
                TTurretTwoAxisDescriptor
                (
                    MountedWeaponDescriptorList = [
                        TMountedWeaponDescriptor( Ammunition = $/GFX/Weapon/Ammo_Canon_AP_120mm NbWeapons = 1 ),
                    ]
                ),
            ]
        )
        """;

    private const string DamageResistance = """
        private DamageResistanceParams is TGameplayDamageResistanceContainer
        (
            DamageFamilyCounts = [
                (DamageFamily_ap, 40),
            ]
        )
        """;

    private static Dictionary<string, string> Files()
    {
        Dictionary<string, string> files = UnitReaderTests.WarnoFiles();
        files["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] += OtherTankText;
        files["Generated/Gameplay/Gfx/WeaponDescriptor.ndf"] += OtherLoadout;
        files["Generated/Gameplay/Gfx/DamageResistance.ndf"] = DamageResistance;
        // a second picture, which only the game's list has, for the Weapon pane's ✎
        files["Generated/UserInterface/Textures/WeaponTextures.ndf"] += """

            Texture_Interface_Weapon_M256 is TUIResourceTexture_Common
            (
                FileName = "GameData:/Assets/2D/Interface/Common/UnitsIcons/Armes/Panel_Info/M256.png"
            )
            """;
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
            Assert.All(result.Stats.Where(rule => rule.Kind != "Group"), rule => Assert.True(rule.Matched > 0, rule.Name + " matched nothing"));
            changed.Add(new ChangedFile("C:/Mod/GameData/" + path, result.Text!));
        }
        return changed;
    }

    /// <summary>What the Armory shows of a weapon, and the units that carry it.</summary>
    private static string Describe(Weapon weapon) =>
        string.Join(" ", weapon.Descriptor, weapon.Kind, weapon.DamageFamily, weapon.DamageLevel, weapon.Damage, weapon.Suppression, weapon.DamageRadius,
            weapon.SuppressionRadius, weapon.Range, weapon.MinimumRange, weapon.HelicopterRange, weapon.PlaneRange, weapon.AccuracyStopped,
            weapon.AccuracyMoving, weapon.DispersionFar, weapon.DispersionNear, weapon.AimingTime, weapon.Reload, weapon.ShotsPerSalvo,
            weapon.SupplyCost, weapon.PictureSource, "[" + string.Join(",", weapon.Traits) + "]", "[" + string.Join(",", weapon.Users) + "]");

    /// <summary>A unit's weapons, as the Armory lists them.</summary>
    private static string WeaponsOf(UnitCatalog catalog, string unit) =>
        string.Join(", ", catalog.Units.Single(each => each.Descriptor == unit).Weapons.Select(weapon => weapon.Ammunition + " " + weapon.Range + " " + weapon.Damage));

    private static (string Zip, UnitCatalog Game) GameFiles(TemporaryFolder temp)
    {
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        return (zip, Read(zip));
    }

    private static JsonArray RulesFor(Mod mod, UnitCatalog game) =>
        WeaponChangeRules.For(mod, game.Weapons!, new TextCatalog(), loadout => loadout);

    [Fact]
    public void A_change_to_a_game_weapon_reaches_the_files_as_the_Armory_shows_it_for_every_unit_that_carries_it()
    {
        using var temp = new TemporaryFolder();
        (string zip, UnitCatalog game) = GameFiles(temp);
        var mod = new Mod { Name = "My mod" };
        WeaponEdit edit = WeaponEdit.For(mod, Gun);
        // every value the gun has (the Weapon pane changes only those: a rule cannot set a value the ammunition lacks)
        Weapon gun = game.Weapons!.Single(weapon => weapon.Descriptor == Gun);
        foreach (WeaponStat stat in WeaponStat.All.Where(stat => stat.Read(gun) is not null))
            edit.Give(stat.Field, stat.Field == WeaponField.DamageLevel ? 35 : 7.5);
        Assert.Equal(17, edit.FieldsGiven().Count());
        // its traits too: one taken away, one added after the others
        edit.Traits = ["KINETIC", "HEAT"];

        UnitCatalog exported = Read(zip, RunOnEachFile(RulesFor(mod, game), Files()));
        UnitCatalog shown = ModWeaponView.Apply(mod, game, game, new TextCatalog()).Catalog;

        Assert.Equal(Describe(shown.Weapons!.Single(weapon => weapon.Descriptor == Gun)), Describe(exported.Weapons!.Single(weapon => weapon.Descriptor == Gun)));
        Assert.Equal(WeaponsOf(exported, Tank), WeaponsOf(shown, Tank));
        Assert.Equal(WeaponsOf(exported, OtherTank), WeaponsOf(shown, OtherTank));
        Assert.Contains($"{Gun} 8 7.5", WeaponsOf(shown, OtherTank), StringComparison.Ordinal);
    }

    [Fact]
    public void Another_picture_given_to_a_weapon_reaches_the_file_as_the_Armory_shows_it()
    {
        using var temp = new TemporaryFolder();
        (string zip, UnitCatalog game) = GameFiles(temp);
        var mod = new Mod { Name = "My mod" };
        WeaponEdit.For(mod, Gun).Picture = "Texture_Interface_Weapon_M256";

        UnitCatalog exported = Read(zip, RunOnEachFile(RulesFor(mod, game), Files()));
        ModWeaponView.Result shown = ModWeaponView.Apply(mod, game, game, new TextCatalog());

        Weapon exportedGun = exported.Weapons!.Single(weapon => weapon.Descriptor == Gun);
        Assert.Equal("GameData:/Assets/2D/Interface/Common/UnitsIcons/Armes/Panel_Info/M256.png", exportedGun.PictureSource);
        Assert.Equal(Describe(exportedGun), Describe(shown.Catalog.Weapons!.Single(weapon => weapon.Descriptor == Gun)));
        Assert.Contains(WeaponField.Picture, shown.WeaponMarks.Single().RuleByField.Keys);
    }

    [Fact]
    public void A_copy_for_one_unit_is_carried_by_that_unit_only_the_other_keeps_the_original()
    {
        using var temp = new TemporaryFolder();
        (string zip, UnitCatalog game) = GameFiles(temp);
        var mod = new Mod { Name = "My mod" };
        var copy = new ModWeapon
        {
            Descriptor = ModWeapon.NewDescriptor("120mm DU", [.. game.Weapons!.Select(weapon => weapon.Descriptor)]),
            MadeFrom = Gun,
            File = "Ammunition.ndf",
            Name = "120MM DU",
            Loadouts = ["WeaponDescriptor_Other_Tank_US"],
        };
        copy.Give(WeaponField.Damage, 2.5);
        copy.Give(WeaponField.Range, 2400);
        mod.Weapons.Add(copy);

        UnitCatalog exported = Read(zip, RunOnEachFile(RulesFor(mod, game), Files()));
        ModWeaponView.Result shown = ModWeaponView.Apply(mod, game, game, new TextCatalog());

        Assert.Equal("Ammo_EZ_120mm_DU", copy.Descriptor);
        Weapon written = exported.Weapons!.Single(weapon => weapon.Descriptor == copy.Descriptor);
        Assert.Equal(Describe(shown.Catalog.Weapons!.Single(weapon => weapon.Descriptor == copy.Descriptor)), Describe(written));
        Assert.Equal([OtherTank], written.Users);
        Assert.Equal([Tank], exported.Weapons!.Single(weapon => weapon.Descriptor == Gun).Users);
        Assert.Equal(WeaponsOf(exported, OtherTank), WeaponsOf(shown.Catalog, OtherTank));
        Assert.Equal(WeaponsOf(game, Tank), WeaponsOf(exported, Tank));
        // the copy keeps its original's picture, and the values not given
        Weapon gun = game.Weapons!.Single(weapon => weapon.Descriptor == Gun);
        Assert.Equal((gun.PictureSource, gun.Reload, gun.DamageLevel), (written.PictureSource, written.Reload, written.DamageLevel));
        // the marks: the copy is the mod's; the original lost a unit to it; the other tank's gun changed
        Assert.Contains(shown.WeaponMarks, mark => mark.Game is null && mark.Shown.Descriptor == copy.Descriptor);
        Assert.Contains(shown.WeaponMarks, mark => mark.Game?.Descriptor == Gun && mark.RuleByField.ContainsKey(WeaponField.Users));
        Assert.Contains(shown.UnitMarks, mark => mark.Shown.Descriptor == OtherTank && mark.Fields.Contains(UnitField.OfWeapon(0, UnitField.WeaponName)));
    }

    [Fact]
    public void A_copy_of_a_copy_is_made_after_it_and_takes_its_loadouts_from_it()
    {
        using var temp = new TemporaryFolder();
        (string zip, UnitCatalog game) = GameFiles(temp);
        var mod = new Mod { Name = "My mod" };
        var first = new ModWeapon { Descriptor = "Ammo_EZ_First", MadeFrom = Gun, File = "Ammunition.ndf", Name = "FIRST", Loadouts = ["WeaponDescriptor_Tank_US", "WeaponDescriptor_Other_Tank_US"] };
        var second = new ModWeapon { Descriptor = "Ammo_EZ_Second", MadeFrom = "Ammo_EZ_First", File = "Ammunition.ndf", Name = "SECOND", Loadouts = ["WeaponDescriptor_Other_Tank_US"] };
        second.Give(WeaponField.Reload, 9);
        mod.Weapons.AddRange([first, second]);

        UnitCatalog exported = Read(zip, RunOnEachFile(RulesFor(mod, game), Files()));
        UnitCatalog shown = ModWeaponView.Apply(mod, game, game, new TextCatalog()).Catalog;

        Assert.Equal([Tank], exported.Weapons!.Single(weapon => weapon.Descriptor == "Ammo_EZ_First").Users);
        Assert.Equal([OtherTank], exported.Weapons!.Single(weapon => weapon.Descriptor == "Ammo_EZ_Second").Users);
        Assert.Equal(WeaponsOf(exported, Tank), WeaponsOf(shown, Tank));
        Assert.Equal(WeaponsOf(exported, OtherTank), WeaponsOf(shown, OtherTank));
        Assert.Equal(9, shown.Weapons!.Single(weapon => weapon.Descriptor == "Ammo_EZ_Second").Reload);
    }

    // a howitzer's ammunition with the values kept as the file writes them, carried by no unit
    private const string Howitzer = "Ammo_Howitzer_Test";

    private const string HowitzerText = """

        Ammo_Howitzer_Test is TAmmunitionDescriptor
        (
            Arme = TDamageTypeRTTI(Family=DamageFamily_howz Index=5)
            ProjectileType = EProjectileType/Artillerie
            FxPower = EFXPower/Moyen
            ImpactHappening = 'Impact_Obus'
            MaximumRangeGRU = 7000
            TimeBetweenTwoSalvos = 10.0
            TimeBetweenTwoSalvos_Min = -1.0
            ShotsCountPerSalvo = 4
            SimultaneousShotsCount = 1
            PitchForParabolic = 0.6108652381980153
            HitRollRuleDescriptor = TDiceHitRollRuleDescriptor
            (
                BaseCriticModifier = 25
                BaseHitValueModifiers = [ (EBaseHitValueModifier/Base, 0), (EBaseHitValueModifier/Idling, 30), ]
                DistanceToTarget = True
            )
            MaxSuccessiveHitCount = 3
            HasDeploymentTime = False
            TirIndirect = True
        )
        """;

    /// <summary>The made-up files with the howitzer's ammunition, and a second kind of damage with 10 levels.</summary>
    private static Dictionary<string, string> FilesWithHowitzer()
    {
        Dictionary<string, string> files = Files();
        files["Generated/Gameplay/Gfx/Ammunition.ndf"] += HowitzerText;
        files["Generated/Gameplay/Gfx/DamageResistance.ndf"] = DamageResistance.Replace("(DamageFamily_ap, 40),", "(DamageFamily_ap, 40),\n        (DamageFamily_howz, 10),", StringComparison.Ordinal);
        return files;
    }

    /// <summary>A weapon's values kept as the file writes them, its kind of damage and whether it must deploy.</summary>
    private static string DescribeFileValues(Weapon weapon) =>
        string.Join(" ", weapon.FileValues.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value))
        + " " + weapon.DamageFamily + " " + weapon.DamageLevel + " " + weapon.MustDeploy;

    [Fact]
    public void Switches_choices_and_numbers_kept_as_the_file_writes_them_reach_the_file_as_the_Armory_shows_them()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, FilesWithHowitzer());
        UnitCatalog game = Read(zip);
        Weapon howitzer = game.Weapons!.Single(weapon => weapon.Descriptor == Howitzer);
        Assert.Equal("True", howitzer.FileValue("DistanceToTarget"));
        Assert.Equal("-1.0", howitzer.FileValue("TimeBetweenTwoSalvos_Min"));
        Assert.Equal("0", howitzer.FileValue(WeaponFileValues.AccuracyBase));

        var mod = new Mod { Name = "My mod" };
        WeaponEdit edit = WeaponEdit.For(mod, Howitzer);
        edit.Choose(WeaponFileValues.DamageFamily, "DamageFamily_ap");
        edit.Choose("ProjectileType", "EProjectileType/Obus");
        edit.Choose("ImpactHappening", "'Impact_Other'");
        edit.Choose(WeaponFileValues.HasDeploymentTime, "True");
        edit.Choose("DistanceToTarget", "False");
        edit.Give("TimeBetweenTwoSalvos_Min", 2.5);
        edit.Give(WeaponFileValues.SimultaneousShots, 2);
        edit.Give(WeaponFileValues.Arc, WeaponStat.Of(WeaponFileValues.Arc)!.ValueOf("45", howitzer, game.ArmorLevels));
        edit.Give("BaseCriticModifier", 50);

        UnitCatalog exported = Read(zip, RunOnEachFile(RulesFor(mod, game), FilesWithHowitzer()));
        ModWeaponView.Result shown = ModWeaponView.Apply(mod, game, game, new TextCatalog());

        Weapon exportedHowitzer = exported.Weapons!.Single(weapon => weapon.Descriptor == Howitzer);
        Weapon shownHowitzer = shown.Catalog.Weapons!.Single(weapon => weapon.Descriptor == Howitzer);
        Assert.Equal(DescribeFileValues(exportedHowitzer), DescribeFileValues(shownHowitzer));
        Assert.Equal(("ap", true, "'Impact_Other'"), (exportedHowitzer.DamageFamily, exportedHowitzer.MustDeploy, exportedHowitzer.FileValue("ImpactHappening")));
        Assert.Equal(45, WeaponStat.Of(WeaponFileValues.Arc)!.Shown(exportedHowitzer.FileNumber(WeaponFileValues.Arc)!.Value), 6);
        Assert.Contains("ProjectileType", shown.WeaponMarks.Single().RuleByField.Keys);
    }

    [Fact]
    public void A_choice_offers_the_values_the_games_weapons_have_and_kinds_of_damage_with_the_weapons_level()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, FilesWithHowitzer());
        UnitCatalog game = Read(zip);
        Weapon gun = game.Weapons!.Single(weapon => weapon.Descriptor == Gun);
        Weapon howitzer = game.Weapons!.Single(weapon => weapon.Descriptor == Howitzer);
        WeaponChoice family = WeaponChoice.Of(WeaponFileValues.DamageFamily)!;

        // a kind whose levels are not known is offered too
        Assert.Equal(["DamageFamily_ap", "DamageFamily_howz", "DamageFamily_missile_he"], family.Offered(howitzer, game.Weapons!, game.ArmorLevels));
        // the gun's level 32 is more than the howitzer's kind has
        Assert.DoesNotContain("DamageFamily_howz", family.Offered(gun, game.Weapons!, game.ArmorLevels));
        Assert.Equal(["True", "False"], WeaponChoice.Of("TirIndirect")!.Offered(howitzer, game.Weapons!, game.ArmorLevels));
        // values whose effect is not known can be changed too: the pane marks them ?
        Assert.Equal(["True", "False"], WeaponChoice.Of("TirReflexe")!.Offered(howitzer, game.Weapons!, game.ArmorLevels));
        Assert.NotNull(WeaponStat.Of("MaxSuccessiveHitCount"));
    }

    [Fact]
    public void Values_the_game_does_not_take_are_refused()
    {
        var weapon = new Weapon { Descriptor = Gun, File = "Ammunition.ndf", Name = "Gun", DamageFamily = "ap", DamageLevel = 32 };
        var levels = new Dictionary<string, int> { ["DamageFamily_ap"] = 40 };
        Assert.Equal(40, WeaponStat.Of(WeaponField.DamageLevel)!.ValueOf("40", weapon, levels));
        Assert.Null(WeaponStat.Of(WeaponField.DamageLevel)!.ValueOf("41", weapon, levels));
        Assert.Null(WeaponStat.Of(WeaponField.DamageLevel)!.ValueOf("0", weapon, levels));
        Assert.Null(WeaponStat.Of(WeaponField.AccuracyStopped)!.ValueOf("101", weapon, levels));
        Assert.Null(WeaponStat.Of(WeaponField.Range)!.ValueOf("12.5", weapon, levels));
        Assert.Equal(1.25, WeaponStat.Of(WeaponField.Reload)!.ValueOf("1.25", weapon, levels));
        // typed as the app writes numbers in its language, never as this computer's settings would read it
        Assert.Null(WeaponStat.Of(WeaponField.Reload)!.ValueOf("1,25", weapon, levels));
        Assert.Equal(1.25, WeaponStat.Of(WeaponField.Reload)!.ValueOf("1,25", weapon, levels, System.Globalization.CultureInfo.GetCultureInfo("fr-FR")));
        Assert.Equal(1.25, WeaponStat.Of(WeaponField.Reload)!.ValueOf("1.25", weapon, levels, System.Globalization.CultureInfo.GetCultureInfo("fr-FR")));

        // shots a salvo: whole, and no fewer than the shots it fires at once
        Weapon twoAtOnce = weapon.WithFileValue(WeaponFileValues.SimultaneousShots, "2");
        Assert.Equal(4, WeaponStat.Of(WeaponField.ShotsPerSalvo)!.ValueOf("4", twoAtOnce, levels));
        Assert.Equal(2, WeaponStat.Of(WeaponField.ShotsPerSalvo)!.ValueOf("2", twoAtOnce, levels));
        Assert.Null(WeaponStat.Of(WeaponField.ShotsPerSalvo)!.ValueOf("1", twoAtOnce, levels));
        Assert.Null(WeaponStat.Of(WeaponField.ShotsPerSalvo)!.ValueOf("2.5", twoAtOnce, levels));
        Assert.Null(WeaponStat.Of(WeaponField.ShotsPerSalvo)!.ValueOf("0", weapon, levels));

        // shots at once no more than a salvo has; -1 for "none" only where the game writes it; an angle up to 90°
        Weapon howitzer = weapon with { ShotsPerSalvo = 4 };
        Assert.Equal(4, WeaponStat.Of(WeaponFileValues.SimultaneousShots)!.ValueOf("4", howitzer, levels));
        Assert.Null(WeaponStat.Of(WeaponFileValues.SimultaneousShots)!.ValueOf("5", howitzer, levels));
        Assert.Null(WeaponStat.Of(WeaponFileValues.SimultaneousShots)!.ValueOf("0", howitzer, levels));
        Assert.Equal(-1, WeaponStat.Of("TimeBetweenTwoSalvos_Min")!.ValueOf("-1", weapon, levels));
        Assert.Null(WeaponStat.Of(WeaponField.Reload)!.ValueOf("-1", weapon, levels));
        Assert.Null(WeaponStat.Of(WeaponFileValues.Arc)!.ValueOf("91", weapon, levels));
        Assert.Null(WeaponStat.Of("FireTriggeringProbability")!.ValueOf("1.5", weapon, levels));
    }

    [Fact]
    public void The_weapons_rules_go_in_the_Weapons_preset_and_deleting_a_copys_rule_takes_it_out_with_the_copies_made_from_it()
    {
        var mod = new Mod { Name = "My mod" };
        var gun = new Weapon { Descriptor = Gun, File = "Ammunition.ndf", Name = "Gun" };
        var tank = new Unit { Descriptor = Tank, Name = "TANK", WeaponSet = "WeaponDescriptor_Tank_US" };
        WeaponEdit.For(mod, Gun).Give(WeaponField.Damage, 3);
        mod.Weapons.Add(new ModWeapon { Descriptor = "Ammo_EZ_First", MadeFrom = Gun, File = "Ammunition.ndf", Name = "FIRST", Loadouts = ["WeaponDescriptor_Tank_US"] });
        mod.Weapons.Add(new ModWeapon { Descriptor = "Ammo_EZ_Second", MadeFrom = "Ammo_EZ_First", File = "Ammunition.ndf", Name = "SECOND" });

        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameUnits: [tank], gameWeapons: [gun]);

        ModPreset weapons = ArmoryPresets.Of(mod, ArmoryPart.Weapons)!;
        Assert.Equal(["Gun", "Edit", "FIRST", "Clone", "Replace", "SECOND", "Clone"],
            weapons.Rules.Select(rule => (string?)rule?["Kind"] == "Group" ? (string?)rule?["Name"] : (string?)rule?["Kind"]));
        Assert.All(weapons.Rules.Where(rule => (string?)rule?["Kind"] != "Group"), rule => Assert.Equal(ArmoryPart.Weapons, DivisionChangeRules.PartOf(rule)));

        ArmoryPresets.TakeBack(mod, [], [weapons.Rules[3]]);
        Assert.Empty(mod.Weapons);
        ArmoryPresets.TakeBack(mod, [], [weapons.Rules[1]]);
        Assert.Empty(mod.WeaponEdits);
    }

    [Fact]
    public void Make_editable_keeps_every_rule_of_the_weapon_by_hand_and_the_Armory_lets_go_of_it()
    {
        var mod = new Mod { Name = "My mod" };
        var gun = new Weapon { Descriptor = Gun, File = "Ammunition.ndf", Name = "Gun" };
        mod.Weapons.Add(new ModWeapon { Descriptor = "Ammo_EZ_First", MadeFrom = Gun, File = "Ammunition.ndf", Name = "FIRST", Loadouts = ["WeaponDescriptor_Tank_US"] });
        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameWeapons: [gun]);

        ArmoryPresets.MakeEditable(mod, [], ArmoryPresets.Of(mod, ArmoryPart.Weapons)!.Rules[1]!);
        ArmoryPresets.Update(mod, [], new TextCatalog(), unit => unit, part => part.ToString(), gameWeapons: [gun]);

        Assert.Empty(mod.Weapons);
        JsonArray kept = ArmoryPresets.Of(mod, ArmoryPart.Weapons)!.Rules;
        Assert.Equal(["Clone", "Replace"], kept.Select(rule => (string?)rule?["Kind"]));
        Assert.DoesNotContain(kept, ArmoryPresets.IsLocked);
    }

    [Fact]
    public void A_mods_weapons_are_kept_in_its_file_its_backup_and_its_copies()
    {
        var mod = new Mod { Name = "My mod" };
        WeaponEdit.For(mod, Gun).Give(WeaponField.AccuracyMoving, 55);
        WeaponEdit.For(mod, Gun).Picture = "Texture_Interface_Weapon_M256";
        WeaponEdit.For(mod, Gun).Choose("FxPower", "EFXPower/UltraLourd");
        var copy = new ModWeapon { Descriptor = "Ammo_EZ_First", MadeFrom = Gun, File = "Ammunition.ndf", Name = "FIRST", Loadouts = ["WeaponDescriptor_Tank_US"] };
        copy.Give(WeaponField.SupplyCost, 4.5);
        mod.Weapons.Add(copy);

        Mod fromFile = ModFiles.Read(ModFiles.ExportText(mod), "My mod.json").Single();
        Mod fromBackup = ModFiles.Read(ModFiles.BackupText([WithAPreset(mod)]), "backup.json").Single();
        Mod copied = mod.DeepCopy();

        foreach (Mod read in new[] { fromFile, fromBackup, copied })
        {
            Assert.Equal(55, read.WeaponEdits.Single().Given(WeaponField.AccuracyMoving));
            Assert.Equal("Texture_Interface_Weapon_M256", read.WeaponEdits.Single().Picture);
            Assert.Equal("EFXPower/UltraLourd", read.WeaponEdits.Single().Chosen("FxPower"));
            Assert.Equal(copy.ToJson().ToJsonString(), read.Weapons.Single().ToJson().ToJsonString());
        }
        copied.Weapons[0].Loadouts.Add("WeaponDescriptor_Other");
        Assert.Single(mod.Weapons[0].Loadouts);
    }

    /// <summary>A backup keeps only mods with presets.</summary>
    private static Mod WithAPreset(Mod mod)
    {
        mod.Presets.Add(new ModPreset { Name = "Rules" });
        return mod;
    }
}
