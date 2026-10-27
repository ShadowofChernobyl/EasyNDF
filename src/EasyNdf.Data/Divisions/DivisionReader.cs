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
        Dictionary<string, List<DivisionUnit>> rules = ReadRules(files, problems);
        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> costs = ReadCosts(files, problems);
        HashSet<string>? listed = ReadDivisionList(files, problems);
        Dictionary<string, string> emblems = ReadEmblemSources(files, problems);
        Dictionary<string, int> deckNumbers = ReadDeckNumbers(files);
        List<(string Division, Battlegroup Battlegroup)> decks = BattlegroupReader.Read(files, texts, problems, cardNames);

        var divisions = new List<Division>();
        foreach (Declaration declaration in UnitReader.Declarations(files, DecksFolder + "Divisions.ndf", _ => true, problems))
        {
            if (declaration.Value is not ObjectValue { TypeName: "TDeckDivisionDescriptor" } division) continue;
            Division read = ReadDivision(declaration.Name, division, texts, rules, costs);
            divisions.Add(read with
            {
                IsForMultiplayer = listed?.Contains(read.Descriptor) ?? read.Tags.Contains("DEFAULT"),
                EmblemSource = emblems.GetValueOrDefault(read.EmblemTexture),
                DeckNumber = deckNumbers.TryGetValue(read.Descriptor, out int number) ? number : null,
            });
        }
        return WithBattlegroups(divisions, decks);
    }

    private static Division ReadDivision(
        string descriptor, ObjectValue division, GameTexts texts,
        Dictionary<string, List<DivisionUnit>> rules, Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> costs)
    {
        string cfgName = Text(Own(division, "CfgName")) ?? "";
        string nameToken = Text(Own(division, "DivisionName")) ?? "";
        string ruleName = Text(Own(division, "DivisionRule")) ?? "";
        NdfValue? costMatrix = Own(division, "CostMatrix");
        // by name (MatrixCostName_...), or written in place as a MAP (a few of Steel Division 2's)
        string costName = Text(costMatrix) ?? "";
        // WARNO's rule is found by its own name; Steel Division 2's by the division's
        List<DivisionUnit> units = rules.GetValueOrDefault(ruleName) ?? rules.GetValueOrDefault(descriptor) ?? [];
        return new Division
        {
            Descriptor = descriptor,
            CfgName = cfgName,
            NameToken = nameToken,
            Name = texts.Get(nameToken) ?? texts.Get(Text(Own(division, "DivisionNickname")))
                ?? UnitReader.DivisionNameFromDescriptor(cfgName != "" ? cfgName : descriptor),
            // WARNO: DivisionCoalition = TWargameCoalition/NATO; Steel Division 2: DivisionNationalite = ENationalite/Allied
            Side = Text(Own(division, "DivisionCoalition")) ?? Text(Own(division, "DivisionNationalite")) ?? "",
            Country = Text(Own(division, "CountryId")) ?? "",
            TypeToken = Text(Own(division, "TypeToken")) ?? "",
            SummaryToken = Text(Own(division, "SummaryTextToken")) ?? "",
            Summary = texts.Get(Text(Own(division, "SummaryTextToken"))) ?? "",
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
            Costs = costMatrix is ListValue inPlace ? CostsOf(inPlace) : costs.GetValueOrDefault(costName) ?? new Dictionary<string, IReadOnlyList<int>>(),
        };
    }

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
    /// Each emblem's picture, by its texture's name: WARNO's DivisionTextures.ndf has
    /// Texture_Division_Emblem_X is TUIResourceTexture_Common ( FileName = "GameData:/Assets/2D/.../X.png" ).
    /// </summary>
    private static Dictionary<string, string> ReadEmblemSources(GameDataFiles files, List<string> problems)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Declaration declaration in UnitReader.Declarations(files, "Generated/UserInterface/Textures/DivisionTextures.ndf",
            name => name.StartsWith("Texture_Division_Emblem_", StringComparison.Ordinal), problems, mustExist: false))
            if (Text(Find(declaration.Value, "FileName")) is string file) sources[declaration.Name] = GamePictures.Normalised(file);
        return sources;
    }

    // ---- the rules: which units, and how ----

    /// <summary>
    /// Each rule's units: WARNO's by the rule's own name (Descriptor_Deck_Division_US_3rd_Arm_multi_Rule), Steel
    /// Division 2's by the division's descriptor (the key of its MAP).
    /// </summary>
    private static Dictionary<string, List<DivisionUnit>> ReadRules(GameDataFiles files, List<string> problems)
    {
        var rules = new Dictionary<string, List<DivisionUnit>>(StringComparer.Ordinal);
        foreach (Declaration declaration in UnitReader.Declarations(files, DecksFolder + "DivisionRules.ndf", _ => true, problems))
        {
            if (declaration.Value is ObjectValue { TypeName: "TDeckDivisionRule" } rule) rules[declaration.Name] = UnitsOf(rule);
            foreach (var (key, value) in PairsOf(Find(declaration.Value, "DivisionRules")))
                if (Text(key) is string division) rules[division] = UnitsOf(value);
        }
        return rules;
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
    private static Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> ReadCosts(GameDataFiles files, List<string> problems)
    {
        var matrices = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>(StringComparer.Ordinal);
        // the second is named with GameData/ in front, or it would also match the end of the first's path
        foreach (string file in new[] { DecksFolder + "DivisionCostMatrix.ndf", "GameData/Gameplay/Decks/DivisionCostMatrix.ndf" })
            foreach (Declaration declaration in UnitReader.Declarations(files, file, _ => true, problems, mustExist: false))
                matrices.TryAdd(declaration.Name, CostsOf(declaration.Value));
        return matrices;
    }

    /// <summary>
    /// One cost matrix's categories and costs. The category is the part after the slash of each key, named as the
    /// units name their factory: WARNO's Factory/Tanks is "Tanks"; Steel Division 2's EDefaultFactories/tank is "Tanks"
    /// too, as its units say EDefaultFactories/Tanks.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<int>> CostsOf(NdfValue matrix)
    {
        var categories = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        foreach (var (key, value) in PairsOf(matrix))
            if (CategoryOf(key) is string category)
                categories[Sd2CostCategories.GetValueOrDefault(category, category)] = ItemsOf(value).Select(n => WholeNumber(n) ?? 0).ToList();
        return categories;
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
