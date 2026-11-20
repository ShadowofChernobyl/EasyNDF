using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;
using EasyNdf.Engine.Editing;

namespace EasyNdf.App.Help;

/// <summary>
/// Examples for the Help tab's Reference, read from the user's own WARNO (its base files, Mods/ModData/base.zip): for each
/// type, the files that use it and how often, and its first block as an example; for each variable, the first line that
/// sets it. Nothing of the game is carried in the app: without a WARNO folder the pages have no examples. Read once, away
/// from the screen's thread, the first time a type's page is opened.
/// </summary>
public static class ReferenceExamples
{
    /// <summary>What was found for one type.</summary>
    public sealed class TypeExamples
    {
        /// <summary>The files that use the type, with how many times, most first.</summary>
        public List<(string File, int Count)> Files { get; } = [];

        /// <summary>The type's first block in the files, at most <see cref="MostLines"/> lines; "" for none.</summary>
        public string Block { get; set; } = "";

        /// <summary>The file the block is from.</summary>
        public string BlockFile { get; set; } = "";

        /// <summary>For each variable, the first line that sets it, and its file.</summary>
        public Dictionary<string, (string Line, string File)> Lines { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The longest example block shown; longer ones end with "…".</summary>
    private const int MostLines = 400;

    /// <summary>The most blocks of one type looked through for its variables' lines.</summary>
    private const int MostBlocksPerType = 400;

    private static Task<IReadOnlyDictionary<string, TypeExamples>?>? reading;

    /// <summary>The examples, once read; null until then, and when no WARNO folder is known.</summary>
    public static IReadOnlyDictionary<string, TypeExamples>? IfRead =>
        reading is { IsCompletedSuccessfully: true } done ? done.Result : null;

    /// <summary>Starts reading the examples, once; the task gives them, or null when there is no WARNO to read.</summary>
    public static Task<IReadOnlyDictionary<string, TypeExamples>?> Read(ValueGuide guide) =>
        reading ??= Task.Run(() => ReadAll(guide));

    /// <summary>WARNO's folder: the one Steam has, else one chosen in the Armory's "Choose a Game Folder…".</summary>
    private static string? GameFolder()
    {
        if (GameInstall.Find(GameTitle.Warno) is string found && File.Exists(GameInstall.BaseZipOf(found))) return found;
        IEnumerable<string> chosen = AppData.Current.State["unitGameFolders"] is JsonArray folders ? folders.Select(folder => (string?)folder).OfType<string>() : [];
        return chosen.FirstOrDefault(folder => GameInstall.IsGameFolder(folder) && GameTitle.OfFolder(folder) == GameTitle.Warno && File.Exists(GameInstall.BaseZipOf(folder)));
    }

    private static IReadOnlyDictionary<string, TypeExamples>? ReadAll(ValueGuide guide)
    {
        if (GameFolder() is not string folder) return null;
        var types = new HashSet<string>(guide.Types.Where(type => type != "meta"), StringComparer.Ordinal);
        var found = new Dictionary<string, TypeExamples>(StringComparer.Ordinal);
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var blocksSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        using var files = new GameDataFiles(GameInstall.BaseZipOf(folder));
        foreach (string path in files.PathsWhere(path => path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (files.ReadPath(path) is not string text) continue;
            string shown = path.Replace('\\', '/');
            int gameData = shown.IndexOf("GameData/", StringComparison.OrdinalIgnoreCase);
            if (gameData > 0) shown = shown[gameData..];
            ReadFile(text, shown, types, guide, found, counts, blocksSeen);
        }
        foreach ((string type, Dictionary<string, int> inFiles) in counts)
            found[type].Files.AddRange(inFiles.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => (pair.Key, pair.Value)));
        return found;
    }

    /// <summary>Every block of a known type in one file: "TType(" or "is TType" then "(", to its closing bracket.</summary>
    private static void ReadFile(string text, string file, HashSet<string> types, ValueGuide guide,
        Dictionary<string, TypeExamples> found, Dictionary<string, Dictionary<string, int>> counts, Dictionary<string, int> blocksSeen)
    {
        int length = text.Length;
        for (int at = 0; at < length; at++)
        {
            char c = text[at];
            // skip comments and quoted text, whose words are not types
            if (c == '/' && at + 1 < length && text[at + 1] == '/') { at = LineEnd(text, at) - 1; continue; }
            if (c is '\'' or '"') { int close = text.IndexOf(c, at + 1); at = close < 0 ? length : close; continue; }
            if (!(char.IsAsciiLetter(c) || c == '_') || (at > 0 && (char.IsAsciiLetterOrDigit(text[at - 1]) || text[at - 1] == '_'))) continue;
            int end = at;
            while (end < length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] == '_')) end++;
            string word = text[at..end];
            int open = end;
            while (open < length && char.IsWhiteSpace(text[open])) open++;
            if (open < length && text[open] == '(' && types.Contains(word))
            {
                if (!counts.TryGetValue(word, out Dictionary<string, int>? inFiles)) counts[word] = inFiles = new(StringComparer.Ordinal);
                inFiles[file] = inFiles.GetValueOrDefault(file) + 1;
                if (!found.TryGetValue(word, out TypeExamples? examples)) found[word] = examples = new TypeExamples();
                int seen = blocksSeen.GetValueOrDefault(word);
                if (seen < MostBlocksPerType && Close(text, open) is int close)
                {
                    blocksSeen[word] = seen + 1;
                    TakeBlock(text, at, open, close, file, examples, guide, word);
                }
            }
            at = end - 1;
        }
    }

    /// <summary>The block's example, if the type has none yet, and the lines that set its variables directly inside it.</summary>
    private static void TakeBlock(string text, int typeStart, int open, int close, string file, TypeExamples examples, ValueGuide guide, string type)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, typeStart - 1)) + 1;
        if (examples.Block == "")
        {
            string[] lines = Outdented(text[lineStart..(close + 1)].Replace("\r", "").Split('\n'));
            examples.Block = lines.Length <= MostLines ? string.Join('\n', lines) : string.Join('\n', lines.Take(MostLines - 1)) + "\n    …";
            examples.BlockFile = file;
        }
        // the lines one bracket in: "Name = value"
        int depth = 0;
        for (int at = open; at <= close; at++)
        {
            char c = text[at];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == '\n' && depth == 1)
            {
                int next = at + 1;
                int lineEnd = LineEnd(text, next);
                string line = text[next..lineEnd].Trim();
                int equals = line.IndexOf('=');
                if (equals > 0)
                {
                    string name = line[..equals].Trim();
                    // "Name = value", without the spaces the files line their "=" up with
                    string tidy = name + " = " + line[(equals + 1)..].Trim();
                    if (guide.Find(type, name) is not null && !examples.Lines.ContainsKey(name))
                        examples.Lines[name] = (tidy.Length > 160 ? tidy[..160] + " …" : tidy, file);
                }
            }
        }
    }

    /// <summary>A block's lines with the indent they all share taken off.</summary>
    private static string[] Outdented(string[] lines)
    {
        int indent = lines.Skip(1).Where(line => line.Trim() != "").Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty(0).Min();
        int first = lines.Length > 0 ? lines[0].Length - lines[0].TrimStart().Length : 0;
        indent = Math.Min(indent, first);
        return [.. lines.Select(line => line.Length >= indent ? line[indent..].TrimEnd() : line.TrimEnd())];
    }

    /// <summary>Where the bracket opened at a place closes; null when the text ends first.</summary>
    private static int? Close(string text, int open)
    {
        int depth = 0;
        for (int at = open; at < text.Length; at++)
        {
            char c = text[at];
            if (c == '/' && at + 1 < text.Length && text[at + 1] == '/') { at = LineEnd(text, at) - 1; continue; }
            if (c is '\'' or '"') { int close = text.IndexOf(c, at + 1); if (close < 0) return null; at = close; continue; }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return at;
        }
        return null;
    }

    private static int LineEnd(string text, int at)
    {
        int end = text.IndexOf('\n', at);
        return end < 0 ? text.Length : end;
    }
}
