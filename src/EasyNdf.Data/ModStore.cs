using System.Globalization;
using System.Text.RegularExpressions;
using EasyNdf.Data.Languages;

namespace EasyNdf.Data;

/// <summary>
/// The mods, kept in the Mods folder: one file per mod, in the same format as "Export mod", so a mod file can also be
/// copied in or out of the folder by hand.
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
        foreach (string path in Directory.GetFiles(folder, "*.mod.json").Order(StringComparer.OrdinalIgnoreCase))
            ReadFile(path);
        Mods.Sort(ByName);
    }

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
        List<string> taken = [.. pathOf.Where(p => p.Key != mod).Select(p => p.Value)];
        pathOf.TryGetValue(mod, out string? own);
        try
        {
            taken.AddRange(Directory.GetFiles(folder!, "*.mod.json").Where(file => !string.Equals(file, own, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // the folder cannot be listed: the mods the app has are all it can go by
        }
        return taken;
    }

    /// <summary>Saves a mod after a change. A renamed mod moves to a file named after its new name.</summary>
    public void Save(Mod mod)
    {
        if (folder is null) { Mods.Sort(ByName); return; }
        pathOf.TryGetValue(mod, out string? oldPath);
        string path = oldPath is not null && FileFitsName(oldPath, mod.Name)
            ? oldPath
            : FileNames.FreePath(folder, mod.Name, ".mod.json", PathsTakenBesides(mod));

        JsonFiles.Write(path, JsonFiles.Parse(ModFiles.StoredText(mod)));
        if (oldPath is not null && oldPath != path) File.Delete(oldPath);
        pathOf[mod] = path;
        Mods.Sort(ByName);
    }

    /// <summary>Whether a mod's file is still named after the mod: "WTO.mod.json", or "WTO (2).mod.json" for a second "WTO?".</summary>
    private static bool FileFitsName(string path, string name)
    {
        string file = Path.GetFileName(path), baseName = FileNames.ForName(name);
        if (string.Equals(file, baseName + ".mod.json", StringComparison.OrdinalIgnoreCase)) return true;
        return file.StartsWith(baseName + " (", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(file[baseName.Length..], @"\A \([0-9]+\)\.mod\.json\z", RegexOptions.IgnoreCase);
    }

    /// <summary>Deletes a mod and its file.</summary>
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
            if (!mods.Any(m => m.Name == old.Name)) Delete(old);
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
            if (old is null || ModFiles.StoredText(old) != ModFiles.StoredText(mod)) Save(mod);
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

    private void ReadFile(string path)
    {
        try
        {
            Mod mod = ModFiles.Read(File.ReadAllText(path), Path.GetFileName(path))[0];
            if (Find(mod.Name) is not null) mod.Name = FreeName(mod.Name); // two files with the same ModName inside
            Mods.Add(mod);
            pathOf[mod] = path;
        }
        catch (Exception error) when (error is ModFileException or IOException or UnauthorizedAccessException)
        {
            Unreadable.Add(Path.GetFileName(path) + " (" + error.Message + ")");
        }
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
