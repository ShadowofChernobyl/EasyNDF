using System.Text.Json.Nodes;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// The two rule types that write something besides NDF on export. A Text rule gives a dictionary file (UNITS.csv,
/// INTERFACE_OUTGAME.csv...) lines of "TOKEN;Text": its rule set applies to that file on the Files tab, which is
/// exported with the lines added. A File rule carries a file into the mod (a picture, a video, a new NDF file...),
/// kept in the app's <see cref="ModFileStore"/>: it is written, on export, where the rule says under the mod's folder,
/// the folder that holds the GameData (or CommonData) folder of a file its rule set applies to.
/// </summary>
public static class FileAndTextRules
{
    /// <summary>The Text rule's type.</summary>
    public const string TextKind = "Text";

    /// <summary>The File rule's type.</summary>
    public const string FileKind = "File";

    /// <summary>A File rule's setting: where the file goes, under the mod's folder, such as "GameData/Assets/2D/Logo.png".</summary>
    public const string DestinationKey = "Destination";

    /// <summary>A File rule's setting: the name the file is kept under in the <see cref="ModFileStore"/>.</summary>
    public const string StoredKey = "Stored";

    /// <summary>A File rule's setting: the name of the file it was chosen as, shown in the rule window.</summary>
    public const string ChosenNameKey = "ChosenName";

    /// <summary>A File rule's setting: the file's size in bytes, shown in the rule window.</summary>
    public const string SizeKey = "Size";

    /// <summary>A Text rule's setting: its lines, "TOKEN;Text" each.</summary>
    public const string LinesKey = "Text";

    /// <summary>A file a File rule writes: where, under the mod's folder, and the name it is kept under in the store.</summary>
    public sealed record CarriedFile(string Destination, string StoredName);

    /// <summary>What writing the files did.</summary>
    /// <param name="Written">How many files were written.</param>
    /// <param name="Problems">What stopped some of them, one line each.</param>
    public sealed record Outcome(int Written, IReadOnlyList<string> Problems);

    /// <summary>Whether a file is a dictionary file, which Text rules write into: a .csv file.</summary>
    public static bool IsDictionaryFile(string fileName) => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a rule is a Text or File rule, which the NDF engine has nothing to do with.</summary>
    public static bool IsTextOrFileRule(JsonNode? rule) => (string?)rule?["Kind"] is TextKind or FileKind;

    /// <summary>The texts the Text rules that are on give, in the order they run.</summary>
    public static List<DictionaryText> TextsOf(IEnumerable<JsonNode?> rules)
    {
        var texts = new List<DictionaryText>();
        foreach (JsonNode rule in DivisionExtras.RulesThatAreOn(rules))
            if ((string?)rule["Kind"] == TextKind) texts.AddRange(LinesOf(JsonValues.AsText(rule["Params"]?[LinesKey])));
        return texts;
    }

    /// <summary>
    /// The texts a Text rule's lines give: "TOKEN;Text" each, either side in quotes or not, as the game's own files and
    /// spreadsheets write them. Empty lines, lines without a ";", and the game's heading line are left out.
    /// </summary>
    public static List<DictionaryText> LinesOf(string lines)
    {
        var texts = new List<DictionaryText>();
        foreach (string raw in lines.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int split = line.IndexOf(';', StringComparison.Ordinal);
            if (split <= 0) continue;
            string token = Unquoted(line[..split]);
            if (token == "" || token == "TOKEN") continue;
            texts.Add(new DictionaryText(token, Unquoted(line[(split + 1)..])));
        }
        return texts;
    }

    /// <summary>A part of a line without the spaces and the one pair of double quotes around it.</summary>
    private static string Unquoted(string part)
    {
        string trimmed = part.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' ? trimmed[1..^1] : trimmed;
    }

    /// <summary>The files the File rules that are on write, each place once (the last rule to name it wins).</summary>
    public static List<CarriedFile> FilesOf(IEnumerable<JsonNode?> rules)
    {
        var files = new List<CarriedFile>();
        foreach (JsonNode rule in DivisionExtras.RulesThatAreOn(rules))
        {
            if (CarriedFileOf(rule) is not CarriedFile file) continue;
            files.RemoveAll(other => SamePlace(other.Destination, file.Destination));
            files.Add(file);
        }
        return files;
    }

    /// <summary>The file a File rule writes; null for another rule, or one with no file or no place chosen yet.</summary>
    public static CarriedFile? CarriedFileOf(JsonNode? rule)
    {
        if ((string?)rule?["Kind"] != FileKind || rule["Params"] is not JsonObject settings) return null;
        string destination = JsonValues.AsText(settings[DestinationKey]).Trim();
        string stored = JsonValues.AsText(settings[StoredKey]);
        return destination == "" || stored == "" ? null : new CarriedFile(destination, stored);
    }

    /// <summary>The names of the files File rules keep in the store: what an exported mod file carries.</summary>
    public static IEnumerable<string> StoredNamesOf(IEnumerable<JsonNode?> rules) =>
        rules.Select(CarriedFileOf).OfType<CarriedFile>().Select(file => file.StoredName).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// The mod's folder for a file just exported: the folder that holds the GameData or CommonData folder it is in (a
    /// mod has both, side by side); null when it is in neither.
    /// </summary>
    public static string? ModFolderOf(string exportedFile)
    {
        for (string? folder = Path.GetDirectoryName(exportedFile); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            string name = Path.GetFileName(folder);
            if (string.Equals(name, "GameData", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "CommonData", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(folder);
        }
        return null;
    }

    /// <summary>
    /// Writes the files into the mod's folder, where each rule says. A place that is not a plain path under the mod's
    /// folder (such as "../x.png" or "C:/x.png"), or a file the store no longer has, is said in the problems.
    /// </summary>
    /// <param name="files">The files to write (<see cref="FilesOf"/>).</param>
    /// <param name="modFolder">The mod's folder (<see cref="ModFolderOf"/>).</param>
    /// <param name="store">Where the files are kept.</param>
    /// <param name="badPlace">The problem to give for a place outside the mod's folder, with {path} for it.</param>
    /// <param name="missing">The problem to give for a file the store no longer has, with {path} for where it was to go.</param>
    public static Outcome WriteFiles(IEnumerable<CarriedFile> files, string modFolder, ModFileStore store, string badPlace, string missing)
    {
        int written = 0;
        var problems = new List<string>();
        foreach (CarriedFile file in files)
        {
            if (DivisionExtras.TargetUnder(modFolder, file.Destination) is not string target)
            {
                problems.Add(badPlace.Replace("{path}", file.Destination, StringComparison.Ordinal));
                continue;
            }
            if (store.PathOf(file.StoredName) is not string source)
            {
                problems.Add(missing.Replace("{path}", file.Destination, StringComparison.Ordinal));
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            written++;
        }
        return new Outcome(written, problems);
    }

    /// <summary>
    /// Deletes, under the mod's folder, the files of File rules that are switched off (themselves, or under a group
    /// switched off, or of a mod that is off), where no rule that is on writes a file too: an earlier export wrote them,
    /// and they would stay in the mod unseen. Only a file that is still the one the rule wrote is deleted, never one
    /// the user put there or changed. Returns how many were deleted.
    /// </summary>
    /// <param name="rules">Every mod's rules, those of mods that are off copied as switched off.</param>
    /// <param name="modFolder">The mod's folder (<see cref="ModFolderOf"/>).</param>
    public static int RemoveFilesOfRulesOff(IEnumerable<JsonNode?> rules, string modFolder)
    {
        List<JsonNode?> all = [.. rules];
        HashSet<JsonNode> on = [.. DivisionExtras.RulesThatAreOn(all)];
        List<CarriedFile> used = FilesOf(all);
        int removed = 0;
        foreach (JsonNode? rule in all)
        {
            if (rule is null || on.Contains(rule) || CarriedFileOf(rule) is not CarriedFile file) continue;
            if (used.Any(other => SamePlace(other.Destination, file.Destination))) continue;
            if (DivisionExtras.TargetUnder(modFolder, file.Destination) is not string target || !File.Exists(target)) continue;
            if (ModFileStore.NameFor(File.ReadAllBytes(target), file.StoredName) != file.StoredName) continue;
            File.Delete(target);
            removed++;
        }
        return removed;
    }

    /// <summary>Whether two places name the same file, as Windows reads them: "/" or "\", in any case.</summary>
    private static bool SamePlace(string one, string other) =>
        string.Equals(one.Replace('\\', '/').Trim('/'), other.Replace('\\', '/').Trim('/'), StringComparison.OrdinalIgnoreCase);
}
