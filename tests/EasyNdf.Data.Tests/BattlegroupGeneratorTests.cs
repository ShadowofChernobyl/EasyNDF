using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Engine.Mods;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Generated battlegroups: the cards Generation Settings choose from a made-up division's units, the settings kept in
/// the mod, and the rules the export fills with the cards.
/// </summary>
public class BattlegroupGeneratorTests
{
    /// <summary>Only survivability counts: no value for points, no variety.</summary>
    private static readonly IReadOnlyList<int> SurvivabilityOnly = [0, 0, 0, 100, 0, 0, 0, 0];

    private static Unit Tank(string name, double strength, int front, double cost) => new()
    {
        Descriptor = "Descriptor_Unit_" + name,
        Name = name,
        Nation = "US",
        Side = "NATO",
        Factory = "Tanks",
        Role = "tank_A",
        HasDeckNumber = true,
        Strength = strength,
        Armor = new UnitArmor(front, null, null, null),
        Cost = cost,
    };

    /// <summary>Four tanks of one role: survivability 30, 16, 7.2 and 1, costing 200, 100, 40 and 10.</summary>
    private static Dictionary<string, Unit> Tanks() => new[]
    {
        Tank("Heavy", 10, 20, 200),
        Tank("Medium", 8, 10, 100),
        Tank("Light", 6, 2, 40),
        Tank("Weak", 1, 0, 10),
    }.ToDictionary(unit => unit.Descriptor);

    /// <summary>A division with every tank, four tank card slots at 1 point each, and 50 activation points.</summary>
    private static Division Division() => new()
    {
        Descriptor = "Descriptor_Deck_Division_Test_multi",
        Name = "TEST",
        Country = "US",
        Side = "NATO",
        ActivationPoints = 50,
        Costs = new Dictionary<string, IReadOnlyList<int>> { ["Tanks"] = [1, 1, 1, 1] },
        Units = [.. Tanks().Keys.Select(unit => new DivisionUnit { Unit = unit, Cards = 1, UnitsPerCard = 4, VeterancyMultipliers = [1, 0, 0, 0], AvailableWithoutTransport = true })],
    };

    private static List<string> Picked(BattlegroupGeneration settings) =>
        [.. BattlegroupGenerator.Cards(Division(), Tanks(), settings).Select(card => card.Card.Unit.Replace("Descriptor_Unit_", "") + " " + card.Pick)];

    [Fact]
    public void Each_role_gets_its_best_its_good_value_and_its_cheap_unit_highest_score_first()
    {
        // good value: Light gives the most score per point; cheap: Medium is the cheapest with half Heavy's score
        Assert.Equal(["Heavy Best", "Medium Cheap", "Light GoodValue"], Picked(new BattlegroupGeneration { Weights = SurvivabilityOnly }));
        Assert.Equal(["Heavy Best"], Picked(new BattlegroupGeneration { Weights = SurvivabilityOnly, TakesGoodValue = false, TakesCheap = false }));
    }

    [Fact]
    public void The_size_keeps_the_highest_ranked_cards()
    {
        Assert.Equal(["Heavy Best", "Medium Cheap"], Picked(new BattlegroupGeneration { Weights = SurvivabilityOnly, CardsPerCategory = 2 }));
        Assert.Equal(["Heavy Best"], Picked(new BattlegroupGeneration { Weights = SurvivabilityOnly, PointsAtMost = 1 }));
    }

    [Fact]
    public void Settings_are_kept_in_the_mod_file_and_custom_weights_are_named_Custom()
    {
        var settings = new BattlegroupGeneration { Weights = SurvivabilityOnly, ReadySetting = BattlegroupGeneration.ReadySettingOf(SurvivabilityOnly), TakesCheap = false, CardsPerCategory = 3, PointsAtMost = 40 };
        BattlegroupGeneration read = BattlegroupGeneration.FromJson(JsonNode.Parse(settings.ToJson().ToJsonString())!.AsObject());

        Assert.Equal(BattlegroupGeneration.Custom, read.ReadySetting);
        Assert.True(read.SameAs(settings));
        Assert.Equal("AntiTank", BattlegroupGeneration.ReadySettingOf(BattlegroupGeneration.ReadySettings["AntiTank"]));
    }

    [Fact]
    public void A_generated_division_s_battlegroup_rules_are_filled_with_its_cards_at_export()
    {
        Division frame = Division() with { IsForMultiplayer = true };
        List<Division> game = [frame];
        var rosters = new DivisionRosters([.. Tanks().Values], game);
        var mod = new Mod { Name = "Test" };
        var generation = new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = "US", Side = "NATO", Slots = 4 };
        string added = DivisionChanges.AddGenerated(mod, game, "US Tanks", frame, generation, ("US", "NATO"), GameTitle.Warno);
        Assert.NotNull(mod.Divisions[0].BattlegroupGeneration);

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Assert.True(GeneratedBattlegroups.AnyIn(rules));
        Dictionary<string, IReadOnlyList<BattlegroupCard>> battlegroups = GeneratedBattlegroups.CardsOf(rules, rosters);
        GeneratedBattlegroups.Fill(rules, battlegroups, new HashSet<string>());

        Division shown = DivisionChanges.Apply(game, mod, rosters).First(division => division.Descriptor == added);
        Assert.Equal(shown.Battlegroup!.Cards, battlegroups[added]);
        Assert.NotEmpty(battlegroups[added]);
        List<JsonNode?> filled = [.. rules.Where(GeneratedBattlegroups.IsGeneratedBattlegroupRule)];
        Assert.Equal(2, filled.Count);
        Assert.All(filled, rule => Assert.True(rule!["Enabled"]!.GetValue<bool>()));
        Assert.Contains(ModDivision.DeckDescriptorOf(added), filled[0]!["Params"]!["Text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("DeckPackDescriptor", filled[1]!["Params"]!["Text"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
