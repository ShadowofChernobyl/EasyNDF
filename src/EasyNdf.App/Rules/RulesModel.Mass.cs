using System.Text.Json.Nodes;
using EasyNdf.App.Text;

namespace EasyNdf.App.Rules;

/// <summary>
/// Several rules configured as one in the rule window (see <see cref="Mixed"/>): while it is open on them, the open rule
/// is one made of them all, which is not among the rule set's rules; Done writes what was changed into each.
/// </summary>
public sealed partial class RulesModel
{
    /// <summary>The rule made of the rules being configured together; null otherwise.</summary>
    private JsonObject? mergedRule;

    /// <summary>The rules being configured together.</summary>
    private List<JsonObject> mergedFrom = [];

    /// <summary>The merged rule's rows as it was made, to tell which were moved, added or deleted.</summary>
    private Dictionary<JsonObject, int> mergedRowPlaces = [];

    /// <summary>Whether the rule window is open on several rules.</summary>
    public bool IsConfiguringSeveral => mergedRule is not null;

    /// <summary>How many rules the rule window is open on, when on several.</summary>
    public int SeveralCount => mergedFrom.Count;

    /// <summary>The selected rules that can be configured here: not those the Armory made.</summary>
    public IReadOnlyList<int> RulesToConfigure => [.. SelectedRules.Where(place => !IsLocked(place) && Rules[place] is JsonObject)];

    /// <summary>The rule window opens on the selected rules as one. False when fewer than two can be configured.</summary>
    public bool StartEditingSeveral()
    {
        List<JsonObject> rules = [.. RulesToConfigure.Select(place => (JsonObject)Rules[place]!)];
        if (rules.Count < 2) return false;
        mergedFrom = rules;
        mergedRule = Mixed.Merge(rules);
        if (mergedRule["Params"] is not JsonObject) mergedRule["Params"] = new JsonObject();
        mergedRowPlaces = Mixed.RowPlaces(mergedRule);
        StartEditingRule();
        return true;
    }

    /// <summary>Done on several rules: what was changed is written into each that is still in the rule set, and saved.</summary>
    private void FinishEditingSeveral(JsonObject before)
    {
        JsonObject merged = mergedRule!;
        List<JsonObject> rules = [.. mergedFrom.Where(rule => Rules.Any(each => ReferenceEquals(each, rule)))];
        Dictionary<JsonObject, int> places = mergedRowPlaces;
        ForgetSeveral();
        if (rules.Count == 0 || JsonNode.DeepEquals(merged, before)) { Refresh(); return; }
        Mixed.Apply(before, merged, rules, places);
        SaveRules();
        Say(Texts.Get("Rules.Mass.Changed", ("what", CountRules(rules.Count))));
    }

    private void ForgetSeveral()
    {
        mergedRule = null;
        mergedFrom = [];
        mergedRowPlaces = [];
    }

    /// <summary>
    /// Rows that stand for rows that differ cannot be copied: a copy would not know what to hold in each rule.
    /// </summary>
    /// <returns>True when refused, so the caller stops.</returns>
    private bool RefusedForMixedRows(string list)
    {
        if (RowsOf(list) is not JsonArray rows || !SelectedRowsOf(list).Any(place => Mixed.Has(rows[place]))) return false;
        Say(Texts.Get("Rules.Mass.NoCopy"), NoticeKind.Caution);
        return true;
    }
}
