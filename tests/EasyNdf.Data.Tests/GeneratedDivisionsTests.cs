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
/// Generated divisions: the units each kind takes and how many of each, the user's changes on top, Make Fixed, the
/// mod file, and the unit list the export makes, run on made-up division files.
/// </summary>
public class GeneratedDivisionsTests
{
    private const string Armored = "Descriptor_Deck_Division_US_Armored_multi";

    private static Unit MadeUp(string name, string nation, string side, string factory = "Tanks") => new()
    {
        Descriptor = "Descriptor_Unit_" + name,
        Name = name,
        Nation = nation,
        Side = side,
        Factory = factory,
        HasDeckNumber = true,
        Cost = 50,
    };

    /// <summary>
    /// US tanks and rifles with a truck that carries them, a Soviet tank, a unit of a mod's own nation, and one no deck
    /// can hold (it has no category).
    /// </summary>
    private static List<Unit> Units() =>
    [
        MadeUp("Tank_US", "US", "NATO"),
        new Unit { Descriptor = "Descriptor_Unit_Rifles_US", Name = "Rifles", Nation = "US", Side = "NATO", Factory = "Infantry", HasDeckNumber = true, Tags = ["Infanterie"], SeatsTaken = 1, Cost = 30 },
        new Unit { Descriptor = "Descriptor_Unit_Truck_US", Name = "Truck", Nation = "US", Side = "NATO", Factory = "Logistic", HasDeckNumber = true, CarriesTags = ["Infanterie"], Seats = 10, Cost = 10 },
        MadeUp("Tank_SOV", "SOV", "PACT"),
        MadeUp("Heli_YSM", "YSM", "NATO", "Helis"),
        MadeUp("Prop_US", "US", "NATO", factory: ""),
    ];

    private static Division Game(string descriptor, string country, params DivisionUnit[] units) => new()
    {
        Descriptor = descriptor, Name = descriptor, Country = country, Side = country == "SOV" ? "PACT" : "NATO", IsForMultiplayer = true, Units = units,
    };

    private static List<Division> GameDivisions() =>
    [
        Game(Armored, "US", new DivisionUnit { Unit = "Descriptor_Unit_Tank_US", Cards = 6, UnitsPerCard = 4, VeterancyMultipliers = [1, 0.6, 0.34, 0] }),
        Game("Descriptor_Deck_Division_SOV_multi", "SOV"),
    ];

    [Fact]
    public void A_nation_s_division_takes_its_deckable_units_A_to_Z_and_the_units_of_nations_in_every_division()
    {
        var rosters = new DivisionRosters(Units(), GameDivisions());

        List<string> units = [.. rosters.UnitsFor(new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = "US", Side = "NATO" }).Select(unit => unit.Unit)];
        List<string> withMods = [.. rosters.UnitsFor(new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = "SOV", Side = "PACT", EveryDivisionNations = ["YSM"] }).Select(unit => unit.Unit)];

        Assert.Equal(["Descriptor_Unit_Rifles_US", "Descriptor_Unit_Tank_US", "Descriptor_Unit_Truck_US"], units);
        // a nation in every division joins those of the other side too
        Assert.Equal(["Descriptor_Unit_Heli_YSM", "Descriptor_Unit_Tank_SOV"], withMods);
    }

    [Fact]
    public void Everything_takes_both_sides_and_a_side_s_division_is_of_its_biggest_nation()
    {
        var rosters = new DivisionRosters(Units(), GameDivisions());
        var everything = new DivisionGeneration { Units = GeneratedUnits.Everything, Side = "PACT" };

        Assert.Equal(5, rosters.UnitsFor(everything).Count);
        Assert.Equal("SOV", rosters.NationOf(everything));
        Assert.Equal("US", rosters.NationOf(new DivisionGeneration { Units = GeneratedUnits.Side, Side = "NATO" }));
    }

    private static string Numbers(DivisionUnit unit) =>
        unit.Cards + " " + unit.UnitsPerCard + " " + string.Join(",", unit.VeterancyMultipliers.Select(share => share.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Each_kind_gives_its_numbers_and_every_unit_its_transports()
    {
        var rosters = new DivisionRosters(Units(), GameDivisions());
        var nato = new DivisionGeneration { Units = GeneratedUnits.Side, Side = "NATO", Slots = 40 };

        DivisionUnit unlimited = rosters.UnitsFor(nato).First(unit => unit.Unit == "Descriptor_Unit_Rifles_US");
        DivisionUnit same = rosters.UnitsFor(nato with { Numbers = GeneratedNumbers.Same }).First(unit => unit.Unit == "Descriptor_Unit_Tank_US");
        DivisionUnit asTheGame = rosters.UnitsFor(nato with { Numbers = GeneratedNumbers.AsTheGame }).First(unit => unit.Unit == "Descriptor_Unit_Tank_US");
        DivisionUnit likeAnother = rosters.UnitsFor(nato with { Numbers = GeneratedNumbers.AsTheGame }).First(unit => unit.Unit == "Descriptor_Unit_Heli_YSM");

        Assert.Equal("40 999 1,1,1,1", Numbers(unlimited));
        Assert.Equal(["Descriptor_Unit_Truck_US"], unlimited.Transports);
        Assert.Equal("40 10 1,0.6,0.3,0.1", Numbers(same));
        Assert.Equal("40 4 1,0.6,0.34,0", Numbers(asTheGame));
        // a unit no division has, and none of its category either: the most generous rule of the units most like it
        // (the tank, the only one with a rule), its units counted at each veterancy (4, 2, 1, 0)
        Assert.Equal("40 4 1,0.5,0.25,0", Numbers(likeAnother));
    }

    [Fact]
    public void Transports_that_fly_come_first_and_a_nation_in_every_division_rides_with_either_side()
    {
        List<Unit> units =
        [
            .. Units(),
            new Unit { Descriptor = "Descriptor_Unit_Huey_US", Name = "Huey", Nation = "US", Side = "NATO", Factory = "Helis", Flies = true, CarriesTags = ["Infanterie"], Seats = 8, Cost = 5 },
            new Unit { Descriptor = "Descriptor_Unit_Bus_YSM", Name = "Bus", Nation = "YSM", Side = "PACT", Factory = "Logistic", CarriesTags = ["Infanterie"], Seats = 40, Cost = 99 },
        ];
        var rosters = new DivisionRosters(units, GameDivisions());

        DivisionUnit rifles = rosters.UnitsFor(new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = "US", Side = "NATO", EveryDivisionNations = ["YSM"] })
            .First(unit => unit.Unit == "Descriptor_Unit_Rifles_US");
        DivisionUnit withoutMods = rosters.UnitsFor(new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = "US", Side = "NATO" })
            .First(unit => unit.Unit == "Descriptor_Unit_Rifles_US");

        // the helicopter first, though the cheapest; then the dearest first
        Assert.Equal(["Descriptor_Unit_Huey_US", "Descriptor_Unit_Bus_YSM", "Descriptor_Unit_Truck_US"], rifles.Transports);
        Assert.Equal(["Descriptor_Unit_Huey_US", "Descriptor_Unit_Truck_US"], withoutMods.Transports);
    }

    [Fact]
    public void A_generated_division_keeps_how_it_is_made_and_the_user_s_changes_and_Make_Fixed_keeps_its_units()
    {
        List<Division> game = GameDivisions();
        var rosters = new DivisionRosters(Units(), game);
        var mod = new Mod { Name = "Test" };
        var generation = new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = "US", Side = "NATO", Slots = 20, EveryDivisionNations = ["YSM"] };
        string added = DivisionChanges.AddGenerated(mod, game, "US Everything", game[0], generation, ("US", "NATO"), GameTitle.Warno);
        DivisionUnit tank = rosters.UnitsFor(generation).First(unit => unit.Unit == "Descriptor_Unit_Tank_US");
        DivisionChanges.SetRule(mod, game, added, tank with { Cards = 3 }, rosters);
        DivisionChanges.RemoveUnit(mod, game, added, "Descriptor_Unit_Truck_US", rosters);

        ModDivision entry = ModDivision.FromJson(mod.Divisions[0].ToJson());
        Division shown = DivisionChanges.Apply(game, mod, rosters).First(division => division.Descriptor == added);

        Assert.True(entry.IsGenerated);
        Assert.True(entry.Generation!.SameAs(generation));
        Assert.Equal((20 * 9, 20), (entry.ActivationPoints!.Value, entry.Costs!["Tanks"].Count));
        Assert.Single(entry.Added);
        Assert.Equal(["Descriptor_Unit_Heli_YSM", "Descriptor_Unit_Rifles_US", "Descriptor_Unit_Tank_US"], shown.Units.Select(unit => unit.Unit));
        Assert.Equal(3, shown.Units.First(unit => unit.Unit == "Descriptor_Unit_Tank_US").Cards);

        DivisionChanges.MakeFixed(mod, game, added, rosters);
        Division fixedDivision = DivisionChanges.Apply(game, mod).First(division => division.Descriptor == added);
        Assert.False(mod.Divisions[0].IsGenerated);
        Assert.Equal(shown.Units.Select(unit => unit.Unit + unit.Cards), fixedDivision.Units.Select(unit => unit.Unit + unit.Cards));
    }

    [Fact]
    public void The_export_writes_the_unit_list_the_Armory_shows()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        var files = new Dictionary<string, string>
        {
            ["Generated/Gameplay/Decks/Divisions.ndf"] = DivisionReaderTests.WarnoDivisions,
            ["Generated/Gameplay/Decks/DivisionRules.ndf"] = DivisionReaderTests.WarnoDivisionRules,
            ["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"] = DivisionReaderTests.WarnoCosts,
            ["Generated/Gameplay/Decks/DeckSerializer.ndf"] = "unnamed TDeckSerializerEntries\n(\n    DivisionIds = MAP\n    [\n        (Descriptor_Deck_Division_US_Armored_multi, 12),\n    ]\n)\n",
        };
        GameFileWriters.BaseZip(zip, files);
        List<Division> game;
        using (var gameData = new GameDataFiles(zip)) game = DivisionReader.Read(gameData, GameTexts.FromPairs([]), []);
        var rosters = new DivisionRosters(Units(), game);
        var mod = new Mod { Name = "Test" };
        var generation = new DivisionGeneration { Units = GeneratedUnits.Side, Side = "NATO", Numbers = GeneratedNumbers.Same };
        string added = DivisionChanges.AddGenerated(mod, game, "NATO Static", game[0], generation, ("US", "NATO"), GameTitle.Warno);
        DivisionChanges.RemoveUnit(mod, game, added, "Descriptor_Unit_Heli_YSM", rosters);

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Assert.True(GeneratedUnitLists.AnyIn(rules));
        GeneratedUnitLists.Fill(rules, GeneratedUnitLists.UnitsOf(rules, rosters));
        var changed = new List<ChangedFile>();
        foreach ((string path, string text) in files)
        {
            JsonArray forFile = DivisionChangeRules.ForFile(rules, Path.GetFileName(path));
            if (forFile.Count == 0) continue;
            var engine = new NdfEngine();
            engine.Load(text);
            RunResult result = engine.Run(RuleLoading.LoadForEngine(forFile), new RunOptions());
            Assert.Equal("", result.ParseError);
            changed.Add(new ChangedFile("C:/Mod/GameData/" + path, result.Text!));
        }
        List<Division> exported;
        using (var gameData = new GameDataFiles(zip, null, changed)) exported = DivisionReader.Read(gameData, GameTexts.FromPairs([]), []);

        Division written = exported.First(division => division.Descriptor == added);
        Division shown = DivisionChanges.Apply(game, mod, rosters).First(division => division.Descriptor == added);
        Assert.Equal(shown.Units.Select(unit => unit.Unit + " " + unit.Cards + " " + unit.UnitsPerCard + " " + string.Join(",", unit.Transports)),
            written.Units.Select(unit => unit.Unit + " " + unit.Cards + " " + unit.UnitsPerCard + " " + string.Join(",", unit.Transports)));
        Assert.Equal(3, written.Units.Count);
        Assert.Equal(80 * 9, written.ActivationPoints);
    }

    [Fact]
    public void A_generated_list_rule_made_editable_keeps_the_users_list_at_export()
    {
        JsonObject ListRule(bool locked) => new()
        {
            ["Kind"] = "Edit",
            ["Actions"] = new JsonArray(new JsonObject { ["Target"] = "UnitRuleList", ["Value"] = "typed by hand" }),
            [DivisionChangeRules.ArmoryKey] = new JsonObject
            {
                [DivisionChangeRules.GeneratedDivisionKey] = new JsonObject { ["Descriptor"] = "Descriptor_Deck_Test" },
                [DivisionChangeRules.LockedKey] = locked,
            },
        };
        JsonObject kept = ListRule(locked: true), editable = ListRule(locked: false);
        var lists = new Dictionary<string, IReadOnlyList<DivisionUnit>> { ["Descriptor_Deck_Test"] = [] };

        GeneratedUnitLists.Fill([kept, editable], lists);

        Assert.Equal(GeneratedUnitLists.ListText([]), (string?)kept["Actions"]![0]!["Value"]);
        Assert.Equal("typed by hand", (string?)editable["Actions"]![0]!["Value"]);
    }
}
