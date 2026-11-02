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
        // This runs for every file on the Files tab, for each mod, each time a mod is opened or changed: the file's
        // folders are worked out once for all the rule sets, and nothing else is copied, as the copies made tens of
        // megabytes of memory to clean up after each click on a mod. Tuned for speed: Keep it that way when tidying.
        string[] folders = FoldersOf(folder);
        int bestFit = byName ? BestFitByName(fileName, folders, ruleSets) : -1;
        var applying = new List<ModPreset>();
        foreach (ModPreset ruleSet in ruleSets)
        {
            if (ruleSet.IsArmoryPreset) continue;
            if (FitsBestByName(ruleSet, fileName, folders, bestFit) || Lists(ruleSet, fileName, folders)) applying.Add(ruleSet);
        }
        return applying;
    }

    /// <summary>
    /// How well the best name of the rule sets without files of their own fits the file (<see cref="PresetMatching.Fit"/>):
    /// when two hold its name, the one that also names more of its folders fits better, as "WTO (Strategic/GDConstants.ndf)"
    /// for Strategic/GDConstants.ndf. -1 when no name holds the file's. Armory presets are left out.
    /// </summary>
    private static int BestFitByName(string fileName, string[] folders, IEnumerable<ModPreset> ruleSets)
    {
        int best = -1;
        foreach (ModPreset ruleSet in ruleSets)
            if (!ruleSet.IsArmoryPreset && ruleSet.AppliesTo is null) best = Math.Max(best, PresetMatching.Fit(ruleSet.Name, fileName, folders));
        return best;
    }

    /// <summary>
    /// Whether a rule set without files of its own applies to the file by its name: its name fits the file as well as
    /// the best does (<see cref="BestFitByName"/>). Rule sets that fit equally well all apply.
    /// </summary>
    private static bool FitsBestByName(ModPreset ruleSet, string fileName, string[] folders, int bestFit) =>
        bestFit >= 0 && ruleSet.AppliesTo is null && PresetMatching.Fit(ruleSet.Name, fileName, folders) == bestFit;

    /// <summary>
    /// Whether a rule set lists a file in its Applies To: a name equal to the file's, with any folders written before it
    /// being the file's own nearest folders.
    /// </summary>
    public static bool Lists(ModPreset ruleSet, string fileName, string folder) =>
        ruleSet.AppliesTo is List<string> && Lists(ruleSet, fileName, FoldersOf(folder));

    /// <summary><see cref="Lists(ModPreset, string, string)"/>, with the file's folders worked out already (<see cref="FoldersOf"/>).</summary>
    private static bool Lists(ModPreset ruleSet, string fileName, string[] folders)
    {
        if (ruleSet.AppliesTo is not List<string> listed) return false;
        foreach (string file in listed)
        {
            // the name after the last folder, whichever way the folders are written, compared where it is: most files
            // listed are other files, and copying each name out made memory to clean up for every file on the list.
            // Tuned for speed: Keep it that way when tidying.
            int lastFolderEnd = Math.Max(file.LastIndexOf('/'), file.LastIndexOf('\\'));
            if (!file.AsSpan(lastFolderEnd + 1).Equals(fileName, StringComparison.Ordinal)) continue;
            if (PresetMatching.Fit(file.Replace('\\', '/'), fileName, folders) >= 0) return true;
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
