using EasyNdf.Data;

namespace EasyNdf.App.Mods;

/// <summary>
/// The conflicts between the mods that run together, as the last Preview or export of each file found them
/// (<see cref="ModConflictFinder"/>). The Mods tab marks each mod in one with ⚠, its tooltip naming the values; the
/// Files tab's Conflicts button opens the Mod Conflicts window on them. A file run again replaces what was found for
/// it; until a file is run, nothing is known of it.
/// </summary>
public static class ModConflictList
{
    private static readonly Dictionary<string, List<ConflictedValue>> byFile = new(StringComparer.Ordinal);

    /// <summary>Raised when what is known of the conflicts changes.</summary>
    public static event Action? Changed;

    /// <summary>What a run of the files found, each by its key on the Files tab, in place of what was found for them before.</summary>
    public static void Found(IReadOnlyDictionary<string, List<ConflictedValue>> conflictsByFile)
    {
        foreach ((string file, List<ConflictedValue> conflicts) in conflictsByFile)
        {
            if (conflicts.Count == 0) byFile.Remove(file);
            else byFile[file] = conflicts;
        }
        Changed?.Invoke();
    }

    /// <summary>Forgets every conflict: the mods that run, or their order, changed, so the files must be run again.</summary>
    public static void Forget()
    {
        if (byFile.Count == 0) return;
        byFile.Clear();
        Changed?.Invoke();
    }

    /// <summary>How many values are in conflict in all the files.</summary>
    public static int Count => byFile.Values.Sum(values => values.Count);

    /// <summary>Every value in conflict, with the key of its file on the Files tab.</summary>
    public static IEnumerable<(string FileKey, ConflictedValue Value)> All =>
        byFile.SelectMany(file => file.Value.Select(value => (file.Key, value)));

    /// <summary>The conflicts found in one file, by its key on the Files tab; none when none were found, or it was not run.</summary>
    public static IReadOnlyList<ModConflict> InFile(string fileKey) =>
        byFile.TryGetValue(fileKey, out List<ConflictedValue>? values) ? [.. values.SelectMany(value => value.Pairs())] : [];

    /// <summary>The conflicts a mod is in, as the winner or the one overridden.</summary>
    public static IReadOnlyList<ModConflict> Of(Mod mod) =>
        [.. byFile.Values.SelectMany(values => values).SelectMany(value => value.Pairs()).Where(conflict => conflict.Winner == mod.Name || conflict.Overridden == mod.Name)];
}
