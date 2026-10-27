using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Rules;

/// <summary>
/// The property scans as the rule window uses them (the HTML app's <c>scanKeys</c>, <c>scanLabel</c>, <c>shortRef</c>,
/// <c>scanKey</c> and <c>curScanFile</c>): which scanned file fills a preset's dropdowns, and how a file is named in a
/// list. A file goes by its folder and name ("Gameplay/Gfx/Ammunition.ndf"); a list shows its name, and its folder only
/// when another file has the same name.
/// </summary>
/// <param name="scans">The scans, by file.</param>
/// <param name="listedFiles">The files loaded on the Files tab, by folder and name, which may not be scanned yet.</param>
public sealed class PropertyScans(IReadOnlyDictionary<string, ScanInfo> scans, IReadOnlyList<string> listedFiles)
{
    /// <summary>Every scanned file, by file name, then by folder.</summary>
    public IReadOnlyList<string> Keys { get; } = ByFileName(scans.Keys);

    /// <summary>The files loaded on the Files tab that are not scanned yet, in the same order.</summary>
    public IReadOnlyList<string> Unread { get; } = ByFileName(listedFiles.Where(f => !scans.ContainsKey(f)).Distinct());

    /// <summary>
    /// Files in the order of <see cref="CompareByFileName"/>, each name put in small letters once rather than at each
    /// comparison: a game has 4,000 files.
    /// </summary>
    private static List<string> ByFileName(IEnumerable<string> files) =>
        [.. files.OrderBy(file => BaseName(file).ToLowerInvariant(), StringComparer.Ordinal).ThenBy(file => file, StringComparer.Ordinal)];

    /// <summary>
    /// How the scanned files can be named, worked out once: for each way, in small letters (the whole name, or its end
    /// from a folder on, such as "gfx/ammunition.ndf"), how many files answer to it, and the first of them in the
    /// order of their whole names. A rule names a file so; comparing each file with every other took over a second
    /// with a game's 4,000 files.
    /// </summary>
    private (Dictionary<string, string> Whole, Dictionary<string, string> End, Dictionary<string, int> Answering)? names;

    private (Dictionary<string, string> Whole, Dictionary<string, string> End, Dictionary<string, int> Answering) Names()
    {
        if (names is { } made) return made;
        var whole = new Dictionary<string, string>(StringComparer.Ordinal);
        var end = new Dictionary<string, string>(StringComparer.Ordinal);
        var answering = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in scans.Keys.Order(StringComparer.Ordinal))
        {
            string lower = key.ToLowerInvariant();
            whole.TryAdd(lower, key);
            answering[lower] = answering.GetValueOrDefault(lower) + 1;
            // each end that starts after a "/": "a/b/c.ndf" answers to "b/c.ndf" and "c.ndf"
            for (int slash = lower.IndexOf('/'); slash >= 0; slash = lower.IndexOf('/', slash + 1))
            {
                string ending = lower[(slash + 1)..];
                end.TryAdd(ending, key);
                answering[ending] = answering.GetValueOrDefault(ending) + 1;
            }
        }
        names = (whole, end, answering);
        return names.Value;
    }

    /// <summary>Whether a file is scanned.</summary>
    public bool IsScanned(string file) => scans.ContainsKey(file);

    /// <summary>What a file holds; nothing for a file not scanned. A rule may name the file by its name alone.</summary>
    public ScanInfo Of(string? reference) => Find(reference) is { Length: > 0 } key ? scans[key] : ScanInfo.Empty;

    /// <summary>
    /// The scanned file a rule names (the HTML app's <c>scanKey</c>): a rule may hold a bare file name, or the name with
    /// some or all of its folders in front, in any capitals. "" when none is scanned.
    /// </summary>
    public string Find(string? reference)
    {
        string wanted = reference ?? "";
        if (wanted == "" || scans.ContainsKey(wanted)) return wanted;
        string lower = wanted.ToLowerInvariant();
        var (whole, end, _) = Names();
        return whole.GetValueOrDefault(lower) ?? end.GetValueOrDefault(lower) ?? "";
    }

    /// <summary>A file's name, without its folders.</summary>
    public static string BaseName(string file) => file[(file.LastIndexOf('/') + 1)..];

    /// <summary>
    /// Every scanned or loaded file, by its name without folders, in any capitals: made once, as a game's 4,000 files
    /// would otherwise each be compared with every other.
    /// </summary>
    private ILookup<string, string>? filesByName;

    /// <summary>
    /// The folder a list shows for a file (the HTML app's <c>fileDir</c>): none when no other scanned or loaded file has
    /// the same name; otherwise the folder it sits in, with folders above it added only while two files still look alike.
    /// </summary>
    public string Folder(string file)
    {
        string name = BaseName(file);
        if (file == name) return "";
        filesByName ??= AllFiles().ToLookup(BaseName, StringComparer.OrdinalIgnoreCase);
        var sameName = filesByName[name].Where(other => other != file).ToList();
        if (sameName.Count == 0) return "";
        string[] mine = file[..(file.Length - name.Length - 1)].Split('/');
        List<string[]> others = [.. sameName.Select(o => o[..Math.Max(0, o.Length - name.Length - 1)].ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries))];
        for (int count = 1; count <= mine.Length; count++)
        {
            string tail = string.Join("/", mine[^count..]).ToLowerInvariant();
            if (count == mine.Length || !others.Any(o => string.Join("/", o.TakeLast(count)) == tail)) return string.Join("/", mine[^count..]);
        }
        return string.Join("/", mine);
    }

    /// <summary>A folder as a list shows it: "(Gameplay\Gfx\)", where the closing backslash says folder in the fewest characters.</summary>
    public static string FolderTag(string folder) => "(" + folder.Replace('/', '\\') + "\\)";

    /// <summary>A file as a list names it: "Ammunition.ndf", or "GDConstants.ndf (Strategic\)" beside another of that name.</summary>
    public string Label(string file)
    {
        string folder = Folder(file);
        return BaseName(file) + (folder != "" ? " " + FolderTag(folder) : "");
    }

    /// <summary>
    /// The shortest way to name a scanned file that no other scanned file answers to (the HTML app's <c>shortRef</c>):
    /// its name, or its name with as many folders as it takes. A Lookup rule stores its other file so.
    /// </summary>
    public string ShortName(string file)
    {
        string[] parts = file.Split('/');
        var (_, _, answering) = Names();
        // a scanned file answers to its own names; another file answering too makes a name ambiguous
        int itself = scans.ContainsKey(file) ? 1 : 0;
        for (int count = 1; count <= parts.Length; count++)
        {
            string reference = string.Join("/", parts[^count..]);
            if (answering.GetValueOrDefault(reference.ToLowerInvariant()) <= itself) return reference;
        }
        return file;
    }

    /// <summary>
    /// The scanned file whose names fill a preset's dropdowns (the HTML app's <c>curScanFile</c>): the one picked for it
    /// in the Property scan; else a loaded file that has the preset; else the scanned file the Files tab would give the
    /// preset to; else the one that fits the preset's name best, when only one does. "" for none.
    /// </summary>
    /// <param name="preset">The preset.</param>
    /// <param name="modPresets">The presets of its mod, which the Files tab chooses among.</param>
    /// <param name="picked">The file picked for it, if any.</param>
    /// <param name="filesWithPreset">The loaded files that have the preset, by folder and name, in list order.</param>
    public string ForPreset(ModPreset preset, IReadOnlyList<ModPreset> modPresets, string? picked, IEnumerable<string> filesWithPreset)
    {
        if (picked is not null && scans.ContainsKey(picked)) return picked;
        if (filesWithPreset.FirstOrDefault(scans.ContainsKey) is string listed) return listed;

        static string FolderOf(string file) => file[..Math.Max(0, file.Length - BaseName(file).Length - 1)];
        // only a file whose name the preset's name holds can fit it: the others are passed over at once, as matching
        // each of a game's 4,000 files against every preset took a moment each time the rule window opened
        string presetName = preset.Name.Replace('\\', '/');
        List<string> named = [.. Keys.Where(file => presetName.Contains(BaseName(file), StringComparison.Ordinal))];
        if (named.FirstOrDefault(f => ReferenceEquals(PresetMatching.Match(BaseName(f), FolderOf(f), modPresets), preset)) is string mine) return mine;

        // The Files tab leaves a file alone when two presets fit it equally; here the question is only which file fits this preset best.
        int best = -1;
        var bestFiles = new List<string>();
        foreach (string file in named)
        {
            int fit = PresetMatching.Fit(preset.Name, BaseName(file), FolderOf(file).ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries));
            if (fit < 0) continue;
            if (fit > best) { best = fit; bestFiles = [file]; }
            else if (fit == best) bestFiles.Add(file);
        }
        return bestFiles.Count == 1 ? bestFiles[0] : "";
    }

    /// <summary>Every scanned and loaded file, once.</summary>
    private IEnumerable<string> AllFiles() => scans.Keys.Concat(listedFiles).Distinct();

    /// <summary>The HTML app's <c>byFileName</c>: by file name in small letters, then by the whole name.</summary>
    public static int CompareByFileName(string a, string b)
    {
        int byName = string.CompareOrdinal(BaseName(a).ToLowerInvariant(), BaseName(b).ToLowerInvariant());
        return byName != 0 ? byName : string.CompareOrdinal(a, b);
    }
}
