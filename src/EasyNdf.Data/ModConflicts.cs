namespace EasyNdf.Data;

/// <summary>
/// A value two mods that run together both change, in one file: the mod that runs later (lower on the Mods tab) wins,
/// and the other's change is overwritten.
/// </summary>
/// <param name="File">The file, such as "Ammunition.ndf".</param>
/// <param name="Descriptor">The descriptor the value is in.</param>
/// <param name="Property">The value's property, as the edit log names it.</param>
/// <param name="Winner">The mod whose change is kept: the later one.</param>
/// <param name="Overridden">The mod whose change is overwritten.</param>
public sealed record ModConflict(string File, string Descriptor, string Property, string Winner, string Overridden);

/// <summary>Finds the values that more than one mod changes in a file, from what each mod's rules changed, in the order the mods ran.</summary>
public static class ModConflictFinder
{
    /// <summary>
    /// The conflicts in a file: every value changed by a mod and changed again by a later one. A value three mods
    /// change gives two conflicts, each earlier mod against the last.
    /// </summary>
    /// <param name="file">The file's name.</param>
    /// <param name="runs">Each mod that ran, in order, with the values its rules changed (descriptor and property).</param>
    public static List<ModConflict> Find(string file, IReadOnlyList<(string Mod, IEnumerable<(string Descriptor, string Property)> Changed)> runs)
    {
        // each value, with the mods that changed it in the order they ran
        var changedBy = new Dictionary<(string, string), List<string>>();
        foreach ((string mod, IEnumerable<(string Descriptor, string Property)> changed) in runs)
            foreach ((string Descriptor, string Property) value in changed.Distinct())
            {
                if (!changedBy.TryGetValue(value, out List<string>? mods)) changedBy[value] = mods = [];
                if (!mods.Contains(mod, StringComparer.Ordinal)) mods.Add(mod);
            }
        var conflicts = new List<ModConflict>();
        foreach (((string descriptor, string property), List<string> mods) in changedBy)
        {
            if (mods.Count < 2) continue;
            string winner = mods[^1];
            foreach (string overridden in mods.Take(mods.Count - 1))
                conflicts.Add(new ModConflict(file, descriptor, property, winner, overridden));
        }
        return conflicts;
    }
}
