using System.Globalization;
using EasyNdf.Data.Languages;

namespace EasyNdf.Data;

/// <summary>
/// The mods, kept in the Mods folder: each in a folder of its own named after it, with its file ("WTO\WTO.mod.json", in
/// the same format as "Export mod", so it can also be copied in or out by hand) beside the files it carries
/// (<see cref="ModFileViews"/>). A mod file kept the earlier way, loose in the Mods folder, moves into its folder at start.
/// </summary>
public sealed class ModStore
{
    /// <summary>The empty mod the app comes with, and the one selected at first.</summary>
    public const string BlankModName = "My mod";

    /// <summary>The Mods folder, or null for mods kept in memory only.</summary>
    private readonly string? folder;
    private readonly Dictionary<Mod, string> pathOf = [];

    /// <summary>Mods kept in memory only, never saved: for drawing the app without touching saved data, and the designer.</summary>
    public ModStore() { }

    /// <summary>Reads every mod in the folder.</summary>
    public ModStore(string modsFolder)
    {
        folder = modsFolder;
        Directory.CreateDirectory(folder);
        MoveIntoFolders();
        // Tuned for speed, as the app's window waits for this: the files are read several at a time, and each from
        // its bytes (ModFiles.Read). They are then taken in order, one by one, as two files may hold one name. Keep
        // it that way when tidying.
        string[] paths = [.. ModFilesOnDisk().Order(StringComparer.OrdinalIgnoreCase)];
        var read = new (Mod? Mod, string? Problem)[paths.Length];
        Parallel.For(0, paths.Length, new ParallelOptions { MaxDegreeOfParallelism = MostReadAtOnce }, at => read[at] = ReadMod(paths[at]));
        for (int at = 0; at < paths.Length; at++) Take(paths[at], read[at]);
        Mods.Sort(ByName);
    }

    /// <summary>How many mod files are read at once at the start: more would hold several large files in memory together.</summary>
    private const int MostReadAtOnce = 4;

    private const string Extension = ".mod.json";

    /// <summary>Held while a mod's folder moves, and while its files are laid out (<see cref="ModFileViews"/>), so the two never meet.</summary>
    public object FolderLock { get; } = new();

    /// <summary>Where the file of a mod whose file is named so is kept: in its own folder.</summary>
    private string PathFor(string stem) => Path.Combine(folder!, stem, stem + Extension);

    /// <summary>Mod files kept loose in the Mods folder (as earlier versions did) move into a folder of their own.</summary>
    private void MoveIntoFolders()
    {
        foreach (string loose in Directory.GetFiles(folder!, "*" + Extension))
        {
            string target = PathFor(StemOf(loose));
            try
            {
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(loose, target);
            }
            // one that can't move (held by another program) is read where it is, and moves another time
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Every mod file: each in its folder, and any still loose in the Mods folder.</summary>
    private IEnumerable<string> ModFilesOnDisk() =>
        Directory.GetFiles(folder!, "*" + Extension)
            .Concat(Directory.GetDirectories(folder!).Select(inner => Path.Combine(inner, Path.GetFileName(inner) + Extension)).Where(File.Exists));

    /// <summary>The path a mod file would have loose in the Mods folder: how names are kept apart ("WTO", "WTO (2)").</summary>
    private string FlatPathOf(string path) => Path.Combine(folder!, Path.GetFileName(path));

    /// <summary>A mod was saved: the name of its file without ".mod.json", and the text written. Raised on the saving thread.</summary>
    public event Action<string, string>? Saved;

    /// <summary>A mod's file was renamed: the old and the new name, without ".mod.json".</summary>
    public event Action<string, string>? FileRenamed;

    /// <summary>The name of a mod's file without ".mod.json"; null for a mod not saved, or kept in memory only.</summary>
    public string? FileStemOf(Mod mod) => pathOf.TryGetValue(mod, out string? path) ? StemOf(path) : null;

    private static string StemOf(string path) => Path.GetFileName(path)[..^Extension.Length];

    /// <summary>The mods, sorted by name.</summary>
    public List<Mod> Mods { get; } = [];

    /// <summary>Files in the folder that could not be read, with the reason. The app tells the user about them once.</summary>
    public List<string> Unreadable { get; } = [];

    /// <summary>The mod with this name, or null.</summary>
    public Mod? Find(string name) => Mods.FirstOrDefault(m => m.Name == name);

    /// <summary>A mod name no mod has yet: the name itself, or "name (2)", "name (3)"...</summary>
    public string FreeName(string name)
    {
        if (Find(name) is null) return name;
        int i = 2;
        while (Find(name + " (" + i + ")") is not null) i++;
        return name + " (" + i + ")";
    }

    /// <summary>Adds a mod and saves it. Its name must be free (see <see cref="FreeName"/>).</summary>
    public void Add(Mod mod)
    {
        if (Find(mod.Name) is not null) throw new InvalidOperationException(DataTexts.Get("Rules.ModFile.NameTaken", ("name", mod.Name)));
        Mods.Add(mod);
        Mods.Sort(ByName);
        Save(mod);
    }

    /// <summary>
    /// The mod files another mod has, and those on disk the app could not read: a file that failed to read is listed as
    /// unreadable, and a new mod of the same name was saved over it, losing it for good.
    /// </summary>
    private List<string> PathsTakenBesides(Mod mod)
    {
        List<string> taken = [.. pathOf.Where(p => p.Key != mod).Select(p => FlatPathOf(p.Value))];
        pathOf.TryGetValue(mod, out string? own);
        try
        {
            taken.AddRange(ModFilesOnDisk().Where(file => !string.Equals(file, own, StringComparison.OrdinalIgnoreCase)).Select(FlatPathOf));
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // the folder cannot be listed: the mods the app has are all it can go by
        }
        return taken;
    }

    /// <summary>
    /// Saves a mod after a change, in the file named after it: "WTO\WTO.mod.json", or "WTO (2)\WTO (2).mod.json" while
    /// another has that name (a mod "WTO?", whose file name is "WTO" too, or a file the app could not read). A renamed
    /// mod's folder and file take its new name, and one kept as "WTO (2)" moves to "WTO" once that is free, so they
    /// never keep a name it no longer has.
    /// </summary>
    /// <remarks>
    /// Tuned for speed, as every change saves the open mod on the window's thread: the mod's text is written as it is
    /// made, rather than read back into JSON and made again, which gives the same text but for one rare case
    /// (<see cref="JsonFiles.AsMadeAgain"/>) and took a tenth of a second for a large mod. Keep it that way when tidying.
    /// </remarks>
    public void Save(Mod mod)
    {
        if (folder is null) { Mods.Sort(ByName); return; }
        pathOf.TryGetValue(mod, out string? oldPath);
        string path = PathFor(StemOf(FileNames.FreePath(folder, mod.Name, Extension, PathsTakenBesides(mod))));

        string text = JsonFiles.AsMadeAgain(ModFiles.StoredText(mod));
        if (oldPath is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonFiles.WriteText(path, text);
        }
        else
        {
            // written where it is first, so a move that fails loses nothing
            JsonFiles.WriteText(oldPath, text);
            if (oldPath != path) path = Move(oldPath, path);
        }
        pathOf[mod] = path;
        if (oldPath is not null && oldPath != path) FileRenamed?.Invoke(StemOf(oldPath), StemOf(path));
        Saved?.Invoke(StemOf(path), text);
        Mods.Sort(ByName);
    }

    /// <summary>
    /// Moves a mod's file to where its new name puts it, with its folder (and the files in it) when it has one; when the
    /// folder can't move (open in another program) or one of the new name is there, the file alone moves. Returns where
    /// it is now.
    /// </summary>
    private string Move(string oldPath, string path)
    {
        string oldFolder = Path.GetDirectoryName(oldPath)!, newFolder = Path.GetDirectoryName(path)!;
        bool hasFolder = string.Equals(Path.GetFileName(oldFolder), StemOf(oldPath), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(oldFolder), folder, StringComparison.OrdinalIgnoreCase);
        lock (FolderLock)
        {
            try
            {
                if (hasFolder && oldFolder != newFolder)
                {
                    if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        // a change in case only: through a name of its own, as Windows sees both as the same folder
                        string between = oldFolder + ".renaming";
                        Directory.Move(oldFolder, between);
                        Directory.Move(between, newFolder);
                    }
                    else if (!Directory.Exists(newFolder)) Directory.Move(oldFolder, newFolder);
                    if (Directory.Exists(newFolder) && File.Exists(Path.Combine(newFolder, Path.GetFileName(oldPath))))
                        oldPath = Path.Combine(newFolder, Path.GetFileName(oldPath));
                }
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
            Directory.CreateDirectory(newFolder);
            // Windows names files without minding case: a name changed in case only is the same file, renamed
            if (oldPath != path) File.Move(oldPath, path);
        }
        return path;
    }

    /// <summary>Deletes a mod's file; its folder, with the files it carries, stays until "Clean Up Mod Files".</summary>
    public void Delete(Mod mod)
    {
        if (pathOf.Remove(mod, out string? path)) File.Delete(path);
        Mods.Remove(mod);
    }

    /// <summary>
    /// Puts back the mods as they were at an earlier moment (Undo and Redo): mods not among them are deleted, and the rest
    /// take the place of the mods of the same name. Only mods that differ are written to their files again.
    /// </summary>
    public void Restore(IReadOnlyList<Mod> mods)
    {
        foreach (Mod old in Mods.ToList())
            if (!mods.Any(m => m.Name == old.Name))
            {
                try { Delete(old); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Mods.Remove(old); }
            }
        foreach (Mod mod in mods)
        {
            Mod? old = Find(mod.Name);
            if (old is not null)
            {
                // the mod's file stays the same file
                if (pathOf.Remove(old, out string? path)) pathOf[mod] = path;
                Mods.Remove(old);
            }
            Mods.Add(mod);
            // as every save of the app: one that fails (a file held by another program) leaves the mod as it is in
            // the app, and the next change saves it again
            try
            {
                if (old is null || ModFiles.StoredText(old) != ModFiles.StoredText(mod)) Save(mod);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        Mods.Sort(ByName);
    }

    /// <summary>
    /// Adds the mods the app comes with that are missing: WARNO Tactical Overhaul (from <paramref name="defaultModText"/>)
    /// and the empty "My mod". The app calls this the first time it runs and after "Clear all data"; a default mod the user
    /// deleted otherwise stays deleted. Returns whether anything was added.
    /// </summary>
    public bool AddDefaultMods(string defaultModText)
    {
        bool added = false;
        Mod example = ModFiles.Read(defaultModText, "default.mod.json")[0];
        if (example.Name != "" && Find(example.Name) is null) { Add(example); added = true; }
        if (Find(BlankModName) is null) { Add(new Mod { Name = BlankModName }); added = true; }
        return added;
    }

    /// <summary>
    /// Adds mods read from a mod file or a backup (see <see cref="ModFiles.Read"/>). When a mod of the same name exists,
    /// <paramref name="choose"/> is asked what to do, as the HTML app asks: replace it, keep both, or skip the new one.
    /// </summary>
    public ImportResult Import(IEnumerable<Mod> incoming, Func<Mod, Mod, ImportChoice> choose)
    {
        var result = new ImportResult();
        foreach (Mod mod in incoming)
        {
            Mod? existing = Find(mod.Name);
            if (existing is not null)
            {
                ImportChoice choice = choose(existing, mod);
                if (choice == ImportChoice.Skip) { result.Skipped += mod.Presets.Count; continue; }
                if (choice == ImportChoice.Replace) { Delete(existing); result.Replaced.Add(mod.Name); }
                else mod.Name = FreeName(mod.Name);
            }
            Add(mod);
            result.Added += mod.Presets.Count;
            result.LastMod = mod;
        }
        return result;
    }

    /// <summary>A mod file read, or why it could not be; nothing of the store is touched, so several can be read at once.</summary>
    private static (Mod? Mod, string? Problem) ReadMod(string path)
    {
        try { return (ModFiles.Read(File.ReadAllBytes(path), Path.GetFileName(path))[0], null); }
        catch (Exception error) when (error is ModFileException or IOException or UnauthorizedAccessException)
        {
            return (null, Path.GetFileName(path) + " (" + error.Message + ")");
        }
    }

    /// <summary>Takes a mod read from a file into the store, or lists the file among those that could not be read.</summary>
    private void Take(string path, (Mod? Mod, string? Problem) read)
    {
        if (read.Mod is not Mod mod)
        {
            Unreadable.Add(read.Problem!);
            return;
        }
        if (Find(mod.Name) is not null) mod.Name = FreeName(mod.Name); // two files with the same ModName inside
        Mods.Add(mod);
        pathOf[mod] = path;
    }

    /// <summary>
    /// Mods are listed by name the way the HTML app's <c>localeCompare</c> sorts them: by letters first, ignoring case,
    /// not by character codes.
    /// </summary>
    private static int ByName(Mod a, Mod b) => string.Compare(a.Name, b.Name, CultureInfo.InvariantCulture, CompareOptions.None);
}

/// <summary>What to do when an imported mod has the name of a mod that is already there.</summary>
public enum ImportChoice
{
    /// <summary>The imported mod takes the old one's place.</summary>
    Replace,

    /// <summary>Both are kept; the imported one gets a free name such as "WTO (2)".</summary>
    KeepBoth,

    /// <summary>The imported mod is not added.</summary>
    Skip,
}

/// <summary>What an import did, for the message the app shows afterwards.</summary>
public sealed class ImportResult
{
    /// <summary>How many presets were added.</summary>
    public int Added { get; set; }

    /// <summary>How many presets were skipped.</summary>
    public int Skipped { get; set; }

    /// <summary>The mods that replaced one of the same name. Files that used the old mod's presets switch to the new ones.</summary>
    public List<string> Replaced { get; } = [];

    /// <summary>The last mod added: the app opens it.</summary>
    public Mod? LastMod { get; set; }
}
