using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Game;

namespace EasyNdf.App.Mods;

/// <summary>
/// The mods in the order the Mods tab lists them, and which are on. Every mod that is on runs on export and Preview,
/// one after the other in that order, so a mod lower in the list runs later and wins where two change the same value
/// (<see cref="ModConflicts"/>). The open mod is the one being edited; it runs only when it is on, like any other.
/// Kept with the app's state by mod name ("modOrder", "modsOn"), not in the mods, so a mod file shared stays the same.
/// The list can have group headings, as a rule list has ("modGroups": the order again, with a heading where each group
/// starts): a mod is in the group whose heading is above it, and runs only while its group is on too.
/// </summary>
public static class ModStack
{
    private const string OrderKey = "modOrder", OnKey = "modsOn", LayoutKey = "modGroups";

    private static AppData Data => AppData.Current;

    /// <summary>Every mod, in the Mods tab's order: those placed first, then any not placed yet, in the order they were made.</summary>
    public static IReadOnlyList<Mod> Ordered => [.. Layout.OfType<Mod>()];

    /// <summary>
    /// The Mods tab's list: each mod, in order, and a <see cref="ModGroup"/> heading where each group starts. Without
    /// groups, the order kept ("modOrder"); a mod not placed yet comes last.
    /// </summary>
    public static IReadOnlyList<object> Layout
    {
        get
        {
            var layout = new List<object>();
            var placed = new HashSet<Mod>();
            if (Data.State[LayoutKey] is JsonArray kept)
                foreach (JsonNode? entry in kept)
                {
                    if (entry is JsonObject heading && (string?)heading["group"] is string name)
                        layout.Add(new ModGroup(name, heading["on"]?.GetValue<bool>() != false, heading["folded"]?.GetValue<bool>() == true));
                    else if (entry is JsonValue value && value.TryGetValue(out string? modName) && Data.ModStore.Find(modName) is Mod mod && placed.Add(mod))
                        layout.Add(mod);
                }
            else
                foreach (string name in NamesIn(OrderKey))
                    if (Data.ModStore.Find(name) is Mod mod && placed.Add(mod)) layout.Add(mod);
            foreach (Mod mod in Data.Mods)
                if (placed.Add(mod)) layout.Add(mod);
            return layout;
        }
    }

    /// <summary>The group a mod is in: the heading above it; null for none.</summary>
    public static ModGroup? GroupOf(Mod mod)
    {
        ModGroup? group = null;
        foreach (object entry in Layout)
            if (entry is ModGroup heading) group = heading;
            else if (ReferenceEquals(entry, mod)) return group;
        return null;
    }

    /// <summary>Whether a mod runs: it is on, and so is its group.</summary>
    public static bool Runs(Mod mod) => IsOn(mod) && GroupOf(mod)?.IsOn != false;

    /// <summary>The mods that run, in the order they run: those on, in a group that is on.</summary>
    public static IReadOnlyList<Mod> Running
    {
        get
        {
            var running = new List<Mod>();
            bool groupOn = true;
            foreach (object entry in Layout)
                if (entry is ModGroup group) groupOn = group.IsOn;
                else if (entry is Mod mod && groupOn && IsOn(mod)) running.Add(mod);
            return running;
        }
    }

    /// <summary>Keeps a new list, its mods' order also as "modOrder", for what reads only that.</summary>
    private static void SaveLayout(List<object> layout)
    {
        if (layout.OfType<ModGroup>().Any())
            Data.State[LayoutKey] = new JsonArray([.. layout.Select(entry => entry is ModGroup group
                ? new JsonObject { ["group"] = group.Name, ["on"] = group.IsOn, ["folded"] = group.IsFolded }
                : (JsonNode?)((Mod)entry).Name)]);
        else Data.State.Remove(LayoutKey);
        Data.State[OrderKey] = new JsonArray([.. layout.OfType<Mod>().Select(mod => (JsonNode?)mod.Name)]);
        Data.SaveState();
        ModConflictList.Forget();
        Data.TellModsChanged();
    }

    /// <summary>Whether a group of that name is in the list.</summary>
    public static bool HasGroup(string name) => Layout.OfType<ModGroup>().Any(group => group.Name == name);

    /// <summary>A new group, its heading just above a mod (so the mod and those under it are in it), or at the end.</summary>
    public static void AddGroup(string name, Mod? above)
    {
        List<object> layout = [.. Layout];
        int at = above is null ? -1 : layout.IndexOf(above);
        layout.Insert(at < 0 ? layout.Count : at, new ModGroup(name, true, false));
        SessionLog.Write("Mods", "New group " + name);
        SaveLayout(layout);
    }

    /// <summary>A group's heading changed: renamed, switched, folded.</summary>
    public static void ChangeGroup(string name, ModGroup changed)
    {
        List<object> layout = [.. Layout];
        int at = layout.FindIndex(entry => entry is ModGroup group && group.Name == name);
        if (at < 0) return;
        layout[at] = changed;
        SaveLayout(layout);
    }

    /// <summary>A group's heading deleted: its mods stay where they are, in the group above, or in none.</summary>
    public static void DeleteGroup(string name)
    {
        List<object> layout = [.. Layout];
        layout.RemoveAll(entry => entry is ModGroup group && group.Name == name);
        SaveLayout(layout);
    }

    /// <summary>Moves a mod to the end of a group, or, for none, above the first heading.</summary>
    public static void MoveToGroup(Mod mod, string? name)
    {
        List<object> layout = [.. Layout];
        layout.Remove(mod);
        int at;
        if (name is null) at = layout.FindIndex(entry => entry is ModGroup);
        else
        {
            at = layout.FindIndex(entry => entry is ModGroup group && group.Name == name);
            if (at >= 0) for (at++; at < layout.Count && layout[at] is not ModGroup; at++) { }
        }
        layout.Insert(at < 0 ? layout.Count : at, mod);
        SaveLayout(layout);
    }

    /// <summary>
    /// Moves a group, its heading and its mods, above the group before it (-1) or below the one after it (+1); false when
    /// it is already first or last. Mods in no group stay above the first heading.
    /// </summary>
    public static bool MoveGroup(string name, int by)
    {
        List<object> layout = [.. Layout];
        List<int> heads = [.. layout.Select((entry, i) => (entry, i)).Where(pair => pair.entry is ModGroup).Select(pair => pair.i)];
        int which = heads.FindIndex(i => ((ModGroup)layout[i]).Name == name), other = which + by;
        if (which < 0 || other < 0 || other >= heads.Count) return false;
        int first = Math.Min(which, other);
        int start = heads[first], middle = heads[first + 1], end = first + 2 < heads.Count ? heads[first + 2] : layout.Count;
        // the two groups side by side change places
        List<object> earlier = layout.GetRange(start, middle - start), later = layout.GetRange(middle, end - middle);
        layout.RemoveRange(start, end - start);
        layout.InsertRange(start, [.. later, .. earlier]);
        SaveLayout(layout);
        return true;
    }

    /// <summary>The mods of a game that are on, in the order they run: those that run on a file of that game.</summary>
    public static IReadOnlyList<Mod> RunningFor(GameTitle game) => [.. Running.Where(mod => mod.Game == game)];

    /// <summary>
    /// The mods that are on whose game has files among these, in the order they run: those an export of the files
    /// runs. A mod whose game has no file there does not run.
    /// </summary>
    public static IReadOnlyList<Mod> RunningOn(IEnumerable<FileEntry> files)
    {
        HashSet<GameTitle> games = [.. files.Select(file => file.Game)];
        return [.. Running.Where(mod => games.Contains(mod.Game))];
    }

    /// <summary>
    /// The first start since mods could run together: only the open mod is on, as only the open mod ran before. From
    /// then on, opening a mod does not switch it on or off. Called once at start-up, on the app's own thread, so that
    /// <see cref="IsOn"/> never writes (an export asks it from another thread).
    /// </summary>
    public static void Start()
    {
        if (Data.State[OnKey] is JsonArray) return;
        Data.State[OnKey] = new JsonArray(Data.OpenMod.Name);
        Data.SaveState();
    }

    /// <summary>Whether a mod is on. Before <see cref="Start"/> has run, only the open mod is.</summary>
    public static bool IsOn(Mod mod)
    {
        if (Data.State[OnKey] is not JsonArray) return mod.Name == Data.OpenMod.Name;
        return NamesIn(OnKey).Contains(mod.Name);
    }

    /// <summary>
    /// Switches a mod on or off. What was found of the conflicts is forgotten: the files must be run again to know them.
    /// </summary>
    public static void SetOn(Mod mod, bool on)
    {
        SessionLog.Write("Mods", (on ? "Switched on " : "Switched off ") + mod.Name);
        List<string> names = [.. Ordered.Where(each => ReferenceEquals(each, mod) ? on : IsOn(each)).Select(each => each.Name)];
        Data.State[OnKey] = new JsonArray([.. names.Select(name => (JsonNode?)name)]);
        Data.SaveState();
        ModConflictList.Forget();
        Data.TellModsChanged();
    }

    /// <summary>A mod deleted is let go of: a mod made later under its name starts at the end, and off.</summary>
    public static void Deleted(string name)
    {
        foreach (string key in new[] { OrderKey, OnKey, LayoutKey })
            if (Data.State[key] is JsonArray names)
                Data.State[key] = new JsonArray([.. names.Where(each => !(each is JsonValue value && value.TryGetValue(out string? text) && text == name)).Select(each => each?.DeepClone())]);
        Data.SaveState();
        ModConflictList.Forget();
    }

    /// <summary>
    /// Moves a mod up (-1) or down (+1) in the list, past one line: a mod or a group's heading, so a mod moved past a
    /// heading goes into the group above or below, as a rule does; false when it is already first or last.
    /// </summary>
    public static bool Move(Mod mod, int by)
    {
        List<object> layout = [.. Layout];
        int at = layout.IndexOf(mod), to = at + by;
        if (at < 0 || to < 0 || to >= layout.Count) return false;
        layout.RemoveAt(at);
        layout.Insert(to, mod);
        SaveLayout(layout);
        return true;
    }

    /// <summary>The Armory's "Mod Being Edited" choices, by game id: kept only once a user chooses one there.</summary>
    private const string ArmoryKey = "armoryMods";

    /// <summary>
    /// The mod chosen in the Armory to save a game's changes in, while it can take them: it is still there, for that
    /// game, and runs. Null when none was chosen, or it cannot take them now; the Armory then picks one itself (the open
    /// mod, or the last that runs), as it did before there was a choice.
    /// </summary>
    public static Mod? ArmoryChoice(GameTitle game) =>
        Data.State[ArmoryKey] is JsonObject byGame && byGame[game.Id] is JsonValue value && value.TryGetValue(out string? name)
            && Data.Mods.FirstOrDefault(mod => mod.Name == name) is Mod chosen && chosen.Game == game && Runs(chosen)
            ? chosen : null;

    /// <summary>Keeps the mod chosen in the Armory for its game's changes.</summary>
    public static void ChooseForArmory(Mod mod)
    {
        if (Data.State[ArmoryKey] is not JsonObject byGame) Data.State[ArmoryKey] = byGame = new JsonObject();
        byGame[mod.Game.Id] = mod.Name;
        Data.SaveState();
    }

    /// <summary>
    /// Lets go of the Armory's choice for a game: a mod that runs was opened on the Mods tab, and the Armory changes
    /// that one, as it always did. Of the two ways to choose, the last one used counts.
    /// </summary>
    public static void ForgetArmoryChoice(GameTitle game)
    {
        if (Data.State[ArmoryKey] is not JsonObject byGame || !byGame.Remove(game.Id)) return;
        Data.SaveState();
    }

    /// <summary>
    /// A mod renamed keeps its place and whether it is on, its folders and its property scan picks: they are kept by
    /// name. (The folders were left under the old name before.) The conflicts found named the old name, so they are
    /// forgotten.
    /// </summary>
    public static void Renamed(string oldName, string newName)
    {
        foreach (string key in new[] { OrderKey, OnKey, LayoutKey })
            if (Data.State[key] is JsonArray names)
                Data.State[key] = new JsonArray([.. names.Select(name => name is JsonValue value && value.TryGetValue(out string? text)
                    ? (JsonNode?)(text == oldName ? newName : text) : name?.DeepClone())]);
        foreach (string key in new[] { "exportFolders", "gameMods", "scanPick" })
            if (Data.State[key] is JsonObject byMod && byMod[oldName] is JsonNode kept)
            {
                byMod.Remove(oldName);
                byMod[newName] = kept.DeepClone();
            }
        if (Data.State[ArmoryKey] is JsonObject byGame)
            foreach (string game in byGame.Select(pair => pair.Key).ToList())
                if (byGame[game] is JsonValue value && value.TryGetValue(out string? chosen) && chosen == oldName) byGame[game] = newName;
        Data.SaveState();
        ModConflictList.Forget();
    }

    /// <summary>The names kept under a key of the state; none when it has none.</summary>
    private static List<string> NamesIn(string key) =>
        Data.State[key] is JsonArray names ? [.. names.OfType<JsonValue>().Select(name => name.TryGetValue(out string? text) ? text : null).OfType<string>()] : [];

    /// <summary>The order and the mods on, as one text, for Undo (see <see cref="UndoHistory"/>).</summary>
    public static JsonObject Saved() => new()
    {
        [OrderKey] = Data.State[OrderKey]?.DeepClone(),
        [OnKey] = Data.State[OnKey]?.DeepClone(),
        [LayoutKey] = Data.State[LayoutKey]?.DeepClone(),
    };

    /// <summary>
    /// Puts back an order and mods on kept by <see cref="Saved"/>. When they differ from now, the conflicts found were
    /// for the order undone, so they are forgotten.
    /// </summary>
    public static void PutBack(JsonObject saved)
    {
        bool isDifferent = Saved().ToJsonString() != saved.ToJsonString();
        Data.State[OrderKey] = saved[OrderKey]?.DeepClone();
        Data.State[OnKey] = saved[OnKey]?.DeepClone();
        if (saved[LayoutKey] is JsonNode layout) Data.State[LayoutKey] = layout.DeepClone();
        else Data.State.Remove(LayoutKey);
        if (isDifferent) ModConflictList.Forget();
    }
}

/// <summary>A group's heading in the Mods tab's list: its name, whether it is on (its mods run only then), whether folded.</summary>
public sealed record ModGroup(string Name, bool IsOn, bool IsFolded);
