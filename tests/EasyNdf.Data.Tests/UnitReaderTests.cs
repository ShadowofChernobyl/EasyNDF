using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

public class UnitReaderTests
{
    // ---- made-up GameData in each game's layout ----

    internal const string WarnoUnits = """
        // a comment before the first declaration
        export Descriptor_Unit_Tank_US is TEntityDescriptor
        (
            ModulesDescriptors = [
                TTypeUnitModuleDescriptor
                (
                    Coalition = TWargameCoalition/NATO
                    MotherCountry = 'US'
                ),
                $/GFX/Weapon/WeaponDescriptor_Tank_US,
                TBaseDamageModuleDescriptor( MaxPhysicalDamages = 10 ),
                TDamageModuleDescriptor
                (
                    BlindageProperties = TBlindageProperties
                    (
                        ResistanceFront = TResistanceTypeRTTI(Family=ResistanceFamily_blindage Index=18)
                        ResistanceSides = TResistanceTypeRTTI(Family=ResistanceFamily_blindage Index=8)
                        ResistanceRear = TResistanceTypeRTTI(Family=ResistanceFamily_blindage Index=5)
                        ResistanceTop = TResistanceTypeRTTI(Family=ResistanceFamily_blindage Index=3)
                    )
                ),
                TVisibilityModuleDescriptor( UnitConcealmentBonus = 1.0 ),
                TGenericMovementModuleDescriptor( MaxSpeedInKmph = 65 ),
                TFuelModuleDescriptor( FuelCapacity = 1900 ),
                TScannerConfigurationDescriptor
                (
                    VisionRangesGRU = MAP [ ( EVisionRange/Standard, 3500.0 ), ( EVisionRange/LowAltitude, 4947.0 ) ]
                    OpticalStrengths = MAP [ ( EOpticalStrength/Standard, 2473.0 ) ]
                ),
                TProductionModuleDescriptor
                (
                    FactoryType = Factory/Tanks
                    ProductionRessourcesNeeded = MAP [ ($/GFX/Resources/Resource_CommandPoints, 275), ($/GFX/Resources/Resource_Tickets, 4) ]
                ),
                TUnitUIModuleDescriptor
                (
                    UnitRole = 'armor'
                    SpecialtiesList = [ '_smoke_launcher', 'leader' ]
                    NameToken = 'TANKNAME'
                    DisplayRoadSpeedInKmph = 75
                    ButtonTexture = 'Texture_Button_Unit_Tank_US'
                ),
            ]
        )
        export Descriptor_Unit_Truck_SOV is TEntityDescriptor
        (
            ModulesDescriptors = [
                TTypeUnitModuleDescriptor( Coalition = TWargameCoalition/PACT MotherCountry = 'SOV' ),
                TProductionModuleDescriptor( FactoryType = Factory/Logistic ),
            ]
        )
        export NotAUnit is TEntityDescriptor ( )
        """;

    private const string WarnoWeapons = """
        export WeaponDescriptor_Tank_US is TWeaponManagerModuleDescriptor
        (
            TurretDescriptorList = [
                TTurretTwoAxisDescriptor
                (
                    MountedWeaponDescriptorList = [
                        TMountedWeaponDescriptor( Ammunition = $/GFX/Weapon/Ammo_Canon_AP_120mm NbWeapons = 1 ),
                        TMountedWeaponDescriptor( Ammunition = $/GFX/Weapon/Ammo_SAM_Stinger NbWeapons = 2 ),
                    ]
                ),
            ]
        )
        """;

    private const string WarnoAmmunition = """
        Ammo_Canon_AP_120mm is TAmmunitionDescriptor
        (
            Arme = TDamageTypeRTTI(Family=DamageFamily_ap Index=32)
            MaximumRangeGRU = 2275
            MaximumRangeHelicopterGRU = 0
            PhysicalDamages = 1.0
            SuppressDamages = 82.0
            TimeBetweenTwoSalvos = 6.5
            MinimumRangeGRU = 35
            RadiusSplashPhysicalDamagesGRU = 1
            RadiusSplashSuppressDamagesGRU = 42
            DispersionAtMaxRangeGRU = 106
            DispersionAtMinRangeGRU = 18
            TraitsToken = [ 'MOTION', 'KINETIC', ]
            HitRollRuleDescriptor = TDiceHitRollRuleDescriptor
            (
                BaseHitValueModifiers = [ (EBaseHitValueModifier/Base, 0), (EBaseHitValueModifier/Idling, 65), (EBaseHitValueModifier/Moving, 60), ]
            )
            AimingTime = 3.0
            ShotsCountPerSalvo = 1
            SupplyCost = 12.0
            HasDeploymentTime = False
            InterfaceWeaponTexture = "Texture_Interface_Weapon_M68_120"
        )
        """;

    private const string WarnoWeaponTextures = """
        Texture_Interface_Weapon_M68_120 is TUIResourceTexture_Common
        (
            FileName = "GameData:/Assets/2D/Interface/Common/UnitsIcons/Armes/Panel_Info/M68_120.png"
        )
        """;

    private const string WarnoMissiles = """
        Ammo_SAM_Stinger is TAmmunitionDescriptor
        (
            Arme = TDamageTypeRTTI(Family=DamageFamily_missile_he Index=1)
            MaximumRangeGRU = 0
            MaximumRangeHelicopterGRU = 2650
            MaximumRangeAirplaneGRU = 2450
            PhysicalDamages = 5
            SuppressDamages = 192
            TimeBetweenTwoSalvos = 5
            MissileDescriptor = ~/Descriptor_Missile_Stinger
        )
        """;

    private const string WarnoDivisions = """
        export Descriptor_Deck_Division_US_Armored_multi is TDeckDivisionDescriptor
        (
            CfgName = 'US_Armored_multi'
            DivisionName = 'DIVNAME'
            DivisionTags = ['DEFAULT', 'US']
            DivisionRule = Descriptor_Deck_Division_US_Armored_multi_Rule
        )
        export Descriptor_Deck_Division_US_Armored_solo is TDeckDivisionDescriptor
        (
            CfgName = 'US_Armored_solo'
            DivisionTags = ['US']
            DivisionRule = Descriptor_Deck_Division_US_Armored_solo_Rule
        )
        export Descriptor_Deck_Division_US_Light_multi is TDeckDivisionDescriptor
        (
            CfgName = 'US_Light_multi'
            DivisionName = 'NOTEXT'
            DivisionTags = ['DEFAULT']
            DivisionRule = Descriptor_Deck_Division_US_Light_multi_Rule
        )
        """;

    private const string WarnoDivisionRules = """
        Descriptor_Deck_Division_US_Armored_multi_Rule is TDeckDivisionRule
        (
            UnitRuleList = [ TDeckUniteRule( UnitDescriptor = $/GFX/Unit/Descriptor_Unit_Tank_US MaxPackNumber = 2 ) ]
        )
        Descriptor_Deck_Division_US_Armored_solo_Rule is TDeckDivisionRule
        (
            UnitRuleList = [ TDeckUniteRule( UnitDescriptor = $/GFX/Unit/Descriptor_Unit_Truck_SOV ) ]
        )
        Descriptor_Deck_Division_US_Light_multi_Rule is TDeckDivisionRule
        (
            UnitRuleList = [ TDeckUniteRule( UnitDescriptor = $/GFX/Unit/Descriptor_Unit_Tank_US ) ]
        )
        """;

    private const string WarnoButtonTextures = """
        export ButtonTexturesUnites is TBUCKToolResourceTextureListDescriptor
        (
            Values = MAP [
                ("Texture_Button_Unit_Tank_US", MAP [( ~/ComponentState/Normal, TUIResourceTexture(FileName = "GameData:/Assets/2D/Interface/Common/UnitsIcons/NATO/Tank_US.png"))]),
            ]
        )
        """;

    private const string WarnoCommonTextures = """
        CommonTexture_MotherCountryFlag_US is TUIResourceTexture_Common(FileName = "GameData:/Assets/2D/Interface/Common/Flags/US_FLAG.png")
        CommonTexture_MotherCountryFlag_NATO_small is TUIResourceTexture_Common(FileName = "GameData:/Assets/2D/Interface/Common/Flags/NATO_FLAG_small.png")
        """;

    internal static Dictionary<string, string> WarnoFiles() => new()
    {
        ["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = WarnoUnits,
        ["Generated/Gameplay/Gfx/WeaponDescriptor.ndf"] = WarnoWeapons,
        ["Generated/Gameplay/Gfx/Ammunition.ndf"] = WarnoAmmunition,
        ["Generated/Gameplay/Gfx/AmmunitionMissiles.ndf"] = WarnoMissiles,
        ["Generated/Gameplay/Decks/Divisions.ndf"] = WarnoDivisions,
        ["Generated/Gameplay/Decks/DivisionRules.ndf"] = WarnoDivisionRules,
        ["UserInterface/Textures/ButtonTexturesUnites.ndf"] = WarnoButtonTextures,
        ["UserInterface/Use/Common/CommonTextures.ndf"] = WarnoCommonTextures,
        ["Generated/Gameplay/Gfx/DamageResistance.ndf"] = WarnoDamageResistance,
        ["Generated/UserInterface/Textures/WeaponTextures.ndf"] = WarnoWeaponTextures,
    };

    private const string WarnoDamageResistance = """
        private DamageResistanceParams is TGameplayDamageResistanceContainer
        (
            ResistanceFamilyCounts = [
                (ResistanceFamily_blindage, 30),
                (ResistanceFamily_helico, 3),
            ]
            DamageFamilyCounts = [ (DamageFamily_ap, 40) ]
        )
        """;

    internal static readonly GameTexts WarnoTexts = GameTexts.FromPairs([("TANKNAME", "M1 TANK"), ("DIVNAME", "1st Armored")]);

    private static UnitCatalog ReadMadeUp(TemporaryFolder temp, Dictionary<string, string> files, GameTitle game, GameTexts texts, string? modFolder = null)
    {
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, files);
        using var gameData = new GameDataFiles(zip, modFolder);
        return UnitReader.Read(gameData, game, texts);
    }

    [Fact]
    public void A_field_a_unit_has_more_than_once_is_read_where_it_is_found_first()
    {
        // the order a unit's fields are looked through: each object's own fields, then the objects inside it, before
        // the next object; so a field nested in an earlier module comes before the same field in a later one, and an
        // object's own field before one nested in it; and a module written twice is read from the first
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = WarnoFiles();
        files["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = """
            export Descriptor_Unit_Twice_US is TEntityDescriptor
            (
                ModulesDescriptors = [
                    TTypeUnitModuleDescriptor
                    (
                        Nested = TSomething( UnitConcealmentBonus = 1.5 MotherCountry = 'SOV' )
                        Coalition = TWargameCoalition/NATO
                        MotherCountry = 'US'
                    ),
                    TVisibilityModuleDescriptor( UnitConcealmentBonus = 2.5 ),
                    TFuelModuleDescriptor( Inner = TSomething( FuelCapacity = 10 ) FuelCapacity = 20 ),
                    TTagsModuleDescriptor( TagSet = ['First'] ),
                    TTagsModuleDescriptor( TagSet = ['Second'] ),
                    TProductionModuleDescriptor( FactoryType = Factory/Tanks ),
                    TTransporterModuleDescriptor( TransportableTagSet = ['First'] NbSeatsAvailable = 4 ),
                    TTransporterModuleDescriptor( TransportableTagSet = ['Second'] NbSeatsAvailable = 9 ),
                ]
            )
            """;

        Unit unit = ReadMadeUp(temp, files, GameTitle.Warno, WarnoTexts).Units.Single();

        Assert.Equal("US", unit.Nation);
        Assert.Equal(1.5, unit.Stealth);
        Assert.Equal(20, unit.Fuel);
        Assert.Equal(["First"], unit.Tags);
        Assert.Equal(["First"], unit.CarriesTags);
        Assert.Equal(4, unit.Seats);
        Assert.Equal("NATO", unit.Side);
        Assert.Equal("Tanks", unit.Factory);
    }

    [Fact]
    public void The_weapon_traits_are_read_with_their_names_and_pictures()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = WarnoFiles();
        files["Generated/UserInterface/WeaponTraits.ndf"] = """
            WeaponTraits is TWeaponTraitsContainer
            (
                Descriptors = MAP
                [
                    (
                        "KE",
                        TWeaponTraitDescriptor
                        (
                            TraitTextureName       = "Texture_Trait_Icon_KE"
                            TraitHintTitleToken    = "KINETICT"
                            ShowAsFilterInShowroom = True
                        )
                    ),
                    (
                        "F&F",
                        TWeaponTraitDescriptor
                        (
                            TraitTextureName       = "Texture_Trait_Icon_fireAndForget"
                            TraitHintTitleToken    = "UNKNOWN"
                        )
                    ),
                ]
            )
            """;
        files["UserInterface/Use/Common/BUCKTextureBank.ndf"] = """
            TextureBank is TBUCKTextureBank
            (
                Textures = MAP [
                    ("Texture_Trait_Icon_ke",   MAP [(~/ComponentState/Normal, TUIResourceTexture_Common( FileName='GameData:/Assets/2D/Interface/Common/traits/KE.png' )), ] ),
                ]
            )
            """;
        GameTexts texts = GameTexts.FromPairs([("KINETICT", "Kinetic")]);

        UnitCatalog catalog = ReadMadeUp(temp, files, GameTitle.Warno, texts);

        Assert.Equal(new Weapons.WeaponTrait("KE", "Kinetic", "GameData:/Assets/2D/Interface/Common/traits/KE.png"), catalog.Traits!["KE"]);
        Assert.Equal(new Weapons.WeaponTrait("F&F", "F&F", null), catalog.Traits["F&F"]);
    }

    [Fact]
    public void The_weapons_are_read_from_the_ammunition_with_their_values_picture_and_the_units_that_carry_them()
    {
        using var temp = new TemporaryFolder();
        UnitCatalog catalog = ReadMadeUp(temp, WarnoFiles(), GameTitle.Warno, WarnoTexts);

        Assert.Equal(["Ammo_Canon_AP_120mm", "Ammo_SAM_Stinger"], catalog.Weapons!.Select(weapon => weapon.Descriptor));
        Weapons.Weapon cannon = catalog.Weapons![0];
        Assert.Equal(("Canon AP 120mm", WeaponKind.AntiArmor, "ap", 32), (cannon.Name, cannon.Kind, cannon.DamageFamily, cannon.DamageLevel));
        Assert.Equal((2275.0, 35.0, 1.0, 82.0, 1.0, 42.0), (cannon.Range, cannon.MinimumRange, cannon.Damage, cannon.Suppression, cannon.DamageRadius, cannon.SuppressionRadius));
        Assert.Equal((65.0, 60.0, 106.0, 18.0), (cannon.AccuracyStopped, cannon.AccuracyMoving, cannon.DispersionFar, cannon.DispersionNear));
        Assert.Equal((3.0, 6.5, 1.0, 12.0, false), (cannon.AimingTime, cannon.Reload, cannon.ShotsPerSalvo, cannon.SupplyCost, cannon.MustDeploy));
        Assert.Equal(["MOTION", "KINETIC"], cannon.Traits);
        Assert.Equal("GameData:/Assets/2D/Interface/Common/UnitsIcons/Armes/Panel_Info/M68_120.png", cannon.PictureSource);
        Assert.Equal(["Descriptor_Unit_Tank_US"], cannon.Users);
        Weapons.Weapon stinger = catalog.Weapons![1];
        Assert.Equal((WeaponKind.Missile, "Descriptor_Missile_Stinger", 2650.0), (stinger.Kind, stinger.Missile, stinger.LongestRange));
    }

    [Fact]
    public void A_WARNO_unit_is_read_with_its_stats_weapons_divisions_and_picture()
    {
        using var temp = new TemporaryFolder();
        UnitCatalog catalog = ReadMadeUp(temp, WarnoFiles(), GameTitle.Warno, WarnoTexts);

        Assert.Empty(catalog.Problems);
        Assert.Equal(["Descriptor_Unit_Tank_US", "Descriptor_Unit_Truck_SOV"], catalog.Units.Select(u => u.Descriptor));
        Unit tank = catalog.Units[0];
        Assert.Equal("M1 TANK", tank.Name);
        Assert.Equal(("NATO", "US", "Tanks", "armor", null), (tank.Side, tank.Nation, tank.Factory, tank.Role, tank.RoleName));
        Assert.Equal((275.0, 10.0, 65.0, 75.0), (tank.Cost, tank.Strength, tank.Speed, tank.RoadSpeed));
        Assert.Equal((2473.0, 3500.0, 1.0, 1900.0), (tank.Optics, tank.Vision, tank.Stealth, tank.Fuel));
        Assert.Equal(new UnitArmor(18, 8, 5, 3), tank.Armor);
        Assert.Equal(["_smoke_launcher", "leader"], tank.Specialties);
        Assert.Equal("WeaponDescriptor_Tank_US", tank.WeaponSet);
        Assert.Equal("GameData:/Assets/2D/Interface/Common/UnitsIcons/NATO/Tank_US.png", tank.PictureSource);
        // the multiplayer divisions only (tagged DEFAULT), by their in-game names, or their own when the game has none
        Assert.Equal(["1st Armored", "US Light"], tank.Divisions);
        Assert.Empty(catalog.Units[1].Divisions);
    }

    [Fact]
    public void A_WARNO_units_weapons_come_from_its_ammunition()
    {
        using var temp = new TemporaryFolder();
        Unit tank = ReadMadeUp(temp, WarnoFiles(), GameTitle.Warno, WarnoTexts).Units[0];

        Assert.Equal(2, tank.Weapons.Count);
        UnitWeapon cannon = tank.Weapons[0];
        Assert.Equal(new UnitWeapon("Canon AP 120mm", 1, "ap", 32, 2275, 1, 82, 6.5, "Ammo_Canon_AP_120mm", "Ammunition.ndf", null), cannon);
        UnitWeapon missile = tank.Weapons[1];
        // its longest range, against helicopters; from the missiles' file
        Assert.Equal((2, "missile_he", 1, 2650.0), (missile.Count, missile.DamageFamily, missile.DamageLevel, missile.Range));
        Assert.Equal(("AmmunitionMissiles.ndf", "Descriptor_Missile_Stinger"), (missile.AmmunitionFile, missile.Missile));
        Assert.Equal(2650, tank.LongestRange);
    }

    [Fact]
    public void A_unit_without_a_name_in_the_game_gets_one_from_its_descriptor()
    {
        using var temp = new TemporaryFolder();
        Unit truck = ReadMadeUp(temp, WarnoFiles(), GameTitle.Warno, WarnoTexts).Units[1];

        Assert.Equal("Truck", truck.Name);
        Assert.Equal(("PACT", "SOV", "Logistic", ""), (truck.Side, truck.Nation, truck.Factory, truck.Role));
        Assert.Null(truck.Cost);
        Assert.Empty(truck.Weapons);
        Assert.Null(truck.LongestRange);
        Assert.Equal(new UnitArmor(null, null, null, null), truck.Armor);
    }

    [Fact]
    public void Nations_flags_are_found_and_variants_left_out()
    {
        using var temp = new TemporaryFolder();
        UnitCatalog catalog = ReadMadeUp(temp, WarnoFiles(), GameTitle.Warno, WarnoTexts);

        Assert.Equal("GameData:/Assets/2D/Interface/Common/Flags/US_FLAG.png", catalog.FlagSources["US"]);
        Assert.False(catalog.FlagSources.ContainsKey("NATO"));
    }

    [Fact]
    public void A_mods_own_copy_of_a_file_replaces_the_games()
    {
        using var temp = new TemporaryFolder();
        string mod = Path.Combine(temp.Path, "Mod");
        string modUnits = Path.Combine(mod, "GameData", "Generated", "Gameplay", "Gfx", "UniteDescriptor.ndf");
        Directory.CreateDirectory(Path.GetDirectoryName(modUnits)!);
        File.WriteAllText(modUnits, WarnoUnits.Replace("MaxSpeedInKmph = 65", "MaxSpeedInKmph = 99", StringComparison.Ordinal));

        UnitCatalog catalog = ReadMadeUp(temp, WarnoFiles(), GameTitle.Warno, WarnoTexts, mod);

        Assert.Equal(99, catalog.Units[0].Speed);
        // the files the mod has no copy of are the game's
        Assert.Equal(2, catalog.Units[0].Weapons.Count);
    }

    [Fact]
    public void A_missing_file_and_a_broken_declaration_are_noted_and_the_rest_still_read()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = WarnoFiles();
        files.Remove("Generated/Gameplay/Gfx/WeaponDescriptor.ndf");
        files["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = WarnoUnits + "\nexport Descriptor_Unit_Broken_US is TEntityDescriptor\n(\n    X = [ unclosed\n)\n";

        UnitCatalog catalog = ReadMadeUp(temp, files, GameTitle.Warno, WarnoTexts);

        Assert.Equal(2, catalog.Units.Count);
        Assert.Contains(catalog.Problems, p => p.StartsWith("WeaponDescriptor.ndf: not found", StringComparison.Ordinal));
        Assert.Contains(catalog.Problems, p => p.StartsWith("UniteDescriptor.ndf, Descriptor_Unit_Broken_US:", StringComparison.Ordinal));
        Assert.Empty(catalog.Units[0].Weapons);
    }

    // ---- Steel Division 2 ----

    private const string Sd2Units = """
        export Descriptor_Unit_Calliope_US is TEntityDescriptor
        (
            ModulesDescriptors = [
                TModuleSelector
                (
                    Default = TTypeUnitModuleDescriptor
                    (
                        Nationalite = ENationalite/Allied
                        MotherCountry = 'US'
                    )
                ),
                TModuleSelector( Default = $/GFX/Everything/WeaponDescriptor_Calliope_US ),
                TModuleSelector
                (
                    Default = TDamageModuleDescriptor
                    (
                        BlindageProperties = TBlindageProperties
                        (
                            ArmorDescriptorFront = ArmorDescriptor_Blindage_22
                            ArmorDescriptorSides = ArmorDescriptor_Blindage_11
                            ArmorDescriptorRear = ArmorDescriptor_Blindage_9
                            ArmorDescriptorTop = ArmorDescriptor_Toit_Lourd
                        )
                        MaxDamages = 10.0
                    )
                ),
                TModuleSelector( Default = TLandMovementModuleDescriptor( Maxspeed = ((68) * Metre) RealRoadSpeed = 38 ) ),
                TModuleSelector( Default = TScannerConfigurationDescriptor( PorteeVision = ((12500) * Metre) OpticalStrength = 70 ) ),
                TModuleSelector( Default = TVisibilityModuleDescriptor( UnitConcealmentBonus = 2.5 ) ),
                TModuleSelector
                (
                    Default = TModernWarfareProductionModuleDescriptor
                    (
                        Factory = EDefaultFactories/Art
                        ProductionRessourcesNeeded = MAP [ (~/Resource_CommandPoints, 130), (~/Resource_Tickets, 3) ]
                    )
                ),
                TModuleSelector( Default = TUnitUIModuleDescriptor( TypeSpecificToken = 'TMLRS' ButtonTexture = 'Texture_Button_Unit_Calliope_US' ) ),
            ]
        )
        """;

    private const string Sd2Weapons = """
        export WeaponDescriptor_Calliope_US is TWeaponManagerModuleDescriptor
        (
            TurretDescriptorList = [
                TTurretTwoAxisDescriptor( MountedWeaponDescriptorList = [ TMountedWeaponDescriptor( Ammunition = ~/Ammo_Roquettes_T34 ) ] ),
            ]
        )
        """;

    private const string Sd2Ammunition = """
        export Ammo_Roquettes_T34 is TAmmunitionDescriptor
        (
            Arme = EArmeType/Artillerie
            PorteeMaximale = ((21000) * Metre)
            PorteeMaximaleTBA = ((0) * Metre)
            PhysicalDamages = 5.7
            SuppressDamages = 424
            TempsEntreDeuxSalves = 240
            MissileDescriptor = nil
        )
        export Ammo_AP_Gun is TAmmunitionDescriptor
        (
            Arme = EArmeType/AP_14
        )
        """;

    private const string Sd2Divisions = """
        export Descriptor_Deck_Division_US_3rd_Armored is TDeckDivisionDescriptor
        (
            DivisionName = '3AD_Dv3'
            DivisionTags = ['West', 'DEFAULT', 'US']
        )
        """;

    private const string Sd2DivisionRules = """
        private DivisionRules is TDeckDivisionRules
        (
            DivisionRules = MAP
            [
                (
                    ~/Descriptor_Deck_Division_US_3rd_Armored,
                    TDeckDivisionRule
                    (
                        UnitRuleList = [ TDeckUniteRule( UnitDescriptor = ~/Descriptor_Unit_Calliope_US ) ]
                    )
                ),
            ]
        )
        """;

    private const string Sd2ButtonTextures = """
        export Texture_Button_Unit_Calliope_US is TUIResourceTextureWithUsefulnessMask
        (
            FileName = 'Assets\2D\Interface\Common\UnitsIcons\Allies\Calliope_US.png'
        )
        """;

    [Fact]
    public void A_Steel_Division_2_unit_is_read_from_its_own_layout()
    {
        using var temp = new TemporaryFolder();
        var files = new Dictionary<string, string>
        {
            ["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = Sd2Units,
            ["Generated/Gameplay/Gfx/WeaponDescriptor.ndf"] = Sd2Weapons,
            ["Generated/Gameplay/Gfx/Ammunition.ndf"] = Sd2Ammunition,
            ["Generated/Gameplay/Decks/Divisions.ndf"] = Sd2Divisions,
            ["Generated/Gameplay/Decks/DivisionRules.ndf"] = Sd2DivisionRules,
            ["Generated/Gameplay/UI/Resources/Textures/ButtonTexturesUnites.ndf"] = Sd2ButtonTextures,
        };

        UnitCatalog catalog = ReadMadeUp(temp, files, GameTitle.SteelDivision2, GameTexts.FromPairs([("TMLRS", "Rocket artillery")]));

        Assert.Empty(catalog.Problems);
        Unit calliope = Assert.Single(catalog.Units);
        Assert.Equal("Calliope", calliope.Name);
        Assert.Equal(("Allied", "US", "Art", "TMLRS", "Rocket artillery"), (calliope.Side, calliope.Nation, calliope.Factory, calliope.Role, calliope.RoleName));
        Assert.Equal((130.0, 10.0, 68.0, 38.0), (calliope.Cost, calliope.Strength, calliope.Speed, calliope.RoadSpeed));
        Assert.Equal((70.0, 12500.0, 2.5), (calliope.Optics, calliope.Vision, calliope.Stealth));
        Assert.Equal(new UnitArmor(22, 11, 9, 0), calliope.Armor);
        Assert.Equal("WeaponDescriptor_Calliope_US", calliope.WeaponSet);
        Assert.Equal("GameData:/Assets/2D/Interface/Common/UnitsIcons/Allies/Calliope_US.png", calliope.PictureSource);
        // the game has no text for '3AD_Dv3' here, so the division is named from its descriptor
        Assert.Equal(["US 3rd Armored"], calliope.Divisions);
        UnitWeapon rockets = Assert.Single(calliope.Weapons);
        Assert.Equal(new UnitWeapon("Roquettes T34", 1, "Artillerie", 0, 21000, 5.7, 424, 240, "Ammo_Roquettes_T34", "Ammunition.ndf", null), rockets);
    }

    [Fact]
    public void Division_names_fall_back_to_their_descriptors()
    {
        Assert.Equal("US 3rd Arm", UnitReader.DivisionNameFromDescriptor("Descriptor_Deck_Division_US_3rd_Arm_multi"));
        Assert.Equal("US 3rd Arm", UnitReader.DivisionNameFromDescriptor("US_3rd_Arm_multi"));
        Assert.Equal("M1A1 Abrams", UnitReader.NameFromDescriptor("Descriptor_Unit_M1A1_Abrams_US", "US"));
        Assert.Equal("Solo", UnitReader.NameFromDescriptor("Descriptor_Unit_Solo", ""));
    }

    [Fact]
    public void A_file_is_split_into_its_top_level_declarations()
    {
        string text = "// heading\nexport A is T\n(\n    X = 1\n)\nprivate B is MAP [\n    (1, 2),\n]\ntemplate C\n[\n] is T()\nD is 4\n";

        var declarations = UnitReader.DeclarationTexts(new StringReader(text)).ToList();

        Assert.Equal(["A", "B", "D"], declarations.Select(d => d.Name));
        Assert.Equal("export A is T\n(\n    X = 1\n)\n", declarations[0].Text);
        // a template's own lines stay with the declaration before it, which is not wanted by the reader anyway
        Assert.Contains("template C", declarations[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mods_rules_change_the_units_and_each_change_names_its_rule()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, WarnoFiles());
        UnitCatalog game;
        using (var gameData = new GameDataFiles(zip, null)) game = UnitReader.Read(gameData, GameTitle.Warno, WarnoTexts);

        // the rule runs on a copy of the units' file added to the Files tab on its own, as Preview runs it
        var engine = new EasyNdf.Engine.NdfEngine();
        engine.Load(WarnoUnits);
        var rule = new EasyNdf.Engine.Rules.Rule
        {
            Name = "Faster tanks", Kind = "Edit", LogicGate = "All",
            Conditions = [new EasyNdf.Engine.Rules.Condition { Operand = "Descriptor Title", Operator = "Is Equal To", Value = "Descriptor_Unit_Tank_US" }],
            Actions = [new EasyNdf.Engine.Rules.RuleAction { Target = "MaxSpeedInKmph", Operator = "Set To", Value = "80" }],
        };
        var result = engine.Run([rule], new EasyNdf.Engine.Rules.RunOptions { LogLimit = int.MaxValue });
        var changedFiles = new List<ChangedFile> { new("C:/My files/UniteDescriptor.ndf", result.Text!) };
        UnitCatalog changed;
        using (var gameData = new GameDataFiles(zip, null, changedFiles)) changed = UnitReader.Read(gameData, GameTitle.Warno, WarnoTexts);

        UnitChanges changes = UnitChanges.Between("My mod", game, changed, [.. RuleEdit.From(result)]);

        Assert.Equal(1, changes.Count);
        UnitChange tank = changes.Of(changed.Units[0])!;
        Assert.Equal([UnitField.Speed], tank.Fields);
        Assert.Equal([new RuleOfMod("My mod", "Faster tanks")], tank.RulesFor(UnitField.Speed));
        // with mods running together, an edit another mod's rule made names that mod
        UnitChanges byOtherMod = UnitChanges.Between("My mod", game, changed, [.. RuleEdit.From(result, "Other mod")]);
        Assert.Equal([new RuleOfMod("Other mod", "Faster tanks")], byOtherMod.Of(changed.Units[0])!.RulesFor(UnitField.Speed));
        Assert.Equal(65.0, tank.Game!.Speed);
        Assert.Equal(80.0, changed.Units[0].Speed);
        Assert.Null(changes.Of(changed.Units[1]));
    }

    [Fact]
    public void A_unit_only_the_mod_has_is_new_and_every_value_counts_as_changed()
    {
        var game = new UnitCatalog(GameTitle.Warno, [], new Dictionary<string, string>(), [], []);
        var clone = new Unit { Descriptor = "Descriptor_Unit_Clone", Name = "Clone" };
        var changed = new UnitCatalog(GameTitle.Warno, [clone], new Dictionary<string, string>(), [], []);

        UnitChange change = UnitChanges.Between("My mod", game, changed, []).Of(clone)!;

        Assert.True(change.IsNew);
        Assert.True(change.Changed(UnitField.Cost));
    }

    private const string WarnoBuildings = """
        export Descriptor_Unit_FOB_US is TEntityDescriptor
        (
            ModulesDescriptors = [
                TTypeUnitModuleDescriptor( Coalition = TWargameCoalition/NATO MotherCountry = 'US' ),
                ~/BuildingDescriptorTagsModuleDescriptor,
                TSupplyModuleDescriptor( SupplyCapacity = 16000.0 ),
                TBaseDamageModuleDescriptor( MaxPhysicalDamages = 10 ),
                TProductionModuleDescriptor
                (
                    FactoryType = Factory/Logistic
                    ProductionRessourcesNeeded = MAP [ ($/GFX/Resources/Resource_CommandPoints, 175) ]
                ),
                TUnitUIModuleDescriptor( UnitRole = 'supply' )
            ]
        )
        """;

    [Fact]
    public void Buildings_such_as_FOBs_are_read_from_their_own_file_into_Logistics()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = WarnoFiles();
        files["Generated/Gameplay/Gfx/BuildingDescriptors.ndf"] = WarnoBuildings;

        UnitCatalog catalog = ReadMadeUp(temp, files, GameTitle.Warno, WarnoTexts);

        Unit fob = Assert.Single(catalog.Units, unit => unit.Descriptor == "Descriptor_Unit_FOB_US");
        Assert.Equal(("BuildingDescriptors.ndf", "Logistic", 175.0, "US"), (fob.File, fob.Factory, fob.Cost, fob.Nation));
        Assert.Equal("UniteDescriptor.ndf", catalog.Units[0].File);
        // a game without the file still reads its units, with no problem noted
        using var other = new TemporaryFolder();
        Assert.Empty(ReadMadeUp(other, WarnoFiles(), GameTitle.Warno, WarnoTexts).Problems);
    }

    [Fact]
    public void The_units_in_a_games_files_are_found_from_their_declaration_lines()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, WarnoFiles());
        using var files = new GameDataFiles(zip, null);

        Assert.Equal(["Descriptor_Unit_Tank_US", "Descriptor_Unit_Truck_SOV"], UnitReader.UnitDescriptorsIn(files).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_armour_familys_levels_are_read_and_a_level_outside_them_is_told()
    {
        using var temp = new TemporaryFolder();
        UnitCatalog catalog = ReadMadeUp(temp, WarnoFiles(), GameTitle.Warno, WarnoTexts);
        Unit tank = catalog.Units[0];

        Assert.Equal(new UnitArmorFamilies("ResistanceFamily_blindage", "ResistanceFamily_blindage", "ResistanceFamily_blindage", "ResistanceFamily_blindage"), tank.ArmorFamilies);
        Assert.Equal((1, 3), ArmorLevels.RangeOf(catalog.ArmorLevels, "ResistanceFamily_helico"));
        Assert.True(ArmorLevels.Allows(catalog.ArmorLevels, "ResistanceFamily_blindage", 30));
        Assert.False(ArmorLevels.Allows(catalog.ArmorLevels, "ResistanceFamily_helico", 20));
        Assert.False(ArmorLevels.Allows(catalog.ArmorLevels, "ResistanceFamily_helico", 0));
        var changed = new ModUnit { Descriptor = "Descriptor_Unit_EZ_X", MadeFrom = tank.Descriptor, Name = "X", FrontArmor = 31, TopArmor = 2 };
        Assert.Equal([("Front", 31, "ResistanceFamily_blindage")], changed.ArmorOutOfRange(tank, catalog.ArmorLevels));
    }
}
