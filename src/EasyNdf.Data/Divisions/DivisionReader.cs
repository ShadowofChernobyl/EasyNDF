using EasyNdf.Data.Game;
using EasyNdf.Data.Units;
using EasyNdf.Engine.Ndf;
using static EasyNdf.Data.Units.NdfValues;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// Reads a game's (or mod's) divisions from GameData/Generated/Gameplay/Decks: Divisions.ndf, DivisionRules.ndf and
/// DivisionCostMatrix.ndf, and their Official Battlegroups from Decks.ndf and DeckPacks.ndf. WARNO gives each division a rule of its own (DivisionRule = ..._Rule); Steel Division 2 keeps
/// every division's rule in one MAP, by division. Each field is looked for under both games' names.
/// </summary>
public static class DivisionReader
{
    private const string DecksFolder = "Generated/Gameplay/Decks/";

    /// <summary>Every division, in the order of Divisions.ndf. What cannot be read is added to <paramref name="problems"/>.</summary>
    /// <param name="files">The game's files.</param>
    /// <param name="texts">The game's texts.</param>
    /// <param name="problems">What could not be read.</param>
    /// <param name="cardNames">When given, it gets the descriptor of every battlegroup card in DeckPacks.ndf.</param>
    public static List<Division> Read(GameDataFiles files, GameTexts texts, List<string> problems, HashSet<string>? cardNames = null)
    {
        var transportCaps = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        Dictionary<string, List<DivisionUnit>> rules = ReadRules(files, problems, transportCaps);
        Dictionary<string, CostMatrix> costs = ReadCosts(files, problems);
        HashSet<string>? listed = ReadDivisionList(files, problems);
        Dictionary<string, string> emblems = ReadEmblemSources(files, problems);
        Dictionary<string, int> deckNumbers = ReadDeckNumbers(files);
        var deckNames = new Dictionary<string, string>(StringComparer.Ordinal);
        List<(string Division, Battlegroup Battlegroup)> decks = BattlegroupReader.Read(files, texts, problems, cardNames, deckNames);

        var divisions = new List<Division>();
        foreach (Declaration declaration in UnitReader.Declarations(files, DecksFolder + "Divisions.ndf", _ => true, problems))
        {
            if (declaration.Value is not ObjectValue { TypeName: "TDeckDivisionDescriptor" } division) continue;
            Division read = ReadDivision(declaration.Name, division, texts, rules, costs, deckNames);
            divisions.Add(read with
            {
                IsForMultiplayer = listed?.Contains(read.Descriptor) ?? read.Tags.Contains("DEFAULT"),
                EmblemSource = emblems.GetValueOrDefault(read.EmblemTexture),
                DeckNumber = deckNumbers.TryGetValue(read.Descriptor, out int number) ? number : null,
                TransportCaps = transportCaps.GetValueOrDefault(read.Descriptor) ?? new Dictionary<string, int>(),
            });
        }
        return WithBattlegroups(WithSharedCosts(divisions), decks);
    }

    /// <summary>
    /// Each division with how many others name its costs: Steel Division 2's divisions of one kind share theirs
    /// (MatrixCostName_HB_RemoveConstraints), so a change to one division's costs would change the others'.
    /// </summary>
    private static List<Division> WithSharedCosts(List<Division> divisions)
    {
        Dictionary<string, int> users = divisions.Where(division => division.CostMatrixName != "")
            .GroupBy(division => division.CostMatrixName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return [.. divisions.Select(division =>
            users.TryGetValue(division.CostMatrixName, out int count) && count > 1 ? division with { CostsSharedWith = count - 1 } : division)];
    }

    private static Division ReadDivision(
        string descriptor, ObjectValue division, GameTexts texts,
        Dictionary<string, List<DivisionUnit>> rules, Dictionary<string, CostMatrix> costs, Dictionary<string, string> deckNames)
    {
        string cfgName = Text(Own(division, "CfgName")) ?? "";
        string nameToken = Text(Own(division, "DivisionName")) ?? "";
        string ruleName = Text(Own(division, "DivisionRule")) ?? "";
        NdfValue? costMatrix = Own(division, "CostMatrix");
        // by name (MatrixCostName_...), or written in place as a MAP (a few of Steel Division 2's)
        string costName = Text(costMatrix) ?? "";
        // WARNO's rule is found by its own name; Steel Division 2's by the division's
        List<DivisionUnit> units = rules.GetValueOrDefault(ruleName) ?? rules.GetValueOrDefault(descriptor) ?? [];
        CostMatrix? matrix = costMatrix is ListValue inPlace ? CostsOf(inPlace) : costs.GetValueOrDefault(costName);
        return new Division
        {
            Descriptor = descriptor,
            CfgName = cfgName,
            NameToken = nameToken,
            // a name token not in the dictionaries: its deck's name in full, before its nickname alone ("TATSKINSK")
            Name = texts.Get(nameToken) ?? deckNames.GetValueOrDefault(descriptor) ?? texts.Get(Text(Own(division, "DivisionNickname")))
                ?? UnitReader.DivisionNameFromDescriptor(cfgName != "" ? cfgName : descriptor),
            // WARNO: DivisionCoalition = TWargameCoalition/NATO; Steel Division 2: DivisionNationalite = ENationalite/Allied
            Side = Text(Own(division, "DivisionCoalition")) ?? Text(Own(division, "DivisionNationalite")) ?? "",
            Country = Text(Own(division, "CountryId")) ?? "",
            TypeToken = Text(Own(division, "TypeToken")) ?? "",
            // Steel Division 2 names its summary DivisionDescription
            SummaryToken = Text(Own(division, "SummaryTextToken") ?? Own(division, SteelDivisionDivisions.SummaryProperty)) ?? "",
            Summary = texts.Get(Text(Own(division, "SummaryTextToken") ?? Own(division, SteelDivisionDivisions.SummaryProperty))) ?? "",
            HistoryToken = Text(Own(division, "HistoryTextToken")) ?? "",
            History = texts.Get(Text(Own(division, "HistoryTextToken"))) ?? "",
            ActivationPoints = WholeNumber(Own(division, "MaxActivationPoints")),
            ListOrder = Number(Own(division, "InterfaceOrder")),
            EmblemTexture = Text(Own(division, "EmblemTexture")) ?? "",
            Tags = TextsOf(Own(division, "DivisionTags")),
            StandoutUnits = TextsOf(Own(division, "StandoutUnits")),
            RuleName = ruleName,
            CostMatrixName = costName,
            Units = units,
            Costs = matrix?.Costs ?? new Dictionary<string, IReadOnlyList<int>>(),
            CostKeys = matrix?.Keys ?? new Dictionary<string, string>(),
            FlagTextures = TextsOf(Own(division, "FlagAnimationTextures")),
            Phases = PhasesOf(division),
            Economy = Text(Own(division, "DivisionEconomyList")) ?? "",
        };
    }

    /// <summary>A Steel Division 2 division's phases: each TPhaseDescriptor of its PhaseList; none for a division without one.</summary>
    private static List<DivisionPhase> PhasesOf(ObjectValue division) =>
        [.. ItemsOf(Own(division, "PhaseList")).OfType<ObjectValue>().Select(phase => new DivisionPhase(
            WholeNumber(Own(phase, "PhaseStartTimeInSeconds")) ?? 0, WholeNumber(Own(phase, "PhaseIncome")) ?? 0, WholeNumber(Own(phase, "PhaseBonus")) ?? 0))];

    /// <summary>
    /// The divisions players can pick, each with the first deck that names it: its Official Battlegroup. (The game
    /// allows only one; the decks of the game's scripted missions name stand-in divisions, which keep none.)
    /// </summary>
    private static List<Division> WithBattlegroups(List<Division> divisions, List<(string Division, Battlegroup Battlegroup)> decks)
    {
        var firstDeck = new Dictionary<string, Battlegroup>(StringComparer.Ordinal);
        foreach ((string division, Battlegroup battlegroup) in decks) firstDeck.TryAdd(division, battlegroup);
        return [.. divisions.Select(division =>
            division.IsForMultiplayer && firstDeck.TryGetValue(division.Descriptor, out Battlegroup? battlegroup)
                ? division with { Battlegroup = battlegroup }
                : division)];
    }

    /// <summary>
    /// Each division's number in deck codes: (Descriptor_Deck_Division_X, 123) in DeckSerializer.ndf's DivisionIds. The
    /// file's one declaration has no name, so its lines are looked through rather than parsed, as for the units'.
    /// </summary>
    private static Dictionary<string, int> ReadDeckNumbers(GameDataFiles files)
    {
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        using TextReader? reader = files.Open(DecksFolder + "DeckSerializer.ndf");
        if (reader is null) return numbers;
        while (reader.ReadLine() is string line)
            if (DeckNumberLine.Match(line) is { Success: true } match)
                numbers.TryAdd(match.Groups[1].Value, int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        return numbers;
    }

    // "(Descriptor_Deck_Division_US_3rd_Arm_multi, 12)", or with "~/" in front, as Steel Division 2 writes it
    private static readonly System.Text.RegularExpressions.Regex DeckNumberLine =
        new(@"\([ \t]*[~/]*(Descriptor_Deck_[A-Za-z0-9_]+)[ \t]*,[ \t]*([0-9]+)[ \t]*\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The divisions Steel Division 2 offers, from its DivisionList.ndf; null when the game has no such list (WARNO),
    /// where the DEFAULT tag says instead.
    /// </summary>
    private static HashSet<string>? ReadDivisionList(GameDataFiles files, List<string> problems)
    {
        HashSet<string>? listed = null;
        foreach (Declaration declaration in UnitReader.Declarations(files, DecksFolder + "DivisionList.ndf", _ => true, problems, mustExist: false))
        {
            listed ??= new HashSet<string>(StringComparer.Ordinal);
            listed.UnionWith(TextsOf(Find(declaration.Value, "DivisionList")));
        }
        return listed;
    }

    /// <summary>
    /// Each emblem's picture, by its texture's name: DivisionTextures.ndf has
    /// Texture_Division_Emblem_X is TUIResourceTexture_Common ( FileName = "GameData:/Assets/2D/.../X.png" ), in
    /// WARNO's UserInterface folder or Steel Division 2's Gameplay/UI/Resources.
    /// </summary>
    private static Dictionary<string, string> ReadEmblemSources(GameDataFiles files, List<string> problems)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in EmblemFiles)
            foreach (Declaration declaration in UnitReader.Declarations(files, file,
                name => name.StartsWith("Texture_Division_Emblem_", StringComparison.Ordinal), problems, mustExist: false))
                if (Text(Find(declaration.Value, "FileName")) is string picture) sources.TryAdd(declaration.Name, GamePictures.Normalised(picture));
        return sources;
    }

    /// <summary>Where each game keeps its division textures: WARNO's, then Steel Division 2's.</summary>
    private static readonly string[] EmblemFiles =
        ["Generated/UserInterface/Textures/DivisionTextures.ndf", "Generated/Gameplay/UI/Resources/Textures/DivisionTextures.ndf"];

    // ---- the rules: which units, and how ----

    /// <summary>
    /// Each rule's units: WARNO's by the rule's own name (Descriptor_Deck_Division_US_3rd_Arm_multi_Rule), Steel
    /// Division 2's by the division's descriptor (the key of its MAP), with its transports' caps.
    /// </summary>
    /// <param name="transportCaps">Gets Steel Division 2's caps on each division's transports (<see cref="Division.TransportCaps"/>).</param>
    private static Dictionary<string, List<DivisionUnit>> ReadRules(GameDataFiles files, List<string> problems, Dictionary<string, Dictionary<string, int>> transportCaps)
    {
        var rules = new Dictionary<string, List<DivisionUnit>>(StringComparer.Ordinal);
        foreach (Declaration declaration in UnitReader.Declarations(files, DecksFolder + "DivisionRules.ndf", _ => true, problems))
        {
            if (declaration.Value is ObjectValue { TypeName: "TDeckDivisionRule" } rule) rules[declaration.Name] = UnitsOf(rule);
            foreach (var (key, value) in PairsOf(Find(declaration.Value, "DivisionRules")))
                if (Text(key) is string division)
                {
                    rules[division] = UnitsOf(value);
                    transportCaps[division] = TransportCapsOf(value);
                }
        }
        return rules;
    }

    /// <summary>A Steel Division 2 rule's caps on its transports: each TDeckTransportRule of its TransportRuleList.</summary>
    private static Dictionary<string, int> TransportCapsOf(NdfValue rule)
    {
        var caps = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (NdfValue item in ItemsOf(Find(rule, "TransportRuleList")))
            if (item is ObjectValue cap && Text(Own(cap, "TransportDescriptor")) is string transport)
                caps.TryAdd(transport, WholeNumber(Own(cap, "MaxNumber")) ?? 0);
        return caps;
    }

    /// <summary>The units of one rule: each TDeckUniteRule in its UnitRuleList.</summary>
    private static List<DivisionUnit> UnitsOf(NdfValue rule)
    {
        var units = new List<DivisionUnit>();
        foreach (NdfValue item in ItemsOf(Find(rule, "UnitRuleList")))
        {
            if (item is not ObjectValue unitRule || Text(Own(unitRule, "UnitDescriptor")) is not string unit) continue;
            units.Add(new DivisionUnit
            {
                Unit = unit,
                Cards = WholeNumber(Own(unitRule, "MaxPackNumber")) ?? 0,
                UnitsPerCard = WholeNumber(Own(unitRule, "NumberOfUnitInPack")),
                UnitsPerCardByPhase = ItemsOf(Own(unitRule, "NumberOfUnitInPackByPhase")).Select(n => WholeNumber(n) ?? 0).ToList(),
                VeterancyMultipliers = ItemsOf(Own(unitRule, "NumberOfUnitInPackXPMultiplier")).Select(n => Number(n) ?? 0).ToList(),
                AvailableWithoutTransport = Text(Own(unitRule, "AvailableWithoutTransport")) == "True",
                Transports = TextsOf(Own(unitRule, "AvailableTransportList")),
            });
        }
        return units;
    }

    // ---- the costs ----

    /// <summary>
    /// Each cost matrix, by name: MatrixCostName_X is MAP [(Factory/Tanks, [1, 1, 2]), ...]. Most are in the generated
    /// DivisionCostMatrix.ndf; Steel Division 2 keeps one more in Gameplay/Decks/DivisionCostMatrix.ndf.
    /// </summary>
    private static Dictionary<string, CostMatrix> ReadCosts(GameDataFiles files, List<string> problems)
    {
        var matrices = new Dictionary<string, CostMatrix>(StringComparer.Ordinal);
        // the second is named with GameData/ in front, or it would also match the end of the first's path
        foreach (string file in new[] { DecksFolder + "DivisionCostMatrix.ndf", "GameData/Gameplay/Decks/DivisionCostMatrix.ndf" })
            foreach (Declaration declaration in UnitReader.Declarations(files, file, _ => true, problems, mustExist: false))
                matrices.TryAdd(declaration.Name, CostsOf(declaration.Value));
        return matrices;
    }

    /// <summary>One cost matrix: its costs by category, and each category's key as the file has it.</summary>
    private sealed record CostMatrix(IReadOnlyDictionary<string, IReadOnlyList<int>> Costs, IReadOnlyDictionary<string, string> Keys);

    /// <summary>
    /// One cost matrix's categories and costs. The category is the part after the slash of each key, named as the
    /// units name their factory: WARNO's Factory/Tanks is "Tanks"; Steel Division 2's EDefaultFactories/tank is "Tanks"
    /// too, as its units say EDefaultFactories/Tanks. Each key is kept as the rules' engine writes it, for a rule to
    /// name it: Steel Division 2's reads as a division, "EDefaultFactories / tank".
    /// </summary>
    private static CostMatrix CostsOf(NdfValue matrix)
    {
        var categories = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in PairsOf(matrix))
        {
            if (CategoryOf(key) is not string found) continue;
            string category = Sd2CostCategories.GetValueOrDefault(found, found);
            categories[category] = ItemsOf(value).Select(n => WholeNumber(n) ?? 0).ToList();
            if (key.IsPrimitive) keys[category] = EasyNdf.Engine.Writing.CanonicalWriter.PrimitiveText(key);
        }
        return new CostMatrix(categories, keys);
    }

    /// <summary>Steel Division 2's cost categories, by the factory names its units use.</summary>
    private static readonly Dictionary<string, string> Sd2CostCategories = new(StringComparer.Ordinal)
    {
        ["infanterie"] = "Infantry", ["reco"] = "Recons", ["tank"] = "Tanks", ["support"] = "Support", ["at"] = "AT",
        ["dca"] = "DCA", ["art"] = "Art", ["air"] = "Planes", ["defense"] = "Defense",
    };

    /// <summary>
    /// A cost matrix's category: the part after the slash. The NDF reader takes WARNO's Factory/Tanks for a path, but
    /// Steel Division 2's EDefaultFactories/tank for a division, as a name in lower case after a slash reads as a value.
    /// </summary>
    private static string? CategoryOf(NdfValue key) =>
        Text(key) ?? (key is BinaryValue { Operator: "/" } division ? Text(division.Right) : null);

    // ---- values ----

    /// <summary>A number as a whole number; null when it is not one.</summary>
    private static int? WholeNumber(NdfValue? value) => Number(value) is double number ? (int)Math.Round(number) : null;

    /// <summary>The texts of a list's items, leaving out what is not a text: names, quoted texts, the last part of references.</summary>
    private static List<string> TextsOf(NdfValue? list) => [.. ItemsOf(list).Select(Text).OfType<string>()];
}
