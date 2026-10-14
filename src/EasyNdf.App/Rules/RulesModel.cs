using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Engine.Mods;

namespace EasyNdf.App.Rules;

/// <summary>
/// The Rules tab (the HTML app's <c>#pagePresets</c>): the open mod, its presets, which are selected and which is open,
/// and the open preset's rules. Questions to the user (names, "Delete...?") are asked by the page, which then calls
/// these methods; every change is saved at once (see <see cref="AppData.Save"/>).
/// </summary>
public sealed partial class RulesModel : INotifyPropertyChanged
{
    private readonly HashSet<ModPreset> selectedPresets = [];
    private ModPreset? presetAnchor;
    private readonly HashSet<int> selectedRules = [];
    private int ruleAnchor;
    private JsonArray? rulesShownFor;

    /// <summary>The mod shown last, and its name then, to tell another mod from the same one renamed or put back by Undo.</summary>
    private Mod? shownMod;
    private string shownModName = "";

    /// <summary>Starts with the open mod and its first preset.</summary>
    public RulesModel() => Refresh();

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private static AppData Data => AppData.Current;

    /// <summary>The open mod.</summary>
    public Mod OpenMod => Data.OpenMod;

    /// <summary>Every mod's name, for the Mod box, in name order.</summary>
    public IReadOnlyList<string> ModNames => [.. Data.Mods.Select(m => m.Name)];

    /// <summary>The open mod's presets, in name order as the HTML app sorts them.</summary>
    public IReadOnlyList<ModPreset> Presets => [.. OpenMod.Presets.OrderBy(p => p.Name, StringComparer.Create(CultureInfo.InvariantCulture, false))];

    /// <summary>The preset whose rules are shown; null when the mod has none.</summary>
    public ModPreset? OpenPreset { get; private set; }

    /// <summary>The selected presets, in the order listed. The open one is always among them.</summary>
    public IReadOnlyList<ModPreset> SelectedPresets => [.. Presets.Where(selectedPresets.Contains)];

    /// <summary>Whether a preset is selected.</summary>
    public bool IsSelected(ModPreset preset) => selectedPresets.Contains(preset);

    /// <summary>The open preset's rules; an empty list when no preset is open.</summary>
    public JsonArray Rules => OpenPreset?.Rules ?? [];

    /// <summary>The rule shown in the rule window and acted on by single-rule buttons.</summary>
    public int CurrentRule { get; private set; }

    /// <summary>The selected rules' places, in order.</summary>
    public IReadOnlyList<int> SelectedRules => [.. selectedRules.Where(i => i < Rules.Count).Order()];

    /// <summary>The rule list's lines, with folded groups' rules left out.</summary>
    public IReadOnlyList<RuleRow> RuleRows { get; private set; } = [];

    /// <summary>What the last action did (the HTML app's <c>#clipNote</c>).</summary>
    public string Note { get; private set; } = "";

    /// <summary>"Presets in WARNO Tactical Overhaul".</summary>
    public string PresetsTitle => "Presets in " + OpenMod.Name;

    /// <summary>"Rules in WTO (Ammunition.ndf)", or "Rules" with no preset open.</summary>
    public string RulesTitle => OpenPreset is null ? "Rules" : "Rules in " + OpenPreset.Name;

    /// <summary>What the rule list says when it has no rules.</summary>
    public string NoRulesText => OpenPreset is not null
        ? "No rules yet. Click \"New rule\" to add one, or paste rules with Ctrl+V."
        : "No rules yet. Click \"New rule\" to add one; it also creates a preset to hold it.";

    /// <summary>What the preset list says when the mod has no presets.</summary>
    public const string NoPresetsText = "No presets in this mod.";

    /// <summary>Raised with each message, so the rule window can show it too while it is open.</summary>
    public event Action<string>? Said;

    /// <summary>Shows a message under the rule list's buttons, and in the rule window while it is open.</summary>
    public void Say(string message)
    {
        Note = message;
        Said?.Invoke(message);
        Changed();
    }

    // ---- mods ----

    /// <summary>The Mod box: opens another mod, with its first preset.</summary>
    public void OpenModNamed(string name)
    {
        if (Data.Mods.FirstOrDefault(m => m.Name == name) is not Mod mod || ReferenceEquals(mod, OpenMod)) return;
        Data.Open(mod);
        OpenPreset = null;
        selectedPresets.Clear();
        Refresh();
    }

    /// <summary>Whether a mod has this name already.</summary>
    public static bool ModExists(string name) => Data.Mods.Any(m => m.Name == name);

    /// <summary>"New": a new empty mod, opened.</summary>
    public void NewMod(string name)
    {
        var mod = new Mod { Name = name };
        Data.ModStore.Add(mod);
        Data.Open(mod);
        OpenPreset = null;
        Refresh();
    }

    /// <summary>"Rename": the open mod gets a new name; its file follows.</summary>
    public void RenameMod(string name)
    {
        Mod mod = OpenMod;
        mod.Name = name;
        Data.Save(mod);
        Data.Open(mod);
        Refresh();
    }

    /// <summary>"Make copy": a copy of the open mod with all its presets, "Name (Copy)", opened.</summary>
    public void CopyMod()
    {
        Mod from = OpenMod;
        var copy = new Mod
        {
            Name = Data.ModStore.FreeName(from.Name + " (Copy)"),
            Watermark = from.Watermark,
            Presets = [.. from.Presets.Select(p => new ModPreset { Name = p.Name, Rules = (JsonArray)p.Rules.DeepClone() })],
        };
        Data.ModStore.Add(copy);
        Data.Open(copy);
        OpenPreset = null;
        Refresh();
        Say("Copied \"" + from.Name + "\" to \"" + copy.Name + "\": " + Count(copy.Presets.Count) + ".");
    }

    /// <summary>"Delete": the open mod goes, with its presets; the first mod left opens.</summary>
    public void DeleteMod()
    {
        Data.ModStore.Delete(OpenMod);
        if (Data.Mods.Count == 0) Data.ModStore.Add(new Mod { Name = ModStore.BlankModName }); // there is always a mod to work in
        Data.Open(Data.Mods[0]);
        OpenPreset = null;
        Refresh();
    }

    // ---- presets ----

    /// <summary>
    /// A click on a preset: opens it. With Ctrl, adds it to the selection or takes it out; with Shift, selects from the
    /// one clicked last to this one.
    /// </summary>
    public void ClickPreset(ModPreset preset, bool control, bool shift)
    {
        IReadOnlyList<ModPreset> listed = Presets;
        ModPreset? was = OpenPreset;
        if (shift && presetAnchor is not null && listed.Contains(presetAnchor))
        {
            int from = IndexIn(listed, presetAnchor), to = IndexIn(listed, preset);
            selectedPresets.Clear();
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) selectedPresets.Add(listed[i]);
            OpenPreset = preset;
        }
        else if (control)
        {
            if (selectedPresets.Contains(preset) && selectedPresets.Count > 1)
            {
                selectedPresets.Remove(preset);
                if (ReferenceEquals(OpenPreset, preset)) OpenPreset = listed.FirstOrDefault(selectedPresets.Contains) ?? preset;
            }
            else
            {
                selectedPresets.Add(preset);
                OpenPreset = preset;
            }
            presetAnchor = preset;
        }
        else
        {
            selectedPresets.Clear();
            selectedPresets.Add(preset);
            presetAnchor = preset;
            OpenPreset = preset;
        }
        if (!ReferenceEquals(OpenPreset, was)) CurrentRule = 0;
        Refresh();
    }

    /// <summary>Whether the open mod has a preset of this name already.</summary>
    public bool PresetExists(string name) => OpenMod.Presets.Any(p => p.Name == name);

    /// <summary>"New": a new empty preset in the open mod, opened.</summary>
    public void NewPreset(string name)
    {
        var preset = new ModPreset { Name = name };
        OpenMod.Presets.Add(preset);
        ShowPresets([preset]);
        Data.Save(OpenMod);
    }

    /// <summary>"Rename": the open preset gets a new name. File rows keep pointing at it.</summary>
    public void RenamePreset(string name)
    {
        if (OpenPreset is null) return;
        OpenPreset.Name = name;
        Data.Save(OpenMod);
        Refresh();
    }

    /// <summary>"Duplicate": a copy of each selected preset, "Name copy", selected.</summary>
    public void DuplicatePresets()
    {
        var made = new List<ModPreset>();
        foreach (ModPreset preset in SelectedPresets)
        {
            var copy = new ModPreset { Name = OpenMod.FreePresetName(preset.Name + " copy"), Rules = (JsonArray)preset.Rules.DeepClone() };
            ReseedClones(copy.Rules);
            OpenMod.Presets.Add(copy);
            made.Add(copy);
        }
        if (made.Count == 0) return;
        ShowPresets(made);
        Data.Save(OpenMod);
    }

    /// <summary>"Delete": the selected presets go. Files that used them have no preset after.</summary>
    public void DeletePresets()
    {
        IReadOnlyList<ModPreset> gone = SelectedPresets;
        foreach (ModPreset preset in gone) OpenMod.Presets.Remove(preset);
        selectedPresets.Clear();
        OpenPreset = null;
        CurrentRule = 0;
        Data.Save(OpenMod);
        Refresh();
        Say("Deleted " + Count(gone.Count) + ".");
    }

    /// <summary>Selects and opens presets just made.</summary>
    private void ShowPresets(IReadOnlyList<ModPreset> made)
    {
        selectedPresets.Clear();
        foreach (ModPreset preset in made) selectedPresets.Add(preset);
        OpenPreset = made[0];
        presetAnchor = OpenPreset;
        CurrentRule = 0;
        Refresh();
    }

    /// <summary>"3 presets", "1 preset".</summary>
    public static string Count(int presets) => presets + (presets == 1 ? " preset" : " presets");

    // ---- pasting ----

    /// <summary>
    /// "Paste" (the HTML app's <c>pasteText</c>): presets on the clipboard, or a whole mod file's text, become presets of
    /// the open mod; rules go in after the selected rule; conditions and actions go into the open rule. Returns whether
    /// presets were added, so the Files tab can give them to files.
    /// </summary>
    public bool PasteText(string text)
    {
        const string nothing = "The clipboard holds no EasyNDF preset, rule, condition or action.";
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(text); }
        catch (System.Text.Json.JsonException) { Say(nothing); return false; }
        List<JsonNode?> items = parsed is JsonArray list ? [.. list] : [parsed];
        // a mod file's text: its presets
        items = [.. items.SelectMany(x => x?["Presets"] is JsonArray presets ? [.. presets] : new List<JsonNode?> { x })];

        List<JsonObject> presetItems = [.. items.OfType<JsonObject>().Where(x => x["Rules"] is JsonArray)];
        if (presetItems.Count > 0)
        {
            PastePresets(presetItems);
            items = [.. items.Where(x => x is not JsonObject one || !presetItems.Contains(one))];
            if (items.Count == 0) return true;
        }
        List<JsonObject> ruleItems = [.. items.OfType<JsonObject>().Where(x => KindOf(x) == "Rule")];
        List<JsonObject> conditionItems = [.. items.OfType<JsonObject>().Where(x => KindOf(x) == "Condition")];
        List<JsonObject> actionItems = [.. items.OfType<JsonObject>().Where(x => KindOf(x) == "Action")];
        if (ruleItems.Count == 0 && conditionItems.Count == 0 && actionItems.Count == 0)
        {
            if (presetItems.Count == 0) Say(nothing);
            return presetItems.Count > 0;
        }

        bool madePreset = ruleItems.Count > 0 && OpenPreset is null;
        if (madePreset) MakePresetForRules();
        if (OpenPreset is null) { Say("Select a rule before pasting conditions or actions."); return presetItems.Count > 0; }
        var done = new List<string>();
        if (ruleItems.Count > 0)
        {
            int count = PasteRules(ruleItems);
            done.Add(count + (count == 1 ? " rule" : " rules"));
        }
        if (conditionItems.Count > 0 || actionItems.Count > 0)
        {
            List<string>? rows = PasteRows(conditionItems, actionItems);
            if (rows is null)
            {
                if (ruleItems.Count > 0) SaveRules();
                return presetItems.Count > 0;
            }
            done.AddRange(rows);
        }
        SaveRules();
        Say("Pasted " + string.Join(" and ", done) + " from the clipboard." + (madePreset ? " " + MadePresetNote : ""));
        return presetItems.Count > 0;
    }

    /// <summary>What "New rule" and Paste say when they had to make a preset to hold the rules.</summary>
    private const string MadePresetNote = "Created the preset \"New preset\" for it. Rename it after its file, such as \"My mod (Ammunition.ndf)\", and files of that name get it by themselves.";

    /// <summary>Makes the preset "New preset" in the open mod, opened, to hold rules made with no preset open.</summary>
    private void MakePresetForRules()
    {
        var preset = new ModPreset { Name = OpenMod.FreePresetName("New preset") };
        OpenMod.Presets.Add(preset);
        ShowPresets([preset]);
    }

    /// <summary>What a pasted item is: "Rule", "Condition", "Action", or "" (the HTML app's kind test).</summary>
    private static string KindOf(JsonObject item) =>
        (string?)item["DataTypeToString"] is { Length: > 0 } type ? type
        : item["Conditions"] is not null || item["Actions"] is not null ? "Rule"
        : item["Operand"] is not null ? "Condition"
        : item["Target"] is not null ? "Action" : "";

    /// <summary>Presets go into the open mod, selected. A name the mod has already gets a number.</summary>
    private void PastePresets(IReadOnlyList<JsonObject> pasted)
    {
        var made = new List<ModPreset>();
        foreach (JsonObject item in pasted)
        {
            string name = ((string?)item["PresetName"] ?? "").Trim();
            var preset = new ModPreset { Name = OpenMod.FreePresetName(name == "" ? "Preset" : name), Rules = RuleLoading.LoadRules(item["Rules"]) };
            OpenMod.Presets.Add(preset);
            made.Add(preset);
        }
        ShowPresets(made);
        Data.Save(OpenMod);
        Say("Pasted " + Count(made.Count) + " into \"" + OpenMod.Name + "\" from the clipboard.");
    }

    /// <summary>Rules go in after the last selected rule, and become the selection. Returns how many.</summary>
    private int PasteRules(IReadOnlyList<JsonObject> pasted)
    {
        JsonArray added = RuleLoading.LoadRules(new JsonArray([.. pasted.Select(r => r.DeepClone())]));
        IReadOnlyList<int> picked = SelectedRules;
        int at = Rules.Count == 0 ? 0 : (picked.Count > 0 ? picked[^1] : CurrentRule) + 1;
        var rules = added.Select(r => r!.DeepClone()).ToList();
        for (int i = 0; i < rules.Count; i++)
        {
            ReseedClone((JsonObject)rules[i]);
            Rules.Insert(at + i, rules[i]);
        }
        selectedRules.Clear();
        for (int i = 0; i < rules.Count; i++) selectedRules.Add(at + i);
        CurrentRule = at + rules.Count - 1;
        ruleAnchor = CurrentRule;
        Reveal(CurrentRule);
        return rules.Count;
    }

    // ---- rules ----

    /// <summary>
    /// A click on a rule's name: selects it. With Ctrl, adds it to the selection or takes it out; with Shift, selects from
    /// the rule clicked last to this one, leaving out rules hidden in folded groups.
    /// </summary>
    public void ClickRule(int index, bool control, bool shift)
    {
        if (shift && ruleAnchor >= 0 && ruleAnchor < Rules.Count)
        {
            selectedRules.Clear();
            for (int i = Math.Min(ruleAnchor, index); i <= Math.Max(ruleAnchor, index); i++)
                if (!IsHidden(i)) selectedRules.Add(i);
            CurrentRule = index;
        }
        else if (control)
        {
            if (selectedRules.Contains(index) && selectedRules.Count > 1)
            {
                selectedRules.Remove(index);
                if (CurrentRule == index) CurrentRule = selectedRules.First();
            }
            else
            {
                selectedRules.Add(index);
                CurrentRule = index;
            }
            ruleAnchor = index;
        }
        else SelectOnly(index);
        Refresh();
    }

    /// <summary>Selects one rule alone.</summary>
    public void SelectOnly(int index)
    {
        CurrentRule = index;
        selectedRules.Clear();
        selectedRules.Add(index);
        ruleAnchor = index;
    }

    /// <summary>A rule's switch.</summary>
    public void SetRuleOn(int index, bool on)
    {
        Rules[index]!["Enabled"] = on;
        SaveRules();
    }

    /// <summary>A Group's fold button: hides its rules, or shows them.</summary>
    public void ToggleFold(int index)
    {
        JsonNode rule = Rules[index]!;
        rule["Collapsed"] = rule["Collapsed"]?.GetValue<bool>() != true;
        SaveRules();
    }

    /// <summary>Whether the open preset has a Group rule, so "Expand all" and "Collapse all" have something to do.</summary>
    public bool HasGroups => Rules.Any(rule => (string?)rule?["Kind"] == "Group");

    /// <summary>"Expand all" and "Collapse all": shows or hides the rules of every Group.</summary>
    public void FoldAllGroups(bool collapsed)
    {
        bool changed = false;
        foreach (JsonNode? rule in Rules)
        {
            if ((string?)rule?["Kind"] != "Group" || (rule["Collapsed"]?.GetValue<bool>() == true) == collapsed) continue;
            rule["Collapsed"] = collapsed;
            changed = true;
        }
        if (changed) SaveRules();
    }

    /// <summary>A rule's ↑ or ↓: swaps it with the rule above or below.</summary>
    public void MoveRule(int index, int direction)
    {
        int other = index + direction;
        if (other < 0 || other >= Rules.Count) return;
        Swap(index, other);
        SelectOnly(other);
        Reveal(other);
        SaveRules();
    }

    /// <summary>A rule's +: a copy of it below it, selected.</summary>
    public void DuplicateRule(int index)
    {
        var copy = (JsonObject)Rules[index]!.DeepClone();
        ReseedClone(copy);
        Rules.Insert(index + 1, copy);
        SelectOnly(index + 1);
        Reveal(index + 1);
        SaveRules();
    }

    /// <summary>
    /// Ctrl+Up and Ctrl+Down (the HTML app's <c>opMove</c>): every selected rule moves one place up or down. A rule that
    /// would pass the top or bottom, or run into another selected rule held there, stays where it is.
    /// </summary>
    public void MoveSelectedRules(int direction)
    {
        IReadOnlyList<int> picked = SelectedRules;
        if (picked.Count == 0) return;
        var placed = new HashSet<int>();
        var movedTo = new Dictionary<int, int>();
        foreach (int place in direction < 0 ? picked : picked.Reverse())
        {
            int other = place + direction;
            if (other < 0 || other >= Rules.Count || placed.Contains(other))
            {
                placed.Add(place);
                movedTo[place] = place;
                continue;
            }
            Swap(place, other);
            placed.Add(other);
            movedTo[place] = other;
        }
        CurrentRule = movedTo.GetValueOrDefault(CurrentRule, CurrentRule);
        selectedRules.Clear();
        selectedRules.UnionWith(placed);
        ruleAnchor = CurrentRule;
        if (CurrentRule < Rules.Count) Reveal(CurrentRule);
        SaveRules();
    }

    /// <summary>Swaps two rules.</summary>
    private void Swap(int first, int second)
    {
        JsonNode? one = Rules[first], other = Rules[second];
        Rules[first] = null; Rules[second] = null; // a node can only be in one place of a list at a time
        Rules[first] = other;
        Rules[second] = one;
    }

    /// <summary>"Duplicate" in the menu (the HTML app's <c>opDup</c>): copies of the selected rules after the last of them, selected.</summary>
    public void DuplicateSelectedRules()
    {
        IReadOnlyList<int> picked = SelectedRules;
        if (picked.Count == 0) return;
        int at = picked[^1] + 1;
        List<JsonObject> copies = [.. picked.Select(place => (JsonObject)Rules[place]!.DeepClone())];
        for (int i = 0; i < copies.Count; i++)
        {
            ReseedClone(copies[i]);
            Rules.Insert(at + i, copies[i]);
        }
        selectedRules.Clear();
        for (int i = 0; i < copies.Count; i++) selectedRules.Add(at + i);
        CurrentRule = at;
        ruleAnchor = at;
        Reveal(at);
        SaveRules();
    }

    /// <summary>Ctrl+A in the rule list: every rule, except those hidden in folded groups.</summary>
    public void SelectAllRules()
    {
        selectedRules.Clear();
        for (int i = 0; i < Rules.Count; i++) if (!IsHidden(i)) selectedRules.Add(i);
        if (!selectedRules.Contains(CurrentRule)) CurrentRule = 0;
        Refresh();
    }

    /// <summary>"Enable" or "Disable" in the menu: every selected rule.</summary>
    public void SetSelectedRulesOn(bool on)
    {
        foreach (int place in SelectedRules) Rules[place]!["Enabled"] = on;
        SaveRules();
    }

    /// <summary>Whether a group heading is among the selected rules: moving it changes which rules belong to it.</summary>
    public bool SelectionHasGroupHeading => SelectedRules.Any(IsGroupHeading);

    /// <summary>
    /// Whether deleting the selected rules would delete a group heading but not the rules under it, which then join the
    /// group above, or none.
    /// </summary>
    public bool DeletingLeavesGroupRulesBehind
    {
        get
        {
            IReadOnlyList<int> picked = SelectedRules;
            return picked.Any(place => IsGroupHeading(place) && place + 1 < Rules.Count && !IsGroupHeading(place + 1) && !picked.Contains(place + 1));
        }
    }

    /// <summary>Ctrl+A in the preset list: every preset of the mod.</summary>
    public void SelectAllPresets()
    {
        selectedPresets.UnionWith(Presets);
        Refresh();
    }

    /// <summary>Deletes rules, by their places.</summary>
    public void DeleteRules(IReadOnlyList<int> places)
    {
        if (places.Count == 0) return;
        string label = places.Count == 1 ? "rule \"" + RuleName(places[0]) + "\"" : places.Count + " rules";
        int first = places.Min();
        foreach (int place in places.OrderDescending()) Rules.RemoveAt(place);
        SelectOnly(Math.Max(0, Math.Min(first, Rules.Count - 1)));
        if (Rules.Count == 0) selectedRules.Clear();
        SaveRules();
        Say("Deleted " + label + ".");
    }

    /// <summary>"New rule": a new rule of a type after the selected one, selected. With no preset, one is made to hold it.</summary>
    public void NewRule(string kind)
    {
        bool madePreset = OpenPreset is null;
        if (madePreset) MakePresetForRules();
        int at = Rules.Count == 0 ? 0 : Math.Min(CurrentRule + 1, Rules.Count);
        Rules.Insert(at, RuleLoading.NewRule(kind));
        SelectOnly(at);
        Reveal(at);
        SaveRules();
        if (madePreset) Say(MadePresetNote);
    }

    /// <summary>A rule's name, or "(unnamed)".</summary>
    public string RuleName(int index) => (string?)Rules[index]?["Name"] is { Length: > 0 } name ? name : "(unnamed)";

    /// <summary>Whether a rule is a Group's heading.</summary>
    public bool IsGroupHeading(int index) => (string?)Rules[index]?["Kind"] == "Group";

    /// <summary>A rule hidden inside a folded group (the HTML app's <c>ruleHidden</c>).</summary>
    public bool IsHidden(int index)
    {
        if (index >= Rules.Count || IsGroupHeading(index)) return false;
        for (int above = index - 1; above >= 0; above--)
            if (IsGroupHeading(above)) return Rules[above]!["Collapsed"]?.GetValue<bool>() == true;
        return false;
    }

    /// <summary>Unfolds the group that holds a rule, so a rule just added or moved stays in view.</summary>
    private void Reveal(int index)
    {
        for (int above = index; above >= 0; above--)
            if (IsGroupHeading(above))
            {
                if (above != index) Rules[above]!["Collapsed"] = false;
                return;
            }
    }

    /// <summary>Gives every copied Clone rule a new seed, so the copy makes different GUIDs from the rule it was copied from.</summary>
    private static void ReseedClones(JsonArray rules)
    {
        foreach (JsonNode? rule in rules) if (rule is JsonObject one) ReseedClone(one);
    }

    private static void ReseedClone(JsonObject rule)
    {
        if ((string?)rule["Kind"] == "Clone" && rule["Params"] is JsonObject parameters) parameters["Seed"] = RuleLoading.NewSeed();
    }

    /// <summary>Saves a change to the open preset's rules, and shows it.</summary>
    public void SaveRules()
    {
        Data.Save(OpenMod);
        Refresh();
    }

    // ---- showing it ----

    /// <summary>Works out again what is open and selected, and the rule list's lines (the HTML app's renderPresetList and renderRuleList).</summary>
    public void Refresh()
    {
        IReadOnlyList<ModPreset> listed = Presets;
        if (!ReferenceEquals(OpenMod, shownMod))
        {
            // another mod: its first preset, unless the mod was only renamed
            if (shownMod is null || OpenMod.Name != shownModName) OpenPreset = null;
            shownMod = OpenMod;
        }
        shownModName = OpenMod.Name;
        if (OpenPreset is not null && !listed.Contains(OpenPreset))
        {
            // Undo and Redo put back copies of the presets: the copy of the open one stays open, on the same rule
            ModPreset? sameName = listed.FirstOrDefault(p => p.Name == OpenPreset.Name);
            OpenPreset = sameName;
            if (sameName is not null) CurrentRule = Math.Max(0, Math.Min(CurrentRule, sameName.Rules.Count - 1));
        }
        if (OpenPreset is null) { OpenPreset = listed.FirstOrDefault(); CurrentRule = 0; }
        selectedPresets.RemoveWhere(p => !listed.Contains(p));
        if (OpenPreset is not null && !selectedPresets.Contains(OpenPreset)) { selectedPresets.Clear(); selectedPresets.Add(OpenPreset); }
        if (presetAnchor is null || !listed.Contains(presetAnchor)) presetAnchor = OpenPreset;

        JsonArray rules = Rules;
        if (!ReferenceEquals(rulesShownFor, rules))
        {
            // another preset: its own selection, not the last one's row numbers
            rulesShownFor = rules;
            selectedRules.Clear();
            if (rules.Count > 0) selectedRules.Add(CurrentRule);
            ruleAnchor = CurrentRule;
        }
        selectedRules.RemoveWhere(i => i >= rules.Count);
        if (rules.Count > 0 && !selectedRules.Contains(CurrentRule)) { selectedRules.Clear(); selectedRules.Add(Math.Min(CurrentRule, rules.Count - 1)); }

        KeepRowSelectionsFor(OpenRule);

        var rows = new List<RuleRow>();
        bool inGroup = false, groupOff = false, folded = false;
        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i] is not JsonObject rule) continue;
            bool isGroup = (string?)rule["Kind"] == "Group";
            if (isGroup) { inGroup = true; groupOff = rule["Enabled"]?.GetValue<bool>() == false; folded = rule["Collapsed"]?.GetValue<bool>() == true; }
            else if (inGroup && folded) continue;
            bool off = rule["Enabled"]?.GetValue<bool>() == false || (!isGroup && inGroup && groupOff);
            int hidden = 0;
            if (isGroup && folded) for (int k = i + 1; k < rules.Count && (string?)rules[k]?["Kind"] != "Group"; k++) hidden++;
            rows.Add(new RuleRow { Index = i, Rule = rule, IsSelected = selectedRules.Contains(i), IsInGroup = !isGroup && inGroup, IsOff = off, HiddenCount = hidden });
        }
        RuleRows = rows;
        Changed();
    }

    private static int IndexIn(IReadOnlyList<ModPreset> list, ModPreset preset)
    {
        for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], preset)) return i;
        return -1;
    }

    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
