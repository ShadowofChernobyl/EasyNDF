using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

/// <summary>A division's units in the order the game shows them, checked against the game's own screens (US 3rd Armored).</summary>
public class UnitGameOrderTests
{
    private static Unit Made(string descriptor, string name, double cost, string? upgradesFrom = null) =>
        new() { Descriptor = descriptor, Name = name, Cost = cost, UpgradesFrom = upgradesFrom };

    private static List<string> Sorted(IReadOnlyList<Unit> division, IEnumerable<Unit>? others = null)
    {
        var all = division.Concat(others ?? []).ToDictionary(unit => unit.Descriptor);
        return [.. UnitGameOrder.Sort(division, unit => unit, all).Select(unit => unit.Name)];
    }

    [Fact]
    public void The_3rd_Armoreds_tanks_come_in_the_games_order()
    {
        List<Unit> tanks =
        [
            Made("M1A1HA_CMD", "M1A1(HA) ABRAMS CP", 350), Made("M1A1HA", "M1A1(HA) ABRAMS", 310, "M1A1HA_CMD"),
            Made("M1A1_CMD", "M1A1 ABRAMS CP", 315), Made("M1A1", "M1A1 ABRAMS", 275, "M1A1_CMD"),
            Made("M728", "M728 CEV", 85, "M728_NG"), Made("M901A1", "M901A3 ITV", 75), Made("M901", "M901 ITV", 60, "M901_NG"),
        ];
        Assert.Equal(["M901 ITV", "M901A3 ITV", "M728 CEV", "M1A1 ABRAMS CP", "M1A1 ABRAMS", "M1A1(HA) ABRAMS CP", "M1A1(HA) ABRAMS"], Sorted(tanks));
    }

    [Fact]
    public void The_3rd_Armoreds_infantry_come_in_the_games_order()
    {
        List<Unit> infantry =
        [
            Made("ITOW", "I-TOW", 80, "RCL_NG"), Made("TOW2", "TOW-2", 95, "RCL_NG"), Made("Aero", "AIR CAV. TROOPERS", 55, "Cavalry"),
            Made("Eng_CMD", "ENGINEERS LDR.", 70), Made("Eng_Dragon", "ENGINEERS (DRAGON)", 50, "Eng_CMD"),
            Made("Eng_Flash", "ENGINEERS (FLASH)", 35, "Eng_CMD"), Made("Eng", "ENGINEERS", 50, "Eng_CMD"),
            Made("M2HB", "M2HB 12,7MM", 30, "M60_NG"), Made("M60", "M60 7,62MM", 15, "M60_NG"),
            Made("MP_RCL", "MP PATROL (M67)", 45, "MP_CMD"), Made("MP", "MP PATROL", 30, "MP_CMD"),
            Made("AT4", "FIRE TEAM (AT-4)", 35, "Rifles_CMD"), Made("Rifles_CMD", "FIRE TEAM LDR.", 35),
            Made("Dragon", "FIRE TEAM (DRAGON)", 40, "Rifles_CMD"), Made("LAW", "FIRE TEAM (LAW)", 30, "Rifles_CMD"),
        ];
        // the chains' first units the division does not have: the National Guard's M60, the MPs' leader, the cavalry, the M40 RCL
        List<Unit> others = [Made("M60_NG", "M60 NG", 10), Made("MP_CMD", "MP LDR.", 40), Made("Cavalry", "CAVALRY", 45), Made("RCL_NG", "M40 RCL", 30)];
        Assert.Equal(
        [
            "M60 7,62MM", "M2HB 12,7MM",
            "FIRE TEAM LDR.", "FIRE TEAM (LAW)", "FIRE TEAM (AT-4)", "FIRE TEAM (DRAGON)",
            "MP PATROL", "MP PATROL (M67)",
            "ENGINEERS LDR.", "ENGINEERS (FLASH)", "ENGINEERS", "ENGINEERS (DRAGON)",
            "AIR CAV. TROOPERS",
            "I-TOW", "TOW-2",
        ], Sorted(infantry, others));
    }

    [Fact]
    public void A_chain_that_leads_back_to_itself_still_ends()
    {
        List<Unit> looped = [Made("A", "A", 10, "B"), Made("B", "B", 20, "A")];
        Assert.Equal(2, Sorted(looped).Count);
    }
}
