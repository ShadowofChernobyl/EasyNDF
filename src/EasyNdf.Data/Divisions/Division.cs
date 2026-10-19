namespace EasyNdf.Data.Divisions;

/// <summary>
/// A division as the game defines it: its entry in Divisions.ndf, the units its rule in DivisionRules.ndf lets a deck
/// take, and its activation-point costs from DivisionCostMatrix.ndf. Both games are read into the same shape; a field
/// one game does not have is empty or null.
/// </summary>
public sealed record Division
{
    /// <summary>Its descriptor's name: "Descriptor_Deck_Division_US_3rd_Arm_multi".</summary>
    public required string Descriptor { get; init; }

    /// <summary>Its internal name (CfgName): "US_3rd_Arm_multi". Empty when the game gives none (Steel Division 2).</summary>
    public string CfgName { get; init; } = "";

    /// <summary>The text key of its name (DivisionName): "RSEACNWCQI".</summary>
    public string NameToken { get; init; } = "";

    /// <summary>Its name as the game shows it, or one made from its descriptor when the game's texts have none.</summary>
    public required string Name { get; init; }

    /// <summary>Its side: "NATO" or "PACT" (WARNO), "Allied" or "Axis" (Steel Division 2).</summary>
    public string Side { get; init; } = "";

    /// <summary>Its nation's code (CountryId): "US". Empty when the game gives none (Steel Division 2).</summary>
    public string Country { get; init; } = "";

    /// <summary>Its type's key (TypeToken): "ARMORED", one of WARNO's eight. Empty in Steel Division 2.</summary>
    public string TypeToken { get; init; } = "";

    /// <summary>The activation points a deck of it has (MaxActivationPoints).</summary>
    public int? ActivationPoints { get; init; }

    /// <summary>Its place in the game's list of divisions (InterfaceOrder).</summary>
    public double? ListOrder { get; init; }

    /// <summary>The name of its emblem's texture (EmblemTexture): "Texture_Division_Emblem_3rd_Armored_Division".</summary>
    public string EmblemTexture { get; init; } = "";

    /// <summary>Its emblem's picture, as the game names it ("GameData:/Assets/2D/.../X.png"); null when it is not found.</summary>
    public string? EmblemSource { get; init; }

    /// <summary>
    /// A picture of the user's own for its emblem, on this computer, which a mod's export copies into the mod; null for
    /// the game's emblem (<see cref="EmblemTexture"/>).
    /// </summary>
    public string? EmblemFile { get; init; }

    /// <summary>Its tags (DivisionTags): "DEFAULT", "US", "NATO", "ARMORED"...</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Whether it is offered in skirmish and multiplayer: in WARNO, the divisions tagged DEFAULT; in Steel Division 2,
    /// those in its DivisionList.ndf, as many divisions there are tagged DEFAULT but cannot be played.
    /// </summary>
    public bool IsForMultiplayer { get; init; }

    /// <summary>The units shown on its card in the game (StandoutUnits), by descriptor.</summary>
    public IReadOnlyList<string> StandoutUnits { get; init; } = [];

    /// <summary>The name of its rule in DivisionRules.ndf (DivisionRule), in WARNO; empty in Steel Division 2.</summary>
    public string RuleName { get; init; } = "";

    /// <summary>The name of its costs in DivisionCostMatrix.ndf (CostMatrix).</summary>
    public string CostMatrixName { get; init; } = "";

    /// <summary>The units a deck of it may take, in the order of its rule.</summary>
    public IReadOnlyList<DivisionUnit> Units { get; init; } = [];

    /// <summary>
    /// Its activation-point costs, by category, named as units name their factory ("Tanks", "Infantry"): the cost of
    /// each card of that category in turn, so the list's length is how many cards the category allows.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> Costs { get; init; } = new Dictionary<string, IReadOnlyList<int>>();
}

/// <summary>A unit a division lets a deck take, and how (one TDeckUniteRule).</summary>
public sealed record DivisionUnit
{
    /// <summary>The unit's descriptor: "Descriptor_Unit_M1A1_Abrams_US".</summary>
    public required string Unit { get; init; }

    /// <summary>How many cards of it a deck may take (MaxPackNumber).</summary>
    public int Cards { get; init; }

    /// <summary>How many units come in one card (NumberOfUnitInPack), in WARNO; null in Steel Division 2.</summary>
    public int? UnitsPerCard { get; init; }

    /// <summary>
    /// How many units come in one card in each phase (NumberOfUnitInPackByPhase), in Steel Division 2; none in WARNO.
    /// </summary>
    public IReadOnlyList<int> UnitsPerCardByPhase { get; init; } = [];

    /// <summary>
    /// The share of units per card at each veterancy level (NumberOfUnitInPackXPMultiplier), from the lowest: 0 where
    /// that level is not offered.
    /// </summary>
    public IReadOnlyList<double> VeterancyMultipliers { get; init; } = [];

    /// <summary>Whether it may be taken without a transport (AvailableWithoutTransport).</summary>
    public bool AvailableWithoutTransport { get; init; }

    /// <summary>The transports it may ride in (AvailableTransportList), by descriptor.</summary>
    public IReadOnlyList<string> Transports { get; init; } = [];
}
