using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Steel Division 2's weapons (its ammunition) changed in the Weapon pane: read with the numbers the game shows
/// (metres), written as rules in the game's own names and forms (distances as ((N) * Metre), the level inside the kind
/// of damage), never WARNO's, and read back as the Armory shows them. What the game's ammunition lacks, and new
/// weapons, are not written. Ammunition laid out as the game's is, with made-up names and values; and, where the game
/// is installed, one of its real weapons.
/// </summary>
public class SteelDivisionWeaponChangesTests
{
    private const string Gun = "Ammo_Test_Canon_75mm_AP", Shell = "Ammo_Test_Canon_75mm_HE";

    // laid out as the game's Ammunition.ndf: one property a line, distances ((N) * Metre), the hit roll nested with its own GUID
    private const string Ammunition = """
        // Ne pas éditer, ce fichier est auto-généré !

        export Ammo_Test_Canon_75mm_AP is TAmmunitionDescriptor
        (
            DescriptorId                      = GUID:{00000000-0000-0000-0000-00000000a001}
            Name                              = 'TEST75AP'
            TypeArme                          = 'Main_Gun'
            TypeCategoryName                  = 'GUN'
            Caliber                           = '75mm'
            WeaponDescriptionToken            = 'WTYP_MAIN'
            WeaponCursorType                  = Weapon_Cursor_Canon
            MinMaxCategory                    = MinMax_GUN
            Arme                              = EArmeType/AP_20
            IsAPCR                            = False
            ProjectileType                    = EProjectileType/Obus
            FxPower                           = EFXPower/Moyen
            Puissance                         = 200.0
            TempsEntreDeuxTirs                = 6.0
            TempsEntreDeuxTirs_Min             = -1.0
            TempsEntreDeuxFx                  = 6.0
            PorteeMaximale                    = ((8000) * Metre)
            PorteeMinimaleTBA                 = ((0) * Metre)
            PorteeMaximaleTBA                 = ((0) * Metre)
            PorteeMinimaleHA                  = ((0) * Metre)
            PorteeMaximaleHA                  = ((500) * Metre)
            PorteeMinimaleProjectile          = ((0) * Metre)
            PorteeMaximaleProjectile          = ((0) * Metre)
            PorteeMinimale                    = ((0) * Metre)
            AffecteParNombre                  = False
            AngleDispersion                   = 0.0
            DispersionAtMaxRange              = ((20) * Metre)
            DispersionAtMinRange              = ((5) * Metre)
            CorrectedShotAimtimeMultiplier    = 1.0
            RadiusSplashPhysicalDamages       = ((1) * Metre)
            PhysicalDamages                   = 6.0
            RadiusSplashSuppressDamages       = ((120) * Metre)
            SuppressDamages                   = 100.0
            TirIndirect                       = False
            TirReflexe                        = True
            NoiseDissimulationMalus           = 5.0
            ShotsBeforeMaxNoise               = 1
            SupplyCost                        = 30
            WeaponRessourcesNeeded            = MAP
            [
            ]
                // DiceHitRollRule
            HitRollRuleDescriptor = TDiceHitRollRuleDescriptor
            (
                 DescriptorId          = GUID:{00000000-0000-0000-0000-00000000a002}
                 BaseCriticModifier = 100
                 BaseEffectModifier = 0
                 BaseHitValueModifiers =
                 [
                     (EBaseHitValueModifier/Base, 0.0),
                     (EBaseHitValueModifier/Idling, 45),
                     (EBaseHitValueModifier/Moving, 20),
                     (EBaseHitValueModifier/SuccessfulShots, 0.0),
                     (EBaseHitValueModifier/Targeted, 0.0),
                 ]
                 HitModifierList       =
                 [
                     EDiceHitModifier/Precision,
                     EDiceHitModifier/DistanceToTarget,
                 ]
            )

            MaxSuccessiveHitCount             = 4
            TempsDeVisee                      = 3.0
            TempsEntreDeuxSalves              = 7.0
            TempsEntreDeuxSalves_Min           = -1.0
            TempsEntreDeuxSalves_Max           = -1.0
            NbTirParSalves                    = 1
            NbrProjectilesSimultanes          = 1
            AffichageMunitionParSalve         = 1
            MissileDescriptor                 = nil
            AffichageMenu                     = True
            InterfaceWeaponTexture            = "Texture_Interface_Weapon_Test_75mm"
            SmokeDescriptor                   = nil
            FireDescriptor                    = nil
            CanHarmInfantry                   = True
            IsHarmlessForAllies               = True
            PiercingWeapon                    = True
            DamageTypeEvolutionOverRangeDescriptor  = ~/DamageTypeEvolutionOverRangeDescriptor_Test_Slow
        )
        export Ammo_Test_Canon_75mm_HE is TAmmunitionDescriptor
        (
            DescriptorId                      = GUID:{00000000-0000-0000-0000-00000000b001}
            Name                              = 'TEST75HE'
            WeaponCursorType                  = Weapon_Cursor_Howitzer
            MinMaxCategory                    = MinMax_ARTI
            Arme                              = EArmeType/HE
            ProjectileType                    = EProjectileType/Artillerie
            FxPower                           = EFXPower/Moyen
            TempsEntreDeuxTirs                = 5.0
            TempsEntreDeuxFx                  = 5.0
            PorteeMaximale                    = ((9000) * Metre)
            PorteeMinimaleTBA                 = ((0) * Metre)
            PorteeMaximaleTBA                 = ((0) * Metre)
            PorteeMinimaleHA                  = ((0) * Metre)
            PorteeMaximaleHA                  = ((0) * Metre)
            PorteeMinimaleProjectile          = ((0) * Metre)
            PorteeMaximaleProjectile          = ((0) * Metre)
            PorteeMinimale                    = ((100) * Metre)
            AffecteParNombre                  = True
            AngleDispersion                   = 0.1
            DispersionAtMaxRange              = ((40) * Metre)
            RadiusSplashPhysicalDamages       = ((30) * Metre)
            PhysicalDamages                   = 2.0
            RadiusSplashSuppressDamages       = ((60) * Metre)
            SuppressDamages                   = 80.0
            TirIndirect                       = True
            TirReflexe                        = False
            NoiseDissimulationMalus           = 6.0
            ShotsBeforeMaxNoise               = 2
            SupplyCost                        = 40
            HitRollRuleDescriptor = TDiceHitRollRuleDescriptor
            (
                 DescriptorId          = GUID:{00000000-0000-0000-0000-00000000b002}
                 BaseCriticModifier = 50
                 BaseHitValueModifiers =
                 [
                     (EBaseHitValueModifier/Base, 0.0),
                     (EBaseHitValueModifier/Idling, 30),
                     (EBaseHitValueModifier/Moving, 10),
                     (EBaseHitValueModifier/Targeted, 0.0),
                 ]
            )
            MaxSuccessiveHitCount             = 1
            TempsDeVisee                      = 4.0
            TempsEntreDeuxSalves              = 9.0
            TempsEntreDeuxSalves_Min           = -1.0
            TempsEntreDeuxSalves_Max           = -1.0
            NbTirParSalves                    = 2
            NbrProjectilesSimultanes          = 1
            AffichageMunitionParSalve         = 2
            MissileDescriptor                 = nil
            InterfaceWeaponTexture            = "Texture_Interface_Weapon_Test_Howitzer"
            SmokeDescriptor                   = ~/Descriptor_Smoke_Test
            FireDescriptor                    = ~/Descriptor_Fire_Test
            IsHarmlessForAllies               = False
            PiercingWeapon                    = False
            DamageTypeEvolutionOverRangeDescriptor  = nil
        )
        """;

    // laid out as the game's WeaponDescriptor.ndf: Salves padded with -1, mounts naming their ammunition with ~/
    private const string Loadouts = """
        export WeaponDescriptor_Test_Tank_US is TWeaponManagerModuleDescriptor
        (
            Salves               = [ 40, 20, -1, -1 ]
            HasMainSalvo = False
            SalvoIsMainSalvo = [ False, False, ]
            TurretDescriptorList = [
                TTurretTwoAxisDescriptor
                (
                    MountedWeaponDescriptorList = [
                        TMountedWeaponDescriptor
                        (
                            Ammunition       = ~/Ammo_Test_Canon_75mm_AP
                            SalvoStockIndex  = 0
                            WeaponNumber_ForInterface = 1
                        ),
                        TMountedWeaponDescriptor
                        (
                            Ammunition       = ~/Ammo_Test_Canon_75mm_HE
                            SalvoStockIndex  = 1
                            WeaponNumber_ForInterface = 1
                        ),
                    ]
                    Tag = 'tourelle1'
                ),
            ]
        )
        """;

    private const string Units = """
        export Descriptor_Unit_Test_Tank_US is TEntityDescriptor
        (
            DescriptorId      = GUID:{00000000-0000-0000-0000-000000000001}
            Modules           = [
                    TModuleSelector
                    (
                        Default            = TTypeUnitModuleDescriptor
                        (
                            Nationalite                      = ENationalite/Allied
                            MotherCountry                    = 'US'
                        )
                    ),
                WeaponManager is
                    TModuleSelector
                    (
                        Default        = $/GFX/Everything/WeaponDescriptor_Test_Tank_US
                        Selection      = [~/NilDescriptorIfGhostOrCadavre]
                    ),
            ]
        )
        """;

    private const string Constants = """
        GDConstantes is TGDConstantes
        (
            MultiplicateurMetreRTSVersDistanceFeedbackTactique = 1.0 div 5.0//7.0
        )
        """;

    private const string Pictures = """
        export Texture_Interface_Weapon_Test_75mm is TUIResourceTexture_Common
        (
            FileName = "Assets/2D/Interface/Common/UnitsIcons/Armes/Panel_Info/Test_75mm.png"
        )
        export Texture_Interface_Weapon_Test_Howitzer is TUIResourceTexture_Common
        (
            FileName = "Assets/2D/Interface/Common/UnitsIcons/Armes/Panel_Info/Test_Howitzer.png"
        )
        """;

    // the weapon-against-armour table, whose keys are the game's kinds of damage: AP_1 to AP_30, and others
    private static string DamageTable() =>
        "private ArmeArmureParams is TGameplayArmeArmureContainer\n(\n    Values = MAP\n    [\n"
        + string.Concat(Enumerable.Range(1, 30).Select(level => "        (\n            EArmeType/AP_" + level + ",\n            [ 1.0, ]\n        ),\n        (\n            EArmeType/AP_" + level + "_Missile,\n            [ 1.0, ]\n        ),\n"))
        + "        (\n            EArmeType/HE,\n            [ 1.0, ]\n        ),\n    ]\n)\n";

    private static Dictionary<string, string> Files() => new()
    {
        ["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = Units,
        ["Generated/Gameplay/Gfx/WeaponDescriptor.ndf"] = Loadouts,
        ["Generated/Gameplay/Gfx/Ammunition.ndf"] = Ammunition,
        ["Generated/Gameplay/Gfx/ArmesArmures.ndf"] = DamageTable(),
        ["Generated/Gameplay/UI/Resources/Textures/WeaponTextures.ndf"] = Pictures,
        ["Gameplay/Constantes/GDConstantes.ndf"] = Constants,
    };

    private static UnitCatalog Read(string zip, IReadOnlyList<ChangedFile>? changed = null)
    {
        using var files = new GameDataFiles(zip, null, changed);
        return UnitReader.Read(files, GameTitle.SteelDivision2, GameTexts.FromPairs([]));
    }

    private static (string Zip, UnitCatalog Game) GameFiles(TemporaryFolder temp, Dictionary<string, string> files)
    {
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, files);
        return (zip, Read(zip));
    }

    /// <summary>Runs the rules on Ammunition.ndf, as the export does: every rule matched and nothing skipped.</summary>
    private static string RunOnAmmunition(JsonArray rules, string text)
    {
        JsonArray forFile = DivisionChangeRules.ForFile(rules, "Ammunition.ndf");
        Assert.NotEmpty(forFile);
        var engine = new NdfEngine();
        engine.Load(text);
        RunResult result = engine.Run(RuleLoading.LoadForEngine(forFile), new RunOptions());
        Assert.Equal("", result.ParseError);
        Assert.All(result.Stats, rule => Assert.Equal(0, rule.Skipped));
        Assert.All(result.Stats.Where(rule => rule.Kind != "Group"), rule => Assert.True(rule.Matched > 0, rule.Name + " matched nothing"));
        return result.Text!;
    }

    /// <summary>The weapon read back from the files the rules wrote.</summary>
    private static Weapon Exported(string zip, string written, string descriptor) =>
        Read(zip, [new ChangedFile("C:/Mod/GameData/Generated/Gameplay/Gfx/Ammunition.ndf", written)]).Weapons!.Single(weapon => weapon.Descriptor == descriptor);

    /// <summary>The weapon as the Armory shows it with the mod's changes.</summary>
    private static Weapon Shown(Mod mod, UnitCatalog game, string descriptor) =>
        ModWeaponView.Apply(mod, game, game, new TextCatalog()).Catalog.Weapons!.Single(weapon => weapon.Descriptor == descriptor);

    /// <summary>The lines of a text that hold a property, trimmed, their spaces in a row as one: "PorteeMaximale = ((9000) * Metre)".</summary>
    private static List<string> Lines(string text, string property) =>
        [.. text.Split('\n').Select(line => Regex.Replace(line.Trim(), "[ \\t]+", " ")).Where(line => Regex.IsMatch(line, "^" + Regex.Escape(property) + " ?="))];

    /// <summary>The lines the rules changed, as the game wrote them: the files keep their lines, one for one.</summary>
    private static List<string> ChangedLines(string before, string after)
    {
        Assert.Equal(before.Split('\n').Length, after.Split('\n').Length);
        return [.. before.Split('\n').Zip(after.Split('\n')).Where(pair => pair.First != pair.Second).Select(pair => pair.First.Trim())];
    }

    /// <summary>The names of WARNO's ammunition properties, and its ways of writing values, that Steel Division 2's files must never get.</summary>
    private static readonly string[] WarnoOnly =
    [
        "GRU", "AimingTime", "TimeBetweenTwo", "ShotsCountPerSalvo", "SimultaneousShotsCount", "TraitsToken", "TDamageTypeRTTI", "DamageFamily_",
        "HasDeploymentTime", "$/GFX/Weapon/", "MaximumRange", "MinimumRange", "PitchForParabolic",
    ];

    /// <summary>A value for every stat a weapon has that the Armory writes for Steel Division 2: whole where the game writes one, within its limits.</summary>
    private static double ValueFor(WeaponStat stat) =>
        stat.Field == WeaponField.DamageLevel ? 25
        : stat.Highest is 1 ? 0.5
        : stat.IsWholeIn(GameTitle.SteelDivision2) ? 3 : 2.5;

    [Fact]
    public void Its_weapons_are_read_with_the_numbers_the_game_shows()
    {
        using var temp = new TemporaryFolder();
        (_, UnitCatalog game) = GameFiles(temp, Files());

        Weapon gun = game.Weapons!.Single(weapon => weapon.Descriptor == Gun);
        // the files' distances divided by 5, as the game shows metres
        Assert.Equal((1600.0, 0.0, 100.0, 0.0, 5.0), (gun.Range, gun.MinimumRange, gun.HelicopterRange, gun.PlaneRange, gun.MetresScale));
        Assert.Equal((0.2, 24.0, 4.0, 1.0), (gun.DamageRadius, gun.SuppressionRadius, gun.DispersionFar, gun.DispersionNear));
        Assert.Equal((3.0, 7.0, 1.0, 30.0, 45.0, 20.0), (gun.AimingTime, gun.Reload, gun.ShotsPerSalvo, gun.SupplyCost, gun.AccuracyStopped, gun.AccuracyMoving));
        Assert.Equal(("AP", 20), (gun.DamageFamily, gun.DamageLevel));
        // values kept under WARNO's names, read from the game's own
        Assert.Equal("EArmeType/AP_20", gun.FileValue(WeaponFileValues.DamageFamily));
        Assert.Equal((6.0, -1.0, 6.0, 1.0), (gun.FileNumber("TimeBetweenTwoShots"), gun.FileNumber("TimeBetweenTwoSalvos_Max"), gun.FileNumber("TimeBetweenTwoFx"), gun.FileNumber(WeaponFileValues.SimultaneousShots)));
        Assert.Equal(0.0, gun.FileNumber("MinimumRangeHelicopterGRU"));
        Assert.Null(gun.FileValue("HasDeploymentTime"));
        Assert.Equal("GameData:/Assets/2D/Interface/Common/UnitsIcons/Armes/Panel_Info/Test_75mm.png", gun.PictureSource);
        Assert.Equal(["Descriptor_Unit_Test_Tank_US"], gun.Users);
        // the AP levels are those of the game's weapon-against-armour table
        Assert.Equal(30, game.ArmorLevels!["DamageFamily_AP"]);
    }

    [Fact]
    public void Every_value_the_pane_changes_lands_in_its_own_property_in_the_games_form_and_reads_back_as_shown()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = Files();
        (string zip, UnitCatalog game) = GameFiles(temp, files);
        Weapon gun = game.Weapons!.Single(weapon => weapon.Descriptor == Gun);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        WeaponEdit edit = WeaponEdit.For(mod, Gun);
        // every number it has that the game writes
        foreach (WeaponStat stat in WeaponStat.All.Where(stat => stat.Read(gun) is not null && WeaponChangeRules.Writes(GameTitle.SteelDivision2, stat.Field, gun)))
            edit.Give(stat.Field, ValueFor(stat), GameTitle.SteelDivision2);
        // every switch and choice it has, but the kind of damage, whose level is given above
        foreach (WeaponChoice choice in WeaponChoice.All.Where(choice => choice.Read(gun) is not null && choice.Field != WeaponFileValues.DamageFamily))
        {
            Assert.True(WeaponChangeRules.Writes(GameTitle.SteelDivision2, choice.Field), choice.Field);
            if (choice.Offered(gun, game.Weapons!, game.ArmorLevels).FirstOrDefault(value => value != choice.Read(gun)) is string other) edit.Choose(choice.Field, other);
        }
        edit.Picture = "Texture_Interface_Weapon_Test_Howitzer";
        Assert.True(UnitChangeRules.WritesEverythingOf(mod));

        JsonArray rules = WeaponChangeRules.For(mod, game.Weapons!, new TextCatalog(), loadout => loadout);
        string written = RunOnAmmunition(rules, files["Generated/Gameplay/Gfx/Ammunition.ndf"]);

        Assert.Equal(2, Parser.Parse(written).Declarations.Count);
        // each in the game's own form
        Assert.Equal(["PorteeMaximale = ((12.5) * Metre)", "PorteeMaximale = ((9000) * Metre)"], Lines(written, "PorteeMaximale"));
        Assert.Equal(["Arme = EArmeType/AP_25", "Arme = EArmeType/HE"], Lines(written, "Arme"));
        Assert.Equal(["PhysicalDamages = 2.5", "PhysicalDamages = 2.0"], Lines(written, "PhysicalDamages"));
        Assert.Equal(["SupplyCost = 3", "SupplyCost = 40"], Lines(written, "SupplyCost"));
        Assert.Equal(["NbTirParSalves = 3", "NbTirParSalves = 2"], Lines(written, "NbTirParSalves"));
        Assert.Equal(["TempsDeVisee = 2.5", "TempsDeVisee = 4.0"], Lines(written, "TempsDeVisee"));
        Assert.Contains("(EBaseHitValueModifier/Idling, 3)", written, StringComparison.Ordinal);
        Assert.Equal(["InterfaceWeaponTexture = \"Texture_Interface_Weapon_Test_Howitzer\"", "InterfaceWeaponTexture = \"Texture_Interface_Weapon_Test_Howitzer\""],
            Lines(written, "InterfaceWeaponTexture"));
        // nothing of WARNO's: neither its names in the rules nor in the file
        IEnumerable<string> targets = rules.SelectMany(rule => rule?["Actions"]?.AsArray() ?? []).Select(action => (string?)action?["Target"] ?? "");
        Assert.All(targets, target => Assert.DoesNotContain(WarnoOnly, warno => target.Contains(warno, StringComparison.Ordinal)));
        Assert.All(WarnoOnly, warno => Assert.DoesNotContain(warno, written, StringComparison.Ordinal));
        // a line for each value given, in the gun only: the other ammunition is as it was
        Assert.Equal(edit.FieldsGiven().Count(), ChangedLines(Ammunition, written).Count);
        Assert.Equal(Ammunition[Ammunition.IndexOf("export " + Shell, StringComparison.Ordinal)..], written[written.IndexOf("export " + Shell, StringComparison.Ordinal)..]);
        // read back as the Armory shows it, every value the same
        Weapon exported = Exported(zip, written, Gun);
        Assert.Empty(WeaponChanges.FieldsChanged(Shown(mod, game, Gun), exported));
        Assert.Equal((2.5, 25), (exported.Range, exported.DamageLevel));
    }

    [Fact]
    public void A_distance_typed_with_decimals_keeps_them_in_the_mod_and_in_the_files_own_units()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = Files();
        (string zip, UnitCatalog game) = GameFiles(temp, files);
        Weapon gun = game.Weapons!.Single(weapon => weapon.Descriptor == Gun);
        WeaponStat radius = WeaponStat.Of(WeaponField.SuppressionRadius)!;
        // whole GRU in WARNO; metres with decimals in Steel Division 2 (127 in the files is 25.4 m)
        Assert.Null(radius.ValueOf("25.4", gun, game.ArmorLevels));
        Assert.Equal(25.4, radius.ValueOf("25.4", gun, game.ArmorLevels, game: GameTitle.SteelDivision2));
        Assert.Null(WeaponStat.Of(WeaponField.SupplyCost)!.ValueOf("2.5", gun, game.ArmorLevels, game: GameTitle.SteelDivision2));
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        WeaponEdit.For(mod, Gun).Give(WeaponField.SuppressionRadius, 25.4, GameTitle.SteelDivision2);

        Mod fromFile = ModFiles.Read(ModFiles.ExportText(mod), "My mod.json").Single();
        Assert.Equal(25.4, fromFile.WeaponEdits.Single().Given(WeaponField.SuppressionRadius));
        string written = RunOnAmmunition(WeaponChangeRules.For(fromFile, game.Weapons!, new TextCatalog(), loadout => loadout), files["Generated/Gameplay/Gfx/Ammunition.ndf"]);

        Assert.Equal(["RadiusSplashSuppressDamages = ((127) * Metre)", "RadiusSplashSuppressDamages = ((60) * Metre)"], Lines(written, "RadiusSplashSuppressDamages"));
        Assert.Equal(25.4, Exported(zip, written, Gun).SuppressionRadius);
    }

    [Fact]
    public void Its_kind_of_damage_is_chosen_with_its_level_and_only_AP_has_a_level()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = Files();
        (string zip, UnitCatalog game) = GameFiles(temp, files);
        Weapon gun = game.Weapons!.Single(weapon => weapon.Descriptor == Gun);
        Weapon shell = game.Weapons!.Single(weapon => weapon.Descriptor == Shell);
        WeaponStat level = WeaponStat.Of(WeaponField.DamageLevel)!;
        // AP's levels are the game's: 1 to 30 here; a kind without levels has none to change
        Assert.Null(level.ValueOf("31", gun, game.ArmorLevels, game: GameTitle.SteelDivision2));
        Assert.True(WeaponChangeRules.Writes(GameTitle.SteelDivision2, WeaponField.DamageLevel, gun));
        Assert.False(WeaponChangeRules.Writes(GameTitle.SteelDivision2, WeaponField.DamageLevel, shell));
        // a new level is a new kind of damage, kept as the choice so the two are never written apart
        Assert.Equal("EArmeType/AP_28", WeaponChangeRules.LevelAsDamageType(GameTitle.SteelDivision2, gun, 28));
        Assert.Null(WeaponChangeRules.LevelAsDamageType(GameTitle.SteelDivision2, shell, 28));
        Assert.Null(WeaponChangeRules.LevelAsDamageType(GameTitle.Warno, gun, 28));
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        WeaponEdit.For(mod, Gun).Choose(WeaponFileValues.DamageFamily, "EArmeType/AP_28");
        WeaponEdit.For(mod, Shell).Choose(WeaponFileValues.DamageFamily, "EArmeType/AP_20");
        Assert.Contains("EArmeType/HE", WeaponChoice.Of(WeaponFileValues.DamageFamily)!.Offered(gun, game.Weapons!, game.ArmorLevels));

        string written = RunOnAmmunition(WeaponChangeRules.For(mod, game.Weapons!, new TextCatalog(), loadout => loadout), files["Generated/Gameplay/Gfx/Ammunition.ndf"]);

        Assert.Equal(["Arme = EArmeType/AP_28", "Arme = EArmeType/AP_20"], Lines(written, "Arme"));
        foreach (string descriptor in new[] { Gun, Shell })
            Assert.Empty(WeaponChanges.FieldsChanged(Shown(mod, game, descriptor), Exported(zip, written, descriptor)));
        Assert.Equal(("AP", 20), (Shown(mod, game, Shell).DamageFamily, Shown(mod, game, Shell).DamageLevel));
    }

    [Fact]
    public void What_its_ammunition_lacks_is_never_written()
    {
        using var temp = new TemporaryFolder();
        (_, UnitCatalog game) = GameFiles(temp, Files());
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        // WARNO's values with no property in Steel Division 2's ammunition
        foreach (string field in new[] { WeaponField.Traits, "TandemCharge", "HasDeploymentTime", "CanShootWhileMoving", "PitchForParabolic", "ProjectileSpeedGRU", "Guidance" })
            Assert.False(WeaponChangeRules.Writes(GameTitle.SteelDivision2, field), field);
        WeaponEdit.For(mod, Gun).Traits = ["HEAT"];
        Assert.False(UnitChangeRules.WritesEverythingOf(mod));
        Assert.Empty(WeaponChangeRules.For(mod, game.Weapons!, new TextCatalog(), loadout => loadout));
        Assert.True(WeaponChangeRules.Writes(GameTitle.Warno, WeaponField.Traits));
    }

    [Fact]
    public void A_new_weapon_is_a_copy_with_GUIDs_of_its_own_given_to_the_chosen_units_loadouts()
    {
        using var temp = new TemporaryFolder();
        (string zip, UnitCatalog game) = GameFiles(temp, Files());
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        var copy = new ModWeapon { Descriptor = "Ammo_EZ_Copy", MadeFrom = Gun, File = "Ammunition.ndf", Name = "COPY", Loadouts = ["WeaponDescriptor_Test_Tank_US"] };
        mod.Weapons.Add(copy);

        Assert.True(WeaponChangeRules.AddsWeapons(GameTitle.SteelDivision2));
        Assert.True(UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = WeaponChangeRules.For(mod, game.Weapons!, new TextCatalog(), loadout => loadout);
        string ammunition = RunOnAmmunition(rules, Ammunition);
        var engine = new NdfEngine();
        engine.Load(Loadouts);
        RunResult loadouts = engine.Run(RuleLoading.LoadForEngine(DivisionChangeRules.ForFile(rules, "WeaponDescriptor.ndf")), new RunOptions());

        // the copy, after its original, with GUIDs of its own: its own and its hit roll's
        List<Declaration> declarations = Parser.Parse(ammunition).Declarations;
        Declaration made = declarations.Single(declaration => declaration.Name == "Ammo_EZ_Copy");
        string original = Engine.Writing.CanonicalWriter.Write(declarations.Single(declaration => declaration.Name == Gun).Value);
        string copied = Engine.Writing.CanonicalWriter.Write(made.Value);
        List<string> Guids(string text) => [.. Regex.Matches(text, "GUID:\\{[0-9a-f-]+\\}").Select(match => match.Value)];
        Assert.Equal(Guids(original).Count, Guids(copied).Count);
        Assert.Empty(Guids(copied).Intersect(Guids(original)));
        // the chosen loadout names the copy, as the game names ammunition there
        Assert.Contains("~/Ammo_EZ_Copy", loadouts.Text!, StringComparison.Ordinal);
        Assert.DoesNotContain("$/GFX/Weapon/", loadouts.Text!, StringComparison.Ordinal);
        UnitCatalog exported = Read(zip, [new ChangedFile("C:/Mod/GameData/Generated/Gameplay/Gfx/Ammunition.ndf", ammunition),
            new ChangedFile("C:/Mod/GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf", loadouts.Text!)]);
        Assert.Contains(exported.Weapons!, weapon => weapon.Descriptor == "Ammo_EZ_Copy");
    }

    // ---- the game's own files, where it is installed ----

    /// <summary>The Steel Division 2 mod made with the game's CreateNewMod on the test computer; the tests pass without looking where it is not.</summary>
    private const string InstalledGameData = @"E:\Steel Division 2\Mods\Test Mod\GameData";

    private static string InstalledFile(params string[] parts) => Path.Combine([InstalledGameData, .. parts]);

    /// <summary>One declaration of a real file of the game, as written.</summary>
    private static string DeclarationIn(string file, string name)
    {
        using var reader = new StreamReader(file);
        return UnitReader.DeclarationTexts(reader).First(declaration => declaration.Name == name).Text;
    }

    [Fact]
    public void A_real_weapon_of_the_game_takes_the_changes_and_only_the_intended_lines_change()
    {
        string ammunitionFile = InstalledFile("Generated", "Gameplay", "Gfx", "Ammunition.ndf");
        if (!File.Exists(ammunitionFile)) return;
        const string gun = "Ammo_Canon_M1A1_76mmL_AP";
        string ammunition = DeclarationIn(ammunitionFile, gun);
        var files = new Dictionary<string, string>
        {
            ["Generated/Gameplay/Gfx/Ammunition.ndf"] = ammunition,
            ["Generated/Gameplay/Gfx/WeaponDescriptor.ndf"] = DeclarationIn(InstalledFile("Generated", "Gameplay", "Gfx", "WeaponDescriptor.ndf"), "WeaponDescriptor_M4A1_76_US"),
            ["Generated/Gameplay/Gfx/ArmesArmures.ndf"] = File.ReadAllText(InstalledFile("Generated", "Gameplay", "Gfx", "ArmesArmures.ndf")),
            ["Gameplay/Constantes/GDConstantes.ndf"] = File.ReadAllText(InstalledFile("Gameplay", "Constantes", "GDConstantes.ndf")),
        };
        using var temp = new TemporaryFolder();
        (string zip, UnitCatalog game) = GameFiles(temp, files);
        Weapon before = game.Weapons!.Single();
        // as the game shows it: 8750 / 5 m, AP 26; its AP levels go up to 60
        Assert.Equal((1750.0, "AP", 26, 60), (before.Range, before.DamageFamily, before.DamageLevel, game.ArmorLevels!["DamageFamily_AP"]));
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        WeaponEdit edit = WeaponEdit.For(mod, gun);
        edit.Give(WeaponField.Range, 1800, GameTitle.SteelDivision2);
        edit.Give(WeaponField.Damage, 9, GameTitle.SteelDivision2);
        edit.Give(WeaponField.SuppressionRadius, 30, GameTitle.SteelDivision2);
        edit.Give(WeaponField.AimingTime, 2.5, GameTitle.SteelDivision2);
        edit.Give(WeaponField.Reload, 7, GameTitle.SteelDivision2);
        edit.Give(WeaponField.ShotsPerSalvo, 2, GameTitle.SteelDivision2);
        edit.Give(WeaponField.SupplyCost, 40, GameTitle.SteelDivision2);
        edit.Give(WeaponField.AccuracyStopped, 50, GameTitle.SteelDivision2);
        edit.Give("TimeBetweenTwoShots", 6.5, GameTitle.SteelDivision2);
        edit.Choose(WeaponFileValues.DamageFamily, WeaponChangeRules.LevelAsDamageType(GameTitle.SteelDivision2, before, 30));
        edit.Choose("TirReflexe", WeaponChoice.Off);

        string written = RunOnAmmunition(WeaponChangeRules.For(mod, game.Weapons!, new TextCatalog(), loadout => loadout), ammunition);

        Assert.Single(Parser.Parse(written).Declarations);
        Assert.Equal(["PorteeMaximale = ((9000) * Metre)"], Lines(written, "PorteeMaximale"));
        Assert.Equal(["RadiusSplashSuppressDamages = ((150) * Metre)"], Lines(written, "RadiusSplashSuppressDamages"));
        Assert.Equal(["Arme = EArmeType/AP_30"], Lines(written, "Arme"));
        Assert.Equal(["SupplyCost = 40"], Lines(written, "SupplyCost"));
        // everything else is left as the game wrote it, line for line
        Assert.All(ChangedLines(ammunition, written), line => Assert.Matches(
            "^(PorteeMaximale |PhysicalDamages|RadiusSplashSuppressDamages|TempsDeVisee|TempsEntreDeuxSalves |NbTirParSalves|SupplyCost|\\(EBaseHitValueModifier/Idling|TempsEntreDeuxTirs |Arme |TirReflexe)", line));
        Assert.Equal(11, ChangedLines(ammunition, written).Count);
        Assert.All(WarnoOnly, warno => Assert.DoesNotContain(warno, written, StringComparison.Ordinal));
        Weapon exported = Exported(zip, written, gun);
        Assert.Empty(WeaponChanges.FieldsChanged(Shown(mod, game, gun), exported));
    }

    [Fact]
    public void Each_property_written_is_found_once_at_most_in_every_real_ammunition()
    {
        string ammunitionFile = InstalledFile("Generated", "Gameplay", "Gfx", "Ammunition.ndf");
        if (!File.Exists(ammunitionFile)) return;
        // each property a rule's target names, by name (its first part, for a path), and the hit roll's keys
        List<string> names = [.. WeaponStat.All.Select(stat => stat.Field).Concat(WeaponChoice.All.Select(choice => choice.Field)).Append(WeaponField.Picture)
            .Select(SteelDivisionAmmunition.Of).OfType<SteelDivisionAmmunition.Property>().Select(property => property.Name.Split(ActionApplier.PathSeparator)[0]).Distinct()];
        string[] keys = ["EBaseHitValueModifier/Idling", "EBaseHitValueModifier/Moving", "EBaseHitValueModifier/Base", "EBaseHitValueModifier/Targeted"];
        int read = 0;
        using var reader = new StreamReader(ammunitionFile);
        foreach ((string name, string text) in UnitReader.DeclarationTexts(reader).Where(declaration => declaration.Name.StartsWith("Ammo_", StringComparison.Ordinal)))
        {
            NdfValue ammunition = Parser.Parse(text).Declarations.Single().Value;
            List<Property> properties = [.. NdfValues.AllProperties(ammunition)];
            foreach (string property in names)
                Assert.True(properties.Count(each => each.Name == property) <= 1, name + " has " + property + " more than once");
            string hitRoll = NdfValues.Find(ammunition, "BaseHitValueModifiers") is NdfValue chances ? Engine.Writing.CanonicalWriter.Write(chances) : "";
            foreach (string key in keys)
                Assert.True(Regex.Matches(hitRoll, Regex.Escape(key) + "[ \\t]*,").Count <= 1, name + " has " + key + " more than once");
            read++;
        }
        Assert.True(read > 1000);
    }
}
