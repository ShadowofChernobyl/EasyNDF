using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Engine.Rules;

namespace EasyNdf.App.Files;

/// <summary>
/// The variables rule sets share: a Variable rule's scope is its own rule set ("This Rule Set", as always), every rule
/// set of its mod ("This Mod") or every rule set of every mod of the same game ("All Mods"). A rule's action aimed at
/// a shared variable ("{Name}" as its target) changes it for what runs after: the mods in the Mods tab's order, each
/// mod's rule sets in its list's order (<see cref="Mod.PresetsInOrder"/>), each rule set on its files in the Files tab's order.
/// <para>
/// A file's rules still run file by file (<see cref="FileRunner"/>); what each rule set starts with on each file is
/// worked out here first (<see cref="Build"/>), by running the rule sets that change shared variables in that order,
/// and asked for as each file runs (<see cref="At"/>). With no action aimed at a shared variable, nothing is run here:
/// every rule set starts with the values the Variable rules give.
/// </para>
/// </summary>
/// <remarks>
/// A mod's Armory changes are not run here: a later mod's action that changes a shared variable for each match is
/// counted on the file as the rule sets before it left it.
/// </remarks>
public sealed class SharedVariables
{
    /// <summary>The scopes, as a Variable rule's VarScope names them; none is the rule set's own.</summary>
    public const string RuleSetScope = "RuleSet", ModScope = "Mod", AllModsScope = "AllMods";

    /// <summary>A variable, as its Variable rule sets it up.</summary>
    public sealed record Definition(string Name, string Value, string Scope, Mod Mod, ModPreset RuleSet, JsonObject Rule);

    /// <summary>Two variables of one name where both can be seen: nothing runs until one is renamed.</summary>
    public sealed record Clash(Definition First, Definition Second);

    /// <summary>A file the rule sets run on: what tells it from the others, and its text, read when wanted.</summary>
    public sealed record SharedFile(object Key, Func<string> Text);

    /// <summary>The run of one rule set on one file's text, with the shared variables it starts with.</summary>
    public delegate RunResult RuleSetRun(Mod mod, ModPreset ruleSet, SharedFile file, string text, IReadOnlyDictionary<string, string> shared);

    private readonly IReadOnlyList<Mod> mods;
    private readonly Dictionary<string, string> first = new(StringComparer.Ordinal);
    private readonly List<(int Mod, int RuleSet, int File, Dictionary<string, string> Values)> changes = [];
    private readonly Dictionary<object, int> fileIndex = [];

    private SharedVariables(IReadOnlyList<Mod> mods) => this.mods = mods;

    /// <summary>The variables of one name that can both be seen somewhere, among the mods that run.</summary>
    public IReadOnlyList<Clash> Clashes { get; private set; } = [];

    /// <summary>Nothing shared, for a run of rules on their own.</summary>
    public static SharedVariables None { get; } = new([]);

    // ---- what the rule sets say ----

    /// <summary>A rule set's rules that are on, a switched-off group's left out.</summary>
    public static IEnumerable<JsonObject> RulesThatRun(ModPreset ruleSet)
    {
        bool groupOn = true;
        foreach (JsonNode? node in ruleSet.Rules)
        {
            if (node is not JsonObject rule) continue;
            bool on = rule["Enabled"] is not JsonValue enabled || !enabled.TryGetValue(out bool isOn) || isOn;
            if ((string?)rule["Kind"] == "Group") { groupOn = on; continue; }
            if (groupOn && on) yield return rule;
        }
    }

    /// <summary>A Variable rule's scope; its own rule set when it names none.</summary>
    public static string ScopeOf(JsonObject variableRule) =>
        (string?)variableRule["Params"]?["VarScope"] is ModScope or AllModsScope ? (string)variableRule["Params"]!["VarScope"]! : RuleSetScope;

    /// <summary>Every variable the mods' rule sets set up, in the order they run; the Armory's presets have none.</summary>
    public static List<Definition> DefinitionsOf(IEnumerable<Mod> mods)
    {
        var found = new List<Definition>();
        foreach (Mod mod in mods)
            foreach (ModPreset ruleSet in mod.Presets.Where(each => each.ArmoryPart is null))
                foreach (JsonObject rule in RulesThatRun(ruleSet))
                {
                    if ((string?)rule["Kind"] != "Variable") continue;
                    // "Read from a file": each row of the file is one variable
                    if (((string?)rule["Params"]?["DataFile"] ?? "").Trim() is { Length: > 0 } file)
                    {
                        string? text = DataFiles.Read(file);
                        // a name twice in one file is not a clash: the last row's value counts, as in the run
                        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (text is not null)
                            foreach ((string rowName, string rowValue) in Engine.Rules.RuleRunner.VariableRows(Engine.Rules.TableText.Read(text)))
                                rows[rowName] = rowValue;
                        foreach ((string rowName, string rowValue) in rows) found.Add(new Definition(rowName, rowValue, ScopeOf(rule), mod, ruleSet, rule));
                        continue;
                    }
                    string name = ((string?)rule["Params"]?["VarName"] ?? "").Trim();
                    if (name != "") found.Add(new Definition(name, ((string?)rule["Params"]?["Value"] ?? "").Trim(), ScopeOf(rule), mod, ruleSet, rule));
                }
        return found;
    }

    /// <summary>Whether a variable can be used in a rule set: its own, its mod's shared ones, and the ones shared by all mods of its game.</summary>
    public static bool Reaches(Definition variable, Mod mod, ModPreset ruleSet) => variable.Scope switch
    {
        AllModsScope => variable.Mod.Game == mod.Game,
        ModScope => ReferenceEquals(variable.Mod, mod),
        _ => ReferenceEquals(variable.RuleSet, ruleSet),
    };

    /// <summary>Whether two variables can both be seen in some rule set.</summary>
    private static bool Overlap(Definition a, Definition b)
    {
        if (a.Mod.Game != b.Mod.Game) return false;
        if (a.Scope == AllModsScope || b.Scope == AllModsScope) return true;
        if (!ReferenceEquals(a.Mod, b.Mod)) return false;
        return a.Scope == ModScope || b.Scope == ModScope || ReferenceEquals(a.RuleSet, b.RuleSet);
    }

    /// <summary>The variables of one name that can both be seen somewhere.</summary>
    public static List<Clash> ClashesAmong(IEnumerable<Mod> mods)
    {
        var clashes = new List<Clash>();
        foreach (var sameName in DefinitionsOf(mods).GroupBy(each => each.Name, StringComparer.Ordinal))
        {
            List<Definition> all = [.. sameName];
            for (int a = 0; a < all.Count; a++)
                for (int b = a + 1; b < all.Count; b++)
                    if (Overlap(all[a], all[b])) clashes.Add(new Clash(all[a], all[b]));
        }
        return clashes;
    }

    /// <summary>
    /// The variable a new name would clash with, for a Variable rule being given that name and scope in a rule set;
    /// null when the name is free. The rule itself, as it was, is not counted.
    /// </summary>
    public static Definition? TakenBy(string name, string scope, Mod mod, ModPreset ruleSet, JsonObject? rule, IEnumerable<Mod> mods)
    {
        var wanted = new Definition(name.Trim(), "", scope, mod, ruleSet, rule ?? []);
        if (wanted.Name == "") return null;
        return DefinitionsOf(mods.Append(mod).Distinct())
            .FirstOrDefault(other => other.Name == wanted.Name && !ReferenceEquals(other.Rule, rule) && Overlap(wanted, other));
    }

    /// <summary>The shared variables an action in a rule set may be aimed at, and the rule set's own, in the order they are set up.</summary>
    public static List<Definition> InReachOf(Mod mod, ModPreset ruleSet, IEnumerable<Mod> mods) =>
        [.. DefinitionsOf(mods.Append(mod).Distinct()).Where(variable => Reaches(variable, mod, ruleSet))];

    /// <summary>The variable an action is aimed at, when its target is "{Name}"; null otherwise.</summary>
    public static string? AimedAt(JsonNode? action)
    {
        bool custom = action?["ManualOverride"] is JsonValue manual && manual.TryGetValue(out bool isCustom) && isCustom;
        string target = (action?[custom ? "CustomTarget" : "Target"] is JsonValue value && value.TryGetValue(out string? text) ? text : "").Trim();
        return target.Length >= 3 && target[0] == '{' && target[^1] == '}' ? target[1..^1] : null;
    }

    /// <summary>Whether a rule set has an Edit rule with an action aimed at one of these names.</summary>
    private static bool Changes(ModPreset ruleSet, HashSet<string> names) =>
        RulesThatRun(ruleSet).Any(rule => (string?)rule["Kind"] is null or "Edit" && rule["Actions"] is JsonArray actions
            && actions.Any(action => AimedAt(action) is string name && names.Contains(name)));

    // ---- the values, as the rule sets run ----

    private static string KeyOf(Definition variable) =>
        (variable.Scope == AllModsScope ? "*" + variable.Mod.Game : variable.Mod.Name) + "\u0001" + variable.Name;

    /// <summary>The shared variables a mod's rule sets see, by name, out of all of them by key.</summary>
    private Dictionary<string, string> SeenBy(Mod mod, Dictionary<string, string> values)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        string all = "*" + mod.Game + "\u0001", own = mod.Name + "\u0001";
        foreach (var (key, value) in values)
        {
            if (key.StartsWith(all, StringComparison.Ordinal)) seen[key[all.Length..]] = value;
            else if (key.StartsWith(own, StringComparison.Ordinal)) seen[key[own.Length..]] = value;
        }
        return seen;
    }

    /// <summary>
    /// The shared variables a rule set starts with on a file, by name: as the Variable rules give them, or as the last
    /// rule set before it that changed one left them.
    /// </summary>
    /// <param name="ruleSet">The rule set; null for what runs after all the mod's rule sets (its Armory changes).</param>
    /// <param name="file">The file's key (<see cref="SharedFile.Key"/>); a file not among those given counts as after them all.</param>
    public IReadOnlyDictionary<string, string> At(Mod mod, ModPreset? ruleSet, object? file)
    {
        if (first.Count == 0) return first;
        int modAt = IndexOfMod(mod), ruleSetAt = ruleSet is null ? int.MaxValue : mod.PresetsInOrder().IndexOf(ruleSet);
        int fileAt = file is not null && fileIndex.TryGetValue(file, out int index) ? index : int.MaxValue;
        Dictionary<string, string> values = first;
        foreach (var change in changes)
        {
            if ((change.Mod, change.RuleSet, change.File).CompareTo((modAt, ruleSetAt, fileAt)) >= 0) break;
            values = change.Values;
        }
        return SeenBy(mod, values);
    }

    private int IndexOfMod(Mod mod)
    {
        for (int at = 0; at < mods.Count; at++)
            if (ReferenceEquals(mods[at], mod)) return at;
        return int.MaxValue;
    }

    /// <summary>
    /// Works out the shared variables for a run: which there are, whether two clash, and, when an action changes one,
    /// its value as each rule set starts on each file.
    /// </summary>
    /// <param name="mods">The mods that run, in the order they run.</param>
    /// <param name="files">The files, in the Files tab's order.</param>
    /// <param name="ruleSetsFor">A mod's rule sets that run on a file; none for a file of another game.</param>
    /// <param name="run">Runs one rule set on a file's text.</param>
    public static SharedVariables Build(IReadOnlyList<Mod> mods, IReadOnlyList<SharedFile> files,
        Func<SharedFile, Mod, IReadOnlyList<ModPreset>> ruleSetsFor, RuleSetRun run)
    {
        var made = new SharedVariables(mods);
        List<Definition> shared = [.. DefinitionsOf(mods).Where(variable => variable.Scope != RuleSetScope)];
        made.Clashes = ClashesAmong(mods);
        if (shared.Count == 0 || made.Clashes.Count > 0) return made;
        foreach (Definition variable in shared) made.first[KeyOf(variable)] = variable.Value;
        for (int at = 0; at < files.Count; at++) made.fileIndex[files[at].Key] = at;

        // the rule sets with an action aimed at a shared variable they can reach
        var changing = new HashSet<ModPreset>(ReferenceEqualityComparer.Instance);
        foreach (Mod mod in mods)
            foreach (ModPreset ruleSet in mod.Presets.Where(each => each.ArmoryPart is null))
            {
                HashSet<string> names = [.. shared.Where(variable => Reaches(variable, mod, ruleSet)).Select(variable => variable.Name)];
                if (names.Count > 0 && Changes(ruleSet, names)) changing.Add(ruleSet);
            }
        if (changing.Count == 0) return made;

        // each rule set's files, and for each file the last place in the order a rule set changes a variable on it:
        // up to there the file's rule sets are run, each on what the one before left
        var filesOf = new Dictionary<ModPreset, List<int>>(ReferenceEqualityComparer.Instance);
        var last = new (int Mod, int RuleSet)?[files.Count];
        for (int modAt = 0; modAt < mods.Count; modAt++)
            for (int fileAt = 0; fileAt < files.Count; fileAt++)
                foreach (ModPreset ruleSet in ruleSetsFor(files[fileAt], mods[modAt]))
                {
                    if (!filesOf.TryGetValue(ruleSet, out List<int>? its)) filesOf[ruleSet] = its = [];
                    its.Add(fileAt);
                    if (changing.Contains(ruleSet)) last[fileAt] = (modAt, mods[modAt].PresetsInOrder().IndexOf(ruleSet));
                }

        var values = new Dictionary<string, string>(made.first, StringComparer.Ordinal);
        var texts = new string?[files.Count];
        var stopped = new bool[files.Count];
        for (int modAt = 0; modAt < mods.Count; modAt++)
        {
            Mod mod = mods[modAt];
            List<ModPreset> inOrder = mod.PresetsInOrder();
            for (int ruleSetAt = 0; ruleSetAt < inOrder.Count; ruleSetAt++)
            {
                ModPreset ruleSet = inOrder[ruleSetAt];
                if (!filesOf.TryGetValue(ruleSet, out List<int>? its)) continue;
                foreach (int fileAt in its)
                {
                    if (stopped[fileAt] || last[fileAt] is not { } until || (modAt, ruleSetAt).CompareTo(until) > 0) continue;
                    RunResult result;
                    try
                    {
                        texts[fileAt] ??= files[fileAt].Text();
                        result = run(mod, ruleSet, files[fileAt], texts[fileAt]!, made.SeenBy(mod, values));
                    }
                    catch (Exception notRead) when (notRead is System.IO.IOException or UnauthorizedAccessException or EasyNdf.Engine.Ndf.NdfParseException)
                    {
                        stopped[fileAt] = true;
                        texts[fileAt] = null;
                        continue;
                    }
                    if (changing.Contains(ruleSet))
                    {
                        bool changed = false;
                        foreach (Definition variable in shared.Where(variable => Reaches(variable, mod, ruleSet)))
                            if (result.Variables.TryGetValue(variable.Name, out string? now) && values[KeyOf(variable)] != now)
                            {
                                values[KeyOf(variable)] = now;
                                changed = true;
                            }
                        if (changed) made.changes.Add((modAt, ruleSetAt, fileAt, new Dictionary<string, string>(values, StringComparer.Ordinal)));
                    }
                    // the text is kept only while a later rule set on the file still has to run on it
                    bool wasLast = (modAt, ruleSetAt).CompareTo(until) == 0;
                    if (result.Text is null || result.ParseError != "") stopped[fileAt] = true;
                    texts[fileAt] = wasLast || stopped[fileAt] ? null : result.Text;
                }
            }
        }
        return made;
    }
}
