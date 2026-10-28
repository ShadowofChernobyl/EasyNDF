using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Engine.Mods;
using EasyNdf.App.Text;

namespace EasyNdf.App.Rules;

/// <summary>
/// The Rules tab (the HTML app's <c>#pagePresets</c>): the open mod, its presets, which are selected and which is open,
/// and the open preset's rules. Questions to the user (names, "Delete...?") are asked by the page, which then calls
/// these methods; every change is saved at once (see <see cref="AppData.Save"/>), except while the rule window has a
/// rule open: its changes are saved on Done (see <see cref="StartEditingRule"/>).
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

    /// <summary>
    /// The open mod's presets of its own, in name order as the HTML app sorts them; its Armory presets are listed apart
    /// (<see cref="ArmoryPresetsOfOpenMod"/>).
    /// </summary>
    public IReadOnlyList<ModPreset> Presets =>
        [.. OpenMod.Presets.Where(p => !p.IsArmoryPreset).OrderBy(p => p.Name, StringComparer.Create(CultureInfo.InvariantCulture, false))];

    /// <summary>The open mod's Armory presets, listed under Armory Changes, in the order they are listed and run.</summary>
    public IReadOnlyList<ModPreset> ArmoryPresetsOfOpenMod => ArmoryPresets.AllOf(OpenMod);

    /// <summary>
    /// The preset whose rules are shown; null when the mod has none, or a part of the Armory with no changes is open
    /// (<see cref="OpenEmptyArmoryPart"/>). Opening a preset closes that part.
    /// </summary>
    public ModPreset? OpenPreset
    {
        get => openPreset;
        private set
        {
            openPreset = value;
            if (value is not null) OpenEmptyArmoryPart = null;
        }
    }

    private ModPreset? openPreset;

    /// <summary>
    /// The part of the Armory open from the Armory Changes list while no change was made in it, so it has no preset
    /// and no rules to show; null otherwise. Its preset opens in its place once a change is made there.
    /// </summary>
    public ArmoryPart? OpenEmptyArmoryPart { get; private set; }

    /// <summary>The selected presets, in the order listed, the Armory's last. The open one is always among them.</summary>
    public IReadOnlyList<ModPreset> SelectedPresets => [.. Presets.Concat(ArmoryPresetsOfOpenMod).Where(selectedPresets.Contains)];

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

    /// <summary>"Presets": the open mod's, the one in bold in the mod list (its name made the heading too long to read).</summary>
    public string PresetsTitle => Texts.Get("Rules.Presets");

    /// <summary>"Rules in WTO (Ammunition.ndf)", "Rules in Units" for a part of the Armory with no changes, or "Rules" with nothing open.</summary>
    public string RulesTitle =>
        OpenPreset is not null ? Texts.Get("Rules.RulesIn", ("preset", OpenPreset.Name))
        : OpenEmptyArmoryPart is ArmoryPart part ? Texts.Get("Rules.RulesIn", ("preset", ArmoryPartTexts.Name(part)))
        : Texts.Get("Rules.Rules");

    /// <summary>What the rule list says when it has no rules.</summary>
    public string NoRulesText =>
        OpenPreset is not null ? Texts.Get("Rules.NoRules")
        : OpenEmptyArmoryPart is ArmoryPart part ? Texts.Get("Rules.Armory.NoChanges", ("part", ArmoryPartTexts.Name(part)))
        : Texts.Get("Rules.NoRulesNoPreset");

    /// <summary>
    /// A click on a line of the Armory Changes list: the part's preset opens, alone; a part with no changes yet opens
    /// with no rules, saying where its changes are made.
    /// </summary>
    public void OpenArmoryPart(ArmoryPart part)
    {
        if (ArmoryPresets.Of(OpenMod, part) is ModPreset preset)
        {
            ClickPreset(preset, control: false, shift: false);
            return;
        }
        OpenPreset = null;
        OpenEmptyArmoryPart = part;
        selectedPresets.Clear();
        CurrentRule = 0;
        Refresh();
    }

    /// <summary>What the preset list says when the mod has no presets.</summary>
    public static string NoPresetsText => Texts.Get("Rules.NoPresetsInThis");

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
        // a mod made to work in starts on; a copy or an import keeps its original's switch
        EasyNdf.App.Mods.ModStack.SetOn(mod, on: true);
        OpenPreset = null;
        Refresh();
    }

    /// <summary>"Rename": the open mod gets a new name; its file follows, and so do its place and switch on the Mods tab.</summary>
    public void RenameMod(string name)
    {
        Mod mod = OpenMod;
        EasyNdf.App.Mods.ModStack.Renamed(mod.Name, name);
        mod.Name = name;
        Data.Save(mod);
        Data.Open(mod);
        Refresh();
    }

    /// <summary>"Make copy": a copy of the open mod with all its presets and Armory changes, "Name (Copy)", opened.</summary>
    public void CopyMod()
    {
        Mod from = OpenMod;
        Mod copy = from.DeepCopy();
        copy.Name = Data.ModStore.FreeName(Texts.Get("Rules.Mod.CopyName", ("mod", from.Name)));
        Data.ModStore.Add(copy);
        // a copy is switched on or off as its original is; two mods that change the same value are marked ⚠
        EasyNdf.App.Mods.ModStack.SetOn(copy, EasyNdf.App.Mods.ModStack.IsOn(from));
        Data.Open(copy);
        OpenPreset = null;
        Refresh();
        Say(Texts.Get("Rules.Mod.Copied", ("from", from.Name), ("copy", copy.Name), ("presets", Count(copy.Presets.Count))));
    }

    /// <summary>"Delete": the open mod goes, with its presets; the first mod left opens.</summary>
    public void DeleteMod()
    {
        EasyNdf.App.Mods.ModStack.Deleted(OpenMod.Name);
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

    /// <summary>
    /// The Applies To window's Done: the files a rule set applies to, or null for the files its name holds. Saved in
    /// the mod, so Undo covers it, and the Files tab follows.
    /// </summary>
    public void SetAppliesTo(ModPreset ruleSet, List<string>? files)
    {
        ruleSet.AppliesTo = files;
        Data.Save(OpenMod);
        Refresh();
    }

    /// <summary>"Duplicate": a copy of each selected preset, "Name copy", selected.</summary>
    public void DuplicatePresets()
    {
        var made = new List<ModPreset>();
        foreach (ModPreset preset in SelectedPresets)
        {
            var copy = new ModPreset { Name = OpenMod.FreePresetName(Texts.Get("Rules.Preset.CopyName", ("name", preset.Name))), Rules = (JsonArray)preset.Rules.DeepClone(), AppliesTo = preset.AppliesTo is null ? null : [.. preset.AppliesTo] };
            ReseedClones(copy.Rules);
            OpenMod.Presets.Add(copy);
            made.Add(copy);
        }
        if (made.Count == 0) return;
        ShowPresets(made);
        Data.Save(OpenMod);
    }

    /// <summary>
    /// "Delete": the selected presets go. Files that used them have no preset after. An Armory preset's
    /// changes are taken back first (see <see cref="Pages.RulesPage"/>), so it is never deleted with changes left.
    /// </summary>
    public void DeletePresets() => DeletePresets(SelectedPresets);

    /// <summary>
    /// Deletes these presets, those still in the mod. The page passes the ones picked before it asked: taking back the
    /// Armory's changes can remove an Armory preset meanwhile, and the selection then moves to another preset.
    /// </summary>
    public void DeletePresets(IReadOnlyList<ModPreset> picked)
    {
        List<ModPreset> gone = [.. picked.Where(OpenMod.Presets.Contains)];
        // an Armory preset goes when the Armory's changes do, so nothing they make is left without its rules
        if (gone.Any(preset => preset.IsArmoryPreset && preset.Rules.Any(ArmoryPresets.IsLocked)))
        {
            Say(Texts.Get("Rules.Locked.PresetRefused"));
            return;
        }
        foreach (ModPreset preset in gone) OpenMod.Presets.Remove(preset);
        selectedPresets.Clear();
        OpenPreset = null;
        CurrentRule = 0;
        // an Armory preset already removed with the Armory's changes still counts as deleted
        if (gone.Count > 0) Data.Save(OpenMod);
        Refresh();
        Say(Texts.Get("Rules.Deleted", ("what", Count(picked.Count))));
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
    public static string Count(int presets) => Texts.Count("Rules.PresetCount", presets);

    /// <summary>"1 rule", "3 rules".</summary>
    public static string CountRules(int rules) => Texts.Count("Rules.RuleCount", rules);

    // ---- pasting ----

    /// <summary>
    /// "Paste" (the HTML app's <c>pasteText</c>): presets on the clipboard, or a whole mod file's text, become presets of
    /// the open mod; rules go in after the selected rule; conditions and actions go into the open rule. Returns whether
    /// presets were added, so the Files tab can give them to files.
    /// </summary>
    public bool PasteText(string text)
    {
        string nothing = Texts.Get("Rules.Paste.Nothing");
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

        bool madePreset = ruleItems.Count > 0 && NeedsPresetForNewRules;
        string madeNote = madePreset ? MadePresetNoteForOpen() : "";
        if (madePreset) MakePresetForRules();
        if (OpenPreset is null) { Say(Texts.Get("Rules.Paste.SelectRule")); return presetItems.Count > 0; }
        var done = new List<string>();
        if (ruleItems.Count > 0)
        {
            int count = PasteRules(ruleItems);
            done.Add(CountRules(count));
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
        Say(Texts.Get("Rules.Paste.Done", ("what", string.Join(" " + Texts.Get("Rules.Paste.And") + " ", done))) + (madePreset ? " " + madeNote : ""));
        return presetItems.Count > 0;
    }

    /// <summary>What "New rule" and Paste say when they had to make a preset to hold the rules.</summary>
    private static string MadePresetNote => Texts.Get("Rules.Paste.MadePreset", ("preset", Texts.Get("Rules.NewPresetName")));

    /// <summary>Makes the preset "New preset" in the open mod, opened, to hold rules made with no preset open.</summary>
    private void MakePresetForRules()
    {
        var preset = new ModPreset { Name = OpenMod.FreePresetName(Texts.Get("Rules.NewPresetName")) };
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
            var preset = new ModPreset { Name = OpenMod.FreePresetName(name == "" ? Texts.Get("Rules.PresetName") : name), Rules = RuleLoading.LoadRules(item["Rules"]),
                AppliesTo = ModFiles.AppliesToOf(item) };
            OpenMod.Presets.Add(preset);
            made.Add(preset);
        }
        ShowPresets(made);
        Data.Save(OpenMod);
        Say(Texts.Get("Rules.Paste.PresetsDone", ("presets", Count(made.Count)), ("mod", OpenMod.Name)));
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

    /// <summary>
    /// Whether a rule is locked: one the Armory made in an Armory preset (<see cref="ArmoryPresets"/>). It is
    /// changed in the Armory, or made editable, not changed here.
    /// </summary>
    public bool IsLocked(int index) =>
        OpenPreset?.IsArmoryPreset == true && index >= 0 && index < Rules.Count && ArmoryPresets.IsLocked(Rules[index]);

    /// <summary>Whether the rule open in the rule window is locked: the window then only shows it.</summary>
    public bool IsOpenRuleLocked => IsLocked(CurrentRule);

    /// <summary>
    /// Refuses a change to locked rules, saying where they are changed instead.
    /// </summary>
    /// <returns>True when refused, so the caller stops.</returns>
    public bool RefusedForLocked(IEnumerable<int> places)
    {
        if (!places.Any(IsLocked)) return false;
        Say(Texts.Get("Rules.Locked.Refused"));
        return true;
    }

    /// <summary>A rule's switch.</summary>
    public void SetRuleOn(int index, bool on)
    {
        if (RefusedForLocked([index])) { Refresh(); return; }
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
        if (RefusedForLocked([index, other])) return;
        Swap(index, other);
        SelectOnly(other);
        Reveal(other);
        SaveRules();
    }

    /// <summary>A rule's +: a copy of it below it, selected.</summary>
    public void DuplicateRule(int index)
    {
        if (RefusedForLocked([index])) return;
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
        // a rule cannot be moved past one the Armory made, nor can one of those move
        if (RefusedForLocked(picked.Concat(picked.Select(place => place + direction)))) return;
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

    /// <summary>
    /// Ctrl+Up and Ctrl+Down, or a heading's arrows, when group headings are selected: each of those groups moves past
    /// the group above or below it, with all its rules, so no rule changes group. Rules before the first group belong to
    /// none, so a group never moves above them. Other selected rules stay selected where they go.
    /// </summary>
    public void MoveSelectedGroups(int direction)
    {
        if (OpenPreset?.IsArmoryPreset == true && RefusedForLocked(Enumerable.Range(0, Rules.Count))) return;
        List<(int Start, int End)> groups = GroupBlocks();
        var picked = new HashSet<int>(SelectedRules);
        List<JsonNode?> selected = [.. picked.Select(place => Rules[place])];
        JsonNode? current = CurrentRule < Rules.Count ? Rules[CurrentRule] : null;
        var held = new HashSet<int>(); // groups that stay where they are, by their place in the list of groups
        IEnumerable<int> order = direction < 0 ? Enumerable.Range(0, groups.Count) : Enumerable.Range(0, groups.Count).Reverse();
        foreach (int group in order)
        {
            if (!picked.Contains(groups[group].Start)) continue;
            int other = group + direction;
            if (other < 0 || other >= groups.Count || held.Contains(other))
            {
                held.Add(group);
                continue;
            }
            SwapBlocks(groups, Math.Min(group, other));
            groups = GroupBlocks();
            held.Add(other);
        }
        selectedRules.Clear();
        foreach (JsonNode? rule in selected) selectedRules.Add(Rules.IndexOf(rule));
        CurrentRule = current is null ? CurrentRule : Rules.IndexOf(current);
        ruleAnchor = CurrentRule;
        if (CurrentRule < Rules.Count) Reveal(CurrentRule);
        SaveRules();
    }

    /// <summary>Each group in the list, as the place of its heading and of its last rule, from the top.</summary>
    private List<(int Start, int End)> GroupBlocks()
    {
        var groups = new List<(int Start, int End)>();
        for (int place = 0; place < Rules.Count; place++)
        {
            if (!IsGroupHeading(place)) continue;
            if (groups.Count > 0) groups[^1] = (groups[^1].Start, place - 1);
            groups.Add((place, Rules.Count - 1));
        }
        return groups;
    }

    /// <summary>Swaps a group with the group below it, each with all its rules.</summary>
    private void SwapBlocks(List<(int Start, int End)> groups, int upper)
    {
        (int upperStart, int upperEnd) = groups[upper];
        (int lowerStart, int lowerEnd) = groups[upper + 1];
        List<JsonNode?> upperRules = TakeRules(upperStart, upperEnd);
        // the lower group's rules are now where the upper group's began
        List<JsonNode?> lowerRules = TakeRules(upperStart, upperStart + lowerEnd - lowerStart);
        int at = upperStart;
        foreach (JsonNode? rule in lowerRules.Concat(upperRules)) Rules.Insert(at++, rule);
    }

    /// <summary>Takes the rules from one place to another, both included, out of the list.</summary>
    private List<JsonNode?> TakeRules(int first, int last)
    {
        var taken = new List<JsonNode?>();
        for (int place = first; place <= last; place++)
        {
            taken.Add(Rules[first]);
            Rules.RemoveAt(first);
        }
        return taken;
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
        if (picked.Count == 0 || RefusedForLocked(picked)) return;
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
        if (RefusedForLocked(SelectedRules)) return;
        foreach (int place in SelectedRules) Rules[place]!["Enabled"] = on;
        SaveRules();
    }

    /// <summary>Whether a group heading is among the selected rules: then whole groups move.</summary>
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
        if (places.Count == 0 || RefusedForLocked(places)) return;
        string label = places.Count == 1 ? Texts.Get("Rules.Rule.Named", ("rule", RuleName(places[0]))) : CountRules(places.Count);
        int first = places.Min();
        foreach (int place in places.OrderDescending()) Rules.RemoveAt(place);
        SelectOnly(Math.Max(0, Math.Min(first, Rules.Count - 1)));
        if (Rules.Count == 0) selectedRules.Clear();
        SaveRules();
        Say(Texts.Get("Rules.Deleted", ("what", label)));
    }

    /// <summary>
    /// "New rule": a new rule of a type after the selected one, selected. With no preset of the user's own open, one is
    /// made to hold it (see <see cref="NeedsPresetForNewRules"/>).
    /// </summary>
    public void NewRule(string kind) => AddRule(RuleLoading.NewRule(kind));

    /// <summary>A rule made elsewhere (such as the Units tab's "New Rule for This Unit"), added as <see cref="NewRule"/> adds one.</summary>
    public void AddRule(JsonObject rule)
    {
        bool madePreset = NeedsPresetForNewRules;
        string madeNote = madePreset ? MadePresetNoteForOpen() : "";
        if (madePreset) MakePresetForRules();
        int at = Rules.Count == 0 ? 0 : Math.Min(CurrentRule + 1, Rules.Count);
        Rules.Insert(at, rule);
        SelectOnly(at);
        Reveal(at);
        SaveRules();
        if (madePreset) Say(madeNote);
    }

    /// <summary>
    /// Whether rules added now need a preset made for them: none is open, or an Armory preset is. An Armory preset runs
    /// only the rules the Armory made, each of which names the file it is for, so a rule added there would never run.
    /// </summary>
    private bool NeedsPresetForNewRules => OpenPreset is null || OpenPreset.IsArmoryPreset;

    /// <summary>What to say when a preset was made for new rules: why, when an Armory preset was open, then what to do with it.</summary>
    private string MadePresetNoteForOpen() =>
        OpenPreset?.IsArmoryPreset == true || OpenEmptyArmoryPart is not null ? Texts.Get("Rules.Armory.OwnRulesElsewhere") + " " + MadePresetNote : MadePresetNote;

    /// <summary>A rule's name, or "(unnamed)".</summary>
    public string RuleName(int index) => (string?)Rules[index]?["Name"] is { Length: > 0 } name ? name : Texts.Get("Rules.Rule.Unnamed");

    /// <summary>Whether a rule is a Group's heading.</summary>
    public bool IsGroupHeading(int index) => (string?)Rules[index]?["Kind"] == "Group";

    /// <summary>Some rules' places, with every rule under a group heading among them, up to the next heading.</summary>
    public List<int> WithRulesUnderGroups(IReadOnlyList<int> places)
    {
        var all = new SortedSet<int>();
        foreach (int place in places)
        {
            all.Add(place);
            if (!IsGroupHeading(place)) continue;
            for (int under = place + 1; under < Rules.Count && !IsGroupHeading(under); under++) all.Add(under);
        }
        return [.. all];
    }

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

    /// <summary>
    /// Saves a change to the open preset's rules, and shows it. While the rule window has a rule open, the change is
    /// only shown in the window: it is saved on Done.
    /// </summary>
    public void SaveRules()
    {
        if (IsEditingRule) { Refresh(); return; }
        // the save tells every tab, this one too: its lists are drawn once, after the save, not also as it is told
        isSavingRules = true;
        try
        {
            Data.Save(OpenMod);
        }
        finally
        {
            isSavingRules = false;
        }
        Refresh();
    }

    /// <summary>Set while <see cref="SaveRules"/> saves: the notice that the mods changed does not draw the lists, as it then does.</summary>
    private bool isSavingRules;

    // ---- the rule window: changes kept until Done ----

    /// <summary>The open rule as it was when the rule window opened; null while the window is closed.</summary>
    private JsonObject? ruleBeforeEditing;

    /// <summary>
    /// Whether the rule window has a rule open. Its changes are written into the rule as they are made, but not saved,
    /// and the Rules tab's lists are not drawn again: saving the whole mod on every key made typing slow.
    /// </summary>
    public bool IsEditingRule => ruleBeforeEditing is not null;

    /// <summary>Whether the rule open in the rule window differs from what it was when the window opened.</summary>
    public bool HasUnsavedRuleChanges => OpenRule is JsonObject rule && ruleBeforeEditing is not null && !JsonNode.DeepEquals(rule, ruleBeforeEditing);

    /// <summary>The rule window opens on the open rule: keeps a copy of it, so Cancel can put it back.</summary>
    public void StartEditingRule()
    {
        if (OpenRule is JsonObject rule) ruleBeforeEditing = (JsonObject)rule.DeepClone();
    }

    /// <summary>Done: the rule window's changes are saved, and the Rules tab shows them.</summary>
    public void FinishEditingRule()
    {
        if (!IsEditingRule) return;
        bool isChanged = HasUnsavedRuleChanges;
        ruleBeforeEditing = null;
        if (isChanged) SaveRules();
        else Refresh();
    }

    /// <summary>Cancel: the open rule is put back as it was when the rule window opened.</summary>
    public void CancelEditingRule()
    {
        if (ruleBeforeEditing is not JsonObject before) return;
        ruleBeforeEditing = null;
        // put back in place, so the rule list and the windows still hold the same rule
        if (OpenRule is JsonObject rule) PutBack(rule, before);
        Refresh();
    }

    /// <summary>Makes a JSON object hold what another holds, in the same order.</summary>
    public static void PutBack(JsonObject target, JsonObject source)
    {
        target.Clear();
        foreach ((string name, JsonNode? value) in source) target[name] = value?.DeepClone();
    }

    // ---- showing it ----

    /// <summary>Works out again what is open and selected, and the rule list's lines (the HTML app's renderPresetList and renderRuleList).</summary>
    public void Refresh()
    {
        if (isSavingRules) return;
        // the presets that can be open: the mod's own, then its Armory presets, listed apart
        IReadOnlyList<ModPreset> listed = [.. Presets, .. ArmoryPresetsOfOpenMod];
        if (!ReferenceEquals(OpenMod, shownMod))
        {
            // another mod: its first preset, unless the mod was only renamed
            if (shownMod is null || OpenMod.Name != shownModName)
            {
                OpenPreset = null;
                OpenEmptyArmoryPart = null;
            }
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
        // a part of the Armory open with no changes: its preset opens once the first change is made
        if (OpenEmptyArmoryPart is ArmoryPart emptyPart && ArmoryPresets.Of(OpenMod, emptyPart) is ModPreset made) OpenPreset = made;
        if (OpenPreset is null && OpenEmptyArmoryPart is null) { OpenPreset = listed.FirstOrDefault(); CurrentRule = 0; }
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
            rows.Add(new RuleRow { Index = i, Rule = rule, IsSelected = selectedRules.Contains(i), IsInGroup = !isGroup && inGroup, IsOff = off, HiddenCount = hidden, IsLocked = IsLocked(i) });
        }
        ShowRows(rows);
        Changed();
    }

    /// <summary>
    /// Takes the rule list's new lines. When they show the same as those on screen but for which are selected (a click),
    /// the lines on screen are kept and only told which are selected: making the whole list again on every click took
    /// up to a quarter of a second with a large mod's rule sets.
    /// </summary>
    private void ShowRows(List<RuleRow> rows)
    {
        if (RuleRows.Count == rows.Count && rows.Zip(RuleRows).All(pair => pair.First.ShowsTheSameAs(pair.Second)))
        {
            for (int i = 0; i < rows.Count; i++) RuleRows[i].IsSelected = rows[i].IsSelected;
            return;
        }
        RuleRows = [.. rows.Select(row => row.NoteShown())];
    }

    private static int IndexIn(IReadOnlyList<ModPreset> list, ModPreset preset)
    {
        for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], preset)) return i;
        return -1;
    }

    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
