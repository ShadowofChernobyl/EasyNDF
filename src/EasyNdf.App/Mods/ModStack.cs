using System.Text.Json.Nodes;
using EasyNdf.Data;

namespace EasyNdf.App.Mods;

/// <summary>
/// The mods in the order the Mods tab lists them, and which are on. Every mod that is on runs on export and Preview,
/// one after the other in that order, so a mod lower in the list runs later and wins where two change the same value
/// (<see cref="ModConflicts"/>). The open mod is the one being edited; it runs only when it is on, like any other.
/// Kept with the app's state by mod name ("modOrder", "modsOn"), not in the mods, so a mod file shared stays the same.
/// </summary>
public static class ModStack
{
    private const string OrderKey = "modOrder", OnKey = "modsOn";

    private static AppData Data => AppData.Current;

    /// <summary>Every mod, in the Mods tab's order: those placed first, then any not placed yet, in the order they were made.</summary>
    public static IReadOnlyList<Mod> Ordered
    {
        get
        {
            var ordered = new List<Mod>();
            foreach (string name in NamesIn(OrderKey))
                if (Data.ModStore.Find(name) is Mod mod && !ordered.Contains(mod)) ordered.Add(mod);
            foreach (Mod mod in Data.Mods)
                if (!ordered.Contains(mod)) ordered.Add(mod);
            return ordered;
        }
    }

    /// <summary>The mods that are on, in the order they run.</summary>
    public static IReadOnlyList<Mod> Running => [.. Ordered.Where(IsOn)];

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
        List<string> names = [.. Ordered.Where(each => ReferenceEquals(each, mod) ? on : IsOn(each)).Select(each => each.Name)];
        Data.State[OnKey] = new JsonArray([.. names.Select(name => (JsonNode?)name)]);
        Data.SaveState();
        ModConflictList.Forget();
        Data.TellModsChanged();
    }

    /// <summary>A mod deleted is let go of: a mod made later under its name starts at the end, and off.</summary>
    public static void Deleted(string name)
    {
        foreach (string key in new[] { OrderKey, OnKey })
            if (Data.State[key] is JsonArray names)
                Data.State[key] = new JsonArray([.. names.Select(each => (string?)each).Where(each => each != name).Select(each => (JsonNode?)each)]);
        Data.SaveState();
        ModConflictList.Forget();
    }

    /// <summary>Moves a mod up (-1) or down (+1) in the order; false when it is already first or last.</summary>
    public static bool Move(Mod mod, int by)
    {
        List<Mod> ordered = [.. Ordered];
        int at = ordered.IndexOf(mod), to = at + by;
        if (at < 0 || to < 0 || to >= ordered.Count) return false;
        ordered.RemoveAt(at);
        ordered.Insert(to, mod);
        Data.State[OrderKey] = new JsonArray([.. ordered.Select(each => (JsonNode?)each.Name)]);
        Data.SaveState();
        ModConflictList.Forget();
        Data.TellModsChanged();
        return true;
    }

    /// <summary>
    /// A mod renamed keeps its place and whether it is on, its folders and its property scan picks: they are kept by
    /// name. (The folders were left under the old name before.) The conflicts found named the old name, so they are
    /// forgotten.
    /// </summary>
    public static void Renamed(string oldName, string newName)
    {
        foreach (string key in new[] { OrderKey, OnKey })
            if (Data.State[key] is JsonArray names)
                Data.State[key] = new JsonArray([.. names.Select(name => (JsonNode?)((string?)name == oldName ? newName : (string?)name))]);
        foreach (string key in new[] { "exportFolders", "gameMods", "scanPick" })
            if (Data.State[key] is JsonObject byMod && byMod[oldName] is JsonNode kept)
            {
                byMod.Remove(oldName);
                byMod[newName] = kept.DeepClone();
            }
        Data.SaveState();
        ModConflictList.Forget();
    }

    /// <summary>The names kept under a key of the state; none when it has none.</summary>
    private static List<string> NamesIn(string key) =>
        Data.State[key] is JsonArray names ? [.. names.Select(name => (string?)name).OfType<string>()] : [];

    /// <summary>The order and the mods on, as one text, for Undo (see <see cref="UndoHistory"/>).</summary>
    public static JsonObject Saved() => new()
    {
        [OrderKey] = Data.State[OrderKey]?.DeepClone(),
        [OnKey] = Data.State[OnKey]?.DeepClone(),
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
        if (isDifferent) ModConflictList.Forget();
    }
}
