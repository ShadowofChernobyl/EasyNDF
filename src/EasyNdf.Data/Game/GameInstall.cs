using System.Text.RegularExpressions;

namespace EasyNdf.Data.Game;

/// <summary>A mod in a game's Mods folder: a folder with its own GameData, as the game's CreateNewMod makes.</summary>
/// <param name="Name">Its name, from its Config.ini, or else its folder's name.</param>
/// <param name="Folder">Its folder.</param>
public sealed record GameMod(string Name, string Folder);

/// <summary>Where a game is installed, and the mods in its Mods folder.</summary>
public static class GameInstall
{
    /// <summary>
    /// Whether a folder is one of the games: it has Data\PC (the game's archives) and Mods\ModData\base.zip (the clean
    /// GameData the game's mod tools start from).
    /// </summary>
    public static bool IsGameFolder(string folder) =>
        Directory.Exists(Path.Combine(folder, "Data", "PC")) && File.Exists(BaseZipOf(folder));

    /// <summary>The game's clean GameData: Mods\ModData\base.zip.</summary>
    public static string BaseZipOf(string gameFolder) => Path.Combine(gameFolder, "Mods", "ModData", "base.zip");

    /// <summary>
    /// A game's folder: under steamapps\common in Steam's own folder, else in any library Steam lists. Null when it is
    /// not found. <paramref name="steamFolders"/> replaces where Steam is looked for (tests pass their own).
    /// </summary>
    public static string? Find(GameTitle game, IEnumerable<string>? steamFolders = null)
    {
        foreach (string library in SteamLibraries(steamFolders ?? DefaultSteamFolders()))
        {
            string candidate = Path.Combine(library, "steamapps", "common", game.SteamFolderName);
            if (IsGameFolder(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// The mods in a game's Mods folder, by name: every folder with a GameData folder in it. ModData, the game's own,
    /// is not a mod. Mods from the Steam Workshop are elsewhere and are not listed: EasyNDF is for mods being made.
    /// </summary>
    public static List<GameMod> ModsOf(string gameFolder)
    {
        string mods = Path.Combine(gameFolder, "Mods");
        if (!Directory.Exists(mods)) return [];
        var found = new List<GameMod>();
        foreach (string folder in Directory.EnumerateDirectories(mods))
        {
            if (string.Equals(Path.GetFileName(folder), "ModData", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Directory.Exists(Path.Combine(folder, "GameData"))) continue;
            found.Add(new GameMod(NameOf(folder), folder));
        }
        return found.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A mod's name: the Name in its Config.ini when it has one, else its folder's name.</summary>
    private static string NameOf(string modFolder)
    {
        string config = Path.Combine(modFolder, "Config.ini");
        if (File.Exists(config))
        {
            try
            {
                foreach (string line in File.ReadLines(config))
                {
                    // lines such as "Name = My mod ; a comment"
                    Match name = Regex.Match(line, @"^\s*Name\s*=\s*([^;]*)");
                    if (name.Success && name.Groups[1].Value.Trim() != "") return name.Groups[1].Value.Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return Path.GetFileName(modFolder);
    }

    /// <summary>
    /// Where Steam is installed: where the Windows registry says, which Steam always fills in, so a Steam installed on
    /// another drive is found too; then its usual folders, Program Files (x86) and Program Files.
    /// </summary>
    private static IEnumerable<string> DefaultSteamFolders()
    {
        if (SteamFolderFromRegistry() is string registered) yield return registered;
        foreach (Environment.SpecialFolder programFiles in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string folder = Environment.GetFolderPath(programFiles);
            if (folder != "") yield return Path.Combine(folder, "Steam");
        }
    }

    /// <summary>
    /// The SteamPath value in HKEY_CURRENT_USER\Software\Valve\Steam, written with forward slashes
    /// ("d:/steam"); null when it is missing, unreadable, or not on Windows.
    /// </summary>
    private static string? SteamFolderFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using Microsoft.Win32.RegistryKey? steam = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (steam?.GetValue("SteamPath") is not string path || path.Trim() == "") return null;
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception problem) when (problem is System.Security.SecurityException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Each Steam folder, then the libraries its libraryfolders.vdf lists.</summary>
    private static IEnumerable<string> SteamLibraries(IEnumerable<string> steamFolders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string steam in steamFolders)
        {
            if (seen.Add(steam)) yield return steam;
            string list = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            string text;
            try { text = File.Exists(list) ? File.ReadAllText(list) : ""; }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            // lines such as:  "path"   "D:\\SteamLibrary"
            foreach (Match path in Regex.Matches(text, @"""path""\s+""([^""]+)"""))
            {
                string library = path.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal);
                if (seen.Add(library)) yield return library;
            }
        }
    }
}
