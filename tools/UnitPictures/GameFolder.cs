using System.Text.RegularExpressions;

namespace UnitPictures;

/// <summary>Finds where the games are installed, when no folder is given.</summary>
internal static class GameFolder
{
    /// <summary>A game's folder (such as "WARNO" or "Steel Division 2") in Steam's default library, else in any library Steam lists.</summary>
    public static string? Find(string steamFolderName)
    {
        foreach (string library in SteamLibraries())
        {
            string candidate = Path.Combine(library, "steamapps", "common", steamFolderName);
            if (IsGame(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Whether a folder looks like one of the games: it has Data\PC and Mods\ModData\base.zip.</summary>
    public static bool IsGame(string folder) =>
        Directory.Exists(Path.Combine(folder, "Data", "PC")) && File.Exists(Path.Combine(folder, "Mods", "ModData", "base.zip"));

    /// <summary>Steam's default folder, then the libraries in its libraryfolders.vdf.</summary>
    private static IEnumerable<string> SteamLibraries()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string steam = Path.Combine(programFiles, "Steam");
        yield return steam;
        string list = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(list)) yield break;
        // lines such as:  "path"   "D:\\SteamLibrary"
        foreach (Match path in Regex.Matches(File.ReadAllText(list), @"""path""\s+""([^""]+)"""))
            yield return path.Groups[1].Value.Replace(@"\\", @"\");
    }
}
