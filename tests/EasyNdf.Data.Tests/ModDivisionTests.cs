using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;

namespace EasyNdf.Data.Tests;

public class ModDivisionTests
{
    private static DivisionUnit Rule(string unit, int cards = 2) => new() { Unit = unit, Cards = cards, UnitsPerCard = 4, VeterancyMultipliers = [1.0, 0.6, 0, 0] };

    /// <summary>Two made-up game divisions: an armored one with a tank and riflemen, and an infantry one with a truck.</summary>
    private static List<Division> Game() =>
    [
        new() { Descriptor = "Descriptor_Deck_Division_Armored_multi", Name = "Armored", Country = "US", Side = "NATO", IsForMultiplayer = true, Units = [Rule("Tank"), Rule("Rifles")] },
        new() { Descriptor = "Descriptor_Deck_Division_Infantry_multi", Name = "Infantry", Country = "US", Side = "NATO", IsForMultiplayer = true, Units = [Rule("Truck", cards: 5)] },
    ];

    [Fact]
    public void A_unit_added_to_a_game_division_is_kept_as_a_change_with_the_rule_another_division_gives_it()
    {
        var mod = new Mod { Name = "Test" };
        List<Division> game = Game();

        int added = DivisionChanges.AddUnits(mod, game, "Descriptor_Deck_Division_Armored_multi", ["Truck", "Tank"]);

        Assert.Equal(1, added);
        ModDivision change = Assert.Single(mod.Divisions);
        Assert.False(change.IsNew);
        Assert.Equal(5, Assert.Single(change.Added).Cards);
        Division armored = DivisionChanges.Apply(game, mod)[0];
        Assert.Equal(["Tank", "Rifles", "Truck"], armored.Units.Select(unit => unit.Unit));
    }

    [Fact]
    public void Taking_out_what_was_added_lets_the_change_go()
    {
        var mod = new Mod { Name = "Test" };
        List<Division> game = Game();
        DivisionChanges.AddUnits(mod, game, "Descriptor_Deck_Division_Armored_multi", ["Truck"]);

        DivisionChanges.RemoveUnit(mod, game, "Descriptor_Deck_Division_Armored_multi", "Truck");
        Assert.Empty(mod.Divisions);

        DivisionChanges.RemoveUnit(mod, game, "Descriptor_Deck_Division_Armored_multi", "Rifles");
        Assert.Equal(["Tank"], DivisionChanges.Apply(game, mod)[0].Units.Select(unit => unit.Unit));
    }

    [Fact]
    public void A_new_division_has_its_starting_divisions_nation_but_only_its_own_units()
    {
        var mod = new Mod { Name = "Test" };
        List<Division> game = Game();

        string descriptor = DivisionChanges.AddNew(mod, game, "  1st Test Brigade ", game[0], ["Truck"], GameTitle.Warno);

        Assert.Equal("Descriptor_Deck_Division_1st_Test_Brigade_multi", descriptor);
        Division created = DivisionChanges.Apply(game, mod)[^1];
        Assert.Equal(("1st Test Brigade", "US", "1st_Test_Brigade_multi"), (created.Name, created.Country, created.CfgName));
        Assert.Equal(["Truck"], created.Units.Select(unit => unit.Unit));
        // a second of the same name gets a descriptor of its own
        Assert.Equal("Descriptor_Deck_Division_1st_Test_Brigade_2_multi", DivisionChanges.AddNew(mod, game, "1st Test Brigade", game[0], [], GameTitle.Warno));
    }

    [Fact]
    public void A_new_division_starts_with_the_games_usual_details_not_those_of_a_modded_division()
    {
        var mod = new Mod { Name = "Test" };
        // a division as a mod that raises everything has it
        Division modded = Game()[0] with
        {
            ActivationPoints = 9999, ListOrder = 3, TypeToken = "ARMORED", EmblemTexture = "Texture_Big", EmblemSource = "big.png",
            StandoutUnits = ["Tank"], Costs = new Dictionary<string, IReadOnlyList<int>> { ["Logistic"] = [.. Enumerable.Repeat(0, 40)] },
        };
        List<Division> game = [modded, Game()[1]];

        DivisionChanges.AddNew(mod, game, "New", modded, [], GameTitle.Warno);

        Division created = DivisionChanges.Apply(game, mod)[^1];
        Assert.Equal(NewDivisionStart.WarnoActivationPoints, created.ActivationPoints);
        Assert.Equal(NewDivisionStart.WarnoCosts()["Logistic"], created.Costs["Logistic"]);
        Assert.Equal(4, created.ListOrder);
        // a type the game can show, as it shuts down on a division with none
        Assert.Equal((NewDivisionStart.WarnoPlaceholderType, "", ""), (created.TypeToken, created.EmblemTexture, created.EmblemSource));
        Assert.Empty(created.StandoutUnits);
    }

    [Fact]
    public void A_game_division_renamed_or_deleted_is_kept_as_a_change_and_comes_back_as_the_game_has_it()
    {
        var mod = new Mod { Name = "Test" };
        List<Division> game = Game();

        DivisionChanges.Rename(mod, game, "Descriptor_Deck_Division_Armored_multi", "Iron Fist");
        DivisionChanges.Delete(mod, "Descriptor_Deck_Division_Armored_multi");
        Assert.Equal("Iron Fist", DivisionChanges.Apply(game, mod)[0].Name);
        Assert.True(DivisionChanges.IsRemoved(mod, "Descriptor_Deck_Division_Armored_multi"));

        DivisionChanges.Restore(mod, "Descriptor_Deck_Division_Armored_multi");
        DivisionChanges.Rename(mod, game, "Descriptor_Deck_Division_Armored_multi", "Armored");
        Assert.Empty(mod.Divisions);
    }

    [Fact]
    public void A_duplicate_is_a_new_division_with_the_same_units_and_changes()
    {
        var mod = new Mod { Name = "Test" };
        List<Division> game = Game();
        DivisionChanges.AddUnits(mod, game, "Descriptor_Deck_Division_Armored_multi", ["Truck"]);

        string copy = DivisionChanges.Duplicate(mod, game, "Descriptor_Deck_Division_Armored_multi", "Armored (2)");
        string copyOfCopy = DivisionChanges.Duplicate(mod, game, copy, "Armored (3)");
        DivisionChanges.Delete(mod, copy);

        List<Division> shown = DivisionChanges.Apply(game, mod);
        Division last = shown[^1];
        Assert.Equal((copyOfCopy, "Armored (3)"), (last.Descriptor, last.Name));
        Assert.Equal(["Tank", "Rifles", "Truck"], last.Units.Select(unit => unit.Unit));
        Assert.DoesNotContain(shown, division => division.Descriptor == copy);
    }

    [Fact]
    public void A_units_cards_and_veterancy_changed_in_a_game_division_stay_in_its_place_and_go_when_put_back()
    {
        var mod = new Mod { Name = "Test" };
        List<Division> game = Game();
        DivisionUnit tank = game[0].Units[0];

        DivisionChanges.SetRule(mod, game, "Descriptor_Deck_Division_Armored_multi", tank with { Cards = 9, VeterancyMultipliers = [0, 1.0, 0.5, 0] });
        Division armored = DivisionChanges.Apply(game, mod)[0];
        Assert.Equal(["Tank", "Rifles"], armored.Units.Select(unit => unit.Unit));
        Assert.Equal(9, armored.Units[0].Cards);
        Assert.Equal([0, 1.0, 0.5, 0], armored.Units[0].VeterancyMultipliers);

        // taken out, a changed unit goes from the division, not back to the game's rule
        DivisionChanges.RemoveUnit(mod, game, "Descriptor_Deck_Division_Armored_multi", "Tank");
        Assert.Equal(["Rifles"], DivisionChanges.Apply(game, mod)[0].Units.Select(unit => unit.Unit));
        DivisionChanges.AddUnits(mod, game, "Descriptor_Deck_Division_Armored_multi", ["Tank"]);

        // set back as the game has it, nothing is left to change
        DivisionChanges.SetRule(mod, game, "Descriptor_Deck_Division_Armored_multi", tank with { Cards = 9 });
        DivisionChanges.SetRule(mod, game, "Descriptor_Deck_Division_Armored_multi", tank with { VeterancyMultipliers = [1.0, 0.6, 0, 0] });
        Assert.Empty(mod.Divisions);
    }

    [Fact]
    public void A_divisions_details_are_kept_in_the_mod_file_and_go_when_set_back()
    {
        var mod = new Mod { Name = "Test" };
        List<Division> game = Game();
        game[0] = game[0] with { ActivationPoints = 50, Costs = new Dictionary<string, IReadOnlyList<int>> { ["Tanks"] = [1, 2] } };
        const string armored = "Descriptor_Deck_Division_Armored_multi";

        DivisionChanges.ChangeDetails(mod, game, armored, change =>
        {
            change.ActivationPoints = 60;
            change.Costs = new() { ["Tanks"] = [1, 2, 3] };
            change.EmblemFile = @"C:\Emblems\Mine.png";
        });
        Mod read = ModFiles.Read(ModFiles.StoredText(mod), "Test.mod.json")[0];
        Division changed = DivisionChanges.Apply(game, read)[0];
        Assert.Equal((60, @"C:\Emblems\Mine.png"), (changed.ActivationPoints, changed.EmblemFile));
        Assert.Equal([1, 2, 3], changed.Costs["Tanks"]);

        DivisionChanges.ChangeDetails(mod, game, armored, change =>
        {
            change.ActivationPoints = 50;
            change.Costs = new() { ["Tanks"] = [1, 2] };
            change.EmblemFile = null;
        });
        Assert.Empty(mod.Divisions);
    }

    [Fact]
    public void A_mods_divisions_are_kept_in_its_file()
    {
        var mod = new Mod { Name = "Test" };
        List<Division> game = Game();
        DivisionChanges.AddUnits(mod, game, "Descriptor_Deck_Division_Armored_multi", ["Truck"]);
        DivisionChanges.RemoveUnit(mod, game, "Descriptor_Deck_Division_Armored_multi", "Rifles");
        DivisionChanges.AddNew(mod, game, "Test Brigade", game[1], ["Tank"], GameTitle.Warno);

        Mod read = ModFiles.Read(ModFiles.StoredText(mod), "Test.mod.json")[0];
        Mod fromBackup = ModFiles.Read(ModFiles.BackupText([new Mod { Name = "Test", Presets = [new ModPreset { Name = "P" }], Divisions = mod.Divisions }]), "backup.json")[0];

        foreach (Mod each in new[] { read, fromBackup, mod.DeepCopy() })
            Assert.Equal(string.Join("\n", DivisionChanges.Apply(game, mod).Select(Describe)), string.Join("\n", DivisionChanges.Apply(game, each).Select(Describe)));
    }

    [Fact]
    public void A_new_WARNO_division_saved_without_a_type_is_read_with_the_placeholder_in_its_details_and_its_rule()
    {
        var mod = new Mod { Name = "Test" };
        List<Division> game = Game();
        string warno = DivisionChanges.AddNew(mod, game, "Test Brigade", game[1], ["Tank"], GameTitle.Warno);
        string steel = DivisionChanges.AddNew(mod, game, "Steel Brigade", game[1], ["Tank"], GameTitle.SteelDivision2);
        // as the app saved new WARNO divisions until October 2026: no type, in the division and in its rule
        mod.Divisions.First(division => division.Descriptor == warno).TypeToken = "";
        ArmoryPresets.Update(mod, game, new TextCatalog(), unit => unit, part => part.ToString());
        Assert.Equal(["\"\""], TypeValues(mod, warno));

        Mod read = ModFiles.Read(ModFiles.StoredText(mod), "Test.mod.json")[0];

        Assert.Equal(NewDivisionStart.WarnoPlaceholderType, read.Divisions.First(division => division.Descriptor == warno).TypeToken);
        Assert.Equal(["\"" + NewDivisionStart.WarnoPlaceholderType + "\""], TypeValues(read, warno));
        // a Steel Division 2 division is left as it is: nothing says the game needs one there
        Assert.Null(read.Divisions.First(division => division.Descriptor == steel).TypeToken);
    }

    /// <summary>The values the Armory's rules for a division set its type (TypeToken) to.</summary>
    private static List<string> TypeValues(Mod mod, string descriptor) =>
        [.. ArmoryPresets.RulesOf(mod).OfType<JsonObject>()
            .Where(rule => JsonValues.AsText(rule["Armory"]?["Division"]) == descriptor)
            .SelectMany(rule => (rule["Actions"] as JsonArray ?? []).OfType<JsonObject>())
            .Where(action => JsonValues.AsText(action["Target"]) == "TypeToken")
            .Select(action => JsonValues.AsText(action["Value"]))];

    private static string Describe(Division division) =>
        division.Descriptor + ": " + division.Name + ": " + string.Join(", ", division.Units.Select(unit => unit.Unit + " " + unit.Cards + "x" + unit.UnitsPerCard + " [" + string.Join(" ", unit.VeterancyMultipliers) + "]"));
}
