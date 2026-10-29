using EasyNdf.Engine.Rules;

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

/// <summary>A few lines of a file around a value, as the game has it or as a mod leaves it, with the value's line.</summary>
/// <param name="Lines">The lines, as in the file.</param>
/// <param name="At">Which of them holds the value.</param>
public sealed record ValueText(IReadOnlyList<string> Lines, int At);

/// <summary>What one mod did to a value in conflict.</summary>
/// <param name="Mod">The mod.</param>
/// <param name="Rule">The rule that made the change (the mod's last one to change the value).</param>
/// <param name="RuleIndex">Where the rule is among the rules the mod ran on the file, from 0.</param>
/// <param name="Old">The value before, shortened to 120 characters.</param>
/// <param name="New">The value after, shortened to 120 characters.</param>
/// <param name="Text">The lines around the value as the mod leaves it; null when they were not found.</param>
public sealed record ModChange(string Mod, string Rule, int RuleIndex, string Old, string New, ValueText? Text = null);

/// <summary>
/// A value more than one mod changes in a file, each mod's change in the order they ran, for the Mod Conflicts window.
/// Values the mods all set the same are not conflicts, as nothing is lost.
/// </summary>
/// <param name="File">The file's name, such as "Ammunition.ndf".</param>
/// <param name="Descriptor">The descriptor the value is in.</param>
/// <param name="Property">The value's property, as the edit log names it; for a name clash, "Clone" or "Add".</param>
/// <param name="IsNameClash">
/// Whether two mods each make a descriptor of this name (with a Clone or Add rule): the first one's is made, and the
/// later one's is refused, as the name is taken.
/// </param>
/// <param name="Changes">Each mod's change, in the order the mods ran.</param>
/// <param name="GameText">The lines around the value before any mod ran; null when they were not found.</param>
public sealed record ConflictedValue(string File, string Descriptor, string Property, bool IsNameClash, IReadOnlyList<ModChange> Changes, ValueText? GameText = null)
{
    /// <summary>The change in the export: the last mod's, or for a name clash the first one's, which made the descriptor.</summary>
    public ModChange Kept => IsNameClash ? Changes[0] : Changes[^1];

    /// <summary>The value as pairs, the mod kept against each other mod, for the ⚠ marks and their tooltips.</summary>
    public IEnumerable<ModConflict> Pairs() =>
        Changes.Where(change => !ReferenceEquals(change, Kept)).Select(lost => new ModConflict(File, Descriptor, Property, Kept.Mod, lost.Mod));
}

/// <summary>Finds the values that more than one mod changes in a file, from what each mod's rules changed, in the order the mods ran.</summary>
public static class ModConflictFinder
{
    /// <summary>The edit log's targets for a descriptor a rule makes: two mods making one name is a name clash.</summary>
    private static readonly HashSet<string> MadeTargets = ["Clone", "Add"];

    /// <summary>
    /// The values in conflict in a file: every value changed by more than one mod, unless they all set it the same; and
    /// every descriptor name more than one mod makes, from the first mod that made it. A mod that changes a value twice
    /// counts once, with its last change. Notes in the logs are left out.
    /// </summary>
    /// <param name="file">The file's name.</param>
    /// <param name="runs">Each mod that ran, in order, with its log entries, each with its rule's name and place.</param>
    public static List<ConflictedValue> Find(string file, IReadOnlyList<(string Mod, IEnumerable<(string Rule, int RuleIndex, EditLogEntry Entry)> Logged)> runs)
    {
        // each value, with each mod's last change to it, in the order the mods ran
        var changedBy = new Dictionary<(string Descriptor, string Target), List<ModChange>>();
        foreach ((string mod, IEnumerable<(string Rule, int RuleIndex, EditLogEntry Entry)> logged) in runs)
            foreach ((string rule, int ruleIndex, EditLogEntry entry) in logged)
            {
                if (entry.IsNote) continue;
                var value = (entry.Descriptor, entry.Target);
                if (!changedBy.TryGetValue(value, out List<ModChange>? changes)) changedBy[value] = changes = [];
                var change = new ModChange(mod, rule, ruleIndex, entry.Old, entry.New);
                int mine = changes.FindIndex(other => other.Mod == mod);
                if (mine >= 0) changes[mine] = change;
                else changes.Add(change);
            }
        var found = new List<ConflictedValue>();
        foreach (((string descriptor, string target), List<ModChange> changes) in changedBy)
        {
            bool isNameClash = MadeTargets.Contains(target);
            // refusals before any mod made the name are not a clash between mods: the name was the game's
            List<ModChange> counted = isNameClash ? [.. changes.SkipWhile(IsRefused)] : changes;
            if (counted.Count < 2) continue;
            if (!isNameClash && counted.All(change => change.New == counted[0].New)) continue;
            found.Add(new ConflictedValue(file, descriptor, target, isNameClash, counted));
        }
        return found;
    }

    /// <summary>Whether a Clone or Add rule's log says it made nothing ("Not cloned. ...", "Not added. ...").</summary>
    private static bool IsRefused(ModChange change) => change.New.StartsWith("Not ", StringComparison.Ordinal);
}
