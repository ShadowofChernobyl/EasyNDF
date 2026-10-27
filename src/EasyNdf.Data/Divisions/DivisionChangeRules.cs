using System.Globalization;
using System.Text.Json.Nodes;
using EasyNdf.Data.Languages;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// The mod's division changes as ordinary rules, for its "Divisions" preset: one rule per change, named in plain words
/// ("16DE PANTSERDIVISION · PIONIERS: cards 2 → 3"), under a group for each division. The export runs them like any
/// rule, so nothing the Armory keeps is hidden. They are made again from the mod's divisions whenever these change, so
/// they always say what the Armory shows; each is locked (<see cref="ArmoryKey"/>) and is changed in the Armory.
/// </summary>
/// <remarks>
/// Each rule names the one file it is for (<see cref="FileKey"/>): a rule that adds or copies a descriptor must run on
/// that file only. Names are not rules of the game's files: a name rule sets the division's name text (its token), and
/// the export writes the name itself into a new UNITS.csv, made from the mod's on the Files tab (<see cref="NameTextKey"/>).
/// </remarks>
public sealed class DivisionChangeRules
{
    /// <summary>The key, in a rule, of what the Armory knows about it: which division and change it is for.</summary>
    public const string ArmoryKey = "Armory";

    /// <summary>The key, under <see cref="ArmoryKey"/>, of the file the rule is for, such as "Divisions.ndf".</summary>
    public const string FileKey = "File";

    /// <summary>The key, under <see cref="ArmoryKey"/>, of the division's descriptor.</summary>
    public const string DivisionKey = "Division";

    /// <summary>The key, under <see cref="ArmoryKey"/>, of what the rule changes: "Name", "Picture", or the rule's own kind.</summary>
    public const string ShowsKey = "Shows";

    /// <summary>The key, under <see cref="ArmoryKey"/>, of a name rule's name text, which the export writes into UNITS.csv.</summary>
    public const string NameTextKey = "NameText";

    /// <summary>
    /// The key, under <see cref="ArmoryKey"/>, of the texts a rule gives tokens to, by token: a division's summary and
    /// history. The export writes them into UNITS.csv as it writes the names.
    /// </summary>
    public const string TextsKey = "Texts";

    /// <summary>
    /// The key, under <see cref="ArmoryKey"/>, of the change the rule writes, such as "Points", "Costs:Logistic" or
    /// "Unit:Descriptor_Unit_X:Cards"; "New" for every rule of a new division. A change can take several rules.
    /// </summary>
    public const string ChangeKey = "Change";

    /// <summary>
    /// The key, under <see cref="ArmoryKey"/>, of whether the rule is locked: made from the Armory and changed there.
    /// Make Editable unlocks it; it is then kept as it is, and no longer follows the Armory.
    /// </summary>
    public const string LockedKey = "Locked";

    /// <summary>
    /// The key, under <see cref="ArmoryKey"/>, of a generated division's entry (<see cref="ModDivision.ToJson"/>), on the
    /// rule that sets its unit list: the export makes the list from it each time (<see cref="GeneratedUnitLists"/>).
    /// </summary>
    public const string GeneratedDivisionKey = "GeneratedDivision";

    /// <summary>
    /// The key, among a rule's Armory details, of a generated battlegroup's part: its deck (Decks.ndf) or its cards
    /// (DeckPacks.ndf), each written by the export once it has made the cards (<see cref="GeneratedBattlegroups"/>).
    /// </summary>
    public const string GeneratedBattlegroupKey = "GeneratedBattlegroup";

    /// <summary>The key, under <see cref="ArmoryKey"/>, of a picture rule's picture file on this computer, which the export copies into the mod.</summary>
    public const string PictureFileKey = "PictureFile";

    /// <summary>The key, under <see cref="ArmoryKey"/>, of a game emblem's picture as the game names it, for the Rules tab to show.</summary>
    public const string PictureSourceKey = "PictureSource";

    /// <summary>The key, under <see cref="ArmoryKey"/>, of where the export puts the picture, under GameData.</summary>
    public const string PicturePathKey = "PicturePath";

    /// <summary>The files division changes are written into, by what they hold.</summary>
    public const string DivisionsFile = "Divisions.ndf", RulesFile = "DivisionRules.ndf", CostsFile = "DivisionCostMatrix.ndf",
        DeckNumbersFile = "DeckSerializer.ndf", TexturesFile = "DivisionTextures.ndf", DecksFile = "Decks.ndf", CardsFile = "DeckPacks.ndf";

    /// <summary>Every file division changes can be written into.</summary>
    public static IReadOnlyList<string> AllFiles { get; } = [DivisionsFile, RulesFile, CostsFile, DeckNumbersFile, TexturesFile, DecksFile, CardsFile];

    /// <summary>Where a picture of the user's own goes in the mod, under GameData, as the game keeps its emblems.</summary>
    public const string EmblemFolder = "Assets/2D/Interface/UseOutGame/Division/Emblem/";

    private readonly TextCatalog texts;
    private readonly Func<string, string> unitName;
    private readonly JsonArray rules = [];

    /// <summary>The battlegroup cards DeckPacks.ndf has, by descriptor, and those a rule made now adds: each is added once.</summary>
    private readonly HashSet<string> cardsInFile;

    /// <summary>The change the rules made now are for (<see cref="ChangeKey"/>).</summary>
    private string changeBeingWritten = "";

    private DivisionChangeRules(TextCatalog texts, Func<string, string> unitName, HashSet<string> cardsInFile)
    {
        this.texts = texts;
        this.unitName = unitName;
        this.cardsInFile = cardsInFile;
    }

    /// <summary>
    /// The rules for a mod's division changes, in the order the Armory lists the divisions. A new division without a
    /// deck number (made before numbers were kept) is given one, so the caller saves the mod after.
    /// </summary>
    /// <param name="mod">The mod.</param>
    /// <param name="game">The game's divisions, before the mod's changes.</param>
    /// <param name="texts">The language the rules are named in.</param>
    /// <param name="unitName">A unit's name as the Armory shows it, by its descriptor.</param>
    /// <param name="gameCards">
    /// The battlegroup cards the game's DeckPacks.ndf has (<see cref="Units.UnitCatalog.BattlegroupCards"/>); when not
    /// given, those of the game's Official Battlegroups.
    /// </param>
    public static JsonArray For(Mod mod, IReadOnlyList<Division> game, TextCatalog texts, Func<string, string> unitName, IReadOnlySet<string>? gameCards = null)
    {
        HashSet<string> cards = gameCards is not null
            ? new HashSet<string>(gameCards, StringComparer.Ordinal)
            : new HashSet<string>(game.SelectMany(division => division.Battlegroup?.Cards ?? []).Select(card => card.PackName), StringComparer.Ordinal);
        var writer = new DivisionChangeRules(texts, unitName, cards);
        // New divisions first: each copies its starting division's unit list as the game has it, before the changes
        // to that division (a unit put in is written as text, and could not be taken out of the copy again).
        foreach (ModDivision change in mod.Divisions.OrderBy(change => change.IsNew ? 0 : 1))
        {
            Division? start = game.FirstOrDefault(division => division.Descriptor == change.StartsFrom);
            if (start is null) continue;
            if (change.IsNew && change.DeckNumber is null) change.DeckNumber = NewDivisionStart.NextDeckNumber(game, mod);
            // a generated division's units are made by the export: none are shown here, so none are written one by one
            Division shown = change.Apply(change.IsGenerated ? start with { Units = [] } : start);
            int before = writer.rules.Count;
            writer.Group(shown.Name);
            if (change.IsNew) writer.NewDivision(change, start, shown);
            else writer.ChangedDivision(change, start, shown);
            // a division whose change adds nothing to its files gets no group
            if (writer.rules.Count == before + 1) writer.rules.RemoveAt(before);
        }
        return writer.rules;
    }

    /// <summary>
    /// The rules of the Armory presets for one file, by its name: those for it, and the group headings, so the rules
    /// that add or copy descriptors run only on their own file. Empty when none is for it.
    /// </summary>
    public static JsonArray ForFile(IEnumerable<JsonNode?> rules, string fileName)
    {
        var found = new JsonArray();
        foreach (JsonNode? rule in rules)
        {
            string? file = (string?)rule?[ArmoryKey]?[FileKey];
            bool isGroup = (string?)rule?["Kind"] == "Group";
            if (isGroup || string.Equals(file, fileName, StringComparison.OrdinalIgnoreCase)) found.Add(rule!.DeepClone());
        }
        // headings alone change nothing
        if (!found.Any(rule => (string?)rule?["Kind"] != "Group")) return [];
        // run after the file's own preset's rules, they start with a group of their own: rules made editable, with no
        // division heading left above them, fell under that preset's last group and its conditions, or were switched
        // off with it
        if ((string?)found[0]?["Kind"] != "Group")
            found.Insert(0, new JsonObject
            {
                ["Name"] = "Divisions", ["Kind"] = "Group", ["Enabled"] = true, ["LogicGate"] = "All",
                ["Conditions"] = new JsonArray(), ["Actions"] = new JsonArray(), ["Params"] = new JsonObject(),
            });
        return found;
    }

    /// <summary>
    /// The names of the files Armory rules are for (each rule names its own; group headings name none), as a set that
    /// ignores case. Looking each file up in it is quick, where <see cref="ForFile"/> copies the rules it finds.
    /// </summary>
    public static HashSet<string> FilesOf(IEnumerable<JsonNode?> rules)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? rule in rules)
            if ((string?)rule?["Kind"] != "Group" && (string?)rule?[ArmoryKey]?[FileKey] is string file) files.Add(file);
        return files;
    }

    /// <summary>Whether a rule was made from the Armory, and is changed there.</summary>
    public static bool IsFromArmory(JsonNode? rule) => rule?[ArmoryKey] is JsonObject;

    /// <summary>
    /// The part of the Armory a rule made from it belongs to: Units for a rule of a unit the mod adds or changes, Weapons for
    /// one of a weapon; by the file it
    /// is for, Battlegroups for the battlegroups' files (Decks.ndf and DeckPacks.ndf), Divisions for the rest.
    /// </summary>
    public static ArmoryPart PartOf(JsonNode? rule)
    {
        if (rule?[ArmoryKey]?[Units.UnitChangeRules.UnitKey] is not null) return ArmoryPart.Units;
        if (rule?[ArmoryKey]?[Weapons.WeaponChangeRules.WeaponKey] is not null) return ArmoryPart.Weapons;
        string? file = (string?)rule?[ArmoryKey]?[FileKey];
        bool isBattlegroups = string.Equals(file, DecksFile, StringComparison.OrdinalIgnoreCase) || string.Equals(file, CardsFile, StringComparison.OrdinalIgnoreCase);
        return isBattlegroups ? ArmoryPart.Battlegroups : ArmoryPart.Divisions;
    }

    // ---- a game division changed ----

    private void ChangedDivision(ModDivision change, Division game, Division shown)
    {
        string division = game.Descriptor;
        changeBeingWritten = "Name";
        if (change.Name != "") NameRule(division, shown.Name, Line(shown, "Divisions.Rule.Rename", ("from", game.Name), ("to", shown.Name)));
        DetailRules(change, game, shown);
        // after the details: a new nation or type sets the whole list of tags, DEFAULT with them, which would put a
        // division taken out back in the list
        changeBeingWritten = "TakenOut";
        if (change.IsRemoved)
            Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.TakenOutOfList"), Action("DivisionTags", "Remove [array]", "DEFAULT"));
        UnitRules(game, shown, ruleName: game.RuleName);
        BattlegroupRules(change, game, shown);
    }

    /// <summary>The division's details the change sets, each a rule on Divisions.ndf (costs on DivisionCostMatrix.ndf).</summary>
    private void DetailRules(ModDivision change, Division before, Division shown)
    {
        string division = shown.Descriptor;
        changeBeingWritten = "Points";
        if (change.ActivationPoints is int points && points != before.ActivationPoints)
            Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.Points", ("from", before.ActivationPoints), ("to", points)),
                Action("MaxActivationPoints", "Set To [int, float, string, boolean]", Whole(points)));
        changeBeingWritten = "ListOrder";
        if (change.ListOrder is double order && order != before.ListOrder)
            Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.ListOrder", ("from", Decimal(before.ListOrder)), ("to", Decimal(order))),
                Action("InterfaceOrder", "Set To [int, float, string, boolean]", Decimal(order)));
        changeBeingWritten = "Type";
        // the type is one of the division's tags too, which the game may filter by: they change with it
        if (change.TypeToken is string type && type != before.TypeToken)
            Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.Type", ("from", before.TypeToken), ("to", type)),
                Action("TypeToken", "Set To [int, float, string, boolean]", Quoted(type)),
                Action("DivisionTags", "Set To [list, object]", TagsWithNation(before, shown)));
        changeBeingWritten = "Nation";
        if ((change.Country is not null && change.Country != before.Country) || (change.Side is not null && change.Side != before.Side))
            Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.Nation", ("from", before.Country), ("to", shown.Country)),
                Action("CountryId", "Set To [int, float, string, boolean]", Quoted(shown.Country)),
                Action("DivisionCoalition", "Set To [int, float, string, boolean]", "TWargameCoalition/" + shown.Side),
                Action("DivisionTags", "Set To [list, object]", TagsWithNation(before, shown)));
        TextRule(change.Summary, before.Summary, division, shown, "summary", "SummaryTextToken", "Summary", "Divisions.Rule.Summary");
        TextRule(change.History, before.History, division, shown, "history", "HistoryTextToken", "History", "Divisions.Rule.History");
        changeBeingWritten = "Standout";
        if (change.StandoutUnits is not null && !change.StandoutUnits.SequenceEqual(before.StandoutUnits))
            Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.Standout", ("units", string.Join(", ", shown.StandoutUnits.Select(unitName)))),
                Action("StandoutUnits", "Set To [list, object]", UnitList(shown.StandoutUnits)));
        EmblemRules(change, before, shown);
        if (change.Costs is not null)
            foreach ((string category, List<int> costs) in change.Costs)
            {
                IReadOnlyList<int> was = before.Costs.GetValueOrDefault(category) ?? [];
                if (was.SequenceEqual(costs)) continue;
                changeBeingWritten = "Costs:" + category;
                Rule(new JsonObject
                {
                    ["Name"] = Line(shown, "Divisions.Rule.Costs", ("category", category), ("from", Numbers(was)), ("to", Numbers(costs))),
                    ["Kind"] = "Edit",
                    ["Params"] = new JsonObject { ["Scope"] = "constants" },
                    ["Actions"] = new JsonArray(Action(before.CostMatrixName + ActionApplier.PathSeparator + "Factory/" + category, "Set To [list, object]", "[" + Numbers(costs) + "]")),
                }, CostsFile, division, "Edit");
            }
    }

    /// <summary>
    /// The emblem: another of the game's, a rule setting EmblemTexture; a picture of the user's own, that rule and two
    /// on DivisionTextures.ndf (the picture's texture, and its entry in the texture bank). The export copies the picture.
    /// </summary>
    private void EmblemRules(ModDivision change, Division before, Division shown)
    {
        string division = shown.Descriptor;
        if (!change.IsNew) changeBeingWritten = "Emblem";
        if (change.EmblemFile is string file)
        {
            string texture = "Texture_Division_Emblem_" + CoreName(division);
            string path = EmblemFolder + CoreName(division) + ".png";
            Rule(new JsonObject
            {
                ["Name"] = Line(shown, "Divisions.Rule.EmblemTexture"),
                ["Kind"] = "Add",
                ["Params"] = new JsonObject
                {
                    ["Text"] = texture + " is TUIResourceTexture_Common\n(\n    FileName = \"GameData:/" + path + "\"\n)",
                    ["Name"] = "",
                },
            }, TexturesFile, division, "Add");
            Rule(new JsonObject
            {
                ["Name"] = Line(shown, "Divisions.Rule.EmblemBank"),
                ["Kind"] = "Edit",
                ["Params"] = new JsonObject { ["Scope"] = "type", ["ScopeType"] = "TBUCKToolAdditionalTextureBank" },
                ["Actions"] = new JsonArray(Action("Textures", "Insert [array]", "LastIndex: (\"" + texture + "\", MAP [(~/ComponentState/Normal, ~/" + texture + ")])")),
            }, TexturesFile, division, "Edit");
            JsonObject picture = Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.Emblem", ("picture", Path.GetFileName(file))),
                Action("EmblemTexture", "Set To [int, float, string, boolean]", Quoted(texture)));
            Shows(picture, "Picture");
            ((JsonObject)picture[ArmoryKey]!)[PictureFileKey] = file;
            ((JsonObject)picture[ArmoryKey]!)[PicturePathKey] = path;
            return;
        }
        if (change.EmblemTexture is string other && other != before.EmblemTexture)
        {
            JsonObject picture = Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.GameEmblem", ("texture", other == "" ? "–" : other)),
                Action("EmblemTexture", "Set To [int, float, string, boolean]", Quoted(other)));
            Shows(picture, "Picture");
            if (shown.EmblemSource is { Length: > 0 } source) ((JsonObject)picture[ArmoryKey]!)[PictureSourceKey] = source;
        }
    }

    // ---- the units of a division ----

    /// <summary>
    /// The units the mod changes in a division: each unit taken out, added, or changed (a rule for each value that
    /// changed). <paramref name="before"/> is the division as its rule in the file has it.
    /// </summary>
    private void UnitRules(Division before, Division shown, string ruleName)
    {
        string division = shown.Descriptor;
        foreach (DivisionUnit gone in before.Units.Where(unit => !shown.Units.Any(each => each.Unit == unit.Unit)))
        {
            changeBeingWritten = "UnitOut:" + gone.Unit;
            Rule(new JsonObject
            {
                ["Name"] = Line(shown, gone.Unit, "Divisions.Rule.UnitTakenOut"),
                ["Kind"] = "Remove",
                ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, ruleName), Condition("UnitDescriptor", UnitReference(gone.Unit))),
                ["Params"] = new JsonObject { ["Scope"] = "type", ["ScopeType"] = "TDeckUniteRule" },
            }, RulesFile, division, "Remove");
        }
        foreach (DivisionUnit unit in shown.Units)
        {
            DivisionUnit? was = before.Units.FirstOrDefault(each => each.Unit == unit.Unit);
            changeBeingWritten = (was is null ? "UnitIn:" : "Unit:") + unit.Unit;
            if (was is null) AddedUnitRule(division, shown, ruleName, unit);
            else ChangedUnitRules(division, shown, ruleName, was, unit);
        }
    }

    /// <summary>A unit put in a division: its whole line inserted at the end of the division's list.</summary>
    private void AddedUnitRule(string division, Division shown, string ruleName, DivisionUnit unit) =>
        Edit(RulesFile, ruleName, Line(shown, unit.Unit, "Divisions.Rule.UnitAdded", ("cards", unit.Cards), ("units", unit.UnitsPerCard ?? 0)),
            Action("UnitRuleList", "Insert [array]", "LastIndex: " + UnitLine(unit)), forDivision: division);

    /// <summary>A unit the division has already, changed: a rule for each of its values that changed.</summary>
    private void ChangedUnitRules(string division, Division shown, string ruleName, DivisionUnit was, DivisionUnit unit)
    {
        void Set(string textName, object? from, object? to, params JsonObject[] actions)
        {
            // the value's own change, named as its text is: "Unit:Descriptor_Unit_X:Cards"
            changeBeingWritten = "Unit:" + unit.Unit + ":" + textName.Split('.')[^1];
            Rule(new JsonObject
            {
                ["Name"] = Line(shown, unit.Unit, textName, ("from", from), ("to", to)),
                ["Kind"] = "Edit",
                ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, ruleName), Condition("UnitDescriptor", UnitReference(unit.Unit))),
                ["Params"] = new JsonObject { ["Scope"] = "type", ["ScopeType"] = "TDeckUniteRule" },
                ["Actions"] = new JsonArray(actions),
            }, RulesFile, division, "Edit");
        }
        if (unit.Cards != was.Cards)
            Set("Divisions.Rule.Cards", was.Cards, unit.Cards, Action("MaxPackNumber", "Set To [int, float, string, boolean]", Whole(unit.Cards)));
        if (unit.UnitsPerCard is int perCard && perCard != was.UnitsPerCard)
            Set("Divisions.Rule.UnitsPerCard", was.UnitsPerCard, perCard, Action("NumberOfUnitInPack", "Set To [int, float, string, boolean]", Whole(perCard)));
        if (!unit.VeterancyMultipliers.SequenceEqual(was.VeterancyMultipliers))
            Set("Divisions.Rule.Veterancy", Decimals(was.VeterancyMultipliers), Decimals(unit.VeterancyMultipliers),
                Action("NumberOfUnitInPackXPMultiplier", "Set To [list, object]", "[" + Decimals(unit.VeterancyMultipliers) + "]"));
        if (unit.AvailableWithoutTransport != was.AvailableWithoutTransport)
            Set("Divisions.Rule.NoTransport", YesOrNo(was.AvailableWithoutTransport), YesOrNo(unit.AvailableWithoutTransport),
                Action("AvailableWithoutTransport", "Set To [int, float, string, boolean]", unit.AvailableWithoutTransport ? "True" : "False"));
        if (!unit.Transports.SequenceEqual(was.Transports))
        {
            // the list is set where the unit has one, and added where it has none (a unit that needed no transport)
            string list = UnitList(unit.Transports);
            Set("Divisions.Rule.Transports", Names(was.Transports), Names(unit.Transports),
                Action("AvailableTransportList", "Set To [list, object]", list), Action("AvailableTransportList", "Add Property", list));
        }
    }

    // ---- a new division ----

    /// <summary>
    /// A new division: its descriptor, its name, its emblem, its costs, its deck number, and its unit list, each unit put
    /// in. The descriptor and the unit list are made by copying another division's as a frame (a Clone rule), as the
    /// rules after them can only change a descriptor that is copied, not one added; every value of the copy is then
    /// set as the new division's own, so nothing of the other division is left: its texts are given tokens of their own
    /// and its list of units is emptied.
    /// </summary>
    private void NewDivision(ModDivision change, Division start, Division shown)
    {
        string division = shown.Descriptor;
        // a new division's rules go together: one alone would leave the division half made
        changeBeingWritten = "New";
        string ruleName = shown.RuleName;
        string costName = "MatrixCostName_" + shown.CfgName;
        var actions = new JsonArray(
                Action("CfgName", "Set To [int, float, string, boolean]", "'" + shown.CfgName + "'"),
                Action("InterfaceOrder", "Set To [int, float, string, boolean]", Decimal(shown.ListOrder)),
                Action("DivisionCoalition", "Set To [int, float, string, boolean]", "TWargameCoalition/" + shown.Side),
                Action("DivisionTags", "Set To [list, object]", TagsText(shown.Tags)),
                Action("MaxActivationPoints", "Set To [int, float, string, boolean]", Whole(shown.ActivationPoints ?? NewDivisionStart.WarnoActivationPoints)),
                Action("DivisionRule", "Set To [int, float, string, boolean]", ruleName),
                Action("CostMatrix", "Set To [int, float, string, boolean]", costName),
                Action("TypeToken", "Set To [int, float, string, boolean]", Quoted(shown.TypeToken)),
                Action("CountryId", "Set To [int, float, string, boolean]", Quoted(shown.Country)),
                Action("StandoutUnits", "Set To [list, object]", UnitList(shown.StandoutUnits)),
                // its summary and history, under tokens of its own (the export writes their texts into UNITS.csv)
                Action("SummaryTextToken", "Set To [int, float, string, boolean]", Quoted(TextToken(division, "summary"))),
                Action("HistoryTextToken", "Set To [int, float, string, boolean]", Quoted(TextToken(division, "history"))));
        // a picture of the user's own is set by its picture rule alone (EmblemRules): set here too, that rule changed
        // nothing and was listed as a rule that did not run
        if (change.EmblemFile is null) actions.Add(Action("EmblemTexture", "Set To [int, float, string, boolean]", Quoted(shown.EmblemTexture)));
        JsonObject clone = Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.NewDivision"),
            ["Kind"] = "Clone",
            ["Params"] = new JsonObject { ["Source"] = start.Descriptor, ["NewName"] = division },
            ["Actions"] = actions,
        }, DivisionsFile, division, "Clone");
        // its summary and history, for the export's UNITS.csv: never left to the game to find empty
        ((JsonObject)clone[ArmoryKey]!)[TextsKey] = new JsonObject
        {
            [TextToken(division, "summary")] = shown.Summary,
            [TextToken(division, "history")] = shown.History,
        };
        NameRule(division, shown.Name, Line(shown, "Divisions.Rule.Name", ("name", shown.Name)));
        if (change.EmblemFile is not null) EmblemRules(change, start, shown);

        Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.NewCosts"),
            ["Kind"] = "Add",
            ["Params"] = new JsonObject { ["Text"] = CostMatrixText(costName, shown.Costs), ["Name"] = "" },
        }, CostsFile, division, "Add");
        Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.DeckNumber", ("number", change.DeckNumber)),
            ["Kind"] = "Edit",
            ["Params"] = new JsonObject { ["Scope"] = "type", ["ScopeType"] = "TDeckSerializerEntries" },
            ["Actions"] = new JsonArray(Action("DivisionIds", "Insert [array]", "LastIndex: (" + division + ", " + Whole(change.DeckNumber ?? 0) + ")")),
        }, DeckNumbersFile, division, "Edit");

        Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.NewUnitList"),
            ["Kind"] = "Clone",
            ["Params"] = new JsonObject { ["Source"] = start.RuleName, ["NewName"] = ruleName },
        }, RulesFile, division, "Clone");
        if (change.Generation is DivisionGeneration generation)
        {
            GeneratedUnitListRule(change, shown, ruleName, generation);
            if (change.BattlegroupGeneration is not null) GeneratedBattlegroupRules(change, shown);
            else if (shown.Battlegroup is Battlegroup fixedCards) NewBattlegroupRules(shown, fixedCards);
            return;
        }
        Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.EmptyUnitList"),
            ["Kind"] = "Remove",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, ruleName)),
            ["Params"] = new JsonObject { ["Scope"] = "type", ["ScopeType"] = "TDeckUniteRule" },
        }, RulesFile, division, "Remove");
        foreach (DivisionUnit unit in shown.Units) AddedUnitRule(division, shown, ruleName, unit);
        if (shown.Battlegroup is Battlegroup battlegroup) NewBattlegroupRules(shown, battlegroup);
    }

    /// <summary>
    /// A generated division's units: one rule that sets its whole unit list, made by the export each time from the files
    /// on the Files tab (<see cref="GeneratedUnitLists.Fill"/>), with the user's own changes on top. The rule keeps the
    /// division's entry for that; its value is left empty until then.
    /// </summary>
    private void GeneratedUnitListRule(ModDivision change, Division shown, string ruleName, DivisionGeneration generation)
    {
        JsonObject rule = Edit(RulesFile, ruleName, Line(shown, "Divisions.Rule.GeneratedUnits", ("how", generation.Describe(texts))),
            Action("UnitRuleList", "Set To [list, object]", GeneratedUnitLists.NotYetMade), forDivision: shown.Descriptor);
        ((JsonObject)rule[ArmoryKey]!)[GeneratedDivisionKey] = change.ToJson();
    }

    /// <summary>
    /// A generated battlegroup: its deck and its cards the game does not have, both added by the export once it has
    /// made the cards from the division's units (<see cref="GeneratedBattlegroups.Fill"/>); empty until then. Each rule
    /// keeps the division's entry for that, and the deck's name token, its text kept for the export's UNITS.csv.
    /// </summary>
    private void GeneratedBattlegroupRules(ModDivision change, Division shown)
    {
        string division = shown.Descriptor;
        string token = TextToken(division, "battlegroup");
        string name = change.BattlegroupName != "" ? change.BattlegroupName : texts.Get("Divisions.Battlegroup.DefaultName", ("division", shown.Name));
        string how = texts.Get(BattlegroupGeneration.TextNamesOf(change.BattlegroupGeneration!.ReadySetting).Name);
        JsonObject deck = Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.GeneratedBattlegroup", ("name", name), ("how", how)),
            ["Kind"] = "Add",
            ["Params"] = new JsonObject { ["Text"] = "", ["Name"] = "" },
        }, DecksFile, division, "Add");
        ((JsonObject)deck[ArmoryKey]!)[TextsKey] = new JsonObject { [token] = name };
        ((JsonObject)deck[ArmoryKey]!)[GeneratedBattlegroupKey] = GeneratedBattlegroups.PartJson(change, GeneratedBattlegroups.DeckPart, token);
        JsonObject cards = Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.GeneratedCards", ("name", name)),
            ["Kind"] = "Add",
            ["Params"] = new JsonObject { ["Text"] = "", ["Name"] = "" },
        }, CardsFile, division, "Add");
        ((JsonObject)cards[ArmoryKey]!)[GeneratedBattlegroupKey] = GeneratedBattlegroups.PartJson(change, GeneratedBattlegroups.CardsPart, token);
    }

    /// <summary>A new division's costs, written as the game writes them, its categories A to Z.</summary>
    private static string CostMatrixText(string name, IReadOnlyDictionary<string, IReadOnlyList<int>> costs) =>
        name + " is MAP\n[\n" + string.Concat(costs.OrderBy(cost => cost.Key, StringComparer.Ordinal)
            .Select(cost => "    (Factory/" + cost.Key + ", [" + Numbers(cost.Value) + "]),\n")) + "]";

    // ---- the Official Battlegroup ----

    /// <summary>
    /// A game division's Official Battlegroup changed: its name, a rule giving its deck a name token of its own (its
    /// text kept for the export's UNITS.csv); its cards, the cards the game does not have added to DeckPacks.ndf, and
    /// the deck's list of cards set whole, as their order is the battlegroup's.
    /// </summary>
    private void BattlegroupRules(ModDivision change, Division game, Division shown)
    {
        if (game.Battlegroup is not Battlegroup before || shown.Battlegroup is not Battlegroup after) return;
        string division = shown.Descriptor;
        changeBeingWritten = "BattlegroupName";
        if (change.BattlegroupName != "" && change.BattlegroupName != before.Name)
        {
            string token = TextToken(division, "battlegroup");
            JsonObject rule = Edit(DecksFile, before.Descriptor, Line(shown, "Divisions.Rule.BattlegroupName", ("from", before.Name), ("to", after.Name)),
                [Action("DeckName", "Set To [int, float, string, boolean]", Quoted(token))], forDivision: division);
            ((JsonObject)rule[ArmoryKey]!)[TextsKey] = new JsonObject { [token] = after.Name };
        }
        changeBeingWritten = "Battlegroup";
        if (change.BattlegroupCards is not null && !change.BattlegroupCards.SequenceEqual(before.Cards))
        {
            NewCardRules(shown, after.Cards);
            Edit(DecksFile, before.Descriptor, Line(shown, "Divisions.Rule.Battlegroup", ("from", before.Cards.Count), ("to", after.Cards.Count)),
                [Action("DeckPackList", "Set To [list, object]", CardList(after.Cards))], forDivision: division);
        }
    }

    /// <summary>
    /// A new division's Official Battlegroup, once it has cards: the cards the game does not have, and the deck, added
    /// with a name token of its own (its text kept for the export's UNITS.csv).
    /// </summary>
    private void NewBattlegroupRules(Division shown, Battlegroup battlegroup)
    {
        string division = shown.Descriptor;
        NewCardRules(shown, battlegroup.Cards);
        string token = TextToken(division, "battlegroup");
        string name = battlegroup.Name != "" ? battlegroup.Name : texts.Get("Divisions.Battlegroup.DefaultName", ("division", shown.Name));
        JsonObject rule = Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.NewBattlegroup", ("name", name), ("cards", battlegroup.Cards.Count)),
            ["Kind"] = "Add",
            ["Params"] = new JsonObject { ["Text"] = DeckText(battlegroup.Descriptor, division, token, battlegroup.Cards), ["Name"] = "" },
        }, DecksFile, division, "Add");
        ((JsonObject)rule[ArmoryKey]!)[TextsKey] = new JsonObject { [token] = name };
    }

    /// <summary>Each card the game's DeckPacks.ndf does not have, added to it once, written as the game writes its own.</summary>
    private void NewCardRules(Division shown, IEnumerable<BattlegroupCard> cards)
    {
        foreach (BattlegroupCard card in cards)
        {
            if (!cardsInFile.Add(card.PackName)) continue;
            string title = card.Transport is string transport
                ? Line(shown, card.Unit, "Divisions.Rule.NewCardInTransport", ("level", card.Veterancy), ("transport", unitName(transport)))
                : Line(shown, card.Unit, "Divisions.Rule.NewCard", ("level", card.Veterancy));
            Rule(new JsonObject
            {
                ["Name"] = title,
                ["Kind"] = "Add",
                ["Params"] = new JsonObject { ["Text"] = CardText(card), ["Name"] = "" },
            }, CardsFile, shown.Descriptor, "Add");
        }
    }

    /// <summary>A card of DeckPacks.ndf: its veterancy (left out at the lowest, as the game does), transport, unit and count.</summary>
    internal static string CardText(BattlegroupCard card)
    {
        var lines = new List<string> { card.PackName + " is DeckPackDescriptor", "(" };
        if (card.Veterancy > 0) lines.Add("    Xp = " + Whole(card.Veterancy));
        if (card.Transport is string transport) lines.Add("    Transport = " + UnitReference(transport));
        lines.Add("    Unit = " + UnitReference(card.Unit));
        lines.Add("    Number = 1");
        lines.Add(")");
        return string.Join("\n", lines);
    }

    /// <summary>A new division's deck in Decks.ndf, written as the game writes its own.</summary>
    internal static string DeckText(string deck, string division, string nameToken, IEnumerable<BattlegroupCard> cards) =>
        "export " + deck + " is TDeckDescriptor\n(\n    DeckDivision = $/GFX/Division/" + division + "\n    DeckName = " + Quoted(nameToken)
        + "\n    DeckPackList =\n    [\n" + string.Concat(cards.Select(card => "        ~/" + card.PackName + ",\n")) + "    ]\n)";

    /// <summary>A deck's list of cards: [~/Descriptor_Deck_Pack_A, ~/Descriptor_Deck_Pack_B].</summary>
    private static string CardList(IEnumerable<BattlegroupCard> cards) => "[" + string.Join(", ", cards.Select(card => "~/" + card.PackName)) + "]";

    // ---- names ----

    /// <summary>
    /// A name: a rule giving the division a name token of its own, its text kept for the export's UNITS.csv. The
    /// game's own token is not reused, so the game's text for it stays as it is.
    /// </summary>
    private void NameRule(string division, string name, string title)
    {
        string token = NameToken(division);
        JsonObject rule = Edit(DivisionsFile, division, title,
            Action("DivisionName", "Set To [int, float, string, boolean]", "'" + token + "'"),
            Action("DescriptionHintTitleToken", "Set To [int, float, string, boolean]", "'" + token + "'"));
        Shows(rule, "Name");
        ((JsonObject)rule[ArmoryKey]!)[NameTextKey] = name;
    }

    /// <summary>
    /// A text the change gives a game division (its summary or history): a rule giving it a token of its own, the text
    /// kept for the export's UNITS.csv. The game's token is not reused, so the game's text for it stays as it is.
    /// </summary>
    private void TextRule(string? text, string was, string division, Division shown, string kind, string target, string change, string title)
    {
        if (text is null || text == was) return;
        changeBeingWritten = change;
        string token = TextToken(division, kind);
        JsonObject rule = Edit(DivisionsFile, division, Line(shown, title), Action(target, "Set To [int, float, string, boolean]", Quoted(token)));
        ((JsonObject)rule[ArmoryKey]!)[TextsKey] = new JsonObject { [token] = text };
    }

    /// <summary>
    /// A division's name token, as the game's are: ten capital letters, made from its descriptor, so it is the same
    /// at every export.
    /// </summary>
    public static string NameToken(string division) => TextToken(division, "name");

    /// <summary>A token for one of a division's texts ("name", "summary", "history"), made as <see cref="NameToken"/> is.</summary>
    public static string TextToken(string division, string text)
    {
        string hex = StableGuid.From(division + "|" + text).Replace("GUID:{", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        // each hexadecimal digit as a letter from A to P
        return "EZ" + string.Concat(hex.Take(8).Select(digit => (char)('A' + Convert.ToInt32(digit.ToString(), 16))));
    }

    // ---- making rules ----

    /// <summary>A group heading for a division's rules.</summary>
    private void Group(string divisionName) => rules.Add(new JsonObject
    {
        ["Name"] = divisionName,
        ["Kind"] = "Group",
        ["Enabled"] = true,
        ["LogicGate"] = "All",
        ["Conditions"] = new JsonArray(),
        ["Actions"] = new JsonArray(),
        ["Params"] = new JsonObject(),
        [ArmoryKey] = new JsonObject { [ShowsKey] = "Group" },
    });

    /// <summary>An Edit rule on one descriptor, by its title, for one file; it is returned to add to.</summary>
    private JsonObject Edit(string file, string descriptor, string name, params JsonObject[] actions) =>
        Edit(file, descriptor, name, actions, forDivision: null);

    private JsonObject Edit(string file, string descriptor, string name, JsonObject action, string forDivision) =>
        Edit(file, descriptor, name, [action], forDivision);

    private JsonObject Edit(string file, string descriptor, string name, JsonObject[] actions, string? forDivision) =>
        Rule(new JsonObject
        {
            ["Name"] = name,
            ["Kind"] = "Edit",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, descriptor)),
            ["Actions"] = new JsonArray(actions),
        }, file, forDivision ?? descriptor, "Edit");

    /// <summary>Adds a rule with what every rule has, and what the Armory knows of it; it is returned to add to.</summary>
    private JsonObject Rule(JsonObject rule, string file, string division, string shows)
    {
        rule["Enabled"] = true;
        rule["LogicGate"] = "All";
        rule["Conditions"] ??= new JsonArray();
        rule["Actions"] ??= new JsonArray();
        rule["Params"] ??= new JsonObject();
        rule[ArmoryKey] = new JsonObject { [FileKey] = file, [DivisionKey] = division, [ShowsKey] = shows, [ChangeKey] = changeBeingWritten, [LockedKey] = true };
        rules.Add(rule);
        return rule;
    }

    private static void Shows(JsonObject rule, string what) => ((JsonObject)rule[ArmoryKey]!)[ShowsKey] = what;

    /// <summary>A condition that a value is equal to another, as the mod file keeps it.</summary>
    internal static JsonObject Condition(string operand, string value) => new()
    {
        ["Name"] = "", ["Operand"] = operand, ["Operator"] = "Is Equal To", ["Value"] = value, ["ManualOverride"] = false, ["CustomOperand"] = "",
    };

    /// <summary>An action, without a comment, as the mod file keeps it.</summary>
    internal static JsonObject Action(string target, string op, string value) => new()
    {
        ["Name"] = "", ["Target"] = target, ["Operator"] = op, ["Value"] = value, ["ManualOverride"] = false, ["CustomTarget"] = "",
        ["Comment"] = new JsonObject { ["Enabled"] = false, ["Text"] = "", ["IncludeTimestamp"] = false, ["IncludeOriginalValue"] = false, ["IncludeMod"] = false },
    };

    // ---- writing values ----

    /// <summary>A unit's whole line in a division's list, on one line.</summary>
    internal static string UnitLine(DivisionUnit unit)
    {
        var parts = new List<string>
        {
            "UnitDescriptor = " + UnitReference(unit.Unit),
            "AvailableWithoutTransport = " + (unit.AvailableWithoutTransport ? "True" : "False"),
        };
        if (unit.Transports.Count > 0) parts.Add("AvailableTransportList = " + UnitList(unit.Transports));
        parts.Add("MaxPackNumber = " + Whole(unit.Cards));
        parts.Add("NumberOfUnitInPack = " + Whole(unit.UnitsPerCard ?? 1));
        parts.Add("NumberOfUnitInPackXPMultiplier = [" + Decimals(unit.VeterancyMultipliers) + "]");
        return "TDeckUniteRule ( " + string.Join(" ", parts) + " )";
    }

    /// <summary>A unit as the division files point at it: $/GFX/Unit/Descriptor_Unit_X.</summary>
    private static string UnitReference(string unit) => "$/GFX/Unit/" + unit[(unit.LastIndexOf('/') + 1)..];

    private static string UnitList(IEnumerable<string> units) => "[" + string.Join(", ", units.Select(UnitReference)) + "]";

    /// <summary>
    /// The division's tags with its nation, side and type in place of those it started from, the rest kept:
    /// ['DEFAULT', 'US', 'NATO', 'ARMORED']. A type of none leaves no type tag.
    /// </summary>
    private static string TagsWithNation(Division before, Division shown) =>
        TagsText(ModDivision.TagsWith(before, shown.Country, shown.Side, shown.TypeToken));

    /// <summary>Tags as the game writes them: ['DEFAULT', 'US', 'NATO'].</summary>
    private static string TagsText(IEnumerable<string> tags) => "[" + string.Join(", ", tags.Select(tag => "'" + tag + "'")) + "]";

    private static string Quoted(string text) => "\"" + text + "\"";
    private static string Whole(int? number) => (number ?? 0).ToString(CultureInfo.InvariantCulture);
    private static string Numbers(IEnumerable<int> numbers) => string.Join(", ", numbers.Select(number => Whole(number)));

    /// <summary>A number as the game writes decimals: always with one decimal at least, "1.0", "0.34".</summary>
    private static string Decimal(double? number) => (number ?? 0).ToString("0.0##############", CultureInfo.InvariantCulture);
    private static string Decimals(IEnumerable<double> numbers) => string.Join(", ", numbers.Select(number => Decimal(number)));

    private string Names(IEnumerable<string> units) => units.Any() ? string.Join(", ", units.Select(unitName)) : "–";
    private string YesOrNo(bool value) => texts.Get(value ? "Divisions.Rule.Yes" : "Divisions.Rule.No");

    /// <summary>A division's descriptor without its "Descriptor_Deck_Division_" and "_multi": "US_3rd_Arm".</summary>
    private static string CoreName(string division)
    {
        string core = division.StartsWith("Descriptor_Deck_Division_", StringComparison.Ordinal) ? division["Descriptor_Deck_Division_".Length..] : division;
        return core.EndsWith("_multi", StringComparison.Ordinal) ? core[..^"_multi".Length] : core;
    }

    // ---- naming rules ----

    /// <summary>A rule's name for a change to a division: "16DE PANTSERDIVISION: activation points 45 → 50".</summary>
    private string Line(Division division, string what, params (string Name, object? Value)[] blanks) =>
        division.Name + ": " + texts.Get(what, blanks);

    /// <summary>A rule's name for a change to a unit in a division: "16DE PANTSERDIVISION · PIONIERS: cards 2 → 3".</summary>
    private string Line(Division division, string unit, string what, params (string Name, object? Value)[] blanks) =>
        division.Name + " · " + unitName(unit) + ": " + texts.Get(what, blanks);
}
