using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Steel Division 2's units changed in the Armory: read with the numbers the game shows (km/h, metres), written as
/// rules in the game's own names, places and forms (both copies of what the placement ghost shows, distances as
/// ((N) * Metre)), and read back as the Armory shows them. A unit laid out as the game's are, with made-up values; and,
/// where the game is installed, one of its real units.
/// </summary>
public class SteelDivisionUnitChangesTests
{
    private const string Tank = "Descriptor_Unit_Test_Tank_US";
    private const string Plane = "Descriptor_Unit_Test_Plane_UK";

    // laid out as the game's UniteDescriptor.ndf: module selectors, the ghost's copies, named modules, the label's copies
    private const string Units = """
        // Ne pas éditer, ce fichier est auto-généré !

        export Descriptor_Unit_Test_Tank_US is TEntityDescriptor
        (
            DescriptorId      = GUID:{00000000-0000-0000-0000-000000000001}
            ClassNameForDebug = 'Unit_Test_Tank_US'
            Modules           = [
                // TypeUnit
                    TModuleSelector
                    (
                        Default            = TTypeUnitModuleDescriptor
                        (
                            Nationalite                      = ENationalite/Allied
                            MotherCountry                    = 'US'
                        )
                        Selection = [
                            NilDescriptorIfCadavre,
                            TModuleSelectorFilter
                            (
                                Condition  = TModuleSelectorCondition_ParamEquals_Integer(ParamId=~/Parameter_ModuleCreationSelectionFilter  ParamValue=~/ModuleCreationFilter_Ghost)
                                Descriptor = TTypeUnitModuleDescriptor
                                (
                                    MotherCountry                    = 'US'
                                    Nationalite                      = ENationalite/Allied
                                )
                            )
                        ]
                    ),

                // Visibility
                    TModuleSelector
                    (
                        Default        = TVisibilityModuleDescriptor
                        (
                            UnitConcealmentBonus = 2.0
                        )
                        Selection = [~/NilDescriptorIfGhostOrCadavre]
                    ),

                // WeaponManager
                WeaponManager is
                    TModuleSelector
                    (
                        Default        = $/GFX/Everything/WeaponDescriptor_Test_Tank_US
                        Selection      = [~/NilDescriptorIfGhostOrCadavre]
                    ),

                // Damage
                    TModuleSelector
                    (
                        Default        = TDamageModuleDescriptor
                        (
                            CommonDamageDescriptor = TDamageDescriptor
                            (
                                BlindageProperties = TBlindageProperties
                                (
                                    ArmorDescriptorFront = ArmorDescriptor_Blindage_12
                                    ArmorDescriptorSides = ArmorDescriptor_Blindage_6
                                    ArmorDescriptorRear = ArmorDescriptor_Blindage_4
                                    ArmorDescriptorTop = ArmorDescriptor_Toit_Lourd
                                )
                                MaxSuppressionDamages = ~/GroundUnit_MaxSuppressionDamages
                            )
                            MaxDamages = 8.0
                            MaxHPForHUD = 8
                        )
                        Selection = [ ~/NilDescriptorIfGhostOrCadavre ]
                    ),

                // LandMovement
                LandMovement is TModuleSelector
                    (
                        Default        = TLandMovementModuleDescriptor
                        (
                            Maxspeed = ((60) * Metre)
                            VitesseCombat = ((60) * Metre)
                            SpeedFakefactorType = ESpeedFakeFactorType/Vehicle
                            SpeedBonusOnRoad = 0.8
                            MaxAcceleration = ((15) * Metre)
                        )
                        Selection = [~/NilDescriptorIfGhostOrCadavre]
                    ),

                // ScannerConfiguration
                    TModuleSelector
                    (
                        Default        = TScannerConfigurationDescriptor
                        (
                            PorteeVisionTBA = ((0.0) * Metre)
                            PorteeVision = ((10000) * Metre)
                            OpticalStrength = 60
                            OpticalStrengthAltitude = 100
                            SpecializedDetections = MAP [
                                ( EVisionUnitType/AlwaysInHighAltitude, 10000.0 * Metre ),
                            ]
                        )
                        Selection = [ ~/NilDescriptorIfCadavre ]
                    ),

                // Production
                    TModuleSelector
                    (
                        Default = TModernWarfareProductionModuleDescriptor
                        (
                            Factory            = EDefaultFactories/Tanks
                            ProductionRessourcesNeeded = MAP [
                                (~/Resource_CommandPoints, 70),
                                (~/Resource_Tickets, 3),
                            ]
                        )
                        Selection = [ ~/NilDescriptorIfGhostOrCadavre ]
                    ),

                // Label
                    TModuleSelector
                    (
                        Default        = TWargameLabelModuleDescriptor
                        (
                            UnitName                   = 'TESTTANK'
                            PlatingValue               = 12
                        )
                        Selection      = [ ~/NilDescriptorIfCadavreOrGhostNotForProd ]
                    ),

                // UnitUI
                    TModuleSelector
                    (
                        Default = TUnitUIModuleDescriptor
                        (
                            NameToken = 'TESTTANK'
                            TypeSpecificToken = 'TArmor'
                            RealRoadSpeed = 30
                            ButtonTexture = 'Texture_Button_Unit_Test_Tank_US'
                            CountryTexture = 'CommonTexture_MotherCountryFlag_US'
                        )
                        Selection = [
                            NilDescriptorIfCadavre,
                            TModuleSelectorFilter
                            (
                                Condition  = TModuleSelectorCondition_ParamEquals_Integer(ParamId = ~/Parameter_ModuleCreationSelectionFilter ParamValue = ~/ModuleCreationFilter_Ghost)
                                Descriptor = TUnitUIModuleDescriptor
                                (
                                    NameToken = 'TESTTANK'
                                    TypeSpecificToken = 'TArmor'
                                    ButtonTexture = 'Texture_Button_Unit_Test_Tank_US'
                                    CountryTexture = 'CommonTexture_MotherCountryFlag_US'
                                )
                            )
                        ]
                    ),

            ]
        )
        export Descriptor_Unit_Test_Plane_UK is TEntityDescriptor
        (
            DescriptorId      = GUID:{00000000-0000-0000-0000-000000000002}
            ClassNameForDebug = 'Unit_Test_Plane_UK'
            Modules           = [
                    TModuleSelector
                    (
                        Default            = TTypeUnitModuleDescriptor
                        (
                            Nationalite                      = ENationalite/Allied
                            MotherCountry                    = 'UK'
                        )
                    ),
                    TModuleSelector
                    (
                        Default        = TDamageModuleDescriptor
                        (
                            CommonDamageDescriptor = TDamageDescriptor
                            (
                                BlindageProperties = TBlindageProperties
                                (
                                    ArmorDescriptorFront = ArmorDescriptor_Avion_Lourd
                                    ArmorDescriptorSides = ArmorDescriptor_Avion_Lourd
                                    ArmorDescriptorRear = ArmorDescriptor_Avion_Lourd
                                    ArmorDescriptorTop = ArmorDescriptor_Avion
                                )
                            )
                            MaxDamages = 6.0
                            MaxHPForHUD = 6
                        )
                    ),
                // AirplaneMovement
                    TModuleSelector
                    (
                        Default        = TAirplaneMovementModuleDescriptor
                        (
                            Altitude = ((3000) * Metre)
                            Speed = ((1000) * Metre)
                            PitchSpeed = $/GFX/Everything/MaxPitchParTick
                        )
                    ),
                // Fuel
                    TModuleSelector
                    (
                        Default        = TFuelModuleDescriptor
                        (
                            FuelCapacity     = 2000.0
                            FuelMoveDuration = 100.0
                        )
                    ),
                    TModuleSelector
                    (
                        Default        = TScannerConfigurationDescriptor
                        (
                            PorteeVision = ((10000) * Metre)
                            OpticalStrength = 0
                        )
                    ),
                    TModuleSelector( Default = TUnitUIModuleDescriptor( NameToken = 'TESTPLANE' TypeSpecificToken = 'THBomber' ) ),
            ]
        )
        """;

    // the constants the game shows its numbers by, as its own files write them
    private const string Constants = """
        GDConstantes is TGDConstantes
        (
            MultiplicateurMetreRTSVersDistanceFeedbackTactique = 1.0 div 5.0//7.0
        )
        """;

    private const string UnitResource = """
        export WargameUnitDescriptorResource is TModernWarfareUnitDescriptorResource
        (
            // Speed (utilise VitesseLineaire)
            SpeedFakeFactorVehicle  = 2.0
            SpeedFakeFactorInfantry = 3.0
            SpeedFakeFactorAir      = 2.0
        )
        """;

    private static string ArmorDescriptors() =>
        string.Concat(Enumerable.Range(1, 30).Select(level => "export ArmorDescriptor_Blindage_" + level + " is TArmorDescriptor\n(\n    BaseBlindage = " + level + "\n)\n"))
        + "export ArmorDescriptor_Toit_Lourd is TArmorDescriptor\n(\n)\n";

    private static Dictionary<string, string> Files() => new()
    {
        ["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = Units,
        ["Generated/Gameplay/Gfx/ArmorDescriptor.ndf"] = ArmorDescriptors(),
        ["Gameplay/Constantes/GDConstantes.ndf"] = Constants,
        ["Gameplay/Gfx/UnitDescriptorResource.ndf"] = UnitResource,
    };

    private static UnitCatalog Read(string zip, IReadOnlyList<ChangedFile>? changed = null)
    {
        using var files = new GameDataFiles(zip, null, changed);
        return UnitReader.Read(files, GameTitle.SteelDivision2, GameTexts.FromPairs([("TESTTANK", "TEST TANK")]));
    }

    /// <summary>Runs the rules on UniteDescriptor.ndf, as the export does: every rule matched and nothing skipped.</summary>
    private static string RunOnUnits(JsonArray rules, string text)
    {
        JsonArray forFile = DivisionChangeRules.ForFile(rules, UnitChangeRules.UnitsFile);
        Assert.NotEmpty(forFile);
        var engine = new NdfEngine();
        engine.Load(text);
        RunResult result = engine.Run(RuleLoading.LoadForEngine(forFile), new RunOptions());
        Assert.Equal("", result.ParseError);
        Assert.All(result.Stats, rule => Assert.Equal(0, rule.Skipped));
        return result.Text!;
    }

    /// <summary>What the Armory shows of a unit's values.</summary>
    private static string Describe(Unit unit) =>
        string.Join(" ", unit.Descriptor, unit.Side, unit.Nation, unit.Role, unit.Cost, unit.Strength, unit.Armor, unit.Speed, unit.RoadSpeed,
            unit.Optics, unit.Vision, unit.Stealth, unit.Fuel);

    /// <summary>The lines of a text that hold a property, trimmed, their spaces in a row as one: "Maxspeed = ((70) * Metre)".</summary>
    private static List<string> Lines(string text, string property) =>
        [.. text.Split('\n').Select(line => Regex.Replace(line.Trim(), "[ \\t]+", " ")).Where(line => Regex.IsMatch(line, "^" + Regex.Escape(property) + " ?="))];

    [Fact]
    public void Its_units_are_read_with_the_numbers_the_game_shows()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());

        UnitCatalog game = Read(zip);

        Unit tank = game.Units.Single(unit => unit.Descriptor == Tank);
        // Maxspeed's 60 divided by the vehicles' factor 2; PorteeVision's 10000 is 2,000 m
        Assert.Equal((30.0, 2.0, false, 2000.0, 5.0), (tank.Speed, tank.SpeedScale, tank.SpeedIsInFlight, tank.Vision, tank.MetresScale));
        Assert.Equal(new UnitArmor(12, 6, 4, 0), tank.Armor);
        Assert.Equal(new UnitArmorFamilies("Blindage", "Blindage", "Blindage", "Toit_Lourd"), tank.ArmorFamilies);
        Assert.Equal(30, game.ArmorLevels!["Blindage"]);
        Unit plane = game.Units.Single(unit => unit.Descriptor == Plane);
        Assert.Equal((500.0, true, 2000.0), (plane.Speed, plane.SpeedIsInFlight, plane.Fuel));
    }

    [Fact]
    public void Without_the_games_constants_the_files_own_numbers_are_shown()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string> { ["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = Units });

        Unit tank = Read(zip).Units.Single(unit => unit.Descriptor == Tank);

        Assert.Equal((60.0, null, 10000.0, null), (tank.Speed, tank.SpeedScale, tank.Vision, tank.MetresScale));
    }

    [Fact]
    public void A_change_to_a_units_values_is_written_in_the_games_own_names_places_and_forms_and_reads_back_as_shown()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        var edit = new UnitEdit
        {
            Descriptor = Tank, Nation = "UK", Side = "Allied", Role = "TRecon", Cost = 95, Strength = 9,
            FrontArmor = 14, SideArmor = 7, RearArmor = 5, Speed = 35, RoadSpeed = 40, Optics = 80, Vision = 2200.5, Stealth = 2.75,
        };
        mod.UnitEdits.Add(edit);

        Assert.True(UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        string written = RunOnUnits(rules, Units);

        // still NDF the engine reads, every unit in it
        Assert.Equal(2, Parser.Parse(written).Declarations.Count);
        // the nation and side in the unit and in its placement ghost; the flag with them
        Assert.Equal(["MotherCountry = 'UK'", "MotherCountry = 'UK'"], Lines(written, "MotherCountry").Take(2));
        Assert.Equal(["Nationalite = ENationalite/Allied", "Nationalite = ENationalite/Allied"], Lines(written, "Nationalite").Take(2));
        Assert.Equal(["CountryTexture = 'CommonTexture_MotherCountryFlag_UK'", "CountryTexture = 'CommonTexture_MotherCountryFlag_UK'"], Lines(written, "CountryTexture"));
        Assert.Equal(["TypeSpecificToken = 'TRecon'", "TypeSpecificToken = 'TRecon'"], Lines(written, "TypeSpecificToken"));
        Assert.Contains("TypeSpecificToken = 'THBomber'", written, StringComparison.Ordinal);
        Assert.Contains("(~/Resource_CommandPoints, 95)", written, StringComparison.Ordinal);
        Assert.Contains("(~/Resource_Tickets, 3)", written, StringComparison.Ordinal);
        // the strength as a float, and its copy the HUD shows; the plane's are its own
        Assert.Equal(["MaxDamages = 9.0", "MaxDamages = 6.0"], Lines(written, "MaxDamages"));
        Assert.Equal(["MaxHPForHUD = 9", "MaxHPForHUD = 6"], Lines(written, "MaxHPForHUD"));
        Assert.Equal("ArmorDescriptorFront = ArmorDescriptor_Blindage_14", Lines(written, "ArmorDescriptorFront")[0]);
        Assert.Equal("ArmorDescriptorSides = ArmorDescriptor_Blindage_7", Lines(written, "ArmorDescriptorSides")[0]);
        Assert.Equal("ArmorDescriptorRear = ArmorDescriptor_Blindage_5", Lines(written, "ArmorDescriptorRear")[0]);
        Assert.Equal("ArmorDescriptorTop = ArmorDescriptor_Toit_Lourd", Lines(written, "ArmorDescriptorTop")[0]);
        Assert.Equal(["PlatingValue = 14"], Lines(written, "PlatingValue"));
        // 35 km/h is 70 of the files' units for a vehicle, in its top speed and its speed in combat; 2,200.5 m is 11002.5
        Assert.Equal(["Maxspeed = ((70) * Metre)"], Lines(written, "Maxspeed"));
        Assert.Equal(["VitesseCombat = ((70) * Metre)"], Lines(written, "VitesseCombat"));
        Assert.Equal(["MaxAcceleration = ((15) * Metre)"], Lines(written, "MaxAcceleration"));
        Assert.Equal(["RealRoadSpeed = 40"], Lines(written, "RealRoadSpeed"));
        Assert.Equal(["PorteeVision = ((11002.5) * Metre)", "PorteeVision = ((10000) * Metre)"], Lines(written, "PorteeVision"));
        Assert.Equal(["OpticalStrength = 80", "OpticalStrength = 0"], Lines(written, "OpticalStrength"));
        Assert.Equal(["UnitConcealmentBonus = 2.75"], Lines(written, "UnitConcealmentBonus"));
        // nothing is written WARNO's way
        foreach (string warno in new[] { "Coalition", "UnitRole", "MaxPhysicalDamages", "MaxSpeedInKmph", "ResistanceFront", "VisionRangesGRU", "OpticalStrengths" })
            Assert.DoesNotContain(warno, written, StringComparison.Ordinal);

        UnitCatalog exported = Read(zip, [new ChangedFile("C:/Mod/GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf", written)]);
        Unit tank = game.Units.Single(unit => unit.Descriptor == Tank);
        Assert.Equal(Describe(edit.Apply(tank)), Describe(exported.Units.Single(unit => unit.Descriptor == Tank)));
        Assert.Equal(Describe(game.Units.Single(unit => unit.Descriptor == Plane)), Describe(exported.Units.Single(unit => unit.Descriptor == Plane)));
    }

    [Fact]
    public void A_planes_speed_and_fuel_are_written_where_a_plane_has_them()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        var edit = new UnitEdit { Descriptor = Plane, Speed = 550, Fuel = 2500 };
        mod.UnitEdits.Add(edit);

        string written = RunOnUnits(UnitChangeRules.For(mod, game.Units, new TextCatalog()), Units);

        Assert.Equal(["Speed = ((1100) * Metre)"], Lines(written, "Speed"));
        Assert.Equal(["FuelCapacity = 2500.0"], Lines(written, "FuelCapacity"));
        // the tank's speed is its own
        Assert.Equal(["Maxspeed = ((60) * Metre)"], Lines(written, "Maxspeed"));
        UnitCatalog exported = Read(zip, [new ChangedFile("C:/Mod/GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf", written)]);
        Assert.Equal(Describe(edit.Apply(game.Units.Single(unit => unit.Descriptor == Plane))), Describe(exported.Units.Single(unit => unit.Descriptor == Plane)));
    }

    // laid out as the game's ButtonTexturesUnites.ndf: each picture a texture of its own, then the bank naming them
    private const string Pictures = """
        // Ne pas éditer, ce fichier est auto-généré !


        export Texture_Button_Unit_Test_Tank_US is TUIResourceTextureWithUsefulnessMask
        (
            TypeMask = TypeMask/BuildMenu
            CivMask  = CivMask/Nationalite1
            FileName = 'Assets\2D\Interface\Common\UnitsIcons\EU\Test_Tank_US.png'
        )
        UnitButtonTextureAdditionalBank is TBUCKToolAdditionalTextureBank
        (
            Textures = MAP
            [
                ("Texture_Button_Unit_Test_Tank_US", MAP [( ~/ComponentState/Normal, ~/Texture_Button_Unit_Test_Tank_US), ]),
            ]
        )
        """;

    [Fact]
    public void A_card_picture_of_the_users_own_is_a_Modded_texture_under_AssetsModding_named_in_the_bank_and_copied_there_by_the_export()
    {
        using var temp = new TemporaryFolder();
        string picture = Path.Combine(temp.Path, "own.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        var tank = new Unit { Descriptor = Tank, Name = "TEST TANK", Side = "Allied", Nation = "US" };
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        mod.UnitEdits.Add(new UnitEdit { Descriptor = Tank, PictureFile = picture });

        Assert.True(UnitChangeRules.Writes(GameTitle.SteelDivision2, UnitField.Picture));
        Assert.True(UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = UnitChangeRules.For(mod, [tank], new TextCatalog());

        // the unit's card and its ghost name the new texture
        string units = RunOnUnits(rules, Units);
        Assert.Equal(2, Lines(units, "ButtonTexture").Count(line => line == "ButtonTexture = 'Texture_Button_EZ_Unit_Test_Tank_US'"));
        // the texture, of the Modded kind, its file under AssetsModding, masked for the unit's side; and its bank entry
        JsonArray forPictures = DivisionChangeRules.ForFile(rules, UnitChangeRules.PicturesFile);
        var engine = new NdfEngine();
        engine.Load(Pictures);
        RunResult result = engine.Run(RuleLoading.LoadForEngine(forPictures), new RunOptions());
        Assert.Equal("", result.ParseError);
        Assert.All(result.Stats, rule => Assert.Equal(0, rule.Skipped));
        string written = result.Text!;
        string texture = written[written.IndexOf("export Texture_Button_EZ_Unit_Test_Tank_US", StringComparison.Ordinal)..];
        Assert.StartsWith("export Texture_Button_EZ_Unit_Test_Tank_US is TUIResourceTextureWithUsefulnessMaskModded", texture, StringComparison.Ordinal);
        Assert.Contains("FileName = 'UnitsIcons\\EZ_Unit_Test_Tank_US.png'", texture, StringComparison.Ordinal);
        Assert.Contains("CivMask  = CivMask/Nationalite1", texture, StringComparison.Ordinal);
        Assert.Contains("(\"Texture_Button_EZ_Unit_Test_Tank_US\", MAP [( ~/ComponentState/Normal, ~/Texture_Button_EZ_Unit_Test_Tank_US), ])", written, StringComparison.Ordinal);
        Assert.Equal(2, Parser.Parse(written).Declarations.Count(declaration => declaration.Name.StartsWith("Texture_Button_", StringComparison.Ordinal)));
        Assert.DoesNotContain("GameData:/", written, StringComparison.Ordinal);

        // the export copies the picture into the mod's AssetsModding folder, beside its GameData
        string exportedFile = Path.Combine(temp.Path, "Mod", "GameData", "Generated", "Gameplay", "UI", "Resources", "Textures", "ButtonTexturesUnites.ndf");
        DivisionExtras.Outcome outcome = DivisionExtras.CopyPictures(rules, exportedFile, [temp.Path], "no {path}", "missing {path}");
        Assert.Equal((1, ""), (outcome.Pictures, outcome.Problem));
        Assert.True(File.Exists(Path.Combine(temp.Path, "Mod", "AssetsModding", "UnitsIcons", "EZ_Unit_Test_Tank_US.png")));
    }

    // the other files the game keeps each unit in, laid out as its own
    private const string AllUnitsTactic = """
        // Ne pas éditer, ce fichier est auto-généré !

        AllUnitsTactic is [

            ~/Descriptor_Unit_Test_Tank_US,
            ~/Descriptor_Unit_Test_Plane_UK,
        ]
        """;

    private const string ShowRoomEquivalence = """
        export ShowRoomEquivalenceManager is TShowRoomEquivalenceManager
        (
            UnitToShowRoomEquivalent = Map [

                ( ~/Descriptor_Unit_Test_Tank_US, ~/Descriptor_ShowRoomUnit_Test_Tank_US ),
            ]
        )
        """;

    private const string ProductionButtons = """
        CubeAction_ProductionUnit_Test_Tank_US is TCubeActionProductionUnitButtonInformationsDescriptor
        (
            CubeActionName               = "CubeAction_ProductionUnit_Test_Tank_US"
            BackgroundTexture            = "Texture_Button_Unit_Test_Tank_US"
            ButtonTextToken              = "TESTTANK"
            HintTitleToken               = "TESTTANK"
            HintBodyToken                = ""
            Descriptor                   = ~/Descriptor_Unit_Test_Tank_US
        )
        """;

    private const string UnitTags = """
        private UnitToUnitTag is TUnitToUnitTagForIAStrat
        (
            UnitToUnitTag = MAP
            [
                ( ~/Descriptor_Unit_Test_Tank_US, "UNITE_Test_Tank_US" ),
            ]
        )
        """;

    private const string DeckNumbers = """
        DeckSerializer is TDeckSerializer
        (
            UnitIds = MAP
            [
                (Descriptor_Unit_Test_Tank_US, 0),
                (Descriptor_Unit_Test_Plane_UK, 7),
            ]
        )
        """;

    [Fact]
    public void A_new_unit_is_a_copy_of_its_model_written_in_every_file_the_game_keeps_a_unit_in()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        Unit model = game.Units.Single(unit => unit.Descriptor == Tank);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        var added = new ModUnit { Descriptor = "Descriptor_Unit_EZ_Hunter_US", MadeFrom = Tank, Name = "HUNTER", DeckNumber = 8, Cost = 120 };
        mod.Units.Add(added);

        Assert.True(UnitChangeRules.AddsUnits(GameTitle.SteelDivision2));
        Assert.True(UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        HashSet<string> files = DivisionChangeRules.FilesOf(rules);
        Assert.Equal(new HashSet<string> { "UniteDescriptor.ndf", "DeckSerializer.ndf", "AllUnitsTactic.ndf", "ShowRoomEquivalence.ndf", "Generated_CubeActionsUnites.ndf", "UnitToUnitTag.ndf" }, files);

        // the unit: a copy of its model, with GUIDs and a name of its own, on its card, its ghost and its label
        string units = RunOnUnits(rules, Units);
        Declaration hunter = Parser.Parse(units).Declarations.Single(declaration => declaration.Name == added.Descriptor);
        string hunterText = units[units.IndexOf("Descriptor_Unit_EZ_Hunter_US is", StringComparison.Ordinal)..];
        string token = UnitChangeRules.NameToken(added.Descriptor);
        Assert.Contains("ClassNameForDebug = 'Unit_EZ_Hunter_US'", Regex.Replace(hunterText, "[ \\t]+", " "), StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(hunterText, "'" + token + "'").Count);
        Assert.Contains("(~/Resource_CommandPoints, 120)", hunterText, StringComparison.Ordinal);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000001", hunterText, StringComparison.Ordinal);

        string Run(string file, string text)
        {
            var engine = new NdfEngine();
            engine.Load(text);
            RunResult result = engine.Run(RuleLoading.LoadForEngine(DivisionChangeRules.ForFile(rules, file)), new RunOptions());
            Assert.Equal("", result.ParseError);
            Assert.All(result.Stats, rule => Assert.Equal(0, rule.Skipped));
            Parser.Parse(result.Text!);
            return result.Text!;
        }
        Assert.Contains("~/Descriptor_Unit_EZ_Hunter_US,", Run("AllUnitsTactic.ndf", AllUnitsTactic), StringComparison.Ordinal);
        Assert.Contains("( ~/Descriptor_Unit_EZ_Hunter_US, ~/Descriptor_ShowRoomUnit_Test_Tank_US )", Run("ShowRoomEquivalence.ndf", ShowRoomEquivalence), StringComparison.Ordinal);
        Assert.Contains("( ~/Descriptor_Unit_EZ_Hunter_US, \"UNITE_Test_Tank_US\" )", Run("UnitToUnitTag.ndf", UnitTags), StringComparison.Ordinal);
        Assert.Contains("(Descriptor_Unit_EZ_Hunter_US, 8)", Run("DeckSerializer.ndf", DeckNumbers), StringComparison.Ordinal);
        string buttons = Run("Generated_CubeActionsUnites.ndf", ProductionButtons);
        string button = Regex.Replace(buttons[buttons.IndexOf("CubeAction_ProductionUnit_EZ_Hunter_US is", StringComparison.Ordinal)..], "[ \\t]+", " ");
        Assert.Contains("CubeActionName = \"CubeAction_ProductionUnit_EZ_Hunter_US\"", button, StringComparison.Ordinal);
        Assert.Contains("ButtonTextToken = \"" + token + "\"", button, StringComparison.Ordinal);
        Assert.Contains("HintTitleToken = \"" + token + "\"", button, StringComparison.Ordinal);
        Assert.Contains("Descriptor = ~/Descriptor_Unit_EZ_Hunter_US", button, StringComparison.Ordinal);
        // the game's own button is left as it is
        Assert.Contains("Descriptor = ~/Descriptor_Unit_Test_Tank_US", Regex.Replace(buttons, "[ \\t]+", " "), StringComparison.Ordinal);

        // read back as the Armory shows it: the model's values, but the cost given
        UnitCatalog exported = Read(zip, [new ChangedFile("C:/Mod/GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf", units)]);
        Unit read = exported.Units.Single(unit => unit.Descriptor == added.Descriptor);
        Assert.Equal((120.0, model.Speed, model.Armor), (read.Cost, read.Speed, read.Armor));
    }

    [Fact]
    public void A_new_name_is_a_token_of_ten_characters_on_the_card_the_ghost_and_the_label_with_its_text_in_MODDING_csv()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        mod.UnitEdits.Add(new UnitEdit { Descriptor = Tank, Name = "BIG TANK; MK II" });

        Assert.True(UnitChangeRules.Writes(GameTitle.SteelDivision2, UnitField.Name));
        Assert.True(UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        string written = RunOnUnits(rules, Units);

        // the game takes tokens of ten characters at most, of letters, digits and "_"
        string token = UnitChangeRules.NameToken(Tank);
        Assert.Matches("^[A-Za-z0-9_]{1,10}$", token);
        Assert.Equal(["NameToken = '" + token + "'", "NameToken = '" + token + "'"], Lines(written, "NameToken"));
        Assert.Contains("NameToken = 'TESTPLANE'", written, StringComparison.Ordinal);
        Assert.Equal(["UnitName = '" + token + "'"], Lines(written, "UnitName"));
        // its text goes into MODDING.csv, in the game's seven columns, with a line feed alone as the game's file has
        Assert.Contains(rules, DivisionExtras.NeedsNamesFile);
        Assert.Equal("MODDING.csv", DivisionExtras.NamesFileOf(GameTitle.SteelDivision2));
        const string heading = "\"TOKEN\";\"COMMENT\";\"DO NOT TRANSLATE\";CONSTRAINT;\"REFTEXT\";\"CLASSEMENT\";\"ExisteDansSlayer\"";
        (string csv, int count) = DivisionExtras.WithTexts("", DivisionExtras.ArmoryTextsOf(rules), GameTitle.SteelDivision2);
        Assert.Equal(1, count);
        Assert.Equal(heading + "\n\"" + token + "\";\"\";\"\";;\"BIG TANK, MK II\";\"\";\"\"\n", csv);
        // the game's own file, as its CreateNewMod makes it, is given the same line; a line of the token is replaced
        Assert.Equal(csv, DivisionExtras.WithTexts(heading + "\n", DivisionExtras.ArmoryTextsOf(rules)).Text);
        Assert.Equal(heading + "\n\"OTHER\";\"\";\"\";;\"Kept\";\"\";\"\"\n\"" + token + "\";\"\";\"\";;\"BIG TANK, MK II\";\"\";\"\"\n",
            DivisionExtras.WithTexts(heading + "\n\"OTHER\";\"\";\"\";;\"Kept\";\"\";\"\"\n\"" + token + "\";\"\";\"\";;\"Old\";\"\";\"\"\n", DivisionExtras.ArmoryTextsOf(rules)).Text);
        // WARNO's dictionary is as it was
        Assert.Equal("\"TOKEN\";\"REFTEXT\"\r\n\"" + token + "\";\"BIG TANK, MK II\"\r\n", DivisionExtras.WithTexts("", DivisionExtras.ArmoryTextsOf(rules)).Text);
    }

    [Fact]
    public void Optics_are_whole_and_only_an_armour_plate_takes_a_level()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        Unit tank = game.Units.Single(unit => unit.Descriptor == Tank);
        EditableStat optics = EditableStat.Of(UnitField.Optics)!;

        Assert.Null(optics.ValueOf("80.5", tank, game.ArmorLevels, game: GameTitle.SteelDivision2));
        Assert.Equal(80.5, optics.ValueOf("80.5", tank, game.ArmorLevels, game: GameTitle.Warno));
        Assert.Equal(30, EditableStat.Of(UnitField.FrontArmor)!.ValueOf("30", tank, game.ArmorLevels, game: GameTitle.SteelDivision2));
        Assert.Null(EditableStat.Of(UnitField.FrontArmor)!.ValueOf("31", tank, game.ArmorLevels, game: GameTitle.SteelDivision2));
        // the roof is not a plate: it has no levels
        Assert.True(ArmorLevels.HasNone(game.ArmorLevels, tank.ArmorFamilies.Top));
        Assert.Null(EditableStat.Of(UnitField.TopArmor)!.ValueOf("3", tank, game.ArmorLevels, game: GameTitle.SteelDivision2));
    }

    // ---- the game's own files, where it is installed ----

    /// <summary>The Steel Division 2 mod made with the game's CreateNewMod on the test computer; the test passes without looking where it is not.</summary>
    private const string InstalledGameData = @"E:\Steel Division 2\Mods\Test Mod\GameData";

    [Fact]
    public void A_real_unit_of_the_game_takes_the_changes_and_its_file_still_reads()
    {
        string units = Path.Combine(InstalledGameData, "Generated", "Gameplay", "Gfx", "UniteDescriptor.ndf");
        if (!File.Exists(units)) return;
        const string sherman = "Descriptor_Unit_M4A1_76_US";
        string unitText;
        using (var reader = new StreamReader(units))
            unitText = UnitReader.DeclarationTexts(reader).First(declaration => declaration.Name == sherman).Text;
        var files = new Dictionary<string, string>
        {
            ["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = unitText,
            ["Generated/Gameplay/Gfx/ArmorDescriptor.ndf"] = File.ReadAllText(Path.Combine(InstalledGameData, "Generated", "Gameplay", "Gfx", "ArmorDescriptor.ndf")),
            ["Gameplay/Constantes/GDConstantes.ndf"] = File.ReadAllText(Path.Combine(InstalledGameData, "Gameplay", "Constantes", "GDConstantes.ndf")),
            ["Gameplay/Gfx/UnitDescriptorResource.ndf"] = File.ReadAllText(Path.Combine(InstalledGameData, "Gameplay", "Gfx", "UnitDescriptorResource.ndf")),
        };
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, files);
        UnitCatalog game = Read(zip);
        Unit before = game.Units.Single();
        // as the game shows it: 68 / 2 km/h, 12500 / 5 m; its plates go up to 60
        Assert.Equal((34.0, 2500.0, 60), (before.Speed, before.Vision, game.ArmorLevels!["Blindage"]));
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        var edit = new UnitEdit
        {
            Descriptor = sherman, Nation = "UK", Side = "Allied", Cost = 90, Strength = 11, FrontArmor = 20, TopArmor = null,
            Speed = 40, RoadSpeed = 42, Optics = 90, Vision = 2600, Stealth = 3,
        };
        mod.UnitEdits.Add(edit);

        string written = RunOnUnits(UnitChangeRules.For(mod, game.Units, new TextCatalog()), unitText);

        Assert.Single(Parser.Parse(written).Declarations);
        Assert.Equal(["MotherCountry = 'UK'", "MotherCountry = 'UK'"], Lines(written, "MotherCountry"));
        Assert.Equal(2, Lines(written, "CountryTexture").Count(line => line.EndsWith("_UK'", StringComparison.Ordinal)));
        Assert.Contains("(~/Resource_CommandPoints, 90)", written, StringComparison.Ordinal);
        Assert.Equal(["MaxDamages = 11.0"], Lines(written, "MaxDamages"));
        Assert.Equal(["ArmorDescriptorFront = ArmorDescriptor_Blindage_20"], Lines(written, "ArmorDescriptorFront"));
        Assert.Equal(["PlatingValue = 20"], Lines(written, "PlatingValue"));
        Assert.Equal(["Maxspeed = ((80) * Metre)"], Lines(written, "Maxspeed"));
        Assert.Equal(["PorteeVision = ((13000) * Metre)"], Lines(written, "PorteeVision"));
        Assert.Equal(["UnitConcealmentBonus = 3.0"], Lines(written, "UnitConcealmentBonus"));
        // everything else is left as the game wrote it, line for line
        List<string> changedLines = [.. unitText.Split('\n').Zip(written.Split('\n')).Where(pair => pair.First != pair.Second).Select(pair => pair.First.Trim())];
        Assert.Equal(unitText.Split('\n').Length, written.Split('\n').Length);
        Assert.All(changedLines, line => Assert.Matches("^(MotherCountry|CountryTexture|\\(~/Resource_CommandPoints|MaxDamages|MaxHPForHUD|ArmorDescriptorFront|PlatingValue|Maxspeed|VitesseCombat|RealRoadSpeed|OpticalStrength|PorteeVision|UnitConcealmentBonus)", line));
        UnitCatalog exported = Read(zip, [new ChangedFile("C:/Mod/GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf", written)]);
        Assert.Equal(Describe(edit.Apply(before)), Describe(exported.Units.Single()));
    }
    // laid out as the game's DefenseProductionsDescriptors.ndf: a defence its divisions list as a unit
    private const string Defences = """
        // Ne pas éditer, ce fichier est auto-généré !

        export Descriptor_Production_Test_Bunker is TEntityDescriptor
        (
            DescriptorId = GUID:{00000000-0000-0000-0000-000000000009}
            ClassNameForDebug = 'Test_Bunker'
            Modules = [
                // DefenseProduction
                TModernWarfareProductionModuleDescriptor(
                    Factory                        = EDefaultFactories/Defense
                    ProductionRessourcesNeeded = MAP [
                        (~/Resource_CommandPoints, 25),
                    ]
                ),

                // DefenseUIInfo
                TDefenseUIModuleDescriptor
                (
                    Name                           = 'BUNKTEST'
                    Vignette                       = 'Texture_Defense_Test_Bunker_vignette'
                ),

                // TypeUnit
                    TModuleSelector
                    (
                        Default            = TTypeUnitModuleDescriptor
                        (
                            Nationalite                      = ENationalite/Axis
                            MotherCountry                    = 'GER'
                        )
                        Selection = [ NilDescriptorIfCadavre ]
                    ),
            ]
        )
        """;

    private const string DefenceTextures = """
        DefenseTextureBank is TBUCKToolAdditionalTextureBank
        (
            Textures = MAP [
                ('Texture_Defense_Test_Bunker_vignette', MAP [(~/ComponentState/Normal, TUIResourceTexture_Common (FileName = 'Assets/2D/Interface/Common/UnitsIcons/Defenses/Defense_Bunker.png'))]),
            ]
        )
        """;

    [Fact]
    public void Its_defences_are_read_as_units_with_their_name_picture_and_number_and_a_change_is_written_in_their_own_file()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        Dictionary<string, string> files = Files();
        files["Generated/Gameplay/Gfx/DefenseProductionsDescriptors.ndf"] = Defences;
        files["Generated/UI/Resources/Textures/DefenseTextures.ndf"] = DefenceTextures;
        files["Generated/Gameplay/Decks/DeckSerializer.ndf"] = "DeckSerializer is TDeckSerializer\n(\n    UnitIds = MAP [\n        (Descriptor_Production_Test_Bunker, 1366),\n    ]\n)\n";
        GameFileWriters.BaseZip(zip, files);
        using (var gameData = new GameDataFiles(zip, null))
            Assert.Contains("Descriptor_Production_Test_Bunker", UnitReader.UnitDescriptorsIn(gameData));
        using var named = new GameDataFiles(zip, null);
        UnitCatalog game = UnitReader.Read(named, GameTitle.SteelDivision2, GameTexts.FromPairs([("BUNKTEST", "TEST BUNKER")]));

        Unit bunker = game.Units.Single(unit => unit.Descriptor == "Descriptor_Production_Test_Bunker");
        Assert.Equal(("DefenseProductionsDescriptors.ndf", "TEST BUNKER", "GER", "Defense", 25.0, 1366),
            (bunker.File, bunker.Name, bunker.Nation, bunker.Factory, bunker.Cost, bunker.DeckNumber));
        Assert.EndsWith("Defenses/Defense_Bunker.png", bunker.PictureSource, StringComparison.Ordinal);
        // with no text for its token, its name comes from its descriptor
        Assert.Equal("Test Bunker", UnitReader.NameFromDescriptor("Descriptor_Production_Test_Bunker", "GER"));

        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        mod.UnitEdits.Add(new UnitEdit { Descriptor = bunker.Descriptor, Cost = 30 });
        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        Assert.Empty(DivisionChangeRules.ForFile(rules, UnitChangeRules.UnitsFile));
        JsonArray forFile = DivisionChangeRules.ForFile(rules, "DefenseProductionsDescriptors.ndf");
        var engine = new NdfEngine();
        engine.Load(Defences);
        RunResult result = engine.Run(RuleLoading.LoadForEngine(forFile), new RunOptions());
        Assert.All(result.Stats, rule => Assert.Equal(0, rule.Skipped));
        Assert.Contains("(~/Resource_CommandPoints, 30)", result.Text, StringComparison.Ordinal);
    }
}
