using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;

namespace EasyNdf.Data.Tests;

/// <summary>
/// The Armory presets: each part's changes in its own preset, and deleting their rules on the Rules tab taking the
/// Armory changes back.
/// </summary>
public class ArmoryPresetsTests
{
    private const string Armored = "Descriptor_Deck_Division_US_Armored_multi";

    private static readonly BattlegroupCard Tank = new("Descriptor_Unit_Tank_US", 0, null);

    private static List<Division> Game() =>
    [
        new()
        {
            Descriptor = Armored, Name = "Armored", RuleName = Armored + "_Rule", IsForMultiplayer = true, EmblemTexture = "T", ActivationPoints = 50,
            Summary = "The game's",
            Battlegroup = new Battlegroup { Descriptor = "Descriptor_Deck_US_Armored_multi", Name = "Game's battlegroup", Cards = [Tank] },
        },
    ];

    private static void Update(Mod mod, List<Division> game) => ArmoryPresets.Update(mod, game, new TextCatalog(), unit => unit, part => part.ToString());

    private static List<JsonNode?> RulesFor(Mod mod, string change) =>
        [.. ArmoryPresets.RulesOf(mod).Where(rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.ChangeKey] == change)];

    private static List<string> Kinds(ModPreset preset) => [.. preset.Rules.Select(rule => (string?)rule?["Kind"] ?? "")];

    [Fact]
    public void Each_part_keeps_its_own_rules_under_its_own_copy_of_the_division_heading()
    {
        List<Division> game = Game();
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.ActivationPoints = 70);
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.BattlegroupCards = [Tank, Tank]);
        Update(mod, game);

        ModPreset divisions = ArmoryPresets.Of(mod, ArmoryPart.Divisions)!;
        ModPreset battlegroups = ArmoryPresets.Of(mod, ArmoryPart.Battlegroups)!;
        Assert.Equal(["Group", "Edit"], Kinds(divisions));
        Assert.Equal(["Group", "Edit"], Kinds(battlegroups));
        Assert.Equal("Armored", (string?)battlegroups.Rules[0]!["Name"]);
        Assert.Equal(ArmoryPart.Battlegroups, DivisionChangeRules.PartOf(battlegroups.Rules[1]));
        Assert.Null(ArmoryPresets.Of(mod, ArmoryPart.Units));
        Assert.Equal([divisions, battlegroups], ArmoryPresets.AllOf(mod));
    }

    [Fact]
    public void A_part_whose_changes_are_all_taken_back_loses_its_preset()
    {
        List<Division> game = Game();
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.ActivationPoints = 70);
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.BattlegroupCards = [Tank, Tank]);
        Update(mod, game);

        ArmoryPresets.TakeBack(mod, game, RulesFor(mod, "Battlegroup"));
        Update(mod, game);

        Assert.Null(ArmoryPresets.Of(mod, ArmoryPart.Battlegroups));
        Assert.NotNull(ArmoryPresets.Of(mod, ArmoryPart.Divisions));
    }

    [Fact]
    public void A_mod_saved_with_one_Armory_preset_has_its_battlegroup_rules_moved_to_their_own()
    {
        List<Division> game = Game();
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.BattlegroupCards = [Tank, Tank]);
        Update(mod, game);
        // as a mod file from before the Armory had parts reads: one preset, marked FromArmory only
        JsonNode file = JsonNode.Parse(ModFiles.ExportText(mod))!;
        ((JsonObject)file["Presets"]![0]!).Remove("ArmoryPart");
        Mod old = ModFiles.Read(file.ToJsonString(), "old.json").Single();
        Assert.Equal(ArmoryPart.Divisions, old.Presets.Single().ArmoryPart);
        Assert.True(ArmoryPresets.HasRulesOutOfPlace(old));

        Update(old, game);

        Assert.False(ArmoryPresets.HasRulesOutOfPlace(old));
        Assert.Equal(ArmoryPart.Battlegroups, old.Presets.Single().ArmoryPart);
    }

    [Fact]
    public void An_Armory_preset_keeps_its_part_in_the_mod_file()
    {
        List<Division> game = Game();
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.BattlegroupCards = [Tank, Tank]);
        Update(mod, game);

        Mod read = ModFiles.Read(ModFiles.ExportText(mod), "mod.json").Single();

        Assert.Equal(ArmoryPart.Battlegroups, read.Presets.Single().ArmoryPart);
    }

    [Fact]
    public void Deleting_a_new_divisions_battlegroup_rules_empties_its_battlegroup_and_keeps_the_division()
    {
        List<Division> game = Game();
        var mod = new Mod { Name = "Test" };
        string added = DivisionChanges.AddNew(mod, game, "Test Brigade", game[0], [], GameTitle.Warno);
        DivisionChanges.ChangeDetails(mod, game, added, change => change.BattlegroupCards = [Tank]);
        Update(mod, game);

        ArmoryPresets.TakeBack(mod, game, [.. ArmoryPresets.Of(mod, ArmoryPart.Battlegroups)!.Rules]);
        Update(mod, game);

        Assert.Single(mod.Divisions);
        Assert.NotNull(ArmoryPresets.Of(mod, ArmoryPart.Divisions));
        Assert.Null(ArmoryPresets.Of(mod, ArmoryPart.Battlegroups));
    }

    [Fact]
    public void Deleting_a_change_rule_gives_the_game_division_that_value_back_and_keeps_its_other_changes()
    {
        List<Division> game = Game();
        var mod = new Mod { Name = "Test" };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.ActivationPoints = 70);
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.Summary = "Mine");
        Update(mod, game);

        ArmoryPresets.TakeBack(mod, game, RulesFor(mod, "Summary"));
        Update(mod, game);

        Division shown = DivisionChanges.Apply(game, mod).Single();
        Assert.Equal("The game's", shown.Summary);
        Assert.Equal(70, shown.ActivationPoints);
        Assert.Empty(RulesFor(mod, "Summary"));
        Assert.NotEmpty(RulesFor(mod, "Points"));
    }

    [Fact]
    public void Deleting_any_rule_of_a_new_division_deletes_the_division_and_the_preset_with_nothing_left()
    {
        List<Division> game = Game();
        var mod = new Mod { Name = "Test" };
        DivisionChanges.AddNew(mod, game, "Test Brigade", game[0], [], GameTitle.Warno);
        Update(mod, game);

        ArmoryPresets.TakeBack(mod, game, RulesFor(mod, "New").Take(1));
        Update(mod, game);

        Assert.Empty(mod.Divisions);
        Assert.False(ArmoryPresets.HasAny(mod));
    }
}
