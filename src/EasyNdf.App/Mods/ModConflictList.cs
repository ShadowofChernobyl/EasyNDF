using EasyNdf.Data;

namespace EasyNdf.App.Mods;

/// <summary>
/// The conflicts between the mods that run together, as the last Preview or export of each file found them
/// (<see cref="ModConflictFinder"/>). The Mods tab marks each mod in one with ⚠, its tooltip naming the values. A file
/// run again replaces what was found for it; until a file is run, nothing is known of it.
/// </summary>
public static class ModConflictList
{
    private static readonly Dictionary<string, List<ModConflict>> byFile = new(StringComparer.Ordinal);

    /// <summary>Raised when what is known of the conflicts changes.</summary>
    public static event Action? Changed;

    /// <summary>What a run of the files found, each by its key on the Files tab, in place of what was found for them before.</summary>
    public static void Found(IReadOnlyDictionary<string, List<ModConflict>> conflictsByFile)
    {
        foreach ((string file, List<ModConflict> conflicts) in conflictsByFile)
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

    /// <summary>The conflicts found in one file, by its key on the Files tab; none when none were found, or it was not run.</summary>
    public static IReadOnlyList<ModConflict> InFile(string fileKey) => byFile.GetValueOrDefault(fileKey) ?? [];

    /// <summary>The conflicts a mod is in, as the winner or the one overridden.</summary>
    public static IReadOnlyList<ModConflict> Of(Mod mod) =>
        [.. byFile.Values.SelectMany(conflicts => conflicts).Where(conflict => conflict.Winner == mod.Name || conflict.Overridden == mod.Name)];
}
