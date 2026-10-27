using System.Text.Json.Nodes;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// What the Armory presets' export writes besides NDF: the divisions' texts, into the mod's UNITS.csv (where the game
/// looks a division's name up), and pictures of the user's own, copied where the game keeps emblems. UNITS.csv is a file
/// on the Files tab like the others, and the export writes a new one with the texts; the pictures go into the export
/// folder's GameData folder, the one the exported Divisions.ndf is in.
/// </summary>
public static class DivisionExtras
{
    /// <summary>
    /// The mod's dictionary file the divisions' texts go into: the game stops with "No string found for token ... in
    /// dictionary list 'UNITS'" when a division's name is not in it.
    /// </summary>
    public const string NamesFile = "UNITS.csv";

    /// <summary>Whether a file is the dictionary the divisions' texts go into.</summary>
    public static bool IsNamesFile(string fileName) => string.Equals(fileName, NamesFile, StringComparison.OrdinalIgnoreCase);

    /// <summary>What was copied, or why not.</summary>
    /// <param name="Pictures">How many pictures were copied.</param>
    /// <param name="Problem">What stopped some of it; "" when nothing did.</param>
    public sealed record Outcome(int Pictures, string Problem);

    /// <summary>
    /// Copies the pictures of the Armory presets' rules that are on into the GameData folder of a file of theirs just
    /// exported, where the game looks for them.
    /// </summary>
    /// <param name="rules">The Armory presets' rules.</param>
    /// <param name="exportedFile">Where the file was exported, such as ".../Export/GameData/Generated/Gameplay/Decks/Divisions.ndf".</param>
    /// <param name="pictureFolders">The folders the app keeps chosen pictures in (<see cref="AppFolders.PictureFoldersOf"/>); a picture anywhere else is not copied.</param>
    /// <param name="noGameData">The problem to give when no GameData folder holds it, with {path} for where it was written.</param>
    /// <param name="pictureMissing">The problem to give for a picture that is no longer where it was chosen, with {path} for it.</param>
    public static Outcome CopyPictures(IEnumerable<JsonNode?> rules, string exportedFile, IReadOnlyList<string> pictureFolders, string noGameData, string pictureMissing)
    {
        if (!rules.Any(HasPicture)) return new Outcome(0, "");
        string? gameData = GameDataFolderOf(exportedFile);
        if (gameData is null) return new Outcome(0, noGameData.Replace("{path}", exportedFile, StringComparison.Ordinal));
        var missing = new List<string>();
        int pictures = CopyPicturesInto(gameData, rules, pictureFolders, missing);
        return new Outcome(pictures, string.Join(" ", missing.Select(path => pictureMissing.Replace("{path}", path, StringComparison.Ordinal))));
    }

    /// <summary>
    /// Whether a rule of the Armory presets gives texts that go into UNITS.csv: a division's or a unit's name, a new
    /// division's summary and history, or an Official Battlegroup's name. Such a change needs UNITS.csv on the Files tab, as the others
    /// need the files they are written into.
    /// </summary>
    public static bool NeedsNamesFile(JsonNode? rule) =>
        (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.ShowsKey] == "Name"
        || (rule?["Actions"] as JsonArray ?? []).Any(action => IsTextTarget((string?)action?["Target"]))
        || (IsAddRule(rule) && rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.TextsKey] is JsonObject { Count: > 0 });

    /// <summary>Whether a rule's target is a text token whose text goes into UNITS.csv: a unit's name is one too.</summary>
    private static bool IsTextTarget(string? target) => target is "SummaryTextToken" or "HistoryTextToken" or "DeckName" or "NameToken";

    /// <summary>Whether a rule adds a descriptor: its texts are kept by token, as it has no action to name them.</summary>
    private static bool IsAddRule(JsonNode? rule) => (string?)rule?["Kind"] == "Add";

    /// <summary>
    /// A UNITS.csv with the texts of the Armory presets' rules that are on: a line "TOKEN";"Text" for each, as the
    /// game writes them, in place of a line of the same token, after the file's other lines. The file's own line
    /// endings are kept; an empty file gets the game's heading line.
    /// </summary>
    /// <param name="csvText">The UNITS.csv on the Files tab, as it is.</param>
    /// <param name="rules">The Armory presets' rules.</param>
    /// <returns>The new file's text, and how many texts it was given.</returns>
    public static (string Text, int Count) WithNames(string csvText, IEnumerable<JsonNode?> rules) => WithTexts(csvText, ArmoryTextsOf(rules));

    /// <summary>
    /// A dictionary file (UNITS.csv, INTERFACE_OUTGAME.csv...) with the given texts: a line "TOKEN";"Text" for each, as
    /// the game writes them, in place of a line of the same token, after the file's other lines. A token given twice
    /// takes the later text, as a lower mod wins. The file's own line endings are kept; an empty file gets the game's
    /// heading line.
    /// </summary>
    /// <param name="csvText">The dictionary file on the Files tab, as it is.</param>
    /// <param name="texts">The texts, in the order the rules giving them run.</param>
    /// <returns>The new file's text, and how many texts it was given.</returns>
    public static (string Text, int Count) WithTexts(string csvText, IReadOnlyList<DictionaryText> texts)
    {
        var byToken = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new List<DictionaryText>();
        foreach (DictionaryText text in texts)
        {
            if (byToken.TryGetValue(text.Token, out int at)) names[at] = text;
            else
            {
                byToken[text.Token] = names.Count;
                names.Add(text);
            }
        }
        string existing = csvText.Trim() == "" ? "\"TOKEN\";\"REFTEXT\"\r\n" : csvText;
        string lineBreak = existing.Contains("\r\n", StringComparison.Ordinal) || !existing.Contains('\n') ? "\r\n" : "\n";
        List<string> lines = [.. existing.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line != "")];
        lines.RemoveAll(line => byToken.ContainsKey(line.Split(';')[0].Trim('"')));
        lines.AddRange(names.Select(name => "\"" + name.Token + "\";\"" + CsvText(name.Text) + "\""));
        return (string.Join(lineBreak, lines) + lineBreak, names.Count);
    }

    /// <summary>
    /// The WARNO mod a folder is in, by the mod's folder name: the folder right under a "Mods" folder, as the game's
    /// CreateNewMod makes them ("…\WARNO\Mods\MyMod\GameData\…" is "MyMod"); null when the folder is in no mod.
    /// </summary>
    public static string? GameModOf(string folder)
    {
        string? child = null;
        for (string? at = Path.GetFullPath(folder).TrimEnd('\\', '/'); at is not null; at = Path.GetDirectoryName(at))
        {
            if (string.Equals(Path.GetFileName(at), "Mods", StringComparison.OrdinalIgnoreCase) && child is not null) return child;
            child = Path.GetFileName(at);
        }
        return null;
    }

    /// <summary>
    /// Where UNITS.csv must be written for the game to read it: in GameData\Localisation\&lt;the mod's folder name&gt;.
    /// The export keeps the folders a file was added from, which name the mod it was added from ("Test"), not the one
    /// it is for. A path with a Localisation folder has the folder under it renamed; one with only a GameData folder
    /// gets Localisation\&lt;mod&gt; under that; any other path is left as it is.
    /// </summary>
    /// <param name="path">Where the export would write the file, keeping its folders.</param>
    /// <param name="gameMod">The mod's folder name.</param>
    public static string NamesFilePathFor(string path, string gameMod)
    {
        string name = Path.GetFileName(path);
        List<string> folders = [.. Path.GetDirectoryName(path)!.Split(['\\', '/'])];
        int localisation = folders.FindLastIndex(folder => string.Equals(folder, "Localisation", StringComparison.OrdinalIgnoreCase));
        if (localisation >= 0)
        {
            folders = [.. folders.Take(localisation + 1), gameMod];
            return string.Join(Path.DirectorySeparatorChar, [.. folders, name]);
        }
        int gameData = folders.FindLastIndex(folder => string.Equals(folder, "GameData", StringComparison.OrdinalIgnoreCase));
        if (gameData >= 0) return string.Join(Path.DirectorySeparatorChar, [.. folders.Take(gameData + 1), "Localisation", gameMod, name]);
        return path;
    }

    /// <summary>The GameData folder a file sits in, at any depth; null when it is not in one.</summary>
    public static string? GameDataFolderOf(string file)
    {
        for (string? folder = Path.GetDirectoryName(file); folder is not null; folder = Path.GetDirectoryName(folder))
            if (string.Equals(Path.GetFileName(folder), "GameData", StringComparison.OrdinalIgnoreCase)) return folder;
        return null;
    }

    /// <summary>
    /// The texts the Armory presets' rules that are on give UNITS.csv: each division's token and its name, a division's
    /// summary and history, and an Official Battlegroup's name.
    /// </summary>
    public static List<DictionaryText> ArmoryTextsOf(IEnumerable<JsonNode?> rules)
    {
        var names = new List<DictionaryText>();
        foreach (JsonNode? rule in RulesThatAreOn(rules))
        {
            if (rule?[DivisionChangeRules.ArmoryKey] is not JsonObject armory || (string?)armory[DivisionChangeRules.ShowsKey] != "Name") continue;
            if (JsonValues.IsFalse(rule["Enabled"])) continue;
            // the token as the rule sets it, so a rule made editable and changed by hand is followed
            string? token = (rule["Actions"] as JsonArray ?? [])
                .Where(action => (string?)action?["Target"] == "DivisionName")
                .Select(action => ((string?)action?["Value"] ?? "").Trim('\'', '"', ' '))
                .FirstOrDefault(value => value != "");
            string text = (string?)armory[DivisionChangeRules.NameTextKey] ?? "";
            if (token is not null && text != "") names.Add(new DictionaryText(token, text));
        }
        // a division's summary and history, and an Official Battlegroup's name: the texts the rule keeps by token; a
        // token a rule sets with no text kept (one made before texts were kept) gets an empty line, so the game still
        // finds it. A rule that adds a descriptor (a new division's deck) sets no token by an action: its own are kept.
        foreach (JsonNode? rule in RulesThatAreOn(rules))
        {
            if (rule?[DivisionChangeRules.ArmoryKey] is not JsonObject armory || JsonValues.IsFalse(rule["Enabled"])) continue;
            JsonObject kept = armory[DivisionChangeRules.TextsKey] as JsonObject ?? [];
            if (IsAddRule(rule))
                foreach ((string token, JsonNode? text) in kept)
                    if (names.All(name => name.Token != token)) names.Add(new DictionaryText(token, (string?)text ?? ""));
            foreach (JsonNode? action in rule["Actions"] as JsonArray ?? [])
            {
                if (!IsTextTarget((string?)action?["Target"])) continue;
                string token = ((string?)action?["Value"] ?? "").Trim('\'', '"', ' ');
                if (token != "" && names.All(name => name.Token != token)) names.Add(new DictionaryText(token, (string?)kept[token] ?? ""));
            }
        }
        return names;
    }

    /// <summary>
    /// A text as a dictionary line holds it: its own ";", quotes and line breaks would break the file's columns, so
    /// ";" becomes ",", a double quote two single ones, and a line break a space.
    /// </summary>
    internal static string CsvText(string text) =>
        text.Replace(';', ',').Replace("\"", "''", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace('\n', ' ').Trim();

    /// <summary>The pictures of the user's own the rules copy into the mod, each once, of those in the app's picture folders.</summary>
    /// <param name="rules">The Armory presets' rules.</param>
    /// <param name="pictureFolders">The folders the app keeps chosen pictures in (<see cref="AppFolders.PictureFoldersOf"/>).</param>
    public static IEnumerable<string> PictureFiles(IEnumerable<JsonNode?> rules, IReadOnlyList<string> pictureFolders) =>
        RulesThatAreOn(rules).Select(rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.PictureFileKey]).OfType<string>()
            .Where(picture => IsInsideOneOf(picture, pictureFolders)).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The rules that are on, as the engine runs them: switched on themselves, and not under a group heading that is
    /// switched off. A division's names and pictures were written even with its group switched off.
    /// </summary>
    internal static IEnumerable<JsonNode> RulesThatAreOn(IEnumerable<JsonNode?> rules)
    {
        bool groupIsOn = true;
        foreach (JsonNode? rule in rules)
        {
            if (rule is null) continue;
            if ((string?)rule["Kind"] == "Group")
            {
                groupIsOn = !JsonValues.IsFalse(rule["Enabled"]);
                continue;
            }
            if (groupIsOn && !JsonValues.IsFalse(rule["Enabled"])) yield return rule;
        }
    }

    /// <summary>The files the rules that give pictures of the user's own are for, such as Divisions.ndf; the pictures go into the GameData folder their export is in.</summary>
    public static HashSet<string> FilesWithPictures(IEnumerable<JsonNode?> rules) =>
        new(RulesThatAreOn(rules).Where(HasPicture).Select(rule => (string?)rule[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.FileKey]).OfType<string>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a rule gives a picture of the user's own to copy.</summary>
    private static bool HasPicture(JsonNode? rule) => rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.PictureFileKey] is not null;

    /// <summary>
    /// Deletes, under an export's GameData folder, the pictures of rules that are switched off (themselves, or under a
    /// group switched off), where no rule that is on puts a picture too: an earlier export copied them, and they would
    /// stay in the mod unseen. Returns how many were deleted.
    /// </summary>
    public static int RemovePicturesOfRulesOff(IEnumerable<JsonNode?> rules, string gameData)
    {
        List<JsonNode?> all = [.. rules];
        HashSet<string> usedPaths = new(RulesThatAreOn(all).Select(rule => (string?)rule[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.PicturePathKey]).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        HashSet<JsonNode> on = [.. RulesThatAreOn(all)];
        int removed = 0;
        foreach (JsonNode? rule in all)
        {
            if (rule is null || on.Contains(rule) || (string?)rule[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.PicturePathKey] is not string path) continue;
            if (usedPaths.Contains(path)) continue;
            if (TargetUnder(gameData, path) is not string target || !File.Exists(target)) continue;
            File.Delete(target);
            removed++;
        }
        return removed;
    }

    /// <summary>Copies the pictures of the user's own that the rules that are on give, where they say. Returns how many.</summary>
    private static int CopyPicturesInto(string gameData, IEnumerable<JsonNode?> rules, IReadOnlyList<string> pictureFolders, List<string> missing)
    {
        int copied = 0;
        foreach (JsonNode? rule in RulesThatAreOn(rules))
        {
            if (rule?[DivisionChangeRules.ArmoryKey] is not JsonObject armory || JsonValues.IsFalse(rule["Enabled"])) continue;
            if ((string?)armory[DivisionChangeRules.PictureFileKey] is not string picture || (string?)armory[DivisionChangeRules.PicturePathKey] is not string path) continue;
            // a mod file shared by someone else names pictures on their computer: one outside the app's own picture
            // folders is never copied, so a mod file cannot put any file the user can read into the mod
            if (!IsInsideOneOf(picture, pictureFolders) || !File.Exists(picture))
            {
                missing.Add(picture);
                continue;
            }
            if (TargetUnder(gameData, path) is not string target) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(picture, target, overwrite: true);
            copied++;
        }
        return copied;
    }

    /// <summary>
    /// Where a picture goes under GameData, from the path a rule gives it ("Assets/2D/..."); null when the path is not a
    /// plain relative one, such as "../x.png" or "C:/x.png", which a mod file could give to write or delete files
    /// elsewhere.
    /// </summary>
    internal static string? TargetUnder(string gameData, string picturePath)
    {
        string[] folders = picturePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (folders.Length == 0 || picturePath.Contains(':', StringComparison.Ordinal)) return null;
        if (folders.Any(folder => folder == ".." || folder == ".")) return null;
        string target = Path.Combine([gameData, .. folders]);
        return IsInsideOneOf(target, [gameData]) ? target : null;
    }

    /// <summary>Whether a file lies inside one of the folders, once ".." and the like are worked out.</summary>
    internal static bool IsInsideOneOf(string file, IReadOnlyList<string> folders)
    {
        string fullFile = Path.GetFullPath(file);
        foreach (string folder in folders)
        {
            string fullFolder = Path.GetFullPath(folder);
            if (!Path.EndsInDirectorySeparator(fullFolder)) fullFolder += Path.DirectorySeparatorChar;
            if (fullFile.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

/// <summary>A text for a dictionary file such as UNITS.csv: its token, and the text the game shows.</summary>
/// <param name="Token">The token NDF files name it by, such as "YSM_UNIT_NAME".</param>
/// <param name="Text">The text.</param>
public sealed record DictionaryText(string Token, string Text);
