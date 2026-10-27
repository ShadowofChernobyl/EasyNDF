namespace EasyNdf.Data.FileList;

/// <summary>
/// Which files a mod's rule sets apply to. A rule set with files listed in its Applies To window applies to those
/// (<see cref="ModPreset.AppliesTo"/>); one without applies to the files its own name holds, such as
/// "WTO (Ammunition.ndf)" for Ammunition.ndf, as rule sets always did (<see cref="PresetMatching"/>). Several rule sets
/// of one mod can apply to the same file: they then run one after the other, in the mod's list order.
/// </summary>
public static class RuleSetFiles
{
    /// <summary>
    /// The rule sets of one mod that apply to a file, in the mod's list order. Armory presets are left out: they run on
    /// their own files by themselves.
    /// </summary>
    /// <param name="fileName">The file's name, such as "GDConstants.ndf". Capitals must match.</param>
    /// <param name="folder">The folders the file sat in, such as "Gameplay/Constantes/Strategic"; "" for none.</param>
    /// <param name="ruleSets">The mod's rule sets, in its list order.</param>
    /// <param name="byName">
    /// Whether rule sets without files of their own apply to the files their names hold (the setting "Apply rule sets
    /// by file name"); when not, they apply to nothing.
    /// </param>
    public static List<ModPreset> For(string fileName, string folder, IEnumerable<ModPreset> ruleSets, bool byName)
    {
        List<ModPreset> own = [.. ruleSets.Where(ruleSet => !ruleSet.IsArmoryPreset)];
        HashSet<ModPreset> byTheirNames = byName ? BestByName(fileName, folder, own) : [];
        return [.. own.Where(ruleSet => byTheirNames.Contains(ruleSet) || Lists(ruleSet, fileName, folder))];
    }

    /// <summary>
    /// The rule sets without files of their own whose names fit the file best: when two hold its name, the one that also
    /// names more of its folders, as "WTO (Strategic/GDConstants.ndf)" for Strategic/GDConstants.ndf. Rule sets that fit
    /// equally well all apply.
    /// </summary>
    private static HashSet<ModPreset> BestByName(string fileName, string folder, List<ModPreset> ruleSets)
    {
        string[] folders = FoldersOf(folder);
        int best = -1;
        var bestRuleSets = new HashSet<ModPreset>();
        foreach (ModPreset ruleSet in ruleSets.Where(ruleSet => ruleSet.AppliesTo is null))
        {
            int fit = PresetMatching.Fit(ruleSet.Name, fileName, folders);
            if (fit < 0) continue;
            if (fit > best)
            {
                best = fit;
                bestRuleSets.Clear();
            }
            if (fit == best) bestRuleSets.Add(ruleSet);
        }
        return bestRuleSets;
    }

    /// <summary>
    /// Whether a rule set lists a file in its Applies To: a name equal to the file's, with any folders written before it
    /// being the file's own nearest folders.
    /// </summary>
    public static bool Lists(ModPreset ruleSet, string fileName, string folder)
    {
        if (ruleSet.AppliesTo is not List<string> listed) return false;
        string[] folders = FoldersOf(folder);
        foreach (string file in listed)
        {
            string written = file.Replace('\\', '/');
            string name = written[(written.LastIndexOf('/') + 1)..];
            if (string.Equals(name, fileName, StringComparison.Ordinal) && PresetMatching.Fit(written, fileName, folders) >= 0) return true;
        }
        return false;
    }

    /// <summary>
    /// The files a rule set says it applies to, as its Applies To line shows them: those it lists, or the file its name
    /// holds ("Ammunition.ndf" in "WTO (Ammunition.ndf)", with any folders written before it); none when its name holds
    /// no file name.
    /// </summary>
    public static IReadOnlyList<string> Named(ModPreset ruleSet)
    {
        if (ruleSet.AppliesTo is List<string> listed) return listed;
        return FileNameIn(ruleSet.Name) is string file ? [file] : [];
    }

    /// <summary>
    /// The last file name a rule set's name holds, with the folders written before it: a run of letters, digits, "_",
    /// ".", "-" and "/" ending in a file type, such as "Strategic/GDConstants.ndf". Null when there is none.
    /// </summary>
    private static string? FileNameIn(string ruleSetName)
    {
        string name = ruleSetName.Replace('\\', '/');
        string? found = null;
        int at = 0;
        while (at < name.Length)
        {
            if (!IsPathCharacter(name[at]))
            {
                at++;
                continue;
            }
            int start = at;
            while (at < name.Length && IsPathCharacter(name[at])) at++;
            string word = name[start..at].Trim('/');
            int dot = word.LastIndexOf('.');
            if (dot > 0 && dot < word.Length - 1 && char.IsAsciiLetter(word[dot + 1]) && word[(dot + 1)..].All(char.IsAsciiLetterOrDigit)) found = word;
        }
        return found;
    }

    /// <summary>The characters a file name and its folders are made of.</summary>
    private static bool IsPathCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or '/';

    private static string[] FoldersOf(string folder) => folder.ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
}
