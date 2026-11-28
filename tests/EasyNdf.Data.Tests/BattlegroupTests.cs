using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

public class BattlegroupTests
{
    // ---- made-up decks for the made-up divisions of DivisionReaderTests ----

    internal const string Decks = """
        // Ne pas éditer
        export Descriptor_Deck_US_Armored_multi is TDeckDescriptor
        (
            DeckDivision = $/GFX/Division/Descriptor_Deck_Division_US_Armored_multi
            DeckName = "DECKNAME"
            DeckPackList =
            [
                ~/Descriptor_Deck_Pack_Tank_US_1_1,
                ~/Descriptor_Deck_Pack_Rifles_US_Humvee_US_0_1,
                ~/Descriptor_Deck_Pack_Missing_US_0_1,
                ~/Descriptor_Deck_Pack_Tank_US_1_1,
            ]
        )
        export Descriptor_Deck_US_Armored_solo_Mission is TDeckDescriptor
        (
            DeckDivision = $/GFX/Division/Descriptor_Deck_Division_US_Armored_solo
            DeckName = "MISSION"
            DeckPackList = [ ~/Descriptor_Deck_Pack_Tank_US_1_1 ]
        )
        """;

    internal const string DeckPacks = """
        Descriptor_Deck_Pack_Tank_US_1_1 is DeckPackDescriptor
        (
            Xp = 1
            Unit = $/GFX/Unit/Descriptor_Unit_Tank_US
            Number = 1
        )

        Descriptor_Deck_Pack_Rifles_US_Humvee_US_0_1 is DeckPackDescriptor
        (
            Transport = $/GFX/Unit/Descriptor_Unit_Humvee_US
            Unit = $/GFX/Unit/Descriptor_Unit_Rifles_US
            Number = 1
        )
        """;

    private static List<Division> ReadMadeUp(TemporaryFolder temp, List<string> problems)
    {
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string>
        {
            ["Generated/Gameplay/Decks/Divisions.ndf"] = DivisionReaderTests.WarnoDivisions,
            ["Generated/Gameplay/Decks/DivisionRules.ndf"] = DivisionReaderTests.WarnoDivisionRules,
            ["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"] = DivisionReaderTests.WarnoCosts,
            ["Generated/Gameplay/Decks/Decks.ndf"] = Decks,
            ["Generated/Gameplay/Decks/DeckPacks.ndf"] = DeckPacks,
        });
        using var gameData = new GameDataFiles(zip, null);
        return DivisionReader.Read(gameData, GameTexts.FromPairs([("DECKNAME", "1ST ARMORED OFFICIAL BATTLEGROUP")]), problems);
    }

    [Fact]
    public void A_division_players_can_pick_has_its_official_battlegroup_with_its_cards_in_order()
    {
        using var temp = new TemporaryFolder();
        var problems = new List<string>();
        List<Division> divisions = ReadMadeUp(temp, problems);

        Battlegroup? battlegroup = divisions[0].Battlegroup;
        Assert.NotNull(battlegroup);
        Assert.Equal(("Descriptor_Deck_US_Armored_multi", "DECKNAME", "1ST ARMORED OFFICIAL BATTLEGROUP"),
            (battlegroup.Descriptor, battlegroup.NameToken, battlegroup.Name));
        // a card without Xp is at the lowest level, and one without Transport has none
        Assert.Equal(
            [
                new BattlegroupCard("Descriptor_Unit_Tank_US", 1, null),
                new BattlegroupCard("Descriptor_Unit_Rifles_US", 0, "Descriptor_Unit_Humvee_US"),
                new BattlegroupCard("Descriptor_Unit_Tank_US", 1, null),
            ],
            battlegroup.Cards);
        // a card DeckPacks.ndf does not have is left out, and said
        Assert.Equal(["Decks.ndf, Descriptor_Deck_US_Armored_multi: Descriptor_Deck_Pack_Missing_US_0_1 is not in DeckPacks.ndf"], problems);
        // a scripted mission's deck names a division no player can pick: it is no Official Battlegroup
        Assert.Null(divisions[1].Battlegroup);
    }

    // ---- the checks ----

    private static readonly Dictionary<string, string> Types = new()
    {
        ["Descriptor_Unit_Tank_US"] = "Tanks", ["Descriptor_Unit_Rifles_US"] = "Infantry", ["Descriptor_Unit_Leopard_BEL"] = "Tanks",
    };

    /// <summary>The made-up armored division, with infantry costs added: 1, 1, 2.</summary>
    private static Division MadeUpDivision()
    {
        using var temp = new TemporaryFolder();
        Division division = ReadMadeUp(temp, []).First();
        var costs = division.Costs.ToDictionary(cost => cost.Key, cost => cost.Value);
        costs["Infantry"] = [1, 1, 2];
        return division with { Costs = costs };
    }

    private static List<BattlegroupProblem> Check(params BattlegroupCard[] cards) =>
        BattlegroupCheck.Problems(cards, MadeUpDivision(), unit => Types.GetValueOrDefault(unit));

    [Fact]
    public void Cards_that_fit_the_division_have_no_problem()
    {
        Assert.Empty(Check(
            new BattlegroupCard("Descriptor_Unit_Tank_US", 0, null),
            new BattlegroupCard("Descriptor_Unit_Rifles_US", 2, "Descriptor_Unit_Bradley_US")));
    }

    [Fact]
    public void A_card_the_division_does_not_allow_is_a_problem_of_that_card()
    {
        List<BattlegroupProblem> problems = Check(
            new BattlegroupCard("Descriptor_Unit_Tank_US", 0, null),
            new BattlegroupCard("Descriptor_Unit_Leopard_BEL", 1, null),
            new BattlegroupCard("Descriptor_Unit_Rifles_US", 0, null),
            new BattlegroupCard("Descriptor_Unit_Rifles_US", 0, "Descriptor_Unit_Truck_US"),
            new BattlegroupCard("Descriptor_Unit_Tank_US", 3, null));

        Assert.Equal(
            [
                new BattlegroupProblem(BattlegroupProblemKind.UnitNotInDivision, [1], "Descriptor_Unit_Leopard_BEL"),
                new BattlegroupProblem(BattlegroupProblemKind.NeedsTransport, [2], "Descriptor_Unit_Rifles_US"),
                new BattlegroupProblem(BattlegroupProblemKind.TransportNotAllowed, [3], "Descriptor_Unit_Rifles_US", Transport: "Descriptor_Unit_Truck_US"),
                // the tank's fourth level has a share of 0: not offered
                new BattlegroupProblem(BattlegroupProblemKind.VeterancyNotOffered, [4], "Descriptor_Unit_Tank_US", Count: 3),
            ],
            problems,
            new ProblemComparer());
    }

    [Fact]
    public void Too_many_cards_of_a_unit_or_a_type_and_too_many_points_are_problems()
    {
        // the division allows 6 tank cards, its tanks cost 1, 1, 1, 2 (4 cards), and it has 50 points
        var tank = new BattlegroupCard("Descriptor_Unit_Tank_US", 0, null);
        List<BattlegroupProblem> problems = Check(tank, tank, tank, tank, tank, tank, tank);

        Assert.Equal(
            [
                new BattlegroupProblem(BattlegroupProblemKind.TooManyCardsOfUnit, [0, 1, 2, 3, 4, 5, 6], "Descriptor_Unit_Tank_US", 7, 6),
                new BattlegroupProblem(BattlegroupProblemKind.TooManyCardsOfType, [0, 1, 2, 3, 4, 5, 6], "Tanks", 7, 4),
            ],
            problems,
            new ProblemComparer());
    }

    [Fact]
    public void Points_are_each_type_s_costs_in_turn()
    {
        Division division = MadeUpDivision();
        var tank = new BattlegroupCard("Descriptor_Unit_Tank_US", 0, null);
        var rifles = new BattlegroupCard("Descriptor_Unit_Rifles_US", 0, "Descriptor_Unit_Humvee_US");

        // tanks: 1 + 1 + 1 + 2; infantry: 1 + 1
        Assert.Equal(7, BattlegroupCheck.Points([tank, rifles, tank, tank, rifles, tank], division, unit => Types.GetValueOrDefault(unit)));
        Assert.Equal(
            [new BattlegroupProblem(BattlegroupProblemKind.TooManyPoints, [], Count: 1, Allowed: 0)],
            BattlegroupCheck.Problems([tank], division with { ActivationPoints = 0 }, unit => Types.GetValueOrDefault(unit)),
            new ProblemComparer());
    }

    /// <summary>Compares problems by value, their lists of cards included.</summary>
    private sealed class ProblemComparer : IEqualityComparer<BattlegroupProblem>
    {
        public bool Equals(BattlegroupProblem? one, BattlegroupProblem? other) =>
            one is not null && other is not null && one.Cards.SequenceEqual(other.Cards)
            && (one.Kind, one.Subject, one.Count, one.Allowed, one.Transport) == (other.Kind, other.Subject, other.Count, other.Allowed, other.Transport);

        public int GetHashCode(BattlegroupProblem problem) => problem.Kind.GetHashCode();
    }
}
