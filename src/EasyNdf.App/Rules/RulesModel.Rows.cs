using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Engine.Mods;
using EasyNdf.App.Text;

namespace EasyNdf.App.Rules;

/// <summary>
/// The open rule's lists of rows, in the rule window: its conditions ("c"), a Lookup rule's conditions on the other file
/// ("s") and its actions ("a"). These are the HTML app's shared list operations (<c>opMove</c>, <c>opDelete</c>...) for
/// those lists; each list has its own selection, which belongs to the open rule.
/// </summary>
public sealed partial class RulesModel
{
    /// <summary>The list of a rule's conditions.</summary>
    public const string Conditions = "c";

    /// <summary>The list of a Lookup rule's conditions on the other file.</summary>
    public const string SourceConditions = "s";

    /// <summary>The list of a rule's actions.</summary>
    public const string Actions = "a";

    /// <summary>The rule types that have conditions, and those that have actions (the HTML app's HAS_CONDS and HAS_ACTS).</summary>
    public static readonly IReadOnlySet<string> KindsWithConditions = new HashSet<string> { "Edit", "Remove", "Replace", "Scale", "Formula", "Lookup", "Check", "Group" };
    public static readonly IReadOnlySet<string> KindsWithActions = new HashSet<string> { "Edit", "Clone", "Lookup" };

    /// <summary>What the lists say when the user acts on a selection they have not made.</summary>
    private static string SelectRowsFirst => Texts.Get("Rules.Rows.SelectFirst");

    private readonly Dictionary<string, HashSet<int>> selectedRows = new() { [Conditions] = [], [SourceConditions] = [], [Actions] = [] };
    private readonly Dictionary<string, int> rowAnchor = new() { [Conditions] = -1, [SourceConditions] = -1, [Actions] = -1 };

    /// <summary>The rule whose rows are selected; another rule starts with none selected.</summary>
    private JsonNode? rowsBelongTo;

    /// <summary>The rule shown in the rule window: the current rule, or null when the preset has none.</summary>
    public JsonObject? OpenRule => CurrentRule < Rules.Count ? Rules[CurrentRule] as JsonObject : null;

    /// <summary>The list of rows the keys act on in the rule window, the one clicked last; null for none.</summary>
    public string? ActiveRowList { get; set; }

    /// <summary>The rows of a list of the open rule, or null when the rule has no such list.</summary>
    public JsonArray? RowsOf(string list) => OpenRule is not JsonObject rule ? null : list switch
    {
        Conditions => rule["Conditions"] as JsonArray,
        SourceConditions => rule["Params"]?["SourceConditions"] as JsonArray,
        Actions => rule["Actions"] as JsonArray,
        _ => null,
    };

    /// <summary>The selected rows of a list, in order.</summary>
    public IReadOnlyList<int> SelectedRowsOf(string list)
    {
        int count = RowsOf(list)?.Count ?? 0;
        return [.. selectedRows[list].Where(i => i >= 0 && i < count).Order()];
    }

    /// <summary>Whether a row is selected.</summary>
    public bool IsRowSelected(string list, int index) => selectedRows[list].Contains(index);

    /// <summary>"Condition" or "Action": what a list holds, as the clipboard names it.</summary>
    public static string RowType(string list) => list == Actions ? "Action" : "Condition";

    /// <summary>"3 conditions", "1 action".</summary>
    public static string CountRows(string list, int count) => list == Actions ? Texts.Count("Rules.Rows.ActionCount", count) : Texts.Count("Rules.Rows.ConditionCount", count);

    /// <summary>Forgets the row selections when another rule is open (the HTML app's <c>lastR</c>).</summary>
    private void KeepRowSelectionsFor(JsonNode? rule)
    {
        if (ReferenceEquals(rule, rowsBelongTo)) return;
        rowsBelongTo = rule;
        foreach (HashSet<int> selection in selectedRows.Values) selection.Clear();
        foreach (string list in rowAnchor.Keys.ToList()) rowAnchor[list] = -1;
    }

    /// <summary>
    /// A press on a row of a list, or on the list's empty part (index -1): selects it alone; with Ctrl, adds it to the
    /// selection or takes it out; with Shift, selects from the row pressed last. A right-click on a selected row keeps the
    /// selection, for the menu; one on the empty part keeps it too.
    /// </summary>
    public void PressRow(string list, int index, bool control, bool shift, bool isRightButton)
    {
        ActiveRowList = list;
        HashSet<int> selection = selectedRows[list];
        if (index < 0)
        {
            if (!isRightButton && !control && !shift) { selection.Clear(); rowAnchor[list] = -1; }
        }
        else if (isRightButton && selection.Contains(index)) { }
        else if (shift && rowAnchor[list] >= 0)
        {
            selection.Clear();
            for (int i = Math.Min(rowAnchor[list], index); i <= Math.Max(rowAnchor[list], index); i++) selection.Add(i);
        }
        else if (control)
        {
            if (!selection.Remove(index)) selection.Add(index);
            rowAnchor[list] = index;
        }
        else
        {
            selection.Clear();
            selection.Add(index);
            rowAnchor[list] = index;
        }
        Changed();
    }

    /// <summary>
    /// Up, Down, Home and End in a list: the selection moves to the next row, the previous one, the first or the last;
    /// with Shift it grows from the row pressed last. Returns the row now at the cursor, or -1.
    /// </summary>
    public int StepRow(string list, int step, bool toEnd, bool shift)
    {
        JsonArray? rows = RowsOf(list);
        if (rows is null || rows.Count == 0) return -1;
        IReadOnlyList<int> picked = SelectedRowsOf(list);
        int from = picked.Count > 0 ? (step > 0 ? picked[^1] : picked[0]) : (step > 0 ? -1 : rows.Count);
        int next = toEnd ? (step > 0 ? rows.Count - 1 : 0) : Math.Clamp(from + step, 0, rows.Count - 1);
        HashSet<int> selection = selectedRows[list];
        selection.Clear();
        if (shift && rowAnchor[list] >= 0)
            for (int i = Math.Min(rowAnchor[list], next); i <= Math.Max(rowAnchor[list], next); i++) selection.Add(i);
        else
        {
            selection.Add(next);
            rowAnchor[list] = next;
        }
        ActiveRowList = list;
        Changed();
        return next;
    }

    /// <summary>Ctrl+A in a list: every row.</summary>
    public void SelectAllRows(string list)
    {
        if (RowsOf(list) is not JsonArray rows) return;
        selectedRows[list].Clear();
        for (int i = 0; i < rows.Count; i++) selectedRows[list].Add(i);
        Changed();
    }

    /// <summary>The + button and "Create New Condition": a new row below the selected one (or at the end), selected. Returns its place.</summary>
    public int AddRow(string list)
    {
        if (IsOpenRuleLocked) { Say(Texts.Get("Rules.Locked.Refused")); return -1; }
        if (RowsOf(list) is not JsonArray rows) return -1;
        IReadOnlyList<int> picked = SelectedRowsOf(list);
        int at = picked.Count > 0 ? picked[^1] + 1 : rows.Count;
        rows.Insert(at, list == Actions ? RuleLoading.NewAction() : RuleLoading.NewCondition());
        SelectOnlyRow(list, at);
        ActiveRowList = list;
        SaveRules();
        return at;
    }

    /// <summary>Selects one row alone.</summary>
    public void SelectOnlyRow(string list, int index)
    {
        selectedRows[list].Clear();
        selectedRows[list].Add(index);
        rowAnchor[list] = index;
    }

    /// <summary>
    /// The ↑ and ↓ buttons, and Ctrl+Up and Ctrl+Down: every selected row moves one place. A row that would pass the
    /// top or bottom, or run into another selected row held there, stays.
    /// </summary>
    public void MoveRows(string list, int direction)
    {
        if (IsOpenRuleLocked) { Say(Texts.Get("Rules.Locked.Refused")); return; }
        JsonArray? rows = RowsOf(list);
        IReadOnlyList<int> picked = SelectedRowsOf(list);
        if (rows is null || picked.Count == 0) { Say(SelectRowsFirst); return; }
        var placed = new HashSet<int>();
        int anchor = rowAnchor[list];
        foreach (int place in direction < 0 ? picked : picked.Reverse())
        {
            int other = place + direction;
            if (other < 0 || other >= rows.Count || placed.Contains(other))
            {
                placed.Add(place);
                continue;
            }
            SwapRows(rows, place, other);
            placed.Add(other);
            if (anchor == place) anchor = other;
        }
        selectedRows[list].Clear();
        selectedRows[list].UnionWith(placed);
        rowAnchor[list] = anchor;
        SaveRules();
    }

    private static void SwapRows(JsonArray rows, int first, int second)
    {
        JsonNode? one = rows[first], other = rows[second];
        rows[first] = null; rows[second] = null; // a node can only be in one place of a list at a time
        rows[first] = other;
        rows[second] = one;
    }

    /// <summary>Delete in a list: the selected rows go, without asking.</summary>
    public void DeleteRows(string list)
    {
        if (IsOpenRuleLocked) { Say(Texts.Get("Rules.Locked.Refused")); return; }
        JsonArray? rows = RowsOf(list);
        IReadOnlyList<int> picked = SelectedRowsOf(list);
        if (rows is null || picked.Count == 0) { Say(SelectRowsFirst); return; }
        foreach (int place in picked.Reverse()) rows.RemoveAt(place);
        selectedRows[list].Clear();
        rowAnchor[list] = -1;
        SaveRules();
        Say(Texts.Get("Rules.Deleted", ("what", CountRows(list, picked.Count))));
    }

    /// <summary>"Duplicate" in a list's menu: copies of the selected rows below the last of them, selected.</summary>
    public void DuplicateRows(string list)
    {
        if (IsOpenRuleLocked) { Say(Texts.Get("Rules.Locked.Refused")); return; }
        JsonArray? rows = RowsOf(list);
        IReadOnlyList<int> picked = SelectedRowsOf(list);
        if (rows is null || picked.Count == 0) return;
        int at = picked[^1] + 1;
        List<JsonNode?> copies = [.. picked.Select(place => rows[place]?.DeepClone())];
        for (int i = 0; i < copies.Count; i++) rows.Insert(at + i, copies[i]);
        selectedRows[list].Clear();
        for (int i = 0; i < copies.Count; i++) selectedRows[list].Add(at + i);
        rowAnchor[list] = at;
        SaveRules();
    }

    /// <summary>The selected rows as clipboard text (the original app's format), or null with none selected.</summary>
    public string? RowsAsClipboardText(string list)
    {
        JsonArray? rows = RowsOf(list);
        IReadOnlyList<int> picked = SelectedRowsOf(list);
        if (rows is null || picked.Count == 0) { Say(SelectRowsFirst); return null; }
        var items = new JsonArray();
        foreach (int place in picked)
        {
            var row = (JsonObject)rows[place]!.DeepClone();
            row["DataTypeToString"] = RowType(list);
            items.Add(row);
        }
        return items.ToJsonString();
    }

    /// <summary>
    /// Pastes conditions and actions into the open rule, below the last selected row of their list, where they become
    /// the selection. Conditions go to a Lookup rule's other file when that list was clicked last. Returns what was
    /// pasted ("2 conditions"), or null when the rule cannot take them, after saying why.
    /// </summary>
    private List<string>? PasteRows(List<JsonObject> conditions, List<JsonObject> actions)
    {
        var done = new List<string>();
        if (OpenRule is not JsonObject rule) { Say(Texts.Get("Rules.Paste.SelectRule")); return null; }
        string kind = (string?)rule["Kind"] ?? "Edit";
        if ((conditions.Count > 0 && !KindsWithConditions.Contains(kind)) || (actions.Count > 0 && !KindsWithActions.Contains(kind)))
        {
            Say(conditions.Count > 0 && !KindsWithConditions.Contains(kind) ? Texts.Get("Rules.Paste.NoConditionsHere") : Texts.Get("Rules.Paste.NoActionsHere"));
            return null;
        }
        if (conditions.Count > 0)
        {
            string list = ActiveRowList == SourceConditions && kind == "Lookup" ? SourceConditions : Conditions;
            var tidy = (JsonArray)RuleLoading.Tidy(new JsonObject { ["Conditions"] = new JsonArray([.. conditions.Select(c => c.DeepClone())]) })["Conditions"]!;
            PutRows(list, tidy);
            done.Add(CountRows(list, conditions.Count));
        }
        if (actions.Count > 0)
        {
            var tidy = (JsonArray)RuleLoading.Tidy(new JsonObject { ["Conditions"] = new JsonArray(), ["Actions"] = new JsonArray([.. actions.Select(a => a.DeepClone())]) })["Actions"]!;
            PutRows(Actions, tidy);
            done.Add(CountRows(Actions, actions.Count));
        }
        return done;
    }

    /// <summary>Puts rows into a list below its last selected row, or at its end, and selects them.</summary>
    private void PutRows(string list, JsonArray tidy)
    {
        JsonArray rows = RowsOf(list)!;
        IReadOnlyList<int> picked = SelectedRowsOf(list);
        int at = picked.Count > 0 ? picked[^1] + 1 : rows.Count;
        List<JsonNode?> items = [.. tidy.Select(row => row?.DeepClone())];
        for (int i = 0; i < items.Count; i++) rows.Insert(at + i, items[i]);
        selectedRows[list].Clear();
        for (int i = 0; i < items.Count; i++) selectedRows[list].Add(at + i);
    }

    /// <summary>
    /// The summary a row shows in its list when it has no name (the HTML app's <c>rowInner</c>): its operand or target,
    /// its operator, and its value.
    /// </summary>
    public static string RowSummary(string list, JsonObject row)
    {
        bool isAction = list == Actions;
        bool custom = JsonValues.IsTruthy(row["ManualOverride"]);
        string subject = JsonValues.AsText(row[custom ? (isAction ? "CustomTarget" : "CustomOperand") : (isAction ? "Target" : "Operand")]);
        string value = JsonValues.AsText(row["Value"]).Replace("\r\n", " ↵ ").Replace("\n", " ↵ ");
        string operatorName = JsonValues.AsText(row["Operator"]);
        if (isAction && ShortOperators.TryGetValue(operatorName, out string? shortName)) operatorName = shortName;
        string text = subject != "" ? subject : isAction ? Texts.Get("Rules.Rows.NoTarget") : Texts.Get("Rules.Rows.NoOperand");
        if (operatorName != "") text += " " + operatorName;
        if (value != "") text += " " + (isAction ? value : "\"" + value + "\"");
        return text;
    }

    /// <summary>The actions' operators as a row's summary shows them (the HTML app's OP_SHORT).</summary>
    private static readonly Dictionary<string, string> ShortOperators = new()
    {
        ["Add [int, float]"] = "Add", ["Subtract [int, float]"] = "Subtract", ["Multiply [int, float]"] = "Multiply",
        ["Divide [int, float]"] = "Divide", ["Set To [int, float, string, boolean]"] = "Set To", ["Set To [list, object]"] = "Set To", ["Append [string]"] = "Append",
        ["Prepend [string]"] = "Prepend", ["Insert [array]"] = "Insert", ["Replace [array]"] = "Replace", ["Remove [array]"] = "Remove", ["Add Property"] = "Add Property",
        ["Remove Property"] = "Remove Property",
    };
}
