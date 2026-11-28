using EasyNdf.Data.Divisions;
using Xunit;

namespace EasyNdf.Data.Tests;

/// <summary>A unit moved to another Armory tab needs a row for that tab in each division that offers it, with a slot for each pack of the tab in the ready-made deck.</summary>
public class TabRowsTests
{
    private static string Units(string tabOfA) =>
        "export Descriptor_Unit_A is TEntityDescriptor\n(\n    ModulesDescriptors = [\n        TProductionModuleDescriptor\n        (\n            FactoryType        = " + tabOfA + "\n        ),\n    ]\n)\n"
        + "export Descriptor_Unit_B is TEntityDescriptor\n(\n    ModulesDescriptors = [\n        TProductionModuleDescriptor\n        (\n            FactoryType        = Factory/Tanks\n        ),\n    ]\n)\n";

    private static string Matrix(string extraRow = "") =>
        "MatrixCostName_One is MAP\n[\n    (Factory/Tanks, [1, 1, 2]),\n" + extraRow + "] /*Inserted (Factory/Support, [1]) at LastIndex Original Value: MAP[(Factory/Tanks,[1,1,2]),]*/\n";

    private const string Divisions = "export Descriptor_Deck_Division_One_multi is TDeckDivisionDescriptor\n(\n    CostMatrix = MatrixCostName_One\n)\n";

    private const string Rules = "Descriptor_Deck_Division_One_multi_Rule is TDeckDivisionRule\n(\n    UnitRuleList =\n    [\n        TDeckUniteRule\n        (\n            UnitDescriptor = $/GFX/Unit/Descriptor_Unit_A\n        ),\n        TDeckUniteRule\n        (\n            UnitDescriptor = $/GFX/Unit/Descriptor_Unit_B\n        ),\n    ]\n)\n";

    private const string Packs = "Descriptor_Deck_Pack_A_1_1 is DeckPackDescriptor\n(\n    Xp = 1\n    Unit = $/GFX/Unit/Descriptor_Unit_A\n    TransportUnit = $/GFX/Unit/Descriptor_Unit_B\n)\n\nDescriptor_Deck_Pack_B_1_1 is DeckPackDescriptor\n(\n    Unit = $/GFX/Unit/Descriptor_Unit_B\n)\n";

    private const string Decks = "export Descriptor_Deck_One_multi is TDeckDescriptor\n(\n    DeckDivision = $/GFX/Division/Descriptor_Deck_Division_One_multi\n    DeckPackList = \n    [\n        ~/Descriptor_Deck_Pack_A_1_1,\n        ~/Descriptor_Deck_Pack_A_1_1,\n        ~/Descriptor_Deck_Pack_B_1_1,\n    ]\n)\n";

    [Fact]
    public void TheGamesOwnShapeHasNoFault() =>
        Assert.Empty(TabRows.Find(Units("Factory/Tanks"), Matrix(), Divisions, Rules, Packs, Decks));

    [Fact]
    public void AUnitMovedToATabTheDivisionHasNoRowForIsFound()
    {
        TabRowProblem problem = Assert.Single(TabRows.Find(Units("Factory/Support"), Matrix(), Divisions, Rules, Packs, Decks));
        Assert.Equal(new TabRowProblem(TabRowFault.NoRow, "Descriptor_Deck_Division_One_multi", "Factory/Support", 1, 0), problem);
    }

    [Fact]
    public void ARowWithFewerSlotsThanTheReadyMadeDeckHasPacksIsFound()
    {
        // the deck holds two packs of unit A; the row a rule inserted has one slot
        TabRowProblem problem = Assert.Single(TabRows.Find(Units("Factory/Support"), Matrix("    (Factory/Support, [1]),\n"), Divisions, Rules, Packs, Decks));
        Assert.Equal(new TabRowProblem(TabRowFault.TooFewSlots, "Descriptor_Deck_Division_One_multi", "Factory/Support", 2, 1), problem);
    }

    [Fact]
    public void ARowWithASlotForEachPackIsFine() =>
        Assert.Empty(TabRows.Find(Units("Factory/Support"), Matrix("    (Factory/Support, [1, 2]),\n"), Divisions, Rules, Packs, Decks));

    [Fact]
    public void ARowInACommentARuleLeftDoesNotCount()
    {
        // the matrix's closing line carries "/*Inserted (Factory/Support, [1]) ...*/": that is no row
        Assert.Single(TabRows.Find(Units("Factory/Support"), Matrix(), Divisions, Rules, Packs, Decks));
    }
}
