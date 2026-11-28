using EasyNdf.Data.Divisions;
using Xunit;

namespace EasyNdf.Data.Tests;

/// <summary>TabRows on WARNO's own files and on the versions of a real mod tested in the game (the files are on this machine only; skipped elsewhere).</summary>
public class TabRowsRealDataTests
{
    private const string Game = @"E:\work\hunt\gamebase\GameData\Generated\Gameplay\";
    private const string Mod = @"E:\work\hunt\sub\";

    private static List<TabRowProblem>? Find(string units, string matrix)
    {
        if (!File.Exists(units) || !File.Exists(matrix) || !File.Exists(Game + @"Decks\Decks.ndf")) return null;
        return TabRows.Find(File.ReadAllText(units), File.ReadAllText(matrix), File.ReadAllText(Game + @"Decks\Divisions.ndf"),
            File.ReadAllText(Game + @"Decks\DivisionRules.ndf"), File.ReadAllText(Game + @"Decks\DeckPacks.ndf"), File.ReadAllText(Game + @"Decks\Decks.ndf"));
    }

    [Fact]
    public void TheGamesOwnFilesHaveNoFault()
    {
        if (Find(Game + "TacticalUnits.ndf", Game + @"Decks\DivisionCostMatrix.ndf") is { } problems) Assert.Empty(problems);
    }

    [Fact]
    public void TheModThatCrashedTheGameHasDivisionsWithNoRow()
    {
        if (Find(Mod + @"support\Generated\Gameplay\TacticalUnits.ndf", Mod + @"support\Generated\Gameplay\Decks\DivisionCostMatrix.ndf") is not { } problems) return;
        Assert.Equal(76, problems.Count(problem => problem.Fault == TabRowFault.NoRow));
    }

    [Fact]
    public void TheVersionTheGameStoppedOnNamesThatDeck()
    {
        if (Find(Mod + @"support\Generated\Gameplay\TacticalUnits.ndf", Mod + @"support_allmatrix\Generated\Gameplay\Decks\DivisionCostMatrix.ndf") is not { } problems) return;
        Assert.Equal(new TabRowProblem(TabRowFault.TooFewSlots, "Descriptor_Deck_Division_BEL_16e_Mecanisee_multi", "Factory/Support", 6, 4), Assert.Single(problems));
    }

    [Fact]
    public void TheVersionThatGeneratedHasNoFault()
    {
        if (Find(Mod + @"support\Generated\Gameplay\TacticalUnits.ndf", Mod + @"support_wide\Generated\Gameplay\Decks\DivisionCostMatrix.ndf") is { } problems) Assert.Empty(problems);
    }
}
