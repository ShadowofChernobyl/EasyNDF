namespace EasyNdf.Data.Game;

/// <summary>
/// The game's English texts: unit names, weapon names, division names and so on, from all of its English dictionaries.
/// A text is found by its key, such as "HMNGALJGAU", whichever dictionary it is in.
/// </summary>
public sealed class GameTexts
{
    private readonly Dictionary<ulong, string> texts = [];

    /// <summary>The dictionaries that could not be read, each with what went wrong.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>How many texts were read.</summary>
    public int Count => texts.Count;

    /// <summary>
    /// Reads the English dictionaries: the .dic files in a "US" folder (WARNO and Steel Division 2), or named "...-US"
    /// (newer WARNO archives). Old ("deprecated") ones are left out, as are maps' texts and the soldiers' names.
    /// </summary>
    public static GameTexts Read(GameArchives archives)
    {
        var read = new GameTexts();
        foreach (ArchiveFile file in archives.Files.Values.Where(IsEnglishDictionary).OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var (key, text) in Dictionaries.Read(file.Read())) read.texts[key] = text;
            }
            catch (Exception problem) when (problem is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                read.Problems.Add(file.Name + ": " + problem.Message);
            }
        }
        return read;
    }

    /// <summary>Texts made up for tests: each key with its text.</summary>
    public static GameTexts FromPairs(IEnumerable<(string Key, string Text)> pairs)
    {
        var made = new GameTexts();
        foreach (var (key, text) in pairs)
            if (Dictionaries.KeyOf(key) is ulong number) made.texts[number] = text;
        return made;
    }

    /// <summary>The text for a key such as "HMNGALJGAU", trimmed; null when there is none, or it is empty.</summary>
    public string? Get(string? key)
    {
        if (string.IsNullOrEmpty(key) || Dictionaries.KeyOf(key) is not ulong number) return null;
        return texts.TryGetValue(number, out string? text) && text.Trim() != "" ? text.Trim() : null;
    }

    /// <summary>Whether a file in the archives is one of the English dictionaries read.</summary>
    private static bool IsEnglishDictionary(ArchiveFile file)
    {
        if (!file.Name.EndsWith(".dic", StringComparison.OrdinalIgnoreCase)) return false;
        string[] parts = file.Name.ToUpperInvariant().Split('/');
        if (parts.Contains("DEPRECATED") || parts.Contains("MAP") || parts.Contains("NAMES")) return false;
        return parts.Contains("US") || parts[^1].EndsWith("-US.DIC", StringComparison.Ordinal);
    }
}
