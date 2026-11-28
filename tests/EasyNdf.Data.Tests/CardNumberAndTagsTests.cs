using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;

namespace EasyNdf.Data.Tests;

/// <summary>
/// A battlegroup card's Number (the game's cards have 1; YSM's Unlimited decks 999) and a division's tags set by hand:
/// kept in the mod file, and written as they are.
/// </summary>
public class CardNumberAndTagsTests
{
    [Fact]
    public void A_card_keeps_its_Number_and_names_its_pack_with_it()
    {
        var card = new BattlegroupCard("Descriptor_Unit_Rifles_US", 1, null, 999);

        Assert.Equal("Descriptor_Deck_Pack_Rifles_US_1_999", card.PackName);
        Assert.Equal("Descriptor_Deck_Pack_Rifles_US_1_1", (card with { Number = 1 }).PackName);
    }

    [Fact]
    public void Numbers_and_tags_are_kept_in_the_mod_file_and_a_Number_of_1_is_not_written()
    {
        var entry = new ModDivision
        {
            Descriptor = "Descriptor_Deck_Division_Test_multi",
            IsNew = true,
            Name = "TEST",
            Tags = ["DEFAULT", "YSM", "NATO", "BEL", "SANDBOX"],
            BattlegroupCards = [new BattlegroupCard("Descriptor_Unit_A", 1, null, 6), new BattlegroupCard("Descriptor_Unit_B", 0, null)],
        };

        JsonObject json = entry.ToJson();
        ModDivision read = ModDivision.FromJson(JsonNode.Parse(json.ToJsonString())!.AsObject());

        Assert.Equal(["DEFAULT", "YSM", "NATO", "BEL", "SANDBOX"], read.Tags);
        Assert.Equal([6, 1], read.BattlegroupCards!.Select(card => card.Number));
        Assert.Null(json["Battlegroup"]!["Cards"]![1]!["Number"]);
    }

    [Fact]
    public void Tags_set_by_hand_take_the_place_of_those_the_nation_gives()
    {
        var game = new Division { Descriptor = "Descriptor_Deck_Division_US_multi", Name = "US", Country = "US", Side = "NATO", Tags = ["DEFAULT", "US", "NATO"], IsForMultiplayer = true };
        var entry = new ModDivision { Descriptor = game.Descriptor, Tags = ["DEFAULT", "US", "NATO", "SANDBOX"] };

        Assert.Equal(["DEFAULT", "US", "NATO", "SANDBOX"], entry.Apply(game).Tags);
        entry.ForgetUnchangedDetails(game with { Tags = ["DEFAULT", "US", "NATO", "SANDBOX"] });
        Assert.Null(entry.Tags);
    }
}
