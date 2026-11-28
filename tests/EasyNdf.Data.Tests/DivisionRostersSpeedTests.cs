using System.Text.Json;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Tests.Reference;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

/// <summary>
/// The generated divisions' units are worked out faster (transports sorted once for a division, the units a unit no
/// division has is likened to found once): every generation must still give exactly the units, numbers and
/// transports, in the same order, as before (<see cref="DivisionRostersBefore"/>, the class as it was), on a few
/// hundred made-up units with ties in cost, missing values, shared descriptors and names that sort differently by
/// culture than by code.
/// </summary>
public class DivisionRostersSpeedTests
{
    private static readonly string[] Nations = ["US", "UK", "SOV", "DDR", "YSM", "FR"];
    private static readonly string[] Factories = ["Tanks", "Infantry", "Logistic", "Helis", "Recons", "Art", ""];
    private static readonly string[] AllTags = ["Infanterie", "Vehicule", "Commandant", "Lourd", "Leger", "infanterie"];
    private static readonly string[] Names = ["M1A1", "m1a1", "M2_Bradley", "M2A2", "BMP-2", "BMP_2", "T-80U", "T80U", "Ural", "UH-1", "uh1", "Mi-8", "Ä_Truck", "a_truck", "Zil", "Leo2", "Leo_2", "LEO2"];

    private static string SideOf(string nation) => nation is "SOV" or "DDR" ? "PACT" : nation == "YSM" ? "" : "NATO";

    /// <summary>Made-up units, the same every run: some carry others, some ride, some are buildings, some share a descriptor.</summary>
    private static List<Unit> MadeUpUnits(int count)
    {
        var random = new Random(1234);
        var units = new List<Unit>();
        for (int i = 0; i < count; i++)
        {
            string nation = Nations[random.Next(Nations.Length)];
            bool carries = random.Next(4) == 0;
            bool rides = random.Next(2) == 0;
            // every so often a descriptor already used, as two files can declare the same unit
            string descriptor = i > 10 && random.Next(25) == 0
                ? units[random.Next(units.Count)].Descriptor
                : "Descriptor_Unit_" + Names[random.Next(Names.Length)] + "_" + nation + "_" + random.Next(40);
            units.Add(new Unit
            {
                Descriptor = descriptor,
                Name = descriptor,
                Nation = random.Next(30) == 0 ? "" : nation,
                Side = random.Next(30) == 0 ? "" : SideOf(nation) == "" ? (random.Next(2) == 0 ? "NATO" : "PACT") : SideOf(nation),
                Factory = Factories[random.Next(Factories.Length)],
                File = random.Next(10) == 0 ? "BuildingDescriptors.ndf" : "UniteDescriptor.ndf",
                Role = random.Next(3) == 0 ? "" : "Role" + random.Next(4),
                StrategicType = random.Next(3) switch { 0 => "", 1 => "NotCounted", _ => "Type" + random.Next(3) },
                Flies = carries && random.Next(3) == 0,
                // costs in steps of five, so many tie; some have none
                Cost = random.Next(8) == 0 ? null : random.Next(20) * 5,
                Tags = [.. AllTags.Where(_ => random.Next(3) == 0)],
                CarriesTags = carries ? [.. AllTags.Where(_ => random.Next(3) == 0)] : null,
                Seats = carries && random.Next(6) != 0 ? random.Next(1, 12) : null,
                SeatsTaken = rides ? random.Next(1, 12) : null,
                HasDeckNumber = true,
            });
        }
        return units;
    }

    /// <summary>Made-up game divisions with rules for about half the units, generous and stingy, some to be ignored.</summary>
    private static List<Division> MadeUpDivisions(List<Unit> units)
    {
        var random = new Random(99);
        var divisions = new List<Division>();
        foreach (string ruleName in new[] { "Rule_US_Armored", "Rule_SOV_Tank", "Rule_UK_Infantry", "Rule_Sandbox_All", "Rule_DDR_Mech" })
        {
            List<DivisionUnit> rules = [.. units.Where(_ => random.Next(4) == 0).Select(unit => new DivisionUnit
            {
                Unit = unit.Descriptor,
                Cards = random.Next(0, 8),
                UnitsPerCard = random.Next(6) == 0 ? null : random.Next(0, 24),
                VeterancyMultipliers = [.. Enumerable.Range(0, random.Next(0, 5)).Select(_ => random.Next(0, 11) / 10.0)],
            })];
            divisions.Add(new Division { Descriptor = "Descriptor_Deck_" + ruleName, Name = ruleName, RuleName = ruleName, IsForMultiplayer = true, Units = rules });
        }
        return divisions;
    }

    /// <summary>Every kind of generation: by nation, side and everything; each kind of numbers; with and without transports and ignored rules.</summary>
    private static IEnumerable<DivisionGeneration> Generations()
    {
        foreach (GeneratedUnits kind in Enum.GetValues<GeneratedUnits>())
            foreach (GeneratedNumbers numbers in Enum.GetValues<GeneratedNumbers>())
                foreach (string nation in new[] { "US", "SOV", "UK" })
                {
                    var generation = new DivisionGeneration { Units = kind, Numbers = numbers, Nation = nation, Side = SideOf(nation), Slots = 40 };
                    yield return generation;
                    yield return generation with { EveryDivisionNations = ["YSM"] };
                    yield return generation with { AllTransports = false };
                    yield return generation with { IgnoredDivisions = ["Sandbox"], IgnoredRulesFrom = 15, Counts = [8, 4, 2, 0] };
                }
    }

    private static string Text(IReadOnlyList<DivisionUnit> units) => JsonSerializer.Serialize(units);

    [Fact]
    public void Every_generation_gives_the_same_units_numbers_and_transports_in_the_same_order_as_before()
    {
        List<Unit> units = MadeUpUnits(400);
        List<Division> game = MadeUpDivisions(units);
        var faster = new DivisionRosters(units, game);
        var before = new DivisionRostersBefore(units, game);

        int compared = 0, withTransports = 0;
        foreach (DivisionGeneration generation in Generations())
        {
            IReadOnlyList<DivisionUnit> expected = before.UnitsFor(generation);
            Assert.Equal(Text(expected), Text(faster.UnitsFor(generation)));
            compared += expected.Count;
            withTransports += expected.Count(unit => unit.Transports.Count > 1);
        }
        // the made-up units must really try the order: many units, many of them with several transports to sort
        Assert.True(compared > 10000, $"only {compared} units compared");
        Assert.True(withTransports > 1000, $"only {withTransports} units with several transports");
    }

    /// <summary>
    /// The game's units each have a tag of their own (UNITE_...), which no transport carries, and riders share their
    /// transports' list with others that need the same: each unit must still get exactly the transports, in the same
    /// order, as before, with tags repeated, in another order, or differing only in case.
    /// </summary>
    [Fact]
    public void Units_with_tags_of_their_own_get_the_same_transports_as_before()
    {
        var random = new Random(4321);
        List<Unit> units = [.. MadeUpUnits(400).Select((unit, index) => new Unit
        {
            Descriptor = unit.Descriptor, Name = unit.Name, Nation = unit.Nation, Side = unit.Side, Factory = unit.Factory, File = unit.File,
            Role = unit.Role, StrategicType = unit.StrategicType, Flies = unit.Flies, Cost = unit.Cost, CarriesTags = unit.CarriesTags,
            Seats = unit.Seats, SeatsTaken = unit.SeatsTaken, HasDeckNumber = true,
            // a tag of its own, the others repeated or in another order now and then
            Tags = [.. (random.Next(3) == 0 ? unit.Tags.Reverse() : unit.Tags), "UNITE_" + index, .. random.Next(5) == 0 ? unit.Tags : []],
        })];
        List<Division> game = MadeUpDivisions(units);
        var faster = new DivisionRosters(units, game);
        var before = new DivisionRostersBefore(units, game);

        // YSM's made-up units are of either side: a division of that nation gives each unit only its own side's transports
        DivisionGeneration bothSidesNation = new() { Units = GeneratedUnits.Nation, Numbers = GeneratedNumbers.Same, Nation = "YSM", Side = "NATO", Slots = 40 };
        int withTransports = 0;
        foreach (DivisionGeneration generation in Generations().Append(bothSidesNation))
        {
            IReadOnlyList<DivisionUnit> expected = before.UnitsFor(generation);
            Assert.Equal(Text(expected), Text(faster.UnitsFor(generation)));
            withTransports += expected.Count(unit => unit.Transports.Count > 1);
        }
        Assert.True(withTransports > 1000, $"only {withTransports} units with several transports");
    }

    /// <summary>
    /// Two riders alike in all but their side, in a division of a nation whose units are of both sides: each gets only
    /// its own side's transport, though they need the same seats and tags.
    /// </summary>
    [Fact]
    public void Riders_alike_but_for_their_side_get_their_own_sides_transports()
    {
        static Unit Transport(string name, string side) => new()
        {
            Descriptor = "Descriptor_Unit_" + name, Name = name, Nation = "YSM", Side = side, Factory = "Logistic", Cost = 10,
            Tags = ["UNITE_" + name], CarriesTags = ["Infanterie"], Seats = 8, HasDeckNumber = true,
        };
        static Unit Rider(string name, string side) => new()
        {
            Descriptor = "Descriptor_Unit_" + name, Name = name, Nation = "YSM", Side = side, Factory = "Infantry", Cost = 20,
            Tags = ["Infanterie", "UNITE_" + name], SeatsTaken = 4, HasDeckNumber = true,
        };
        List<Unit> units = [Transport("Truck_NATO", "NATO"), Transport("Truck_PACT", "PACT"), Rider("Squad_NATO", "NATO"), Rider("Squad_PACT", "PACT")];
        var generation = new DivisionGeneration { Units = GeneratedUnits.Nation, Numbers = GeneratedNumbers.Same, Nation = "YSM", Side = "NATO", Slots = 4 };

        IReadOnlyList<DivisionUnit> made = new DivisionRosters(units, []).UnitsFor(generation);

        Assert.Equal(["Descriptor_Unit_Truck_NATO"], made.Single(unit => unit.Unit == "Descriptor_Unit_Squad_NATO").Transports);
        Assert.Equal(["Descriptor_Unit_Truck_PACT"], made.Single(unit => unit.Unit == "Descriptor_Unit_Squad_PACT").Transports);
        Assert.Equal(Text(new DivisionRostersBefore(units, []).UnitsFor(generation)), Text(made));
    }

    [Fact]
    public void A_generation_asked_for_again_gives_the_same_units_as_the_first_time()
    {
        List<Unit> units = MadeUpUnits(200);
        List<Division> game = MadeUpDivisions(units);
        var rosters = new DivisionRosters(units, game);
        var generation = new DivisionGeneration { Units = GeneratedUnits.Side, Numbers = GeneratedNumbers.AsTheGame, Side = "NATO" };

        string first = Text(rosters.UnitsFor(generation));
        // another generation in between, with other ignored rules, uses its own game rules and units to liken to
        rosters.UnitsFor(generation with { IgnoredDivisions = ["Sandbox"] });

        Assert.Equal(first, Text(rosters.UnitsFor(generation with { })));
        Assert.Equal(first, Text(new DivisionRostersBefore(units, game).UnitsFor(generation)));
    }
}
