using System.Globalization;
using System.Text.Json.Nodes;
using EasyNdf.Data.Game;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// The rules a mod's changes to WARNO's Armory tabs become (<see cref="ModTab"/>, <see cref="Mod.TabOrder"/>), each for
/// one file (<see cref="DivisionChangeRules.FileKey"/>), as the game was seen to need them:
/// <list type="bullet">
/// <item>the tab's place in the order, in FactoryResources.ndf and the four screens that list the tabs;</item>
/// <item>its name and tooltip, in FactoryResources.ndf: a description added for a tab the game has none for (Support,
/// AT, Vehicles), or the game's own changed; the texts themselves go into the dictionary the Armory's texts go into;</item>
/// <item>a cost row in DivisionCostMatrix.ndf for each division that offers a unit of the tab and has no row for it,
/// with a slot for each pack of the tab in the division's ready-made deck: without the row the game crashes as it
/// generates the mod (<see cref="TabRows"/>).</item>
/// </list>
/// The game knows twelve tab names and no others (a new name stops the mod's generation: "object not found").
/// </summary>
public static class TabChangeRules
{
    /// <summary>The tabs WARNO shows, in its order.</summary>
    public static IReadOnlyList<string> GameTabs { get; } = ["Logistic", "Infantry", "Art", "Tanks", "Recons", "DCA", "Helis", "Planes"];

    /// <summary>The tabs WARNO's files name and shows none for, which a mod can bring in.</summary>
    public static IReadOnlyList<string> SpareTabs { get; } = ["Support", "AT", "Vehicles", "Defense"];

    /// <summary>The name token the game gives each tab it has a description for (FactoryName in FactoryResources.ndf).</summary>
    private static readonly Dictionary<string, string> GameNameTokens = new(StringComparer.Ordinal)
    {
        ["Logistic"] = "LOGISTIC", ["Art"] = "ARTILLERY", ["Infantry"] = "INFANTRY", ["Tanks"] = "TANK", ["Recons"] = "RECON",
        ["DCA"] = "ANTIAIR", ["Helis"] = "HELO", ["Planes"] = "AIR", ["Defense"] = "DEFENSE",
    };

    /// <summary>The middle of the tooltip tokens the game gives each tab it has a description for: "HPD_LOGT" is Logistic's tooltip title.</summary>
    private static readonly Dictionary<string, string> GameHintStems = new(StringComparer.Ordinal)
    {
        ["Logistic"] = "LOG", ["Art"] = "ART", ["Infantry"] = "INF", ["Tanks"] = "TNK", ["Recons"] = "RECO",
        ["DCA"] = "DCA", ["Helis"] = "HELO", ["Planes"] = "AIR", ["Defense"] = "DEF",
    };

    /// <summary>
    /// The game's own tokens for a tab's label and its tooltip's title, text and longer text, for showing the texts a
    /// mod has not changed; null for a tab the game has no description for.
    /// </summary>
    public static (string Label, string Title, string Text, string More)? GameTokensOf(string factory) =>
        GameNameTokens.TryGetValue(factory, out string? name) && GameHintStems.TryGetValue(factory, out string? stem)
            ? (name, "HPD_" + stem + "T", "HPD_" + stem + "B", "HPD_" + stem + "E") : null;

    /// <summary>Every token <see cref="GameTokensOf"/> answers, for keeping their texts as the game's are read.</summary>
    public static IEnumerable<string> GameTokens() =>
        GameNameTokens.Keys.Select(GameTokensOf).OfType<(string Label, string Title, string Text, string More)>().SelectMany(each => new[] { each.Label, each.Title, each.Text, each.More });

    /// <summary>The files that list the tabs in order: the Armory's screen starts with the tab of every unit.</summary>
    private static readonly (string File, bool StartsWithAll)[] OrderFiles =
    [
        ("FactoryResources.ndf", false), ("UISpecificShowRoomArmoryComponent.ndf", true), ("UISpecificShowRoomDeckCreatorScreenComponent.ndf", false),
        ("UISpecificBattlegroupsListView.ndf", false), ("UISpecificShowRoomGroupsDeckCreatorScreenView.ndf", false),
    ];

    /// <summary>Whether a file, by its name, is one of the five that list the tabs in order, which the tab rules write to.</summary>
    public static bool ListsTabs(string fileName) => OrderFiles.Any(each => string.Equals(each.File, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The cost row a new tab's divisions get unless told otherwise.</summary>
    public static IReadOnlyList<int> UsualSlots { get; } = [1, 1, 2, 3];

    /// <summary>Whether the game has a description (a name and tooltip) for a tab already.</summary>
    public static bool HasGameDescription(string factory) => GameNameTokens.ContainsKey(factory);

    /// <summary>The tabs in the order the mod gives them: the game's, then the spare ones it uses, unless it sets an order of its own.</summary>
    /// <param name="inUse">The tabs units are in, for the spare ones to be listed.</param>
    public static List<string> OrderOf(Mod mod, IEnumerable<string> inUse)
    {
        HashSet<string> used = [.. inUse, .. mod.Tabs.Select(tab => tab.Factory)];
        List<string> all = [.. GameTabs, .. SpareTabs.Where(used.Contains)];
        if (mod.TabOrder is not { Count: > 0 } wanted) return all;
        return [.. wanted.Where(all.Contains).Distinct(), .. all.Where(tab => !wanted.Contains(tab))];
    }

    /// <summary>A text's token for a tab: "EZT_SUPP_N" for Support's name; T, B and E for the tooltip's title, text and longer text.</summary>
    public static string Token(string factory, char text) => "EZT_" + factory.ToUpperInvariant()[..Math.Min(4, factory.Length)] + "_" + text;

    /// <summary>The rules for a mod's tab changes; none for another game than WARNO, or a mod that changes no tab.</summary>
    /// <param name="mod">The mod.</param>
    /// <param name="divisions">The divisions, as the mod leaves them.</param>
    /// <param name="tabOf">Each unit's tab as the mod leaves it, by its descriptor.</param>
    /// <param name="groupName">The heading the rules go under ("Armory Tabs").</param>
    /// <param name="ruleName">A rule's name, from what it does and the tab: ("Order", ""), ("Name", "Support"), ("Row", "Support").</param>
    public static JsonArray For(Mod mod, IReadOnlyList<Division> divisions, IReadOnlyDictionary<string, string> tabOf, string groupName, Func<string, string, string> ruleName)
    {
        var rules = new JsonArray();
        if (mod.Game != GameTitle.Warno) return rules;
        // a spare tab is in the order once the mod has it (Move to Tab brings it in), not for a unit some rule moved there
        List<string> order = OrderOf(mod, []);
        bool orderChanged = !order.SequenceEqual(GameTabs);
        if (mod.Tabs.Count == 0 && !orderChanged) return rules;

        if (orderChanged)
            foreach (var (file, startsWithAll) in OrderFiles)
            {
                // the game's own list keeps Defense, which it names last and shows no tab for
                IEnumerable<string> listed = file == "FactoryResources.ndf" && !order.Contains("Defense") ? order.Append("Defense") : order;
                if (startsWithAll) listed = listed.Prepend("UniversalFactory");
                rules.Add(Rule(ruleName("Order", ""), file, "Order", [], [Action("FactoryDisplayOrder", "Set To [list, object]", "[" + string.Join(", ", listed.Select(tab => "Factory/" + tab)) + "]")]));
            }

        foreach (ModTab tab in mod.Tabs)
        {
            var texts = new JsonObject();
            string Give(char which, string text)
            {
                string token = Token(tab.Factory, which);
                texts[token] = text;
                return token;
            }
            if (!HasGameDescription(tab.Factory))
            {
                // a description of its own: every text is given, an empty one from the one before it
                string label = tab.Label ?? tab.Factory.ToUpperInvariant()[..Math.Min(3, tab.Factory.Length)];
                string title = tab.TipTitle is { Length: > 0 } ? tab.TipTitle : label;
                string text = tab.TipText is { Length: > 0 } ? tab.TipText : title;
                string more = tab.TipMore is { Length: > 0 } ? tab.TipMore : text;
                string description = "(Factory/" + tab.Factory + ", TSpecificFactoryDescription(FactoryName = \"" + Give('N', label) + "\" FactoryHintTitle = \"" + Give('T', title)
                    + "\" FactoryHintBody = \"" + Give('B', text) + "\" FactoryHintExtended = \"" + Give('E', more) + "\"))";
                JsonObject rule = Rule(ruleName("Name", tab.Factory), "FactoryResources.ndf", "Tab:" + tab.Factory, [], [Action("FactoryDescriptions", "Insert [array]", "LastIndex: " + description)]);
                rule[DivisionChangeRules.ArmoryKey]![DivisionChangeRules.TextsKey] = texts;
                rules.Add(rule);
            }
            else if (tab.Label is not null || tab.TipTitle is not null || tab.TipText is not null || tab.TipMore is not null)
            {
                // the game's own description, found by its name token: only what is given is changed
                var actions = new List<JsonObject>();
                if (tab.TipTitle is string title) actions.Add(Action("FactoryHintTitle", "Set To [int, float, string, boolean]", "\"" + Give('T', title) + "\""));
                if (tab.TipText is string text) actions.Add(Action("FactoryHintBody", "Set To [int, float, string, boolean]", "\"" + Give('B', text) + "\""));
                if (tab.TipMore is string more) actions.Add(Action("FactoryHintExtended", "Set To [int, float, string, boolean]", "\"" + Give('E', more) + "\""));
                // the name last: the rule finds the description by it
                if (tab.Label is string label) actions.Add(Action("FactoryName", "Set To [int, float, string, boolean]", "\"" + Give('N', label) + "\""));
                JsonObject rule = Rule(ruleName("Name", tab.Factory), "FactoryResources.ndf", "Tab:" + tab.Factory,
                    [Condition("FactoryName", "Is Equal To", "\"" + GameNameTokens[tab.Factory] + "\"")], actions, scopeType: "TSpecificFactoryDescription");
                rule[DivisionChangeRules.ArmoryKey]![DivisionChangeRules.TextsKey] = texts;
                rules.Add(rule);
            }

            if (tab.Slots is not { Count: > 0 } && tab.SlotsFor is not { Count: > 0 }) continue;
            // one row for each cost matrix whose divisions offer a unit of the tab and have no row for it
            foreach (var sameMatrix in divisions.Where(division => division.CostMatrixName != "" && !division.Costs.ContainsKey(tab.Factory)
                         && division.Units.Any(unit => tabOf.GetValueOrDefault(unit.Unit) == tab.Factory)).GroupBy(division => division.CostMatrixName))
            {
                // a slot for each pack of the tab in a ready-made deck, the last cost again for those beyond the row
                int packs = sameMatrix.Max(division => division.Battlegroup?.Cards.Count(card => tabOf.GetValueOrDefault(card.Unit) == tab.Factory) ?? 0);
                // a division's own row, when the tab was edited with it shown; divisions sharing a cost matrix share it
                List<int>? slots = sameMatrix.Select(division => tab.SlotsFor?.GetValueOrDefault(division.Descriptor)).FirstOrDefault(own => own is { Count: > 0 }) ?? tab.Slots;
                if (slots is not { Count: > 0 }) continue;
                List<int> row = [.. slots];
                while (row.Count < packs) row.Add(row[^1]);
                JsonObject rule = Rule(ruleName("Row", tab.Factory) + ": " + string.Join(", ", sameMatrix.Select(division => division.Name)), "DivisionCostMatrix.ndf", "TabRow:" + tab.Factory,
                    [], [Action(sameMatrix.Key, "Insert [array]", "LastIndex: (Factory/" + tab.Factory + ", [" + string.Join(", ", row.Select(cost => cost.ToString(CultureInfo.InvariantCulture))) + "])")], scope: "constants");
                rules.Add(rule);
            }
        }
        if (rules.Count == 0) return rules;
        rules.Insert(0, new JsonObject
        {
            ["Name"] = groupName, ["Kind"] = "Group", ["Enabled"] = true, ["LogicGate"] = "All", ["Conditions"] = new JsonArray(), ["Actions"] = new JsonArray(),
            ["Params"] = new JsonObject(), [DivisionChangeRules.ArmoryKey] = new JsonObject { [DivisionChangeRules.ShowsKey] = "Group" },
        });
        return rules;
    }

    private static JsonObject Rule(string name, string file, string change, List<JsonObject> conditions, List<JsonObject> actions, string? scope = null, string? scopeType = null)
    {
        var parameters = new JsonObject();
        if (scopeType is not null) { parameters["Scope"] = "type"; parameters["ScopeType"] = scopeType; }
        else if (scope is not null) parameters["Scope"] = scope;
        return new JsonObject
        {
            ["Name"] = name, ["Kind"] = "Edit",
            ["Conditions"] = new JsonArray([.. conditions]), ["Actions"] = new JsonArray([.. actions]),
            ["Enabled"] = true, ["LogicGate"] = conditions.Count > 0 ? "Any" : "All", ["Params"] = parameters,
            [DivisionChangeRules.ArmoryKey] = new JsonObject
            {
                [DivisionChangeRules.FileKey] = file, [DivisionChangeRules.ShowsKey] = "Edit", [DivisionChangeRules.ChangeKey] = change, [DivisionChangeRules.LockedKey] = true,
            },
        };
    }

    private static JsonObject Condition(string operand, string op, string value) => new()
    {
        ["Name"] = "", ["Operand"] = operand, ["Operator"] = op, ["Value"] = value, ["ManualOverride"] = false, ["CustomOperand"] = "",
    };

    private static JsonObject Action(string target, string op, string value) => new()
    {
        ["Name"] = "", ["Target"] = target, ["Operator"] = op, ["Value"] = value, ["ManualOverride"] = false, ["CustomTarget"] = "",
        ["Comment"] = new JsonObject { ["Enabled"] = false, ["Text"] = "", ["IncludeTimestamp"] = false, ["IncludeOriginalValue"] = false, ["IncludeMod"] = false },
    };
}
