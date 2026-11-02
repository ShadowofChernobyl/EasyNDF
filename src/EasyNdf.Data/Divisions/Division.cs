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

    /// <summary>The token of its summary in the game's dictionaries ("SUMBEL16PD"); "" for none.</summary>
    public string SummaryToken { get; init; } = "";

    /// <summary>Its summary, a few lines the game's armory shows; "" when it has none, or the text is not found.</summary>
    public string Summary { get; init; } = "";

    /// <summary>The token of its history in the game's dictionaries ("HISBEL16PD"); "" for none.</summary>
    public string HistoryToken { get; init; } = "";

    /// <summary>Its history, the longer text the game's armory shows; "" when it has none, or the text is not found.</summary>
    public string History { get; init; } = "";

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

    /// <summary>
    /// How many other divisions of Divisions.ndf name the same costs (<see cref="CostMatrixName"/>): a change to them
    /// is a change to theirs too. 0 for costs of its own, or written in place.
    /// </summary>
    public int CostsSharedWith { get; init; }

    /// <summary>
    /// Each cost category's key as the costs' file has it, written as a rule's target names it, by the category's
    /// name in <see cref="Costs"/>: Steel Division 2's "Tanks" is "EDefaultFactories / tank".
    /// </summary>
    public IReadOnlyDictionary<string, string> CostKeys { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The textures of its waving flag, in order (FlagAnimationTextures), in Steel Division 2: "Texture_Division_Flag_US_0"
    /// and on, those of its nation (<see cref="Country"/>). None in WARNO.
    /// </summary>
    public IReadOnlyList<string> FlagTextures { get; init; } = [];

    /// <summary>
    /// Its phases, in order (PhaseList), in Steel Division 2: when each starts, its income and its bonus. None in
    /// WARNO, which has no phases.
    /// </summary>
    public IReadOnlyList<DivisionPhase> Phases { get; init; } = [];

    /// <summary>
    /// Its income choices in Steel Division 2 (DivisionEconomyList): the name of one of DivisionEconomy.ndf's lists,
    /// such as "DivisionEconomy_German", each choice an income for each phase. "" in WARNO.
    /// </summary>
    public string Economy { get; init; } = "";

    /// <summary>
    /// Steel Division 2's caps on its transports (its rule's TransportRuleList: how many of each transport a deck may
    /// take), by the transport's descriptor; none in WARNO, which has no such list.
    /// </summary>
    public IReadOnlyDictionary<string, int> TransportCaps { get; init; } = new Dictionary<string, int>();

    /// <summary>Its number in deck codes (DeckSerializer.ndf's DivisionIds); null when it has none.</summary>
    public int? DeckNumber { get; init; }

    /// <summary>The units a deck of it may take, in the order of its rule.</summary>
    public IReadOnlyList<DivisionUnit> Units { get; init; } = [];

    /// <summary>
    /// Its activation-point costs, by category, named as units name their factory ("Tanks", "Infantry"): the cost of
    /// each card of that category in turn, so the list's length is how many cards the category allows.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> Costs { get; init; } = new Dictionary<string, IReadOnlyList<int>>();

    /// <summary>
    /// Its Official Battlegroup, the one deck of Decks.ndf that names it; null when it has none (a division no player
    /// can pick, or one of Steel Division 2's, whose decks are not read).
    /// </summary>
    public Battlegroup? Battlegroup { get; init; }
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

/// <summary>One of a Steel Division 2 division's phases (a TPhaseDescriptor of its PhaseList).</summary>
/// <param name="Start">When it starts, in seconds from the start of the battle (PhaseStartTimeInSeconds).</param>
/// <param name="Income">The points it gives (PhaseIncome).</param>
/// <param name="Bonus">The points given once when it starts (PhaseBonus).</param>
public sealed record DivisionPhase(int Start, int Income, int Bonus);
