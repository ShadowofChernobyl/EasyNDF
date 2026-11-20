using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;

namespace EasyNdf.Data;

/// <summary>
/// Where each mod's files are kept: a folder beside the mod's file in the Mods folder, named as it is ("WTO" for
/// "WTO.mod.json"), with every file its File rules carry at the place it goes in the exported mod
/// ("GameData\Assets\2D\...\SOV_FLAG.png"), and the Armory pictures it uses in "Armory Pictures".
/// <para>
/// A file the user picks is first kept where it always was (by content in Mod Files, a picture in Pictures), and moved
/// into the mod's folder a moment later (<see cref="Sync"/>); those two folders then hold only what no mod's folder has,
/// such as a file a rule no longer uses, until "Clean Up Mod Files" takes it away. A file several places use (in one mod
/// or several) is stored once: each place is a hard link to it. Mods name their files as before (a File rule by content,
/// a picture by its path), and a file is found by that name wherever it now is, so no mod is rewritten.
/// </para>
/// </summary>
/// <param name="ownPictures">The app's own Pictures folder: where a picture is kept until it is moved, and kept again when no mod has it.</param>
public sealed class ModFileViews
{
    /// <summary>The folder of a mod's pictures in its folder.</summary>
    public const string PicturesFolderName = "Armory Pictures";

    /// <summary>The list of what a mod's folder holds: each place in it and the file it is (its stored name, or "picture:" and a picture's name).</summary>
    private const string ListName = ".easyndf.json";

    /// <summary>The list an earlier build kept (each place and the file it linked to); it is replaced.</summary>
    private const string OldListName = ".easyndf-files.json";

    private const string PicturePrefix = "picture:";

    private readonly string modsFolder;
    private readonly ModFileStore store;
    private readonly string ownPictures;

    /// <summary>Folder changes, one at a time.</summary>
    private readonly object gate;

    /// <summary>The list of where each stored file is, read from the folders' lists when first asked for.</summary>
    private readonly object indexGate = new();
    private Dictionary<string, List<string>>? index;

    /// <param name="folderLock">Held while files are laid out: a mod's folder moving waits (<see cref="ModStore.FolderLock"/>).</param>
    public ModFileViews(string modsFolder, ModFileStore store, string ownPictures, object? folderLock = null)
    {
        this.modsFolder = modsFolder;
        this.store = store;
        this.ownPictures = ownPictures;
        gate = folderLock ?? new object();
        store.Elsewhere = FindStored;
    }

    /// <summary>A mod's folder, by the name of its file without ".mod.json".</summary>
    public string FolderOf(string modFileStem) => Path.Combine(modsFolder, modFileStem);

    /// <summary>
    /// The files a mod's folder should hold, by their place in it: the files its File rules carry, at their destination,
    /// and the pictures its text names, under Armory Pictures. A destination that would leave the folder ("..", a drive)
    /// is left out.
    /// </summary>
    public static Dictionary<string, string> WantedIn(string modText)
    {
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        JsonNode? root;
        try { root = JsonNode.Parse(modText); }
        catch (JsonException) { return wanted; }
        foreach (JsonNode? node in Walk(root))
        {
            if (node is JsonObject && FileAndTextRules.CarriedFileOf(node) is { } file && ModFileStore.IsStoredName(file.StoredName)
                && SafePlace(file.Destination) is string place)
                wanted[place] = file.StoredName;
            else if (node is JsonValue value && value.TryGetValue(out string? text) && PictureNameIn(text) is string picture)
                wanted[Path.Combine(PicturesFolderName, picture)] = PicturePrefix + picture;
        }
        return wanted;
    }

    /// <summary>
    /// The name of a picture the app kept, when a text is the path of one (in a Pictures or Armory Pictures folder, on
    /// this computer or the one the mod was made on); null otherwise.
    /// </summary>
    private static string? PictureNameIn(string text)
    {
        if (text.Length < 5 || !text.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(text)) return null;
        string? folder = Path.GetFileName(Path.GetDirectoryName(text));
        if (!string.Equals(folder, "Pictures", StringComparison.OrdinalIgnoreCase) && !string.Equals(folder, PicturesFolderName, StringComparison.OrdinalIgnoreCase)) return null;
        string name = Path.GetFileName(text);
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? name : null;
    }

    private static IEnumerable<JsonNode?> Walk(JsonNode? node)
    {
        yield return node;
        if (node is JsonObject obj)
            foreach ((_, JsonNode? child) in obj) foreach (JsonNode? inner in Walk(child)) yield return inner;
        else if (node is JsonArray array)
            foreach (JsonNode? child in array) foreach (JsonNode? inner in Walk(child)) yield return inner;
    }

    /// <summary>A destination as a place inside a mod's folder ("GameData\Assets\..."); null when it would lead out of it.</summary>
    public static string? SafePlace(string destination)
    {
        string place = destination.Replace('/', '\\').Trim().TrimStart('\\');
        if (place == "" || place.Contains(':') || Path.IsPathRooted(place)) return null;
        string[] parts = place.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return null;
        if (parts.Length > 0 && (parts[0].StartsWith('.') || string.Equals(parts[0], PicturesFolderName, StringComparison.OrdinalIgnoreCase))) return null;
        // the mod's own file is beside its files
        if (parts.Length == 1 && parts[0].EndsWith(".mod.json", StringComparison.OrdinalIgnoreCase)) return null;
        return string.Join('\\', parts);
    }

    // ---- finding a file ----

    /// <summary>Finds a picture the app kept by its name, wherever it is now (<see cref="FindPicture"/>); null with no data folder.</summary>
    public static Func<string, string?>? PictureFinder { get; set; }

    /// <summary>Where a stored file is in the mods' folders; null when none has it.</summary>
    public string? FindStored(string storedName)
    {
        lock (indexGate)
        {
            index ??= ReadIndex();
            if (index.TryGetValue(storedName, out List<string>? paths))
                foreach (string path in paths)
                    if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// Where a picture the app kept is, by its name: in Pictures, or in a mod's Armory Pictures; null when it is in
    /// neither. Only the name is used, so a mod made on another computer finds its pictures here, and a mod file can't
    /// name any other file on this one.
    /// </summary>
    public string? FindPicture(string pictureName)
    {
        if (pictureName == "" || pictureName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        string kept = Path.Combine(ownPictures, pictureName);
        if (File.Exists(kept)) return kept;
        foreach (string folder in ModFolders())
        {
            string inMod = Path.Combine(folder, PicturesFolderName, pictureName);
            if (File.Exists(inMod)) return inMod;
        }
        return null;
    }

    /// <summary>Whether a picture of this name is kept anywhere: a new one takes another name.</summary>
    public bool HasPicture(string pictureName) => FindPicture(pictureName) is not null;

    private IEnumerable<string> ModFolders() =>
        Directory.Exists(modsFolder) ? Directory.EnumerateDirectories(modsFolder).Where(folder => !Path.GetFileName(folder).StartsWith('.')) : [];

    private Dictionary<string, List<string>> ReadIndex()
    {
        var read = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string folder in ModFolders())
                foreach ((string place, string id) in ReadList(Path.Combine(folder, ListName)))
                    if (!id.StartsWith(PicturePrefix, StringComparison.Ordinal)) Add(read, id, Path.Combine(folder, place));
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
        return read;
    }

    private static void Add(Dictionary<string, List<string>> to, string id, string path)
    {
        if (!to.TryGetValue(id, out List<string>? paths)) to[id] = paths = [];
        if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
    }

    private void Indexed(string id, string path)
    {
        if (id.StartsWith(PicturePrefix, StringComparison.Ordinal)) return;
        lock (indexGate) if (index is not null) Add(index, id, path);
    }

    private void Unindexed(string id, string path)
    {
        lock (indexGate) if (index is not null && index.TryGetValue(id, out List<string>? paths)) paths.RemoveAll(each => string.Equals(each, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Where a file is now, by what a list calls it; null when it is nowhere.</summary>
    private string? SourceOf(string id) =>
        id.StartsWith(PicturePrefix, StringComparison.Ordinal) ? FindPicture(id[PicturePrefix.Length..]) : store.PathOf(id);

    /// <summary>Where a file waits until a mod's folder has it (Mod Files or Pictures).</summary>
    private string WaitingPlaceOf(string id) =>
        id.StartsWith(PicturePrefix, StringComparison.Ordinal) ? Path.Combine(ownPictures, id[PicturePrefix.Length..]) : Path.Combine(store.Folder, id);

    /// <summary>Whether a file is anywhere but at one path.</summary>
    private bool IsElsewhere(string id, string butPath)
    {
        if (File.Exists(WaitingPlaceOf(id))) return true;
        if (id.StartsWith(PicturePrefix, StringComparison.Ordinal))
        {
            string name = id[PicturePrefix.Length..];
            return ModFolders().Select(folder => Path.Combine(folder, PicturesFolderName, name))
                .Any(path => !string.Equals(path, butPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path));
        }
        lock (indexGate)
        {
            index ??= ReadIndex();
            return index.TryGetValue(id, out List<string>? paths)
                && paths.Any(path => !string.Equals(path, butPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path));
        }
    }

    // ---- laying out a mod's folder ----

    /// <summary>
    /// Lays out a mod's folder as its text says: new or changed files put in place, and the places it no longer has
    /// taken away. A file taken away that is nowhere else goes back to wait in Mod Files or Pictures, so nothing is lost
    /// without the user asking (an undo brings it back). The copies waiting there of what the folder now has go. Only
    /// what changed since the last time is touched (the folder's list says what was there); a problem with one file
    /// (held by another program) leaves it for the next time.
    /// </summary>
    public void Sync(string modFileStem, string modText)
    {
        Dictionary<string, string> wanted = WantedIn(modText);
        lock (gate)
        {
            string folder = FolderOf(modFileStem);
            // only the folder the mod is in: one renamed or deleted since is left as it is
            if (!File.Exists(Path.Combine(folder, modFileStem + ".mod.json"))) return;
            string listPath = Path.Combine(folder, ListName);
            Dictionary<string, string> had = ReadList(listPath);
            TryDelete(Path.Combine(folder, OldListName));
            foreach ((string place, string was) in had)
                if (!wanted.TryGetValue(place, out string? now) || now != was)
                    Retire(Path.Combine(folder, place), was);
            var made = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string place, string id) in wanted)
            {
                string path = Path.Combine(folder, place);
                if (had.TryGetValue(place, out string? was) && was == id && File.Exists(path))
                {
                    made[place] = id;
                    Indexed(id, path);
                    continue;
                }
                if (SourceOf(id) is not string source) continue;
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) || Link(source, path))
                {
                    made[place] = id;
                    Indexed(id, path);
                }
            }
            WriteList(listPath, made);
            RemoveEmptyFolders(folder);
            // a file now in the mod's folder no longer waits
            foreach (string id in made.Values.Distinct())
            {
                string waiting = WaitingPlaceOf(id);
                if (File.Exists(waiting) && IsElsewhere(id, waiting)) TryDelete(waiting);
            }
        }
    }

    /// <summary>A file a mod's folder no longer has: gone, or back to wait in Mod Files or Pictures when it is nowhere else.</summary>
    private void Retire(string path, string id)
    {
        Unindexed(id, path);
        try
        {
            if (!File.Exists(path)) return;
            string waiting = WaitingPlaceOf(id);
            if (IsElsewhere(id, path)) File.Delete(path);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(waiting)!);
                File.Move(path, waiting);
            }
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// When nothing waits in Mod Files or Pictures any more, the folder goes: what the user sees is the mods' folders.
    /// </summary>
    public void RemoveEmptyWaitingFolders()
    {
        foreach (string folder in new[] { store.Folder, ownPictures })
        {
            try { if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>A mod's file renamed: its folder takes the new name too. A folder already there of that name is left as it is.</summary>
    public void Rename(string oldStem, string newStem)
    {
        lock (gate)
        {
            string from = FolderOf(oldStem), to = FolderOf(newStem);
            if (from == to) return;
            try
            {
                if (!Directory.Exists(from)) return;
                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                {
                    // a change in case only: through a name of its own, as Windows sees both as the same folder
                    string between = from + ".renaming";
                    Directory.Move(from, between);
                    Directory.Move(between, to);
                }
                else if (!Directory.Exists(to)) Directory.Move(from, to);
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
            finally
            {
                lock (indexGate) index = null;
            }
        }
    }

    private static Dictionary<string, string> ReadList(string path)
    {
        try
        {
            return File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) is { } list
                ? new Dictionary<string, string>(list, StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void WriteList(string path, Dictionary<string, string> list)
    {
        try
        {
            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
            File.WriteAllText(path, JsonSerializer.Serialize(list));
            File.SetAttributes(path, FileAttributes.Hidden);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Puts a file at a place: a hard link to it, or a copy when there can't be one. Returns whether it is there.</summary>
    private static bool Link(string source, string path)
    {
        try
        {
            if (!File.Exists(source)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path)) File.Delete(path);
            if (!CreateHardLink(Long(path), Long(source), IntPtr.Zero)) File.Copy(source, path);
            return true;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>A path Windows takes past 260 characters.</summary>
    private static string Long(string path) => path.Length >= 240 && !path.StartsWith(@"\\?\", StringComparison.Ordinal) ? @"\\?\" + Path.GetFullPath(path) : path;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
    }

    private static void RemoveEmptyFolders(string folder)
    {
        try
        {
            foreach (string inner in Directory.GetDirectories(folder))
            {
                RemoveEmptyFolders(inner);
                if (!Directory.EnumerateFileSystemEntries(inner).Any()) Directory.Delete(inner);
            }
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);

    // ---- Clean Up Mod Files ----

    /// <summary>What a clean-up would take away, and the room it would give back.</summary>
    /// <param name="Folders">Folders in the Mods folder of mods that are gone.</param>
    /// <param name="Files">Files waiting in Mod Files and Pictures that no mod uses.</param>
    /// <param name="Bytes">The room those take.</param>
    /// <param name="Used">What the mods use: a folder's file still used is kept when the folder goes.</param>
    public sealed record CleanUp(IReadOnlyList<string> Folders, IReadOnlyList<string> Files, long Bytes, IReadOnlySet<string> Used)
    {
        /// <summary>Whether there is nothing to take away.</summary>
        public bool IsEmpty => Folders.Count == 0 && Files.Count == 0;
    }

    /// <summary>What no mod uses any more: the folders of mods that are gone, and the files waiting that no mod uses.</summary>
    /// <param name="mods">Every mod: the name of its file without ".mod.json", and its text.</param>
    public CleanUp FindUnused(IReadOnlyList<(string Stem, string Text)> mods)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((_, string text) in mods) used.UnionWith(WantedIn(text).Values);
        var stems = new HashSet<string>(mods.Select(mod => mod.Stem), StringComparer.OrdinalIgnoreCase);
        // a folder still holding a mod file (one the app could not read) is never a gone mod's
        List<string> folders = [.. ModFolders().Where(folder => !stems.Contains(Path.GetFileName(folder)) && !Directory.EnumerateFiles(folder, "*.mod.json").Any())];
        long bytes = 0;
        foreach (string folder in folders)
            foreach ((string place, string id) in ReadList(Path.Combine(folder, ListName)))
                if (!used.Contains(id)) bytes += SizeOf(Path.Combine(folder, place));
        var files = new List<string>();
        IEnumerable<(string Path, string Id)> waiting =
            (Directory.Exists(store.Folder) ? Directory.GetFiles(store.Folder).Where(file => ModFileStore.IsStoredName(Path.GetFileName(file))).Select(file => (file, Path.GetFileName(file))) : [])
            .Concat(Directory.Exists(ownPictures) ? Directory.GetFiles(ownPictures, "*.png").Select(file => (file, PicturePrefix + Path.GetFileName(file))) : []);
        foreach ((string file, string id) in waiting)
        {
            if (used.Contains(id)) continue;
            files.Add(file);
            bytes += SizeOf(file);
        }
        return new CleanUp(folders, files, bytes, used);
    }

    private static long SizeOf(string file)
    {
        try { return File.Exists(file) ? new FileInfo(file).Length : 0; }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>
    /// Takes away what a clean-up found. A file in a gone mod's folder that a mod still uses and that is nowhere else
    /// goes back to wait instead (the next lay-out puts it in that mod's folder). Returns how many files and folders
    /// were taken away.
    /// </summary>
    public int Remove(CleanUp cleanUp)
    {
        int removed = 0;
        lock (gate)
        {
            foreach (string folder in cleanUp.Folders)
            {
                foreach ((string place, string id) in ReadList(Path.Combine(folder, ListName)))
                    if (cleanUp.Used.Contains(id)) Retire(Path.Combine(folder, place), id);
                try { Directory.Delete(folder, recursive: true); removed++; }
                catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
            }
            foreach (string file in cleanUp.Files)
            {
                try { File.Delete(file); removed++; }
                catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
            }
            lock (indexGate) index = null;
        }
        RemoveEmptyWaitingFolders();
        return removed;
    }
}
