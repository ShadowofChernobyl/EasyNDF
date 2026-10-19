using EasyNdf.App.Files;
using EasyNdf.Data.FileList;
using EasyNdf.Engine;

namespace EasyNdf.App.Editor;

/// <summary>
/// What every read file on the Files tab declares and refers to, for the descriptor viewer (the HTML app's
/// <c>refIndex</c>, <c>refUsers</c> and <c>refChains</c>): which file declares a name, which descriptors use one, and
/// the chains "All files" lists. It is made again only when the files or what they hold change.
/// </summary>
public sealed class ReferenceIndex
{
    /// <summary>A file that declares a name, and the name's type ("" for a constant). A struct: a game has some hundred thousand names.</summary>
    public readonly record struct Declared(FileEntry Entry, string Type);

    /// <summary>
    /// A descriptor that uses a name: its file, its name and its type. A struct, as the read files of a whole game hold
    /// about a million references.
    /// </summary>
    public readonly record struct User(FileEntry Entry, string Name, string Type);

    /// <summary>The files read, by folder and name.</summary>
    public IReadOnlyList<FileEntry> Files { get; }

    /// <summary>Each name declared, and the first file (in the Files tab's order) that declares it.</summary>
    public IReadOnlyDictionary<string, Declared> Names { get; }

    /// <summary>The names some other declaration refers to.</summary>
    private readonly HashSet<string> used = new(StringComparer.Ordinal);

    private Chains? chains;

    private ReferenceIndex(IReadOnlyList<FileEntry> entries)
    {
        var names = new Dictionary<string, Declared>(StringComparer.Ordinal);
        var files = new List<FileEntry>();
        foreach (FileEntry entry in entries)
        {
            if (entry.Source is null || entry.References is null) continue;
            files.Add(entry);
            foreach (DeclarationReferences declaration in entry.References)
            {
                if (declaration.Name != "") names.TryAdd(declaration.Name, new Declared(entry, declaration.Type));
                foreach (string refers in declaration.Refers)
                    if (refers != declaration.Name) used.Add(refers);
            }
        }
        files.Sort((a, b) => string.CompareOrdinal(FileListModel.ScanKey(a), FileListModel.ScanKey(b)));
        Files = files;
        Names = names;
    }

    // ---- made again only when needed ----

    private static ReferenceIndex? last;
    private static List<(FileEntry Entry, object? References)>? lastFiles;

    /// <summary>The index of the files as they are now; the one made before when nothing has changed.</summary>
    public static ReferenceIndex Of(IReadOnlyList<FileEntry> entries)
    {
        List<(FileEntry, object?)> now = [.. entries.Select(e => (e, (object?)(e.Source is null ? null : e.References)))];
        if (last is not null && lastFiles is not null && lastFiles.Count == now.Count
            && lastFiles.Zip(now).All(pair => ReferenceEquals(pair.First.Item1, pair.Second.Item1) && ReferenceEquals(pair.First.Item2, pair.Second.Item2)))
            return last;
        lastFiles = now;
        return last = new ReferenceIndex(entries);
    }

    /// <summary>Each file's descriptors by name, made for a file the first time one of its descriptors is asked about.</summary>
    private readonly Dictionary<FileEntry, Dictionary<string, List<string>>> refersByFile = [];

    /// <summary>The names a descriptor of a read file refers to; null when the file declares no descriptor of that name.</summary>
    public IReadOnlyList<string>? RefersOf(FileEntry entry, string name)
    {
        if (!refersByFile.TryGetValue(entry, out var byName))
        {
            if (entry.References is null || !Files.Contains(entry)) return null;
            byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (DeclarationReferences declaration in entry.References) byName.TryAdd(declaration.Name, declaration.Refers);
            refersByFile[entry] = byName;
        }
        return byName.TryGetValue(name, out List<string>? refers) ? refers : null;
    }

    /// <summary>The last name <see cref="UsersOf"/> was asked about, and what it found.</summary>
    private (string Name, List<User> Users)? lastUsers;

    /// <summary>
    /// The descriptors, in the read files, that refer to a name (the HTML app's <c>refUsers</c>). They are looked for
    /// each time a descriptor is shown, through every reference (a few milliseconds even for a whole game), rather than
    /// kept for every name, which took over a hundred MB.
    /// </summary>
    public IReadOnlyList<User> UsersOf(string name)
    {
        if (name == "" || !Names.ContainsKey(name)) return [];
        if (lastUsers is (string asked, List<User> found) && asked == name) return found;
        var users = new List<User>();
        foreach (FileEntry entry in Files)
        {
            foreach (DeclarationReferences declaration in entry.References!)
            {
                if (declaration.Name == "" || declaration.Name == name) continue;
                foreach (string refers in declaration.Refers)
                    if (refers == name) users.Add(new User(entry, declaration.Name, declaration.Type));
            }
        }
        lastUsers = (name, users);
        return users;
    }

    /// <summary>
    /// The chains "All files" lists (the HTML app's <c>refChains</c>): the descriptors each file starts a chain with,
    /// how many other files a file's chains reach, and how many chains there are.
    /// </summary>
    /// <param name="Tops">For each file that starts a chain, the descriptors that start one.</param>
    /// <param name="Into">For each such file, how many other files its chains can reach.</param>
    /// <param name="Count">How many chains start in all.</param>
    public sealed record Chains(Dictionary<FileEntry, HashSet<string>> Tops, Dictionary<FileEntry, int> Into, int Count);

    /// <summary>
    /// A chain starts at a descriptor that nothing in the read files refers to and that leads, directly or through
    /// descriptors of its own file, to a descriptor of another file: a unit leads to its weapon, which leads to its
    /// ammunition. Only the files chains start in are listed; everything else is reached by opening them.
    /// </summary>
    public Chains ChainsOf()
    {
        if (chains is not null) return chains;
        // each name's first declaration, in the order of the files by name
        var info = new Dictionary<string, (FileEntry Entry, List<string> Refers)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (FileEntry entry in Files)
            foreach (DeclarationReferences declaration in entry.References!)
                if (declaration.Name != "" && info.TryAdd(declaration.Name, (entry, declaration.Refers))) order.Add(declaration.Name);

        // whether a name leads to another file: 0 while being worked out (a loop leads nowhere), then 0 or 1
        var leadsOut = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Leads(string name, int depth)
        {
            if (leadsOut.TryGetValue(name, out int known)) return known == 1;
            leadsOut[name] = 0;
            (FileEntry entry, List<string> refers) = info[name];
            bool leads = false;
            foreach (string other in refers)
            {
                if (other == name || !info.TryGetValue(other, out var target)) continue;
                if (target.Entry != entry || (depth < 200 && Leads(other, depth + 1))) { leads = true; break; }
            }
            leadsOut[name] = leads ? 1 : 0;
            return leads;
        }

        var tops = new Dictionary<FileEntry, HashSet<string>>();
        var reaches = new Dictionary<FileEntry, HashSet<FileEntry>>();
        int count = 0;
        foreach (string name in order)
        {
            (FileEntry entry, List<string> refers) = info[name];
            if (!reaches.TryGetValue(entry, out HashSet<FileEntry>? reached)) reaches[entry] = reached = [];
            foreach (string other in refers)
                if (info.TryGetValue(other, out var target) && target.Entry != entry) reached.Add(target.Entry);
            if (used.Contains(name) || !Leads(name, 0)) continue;
            if (!tops.TryGetValue(entry, out HashSet<string>? starts)) tops[entry] = starts = new HashSet<string>(StringComparer.Ordinal);
            starts.Add(name);
            count++;
        }

        // how many other files a file's chains can reach
        var into = new Dictionary<FileEntry, int>();
        foreach (FileEntry entry in Files)
        {
            if (!tops.ContainsKey(entry)) continue;
            var seen = new HashSet<FileEntry> { entry };
            var waiting = new Stack<FileEntry>([entry]);
            while (waiting.Count > 0)
                if (reaches.TryGetValue(waiting.Pop(), out HashSet<FileEntry>? next))
                    foreach (FileEntry other in next)
                        if (seen.Add(other)) waiting.Push(other);
            into[entry] = seen.Count - 1;
        }
        return chains = new Chains(tops, into, count);
    }
}
