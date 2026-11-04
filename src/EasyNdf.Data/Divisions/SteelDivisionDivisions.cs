namespace EasyNdf.Data.Divisions;

/// <summary>
/// How Steel Division 2 keeps a division's details in its Divisions.ndf and DivisionCostMatrix.ndf, under its own names
/// and in its own forms (see the format notes, section 4), for the changes the Armory writes for it. Each property here
/// is found once in every one of the game's TDeckDivisionDescriptor, and each cost key once in every cost matrix, so a
/// rule's target needs no path beyond the matrix's name.
/// </summary>
/// <remarks>
/// What the Division pane changes and the game's divisions lack is not written (<see cref="Lacks"/>): a type key, a
/// place in the list, a history and standout units. A name and a description are tokens whose texts go into the mod's
/// MODDING.csv, of seven columns (DivisionName, DivisionDescription). An emblem picture of the user's own is a texture
/// of the Modded kind, its file under the mod's AssetsModding folder. A new division, generated ones among them, is
/// written by <see cref="DivisionChangeRules"/>'s Steel Division 2 new-division rules. The
/// division's units, spread over DivisionRules.ndf, Packs.ndf, DeckSerializer.ndf and the division's own PackList, are
/// written by <see cref="DivisionChangeRules"/>'s Steel Division 2 unit rules. Values only Steel Division 2 has have no place in the pane and are not written: its phases' income, start
/// times and bonuses (PhaseList), its income choices (DivisionEconomyList), its portrait, type picture and power class,
/// and its transports' caps (TransportRuleList).
/// </remarks>
public static class SteelDivisionDivisions
{
    /// <summary>The details of a game division the Armory writes for Steel Division 2.</summary>
    public static IReadOnlyList<string> Written { get; } =
        [
            DivisionDetail.Points, DivisionDetail.Nation, DivisionDetail.Tags, DivisionDetail.Emblem, DivisionDetail.EmblemPicture, DivisionDetail.Costs, DivisionDetail.Units, DivisionDetail.TakenOut,
            DivisionDetail.Name, DivisionDetail.Summary, DivisionDetail.New, DivisionDetail.Phases, DivisionDetail.Economy, DivisionDetail.Generated,
        ];

    /// <summary>
    /// The game's nine cost categories, named as its divisions' matrices are read (DivisionReader: "EDefaultFactories/tank"
    /// is "Tanks"), for the costs of a generated division.
    /// </summary>
    public static IReadOnlyList<string> CostCategories { get; } = ["Recons", "Infantry", "Tanks", "Support", "AT", "DCA", "Art", "Planes", "Defense"];

    /// <summary>
    /// The phases of an income choice (a DivisionEconomy list's name): those of the first of the game's multiplayer
    /// divisions (tagged DEFAULT) with that choice; null when none has it.
    /// </summary>
    public static IReadOnlyList<DivisionPhase>? PhasesOf(string economy, IEnumerable<Division> game) =>
        game.FirstOrDefault(division => division.Economy == economy && division.Phases.Count > 0 && division.Tags.Contains("DEFAULT"))?.Phases;

    /// <summary>
    /// The units only DLC divisions give (<see cref="IsInDlc"/>): those some of the game's divisions have, every one of
    /// them a DLC division. A generated division leaves them out until the game is known to take them in a division of
    /// the base game (Paul's choice, November 2026).
    /// </summary>
    public static HashSet<string> UnitsOnlyInDlc(IEnumerable<Division> game)
    {
        var inBase = new HashSet<string>(StringComparer.Ordinal);
        var inDlc = new HashSet<string>(StringComparer.Ordinal);
        foreach (Division division in game)
            foreach (DivisionUnit unit in division.Units)
                (IsInDlc(division.Descriptor) ? inDlc : inBase).Add(unit.Unit);
        inDlc.ExceptWith(inBase);
        return inDlc;
    }

    /// <summary>
    /// Whether a game division comes with one of Steel Division 2's DLCs. The game keeps each division's and pack's DLC
    /// outside the files a mod changes, and a pack a mod adds is the base game's: put in a DLC division, the game stops
    /// ("Le DLC_ID de la division ... n'est pas le même que DLC_ID de son pack ..."). So no unit is added to one; its own
    /// units can still be changed or taken out, and a new division (of the base game) can have any unit.
    /// </summary>
    public static bool IsInDlc(string descriptor) => InDlc.Contains(descriptor);

    /// <summary>The game's DLC divisions, by descriptor, from the DLCs' store pages.</summary>
    private static readonly HashSet<string> InDlc = new(StringComparer.Ordinal)
    {
        // Back to War
        "Descriptor_Deck_Division_US_3rd_Armored_Dv2", "Descriptor_Deck_Division_CAN_3CID_Dv2", "Descriptor_Deck_Division_UK_15Scot_multi", "Descriptor_Deck_Division_FR_2e_DB_multi", "Descriptor_Deck_Division_GR_21_Panzer_multi", "Descriptor_Deck_Division_GR_Panzer_Lehr_multi", "Descriptor_Deck_Division_GR_116_Panzer_multi", "Descriptor_Deck_Division_GR_352_Infanterie_multi",
        // Pre-order Pack
        "Descriptor_Deck_Division_GR_1_Skijager_multi", "Descriptor_Deck_Division_SOV_3CCharGd_multi",
        // Death on the Vistula
        "Descriptor_Deck_Division_GR_5SS_Panzer_multi", "Descriptor_Deck_Division_GR_Panzer_HG_multi", "Descriptor_Deck_Division_POL_ArmiaKrajowa_multi", "Descriptor_Deck_Division_POL_1DI_AWP_multi",
        // The Fate of Finland
        "Descriptor_Deck_Division_FIN_Panssaridivisioona_multi", "Descriptor_Deck_Division_FIN_Gr_Raappana_multi", "Descriptor_Deck_Division_GR_122_Infanterie_multi", "Descriptor_Deck_Division_SOV_126CFusMont_multi", "Descriptor_Deck_Division_SOV_VyborgReserve_multi", "Descriptor_Deck_Division_SOV_358DFusGd_multi",
        // Tribute to D-Day
        "Descriptor_Deck_Division_US_2nd_Infantry_multi", "Descriptor_Deck_Division_UK_6_Airborne_multi", "Descriptor_Deck_Division_POL_1_Pancera_multi", "Descriptor_Deck_Division_GR_17SS_Panzergrenadier_multi", "Descriptor_Deck_Division_GR_3_Fallschirmjager_multi", "Descriptor_Deck_Division_GR_12SS_Panzer_multi",
        // Black Sunday
        "Descriptor_Deck_Division_ROU_1_Blindata_multi", "Descriptor_Deck_Division_ROU_4_Munte_multi", "Descriptor_Deck_Division_ROU_5_CavMot_multi", "Descriptor_Deck_Division_SOV_7CMech_multi", "Descriptor_Deck_Division_SOV_NavalGroup_Bakthin", "Descriptor_Deck_Division_SOV_3VDV_multi",
        // Nemesis #2
        "Descriptor_Deck_Division_GR_20_PzGren_multi", "Descriptor_Deck_Division_SOV_10CCharGd_multi",
        // Burning Baltics
        "Descriptor_Deck_Division_GR_Grossdeutschland_multi", "Descriptor_Deck_Division_GR_11SS_Panzergrenadier_multi", "Descriptor_Deck_Division_GR_Pzverbande_Strachwitz_multi", "Descriptor_Deck_Division_GR_52_Sicherungs_multi", "Descriptor_Deck_Division_SOV_19CChar_multi", "Descriptor_Deck_Division_SOV_GM_Fedyunkin_multi", "Descriptor_Deck_Division_SOV_7DFus_Esto_multi", "Descriptor_Deck_Division_SOV_43A_Reserve_multi",
        // Nemesis #3
        "Descriptor_Deck_Division_GR_1_Fallschirmjager_multi", "Descriptor_Deck_Division_NZ_2nd_NZ_multi",
        // Nemesis #4
        "Descriptor_Deck_Division_FR_1e_DFL_multi", "Descriptor_Deck_Division_GR_Verteidigungsbereich_Toulon_multi",
        // Tribute to the Liberation of Italy
        "Descriptor_Deck_Division_GR_5_Gebirgs_multi", "Descriptor_Deck_Division_GR_4_Fallschirmjager_multi", "Descriptor_Deck_Division_GR_26_Panzer_multi", "Descriptor_Deck_Division_RSI_Monterosa_multi", "Descriptor_Deck_Division_SA_6SAAD_multi", "Descriptor_Deck_Division_ITA_CIL_multi", "Descriptor_Deck_Division_UK_8Indian_multi", "Descriptor_Deck_Division_US_TF_45_multi",
        // Nemesis #5
        "Descriptor_Deck_Division_GR_Rosselsprung_multi", "Descriptor_Deck_Division_YUG_NOV_multi",
        // Nemesis #6
        "Descriptor_Deck_Division_GR_Festung_Dunkirk_multi", "Descriptor_Deck_Division_CSSR_1_CIABG_multi",
        // Blood Feud in Transylvania
        "Descriptor_Deck_Division_HO_2_Panzer_multi", "Descriptor_Deck_Division_HO_25_Infanterie_multi", "Descriptor_Deck_Division_GR_Sperr_Kessel_multi", "Descriptor_Deck_Division_ROU2_9_Infanterie_multi", "Descriptor_Deck_Division_ROU2_8_CavMot_multi", "Descriptor_Deck_Division_ROU2_Corp6T_multi",
        // Men of Steel
        "Descriptor_Deck_Division_UK_1st_SSB_multi", "Descriptor_Deck_Division_BUL_Sofia_Brigade_multi", "Descriptor_Deck_Division_FR_Groupement_Dody_multi", "Descriptor_Deck_Division_US_1st_ATF_multi", "Descriptor_Deck_Division_GR_715_Infanteri_multi", "Descriptor_Deck_Division_GR_KG_Tettau_multi", "Descriptor_Deck_Division_FIN_17_Division_multi", "Descriptor_Deck_Division_HO_1_Panzer_multi",
        // Tribute to Normandy '44
        "Descriptor_Deck_Division_GR_2_Panzer_multi", "Descriptor_Deck_Division_GR_9_Panzer_multi", "Descriptor_Deck_Division_GR_1SS_Panzer_multi", "Descriptor_Deck_Division_GR_16_Luftwaffe_multi", "Descriptor_Deck_Division_GR_Festung_GrossParis_multi", "Descriptor_Deck_Division_GR_91_Luftlande_multi", "Descriptor_Deck_Division_GR_716_Infanterie_multi", "Descriptor_Deck_Division_US_1st_Infantry_multi", "Descriptor_Deck_Division_US_101st_Airborne_multi", "Descriptor_Deck_Division_US_4th_Armored_multi", "Descriptor_Deck_Division_UK_Guards_Armoured_multi", "Descriptor_Deck_Division_UK_7th_Armoured_multi", "Descriptor_Deck_Division_CAN_4th_Armoured_multi", "Descriptor_Deck_Division_FR_DBSAS_multi",
        // Nemesis #7
        "Descriptor_Deck_Division_UK_43Wessex_multi", "Descriptor_Deck_Division_GR_KG_Walther_multi",
        // Battle of Dukla Pass
        "Descriptor_Deck_Division_GR_208_Inf_multi", "Descriptor_Deck_Division_HO_13_Infanterie_multi", "Descriptor_Deck_Division_GR_18SS_Panzergrenadier_multi", "Descriptor_Deck_Division_CSSR_1CSAK_multi", "Descriptor_Deck_Division_SOV_242Gorno_multi", "Descriptor_Deck_Division_CSSR_SNP_multi",
        // Reinforcement Pack #1 (free)
        "Descriptor_Deck_Division_GR_25_PzGren_multi", "Descriptor_Deck_Division_SOV_84DFusGd_multi",
        // Nemesis #1 (free)
        "Descriptor_Deck_Division_GR_16_Panzer_multi", "Descriptor_Deck_Division_SOV_97DFusGd_multi",
        // Reinforcement Pack #13 (free)
        "Descriptor_Deck_Division_GR_Panzerdivision_Tatra_multi", "Descriptor_Deck_Division_US_TF_Butler_multi",
    };

    /// <summary>The details only Steel Division 2's divisions have: WARNO's have no phases or income choices.</summary>
    public static IReadOnlyList<string> Only { get; } = [DivisionDetail.Phases, DivisionDetail.Economy];

    /// <summary>The property of a division's description, a token whose text goes into MODDING.csv (WARNO's SummaryTextToken).</summary>
    public const string SummaryProperty = "DivisionDescription";

    /// <summary>
    /// Steel Division 2's list of the divisions it offers (DivisionList.ndf: DivisionList = [~/Descriptor_Deck_Division_X, ...]):
    /// a division taken out of it is no longer offered, as a WARNO division without its DEFAULT tag.
    /// </summary>
    public const string ListFile = "DivisionList.ndf", ListDescriptor = "DivisionList";

    /// <summary>The details the Division pane shows that Steel Division 2's divisions do not have at all.</summary>
    public static IReadOnlyList<string> Lacks { get; } =
        [DivisionDetail.Type, DivisionDetail.ListOrder, DivisionDetail.History, DivisionDetail.Standout];

    /// <summary>The tags of the fronts Steel Division 2 lists its divisions by: "East" and "West".</summary>
    public static IReadOnlyList<string> Fronts { get; } = ["East", "West"];

    /// <summary>
    /// Each nation whose game divisions are all on one front, with that front's tag ("SOV" is "East", "US" "West");
    /// nations with divisions on both, or none, are left out. Empty for WARNO, whose divisions have no front tags.
    /// </summary>
    public static Dictionary<string, string> FrontsOf(IEnumerable<Division> game)
    {
        var fronts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (IGrouping<string, Division> nation in game.Where(division => division.Country != "").GroupBy(division => division.Country))
        {
            List<string> onFronts = [.. nation.SelectMany(division => division.Tags).Where(tag => Fronts.Contains(tag)).Distinct()];
            if (onFronts.Count == 1) fronts[nation.Key] = onFronts[0];
        }
        return fronts;
    }

    /// <summary>The property of a division's side: DivisionNationalite = ENationalite/Allied (WARNO's DivisionCoalition).</summary>
    public const string SideProperty = "DivisionNationalite";

    /// <summary>What a side's value starts with: "ENationalite/" before "Allied" or "Axis".</summary>
    public const string SidePrefix = "ENationalite/";

    /// <summary>
    /// The property of a division's waving flag: a list of textures, one per frame, each named after its nation
    /// ("Texture_Division_Flag_US_0" and on). A new nation takes the new nation's flag.
    /// </summary>
    public const string FlagProperty = "FlagAnimationTextures";

    /// <summary>What a flag texture's name starts with, before the nation and the frame: "Texture_Division_Flag_".</summary>
    public const string FlagPrefix = "Texture_Division_Flag_";

    /// <summary>What a cost key starts with, as the rules' engine writes it: "EDefaultFactories / " before "tank".</summary>
    public const string CostKeyPrefix = "EDefaultFactories / ";

    /// <summary>
    /// The flag a division of a nation waves: that of the first of the game's divisions of the nation, as the game
    /// names it; when none has one, the textures named as the game names its own, as many as the division has now.
    /// </summary>
    /// <param name="country">The nation's code: "US".</param>
    /// <param name="before">The division as it is now, for how many frames its flag has.</param>
    /// <param name="game">The game's divisions.</param>
    public static IReadOnlyList<string> FlagOf(string country, Division before, IReadOnlyList<Division> game)
    {
        Division? ofNation = game.FirstOrDefault(division => division.Country == country && division.FlagTextures.Count > 0);
        if (ofNation is not null) return ofNation.FlagTextures;
        return [.. Enumerable.Range(0, before.FlagTextures.Count).Select(frame => FlagPrefix + country + "_" + frame.ToString(System.Globalization.CultureInfo.InvariantCulture))];
    }

    /// <summary>A flag as the game writes it: ["Texture_Division_Flag_US_0", "Texture_Division_Flag_US_1"], one texture a line.</summary>
    public static string FlagText(IEnumerable<string> textures) =>
        "[\n" + string.Concat(textures.Select(texture => "    \"" + texture + "\",\n")) + "]";

    /// <summary>
    /// A cost category's key in a division's costs, as a rule's target names it: the one its matrix has; null when the
    /// matrix has no such category.
    /// </summary>
    public static string? CostKey(Division division, string category) => division.CostKeys.GetValueOrDefault(category);
}
