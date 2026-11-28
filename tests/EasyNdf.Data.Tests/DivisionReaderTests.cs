using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

public class DivisionReaderTests
{
    // ---- made-up GameData in each game's layout ----

    internal const string WarnoDivisions = """
        // a comment before the first declaration
        export Descriptor_Deck_Division_US_Armored_multi is TDeckDivisionDescriptor
        (
            DescriptorId = GUID:{462b0cc2-0952-47d9-baf9-ad3af39c68a9}
            CfgName = 'US_Armored_multi'
            DivisionName = 'DIVNAME'
            InterfaceOrder = 3.0
            DivisionCoalition = TWargameCoalition/NATO
            DivisionTags = ['DEFAULT', 'US', 'NATO', 'ARMORED']
            MaxActivationPoints = 50
            DivisionRule = Descriptor_Deck_Division_US_Armored_multi_Rule
            CostMatrix = MatrixCostName_US_Armored_multi
            EmblemTexture     = "Texture_Division_Emblem_US_Armored"
            TypeToken        = "ARMORED"
            CountryId = "US"
            StandoutUnits = [ $/GFX/Unit/Descriptor_Unit_Tank_US, $/GFX/Unit/Descriptor_Unit_Rifles_US ]
        )
        export Descriptor_Deck_Division_US_Armored_solo is TDeckDivisionDescriptor
        (
            CfgName = 'US_Armored_solo'
            DivisionTags = ['US']
        )
        """;

    internal const string WarnoDivisionRules = """
        Descriptor_Deck_Division_US_Armored_multi_Rule is TDeckDivisionRule
        (
            UnitRuleList =
            [
                TDeckUniteRule
                (
                    UnitDescriptor = $/GFX/Unit/Descriptor_Unit_Tank_US
                    AvailableWithoutTransport = True
                    MaxPackNumber = 6
                    NumberOfUnitInPack = 4
                    NumberOfUnitInPackXPMultiplier = [1.0, 0.6, 0.34, 0.0]
                ),
                TDeckUniteRule
                (
                    UnitDescriptor = $/GFX/Unit/Descriptor_Unit_Rifles_US
                    AvailableWithoutTransport = False
                    AvailableTransportList = [ $/GFX/Unit/Descriptor_Unit_Humvee_US, $/GFX/Unit/Descriptor_Unit_Bradley_US ]
                    MaxPackNumber = 4
                    NumberOfUnitInPack = 9
                    NumberOfUnitInPackXPMultiplier = [1.0, 0.68, 0.49, 0.0]
                ),
            ]
        )
        """;

    internal const string WarnoCosts = """
        MatrixCostName_US_Armored_multi is MAP
        [
            (Factory/Art, [1, 2, 2, 3, 3]),
            (Factory/Defense, []),
            (Factory/Tanks, [1, 1, 1, 2]),
        ]
        """;

    internal const string WarnoEmblems = """
        Texture_Division_Emblem_US_Armored is TUIResourceTexture_Common
        (
            FileName = "GameData:/Assets/2D/Interface/UseOutGame/Division/Emblem/US_Armored.png"
        )
        DivisionAdditionalTextureBank is TBUCKToolAdditionalTextureBank
        (
            Textures = MAP [ ("Texture_Division_Emblem_US_Armored", MAP [(~/ComponentState/Normal, ~/Texture_Division_Emblem_US_Armored)]) ]
        )
        """;

    private static List<Division> ReadMadeUp(TemporaryFolder temp, Dictionary<string, string> files, GameTexts texts, List<string> problems)
    {
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, files);
        using var gameData = new GameDataFiles(zip, null);
        return DivisionReader.Read(gameData, texts, problems);
    }

    [Fact]
    public void A_WARNO_division_is_read_with_its_details_units_and_costs()
    {
        using var temp = new TemporaryFolder();
        var problems = new List<string>();
        List<Division> divisions = ReadMadeUp(temp, new()
        {
            ["Generated/Gameplay/Decks/Divisions.ndf"] = WarnoDivisions,
            ["Generated/Gameplay/Decks/DivisionRules.ndf"] = WarnoDivisionRules,
            ["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"] = WarnoCosts,
            ["Generated/UserInterface/Textures/DivisionTextures.ndf"] = WarnoEmblems,
        }, GameTexts.FromPairs([("DIVNAME", "1st Armored")]), problems);

        Assert.Empty(problems);
        Assert.Equal(["Descriptor_Deck_Division_US_Armored_multi", "Descriptor_Deck_Division_US_Armored_solo"], divisions.Select(d => d.Descriptor));
        Division armored = divisions[0];
        Assert.Equal(("US_Armored_multi", "DIVNAME", "1st Armored"), (armored.CfgName, armored.NameToken, armored.Name));
        Assert.Equal(("NATO", "US", "ARMORED"), (armored.Side, armored.Country, armored.TypeToken));
        Assert.Equal((50, 3.0), (armored.ActivationPoints, armored.ListOrder));
        Assert.Equal("Texture_Division_Emblem_US_Armored", armored.EmblemTexture);
        Assert.Equal("GameData:/Assets/2D/Interface/UseOutGame/Division/Emblem/US_Armored.png", armored.EmblemSource);
        Assert.Equal(["Descriptor_Unit_Tank_US", "Descriptor_Unit_Rifles_US"], armored.StandoutUnits);
        Assert.True(armored.IsForMultiplayer);
        Assert.Equal(("Descriptor_Deck_Division_US_Armored_multi_Rule", "MatrixCostName_US_Armored_multi"), (armored.RuleName, armored.CostMatrixName));

        Assert.Equal(2, armored.Units.Count);
        DivisionUnit tank = armored.Units[0];
        Assert.Equal(("Descriptor_Unit_Tank_US", 6, (int?)4, true), (tank.Unit, tank.Cards, tank.UnitsPerCard, tank.AvailableWithoutTransport));
        Assert.Equal([1.0, 0.6, 0.34, 0.0], tank.VeterancyMultipliers);
        Assert.Empty(tank.Transports);
        Assert.Empty(tank.UnitsPerCardByPhase);
        DivisionUnit rifles = armored.Units[1];
        Assert.False(rifles.AvailableWithoutTransport);
        Assert.Equal(["Descriptor_Unit_Humvee_US", "Descriptor_Unit_Bradley_US"], rifles.Transports);

        // each category's cost of each card in turn; a category with none allows none
        Assert.Equal([1, 2, 2, 3, 3], armored.Costs["Art"]);
        Assert.Equal([1, 1, 1, 2], armored.Costs["Tanks"]);
        Assert.Empty(armored.Costs["Defense"]);

        // a division the game does not tag DEFAULT is read too, but is not for multiplayer; a name is made for it
        Division solo = divisions[1];
        Assert.False(solo.IsForMultiplayer);
        Assert.Equal("US Armored solo", solo.Name);
        Assert.Empty(solo.Units);
        Assert.Empty(solo.Costs);
    }

    private const string Sd2Divisions = """
        export Descriptor_Deck_Division_US_3rd_Armored is TDeckDivisionDescriptor
        (
            DivisionName = 'SDDIVNAME'
            DivisionNationalite = ENationalite/Allied
            DivisionTags = ['West', 'DEFAULT', 'US']
            CostMatrix = MatrixCostName_US_3rd_Armored
        )
        export Descriptor_Deck_Division_US_Challenge is TDeckDivisionDescriptor
        (
            DivisionNationalite = ENationalite/Allied
            DivisionTags = ['DEFAULT', 'US']
            CostMatrix = MAP [ (EDefaultFactories/reco, [0, 0]) ]
        )
        """;

    private const string Sd2DivisionList = """
        private DivisionList is TDeckDivisionList
        (
            DivisionList =
            [
                ~/Descriptor_Deck_Division_US_3rd_Armored,
            ]
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
                        UnitRuleList =
                        [
                            TDeckUniteRule
                            (
                                UnitDescriptor = ~/Descriptor_Unit_Calliope_US
                                AvailableWithoutTransport = True
                                AvailableTransportList = [~/Descriptor_Unit_Truck_US]
                                MaxPackNumber = 1
                                NumberOfUnitInPackByPhase = [6, 12, 0]
                                NumberOfUnitInPackXPMultiplier = [0.0, 1.0, 0.0]
                            ),
                        ]
                    )
                ),
            ]
        )
        """;

    private const string Sd2Costs = """
        MatrixCostName_US_3rd_Armored is MAP
        [
            (EDefaultFactories/tank, [1, 1, 2, 3]),
        ]
        """;

    [Fact]
    public void A_Steel_Division_2_division_is_read_from_its_own_layout()
    {
        using var temp = new TemporaryFolder();
        var problems = new List<string>();
        List<Division> divisions = ReadMadeUp(temp, new()
        {
            ["Generated/Gameplay/Decks/Divisions.ndf"] = Sd2Divisions,
            ["Generated/Gameplay/Decks/DivisionRules.ndf"] = Sd2DivisionRules,
            ["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"] = Sd2Costs,
            ["Generated/Gameplay/Decks/DivisionList.ndf"] = Sd2DivisionList,
        }, GameTexts.FromPairs([("SDDIVNAME", "3rd Armored Division")]), problems);

        Assert.Empty(problems);
        Assert.Equal(2, divisions.Count);
        Division armored = divisions[0];
        Assert.Equal(("3rd Armored Division", "Allied", ""), (armored.Name, armored.Side, armored.Country));
        // offered when in the game's list of divisions; tagged DEFAULT but not in the list, it is not
        Assert.True(armored.IsForMultiplayer);
        Assert.False(divisions[1].IsForMultiplayer);
        // costs written in place, as a MAP, rather than named
        Assert.Equal([0, 0], divisions[1].Costs["Recons"]);
        // the rule is found by the division's own name, the key of the game's one MAP of rules
        DivisionUnit calliope = Assert.Single(armored.Units);
        Assert.Equal(("Descriptor_Unit_Calliope_US", 1, (int?)null), (calliope.Unit, calliope.Cards, calliope.UnitsPerCard));
        Assert.Equal([6, 12, 0], calliope.UnitsPerCardByPhase);
        Assert.Equal(["Descriptor_Unit_Truck_US"], calliope.Transports);
        // named as the units name their factory (EDefaultFactories/Tanks), not as the costs write it
        Assert.Equal([1, 1, 2, 3], armored.Costs["Tanks"]);
    }

    [Fact]
    public void The_catalogue_of_units_carries_every_division()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string>
        {
            ["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = "",
            ["Generated/Gameplay/Decks/Divisions.ndf"] = WarnoDivisions,
            ["Generated/Gameplay/Decks/DivisionRules.ndf"] = WarnoDivisionRules,
        });
        using var gameData = new GameDataFiles(zip, null);

        UnitCatalog catalog = UnitReader.Read(gameData, GameTitle.Warno, GameTexts.FromPairs([]));

        Assert.Equal(2, catalog.Divisions.Count);
        Assert.Equal(2, catalog.Divisions[0].Units.Count);
    }
}
