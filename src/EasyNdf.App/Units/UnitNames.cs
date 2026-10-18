using System.Text.RegularExpressions;
using EasyNdf.App.Text;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// The names the Units tab shows for the games' codes: nations ("US" is United States), sides, factories, WARNO's
/// roles, the weapon groups and the compared stats. Each code's text name is written out here in full, so the app's
/// text check finds it. A code not listed here (a mod's own nation, say) is shown as it is, made readable.
/// </summary>
public static partial class UnitNames
{
    private static readonly Dictionary<string, string> Nations = new(StringComparer.Ordinal)
    {
        // WARNO
        ["US"] = "Units.Nation.US", ["UK"] = "Units.Nation.UK", ["FR"] = "Units.Nation.FR", ["RFA"] = "Units.Nation.RFA",
        ["BEL"] = "Units.Nation.BEL", ["NL"] = "Units.Nation.NL", ["DK"] = "Units.Nation.DK", ["CAN"] = "Units.Nation.CAN",
        ["ESP"] = "Units.Nation.ESP", ["LUX"] = "Units.Nation.LUX", ["SOV"] = "Units.Nation.SOV", ["DDR"] = "Units.Nation.DDR",
        ["POL"] = "Units.Nation.POL", ["TCH"] = "Units.Nation.TCH",
        // Steel Division 2 (those not above)
        ["CZ"] = "Units.Nation.CZ", ["SA"] = "Units.Nation.SA", ["NZ"] = "Units.Nation.NZ", ["YUG"] = "Units.Nation.YUG",
        ["GRE"] = "Units.Nation.GRE", ["BRZ"] = "Units.Nation.BRZ", ["EST"] = "Units.Nation.EST", ["BUL"] = "Units.Nation.BUL",
        ["ITA"] = "Units.Nation.ITA", ["ROU"] = "Units.Nation.ROU", ["GER"] = "Units.Nation.GER", ["HON"] = "Units.Nation.HON",
        ["FIN"] = "Units.Nation.FIN", ["RSI"] = "Units.Nation.RSI", ["CRO"] = "Units.Nation.CRO", ["LIT"] = "Units.Nation.LIT",
    };

    private static readonly Dictionary<string, string> Sides = new(StringComparer.Ordinal)
    {
        ["NATO"] = "Units.Side.NATO", ["PACT"] = "Units.Side.PACT", ["Allied"] = "Units.Side.Allied", ["Axis"] = "Units.Side.Axis",
    };

    /// <summary>Each factory's name, and its short name for the tabs above the list.</summary>
    private static readonly Dictionary<string, (string Name, string Short)> Factories = new(StringComparer.Ordinal)
    {
        ["Logistic"] = ("Units.Factory.Logistic", "Units.Factory.Logistic.Short"),
        ["Infantry"] = ("Units.Factory.Infantry", "Units.Factory.Infantry.Short"),
        ["Art"] = ("Units.Factory.Art", "Units.Factory.Art.Short"),
        ["Tanks"] = ("Units.Factory.Tanks", "Units.Factory.Tanks.Short"),
        ["Recons"] = ("Units.Factory.Recons", "Units.Factory.Recons.Short"),
        ["DCA"] = ("Units.Factory.DCA", "Units.Factory.DCA.Short"),
        ["Helis"] = ("Units.Factory.Helis", "Units.Factory.Helis.Short"),
        ["Planes"] = ("Units.Factory.Planes", "Units.Factory.Planes.Short"),
        ["Support"] = ("Units.Factory.Support", "Units.Factory.Support.Short"),
        ["AT"] = ("Units.Factory.AT", "Units.Factory.AT.Short"),
        ["Defense"] = ("Units.Factory.Defense", "Units.Factory.Defense.Short"),
    };

    /// <summary>The factory tabs of each game, in the order the game's armory shows them.</summary>
    public static IReadOnlyList<string> FactoriesOf(GameTitle game) => game == GameTitle.SteelDivision2
        ? ["Infantry", "Recons", "Tanks", "Support", "AT", "DCA", "Art", "Planes", "Defense"]
        : ["Logistic", "Infantry", "Art", "Tanks", "Recons", "DCA", "Helis", "Planes"];

    /// <summary>The sides of each game, the first one drawn in the first side colour.</summary>
    public static IReadOnlyList<string> SidesOf(GameTitle game) => game == GameTitle.SteelDivision2 ? ["Allied", "Axis"] : ["NATO", "PACT"];

    /// <summary>WARNO's roles, which are codes; Steel Division 2's are the game's own text keys (<see cref="Unit.RoleName"/>).</summary>
    private static readonly Dictionary<string, string> Roles = new(StringComparer.Ordinal)
    {
        ["armor"] = "Units.Role.Armor", ["ifv"] = "Units.Role.Ifv", ["infantry"] = "Units.Role.Infantry",
        ["engineer"] = "Units.Role.Engineer", ["AT"] = "Units.Role.AntiTank", ["AA"] = "Units.Role.AntiAir",
        ["reco"] = "Units.Role.Recon", ["appui"] = "Units.Role.Support", ["howitzer"] = "Units.Role.Howitzer",
        ["mortar"] = "Units.Role.Mortar", ["mlrs"] = "Units.Role.RocketArtillery", ["sead"] = "Units.Role.Sead",
        ["transport"] = "Units.Role.Transport", ["supply"] = "Units.Role.Supply", ["uav"] = "Units.Role.Drone",
        ["hq_inf"] = "Units.Role.CommandInfantry", ["hq_veh"] = "Units.Role.CommandVehicle",
        ["hq_tank"] = "Units.Role.CommandTank", ["hq_helo"] = "Units.Role.CommandHelicopter",
    };

    private static readonly Dictionary<WeaponKind, string> WeaponKindNames = new()
    {
        [WeaponKind.AntiArmor] = "Units.Weapons.AntiArmor", [WeaponKind.Missile] = "Units.Weapons.Missile",
        [WeaponKind.Rocket] = "Units.Weapons.Rocket", [WeaponKind.HighExplosive] = "Units.Weapons.HighExplosive",
        [WeaponKind.SmallArms] = "Units.Weapons.SmallArms", [WeaponKind.Smoke] = "Units.Weapons.Smoke",
    };

    private static readonly Dictionary<string, string> StatNames = new(StringComparer.Ordinal)
    {
        ["Cost"] = "Units.Stat.Cost", ["Strength"] = "Units.Stat.Strength", ["FrontArmor"] = "Units.Stat.FrontArmor",
        ["SideArmor"] = "Units.Stat.SideArmor", ["RearArmor"] = "Units.Stat.RearArmor", ["TopArmor"] = "Units.Stat.TopArmor",
        ["Speed"] = "Units.Stat.Speed", ["RoadSpeed"] = "Units.Stat.RoadSpeed", ["Optics"] = "Units.Stat.Optics",
        ["Stealth"] = "Units.Stat.Stealth", ["LongestRange"] = "Units.Stat.LongestRange",
    };

    /// <summary>A nation's name: "US" is "United States"; a code not known is shown as it is.</summary>
    public static string Nation(string code) => Nations.TryGetValue(code, out string? name) ? Texts.Get(name) : code;

    /// <summary>A side's name: "Allied" is "Allies".</summary>
    public static string Side(string code) => Sides.TryGetValue(code, out string? name) ? Texts.Get(name) : code;

    /// <summary>A factory's name: "DCA" is "Anti-air", "Art" is "Artillery".</summary>
    public static string Factory(string code) => Factories.TryGetValue(code, out var names) ? Texts.Get(names.Name) : Readable(code);

    /// <summary>A factory's short name, for its tab: "DCA" is "AA", "Tanks" is "TNK".</summary>
    public static string FactoryShort(string code) => Factories.TryGetValue(code, out var names) ? Texts.Get(names.Short) : code.ToUpperInvariant();

    /// <summary>
    /// A role's name: the game's own name when it gives one, then the app's for WARNO's codes, else the code made
    /// readable ("TArmorReco" is "Armor Reco"). No role is "Other".
    /// </summary>
    public static string Role(string code, string? gameName = null)
    {
        if (code == "") return Texts.Get("Units.Role.Other");
        if (gameName is not null) return gameName;
        return Roles.TryGetValue(code, out string? name) ? Texts.Get(name) : Readable(code);
    }

    /// <summary>A unit's role's name.</summary>
    public static string RoleOf(Unit unit) => Role(unit.Role, unit.RoleName);

    /// <summary>A weapon group's name: "Anti-armor", "Missiles"...</summary>
    public static string WeaponGroup(WeaponKind kind) => Texts.Get(WeaponKindNames[kind]);

    /// <summary>A compared stat's name: "Cost", "Front armor"...</summary>
    public static string Stat(string key) => Texts.Get(StatNames[key]);

    /// <summary>
    /// A code made readable: Steel Division 2's leading "T" (for type) is dropped, underscores become spaces and words
    /// run together are split: "TSP_AA" is "SP AA", "TArmorReco" is "Armor Reco", "smoke_launcher" is "smoke launcher".
    /// </summary>
    public static string Readable(string code)
    {
        string text = TypePrefix().Replace(code, "");
        text = text.Replace('_', ' ');
        text = WordStart().Replace(text, " ");
        return MoreSpaces().Replace(text, " ").Trim();
    }

    [GeneratedRegex("\\AT(?=[A-Z])")]
    private static partial Regex TypePrefix();

    // a capital after a small letter starts a word: "ArmorReco" is "Armor Reco"
    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordStart();

    [GeneratedRegex(" {2,}")]
    private static partial Regex MoreSpaces();
}
