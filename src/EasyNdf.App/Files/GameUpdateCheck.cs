using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace EasyNdf.App.Files;

/// <summary>
/// Whether an export folder is a game mod folder left behind by a game update. A mod folder holds a copy of the game's
/// base files (<c>base.zip</c>) from when it was made or last updated; the game keeps its own, current one in
/// <c>Mods\ModData\base.zip</c>. While the two differ, the game refuses to generate the mod until
/// <c>UpdateMod.bat</c> has run in the folder, so an export into it would be written over files of an older game.
/// Only the export folder is looked at: a mod made of rules is never out of date, as its rules run on the files loaded.
/// </summary>
public static class GameUpdateCheck
{
    private const string BaseFile = "base.zip";

    /// <summary>What is said about a folder that is out of date.</summary>
    /// <param name="ModFolder">The mod's folder.</param>
    /// <param name="MadeFor">The game version it is on: its number and date, or what of them is known.</param>
    /// <param name="Latest">The game's current version, likewise.</param>
    /// <param name="UpdateScript">The folder's UpdateMod.bat; null when it has none.</param>
    public sealed record OutOfDate(string ModFolder, string MadeFor, string Latest, string? UpdateScript);

    /// <summary>
    /// The game mod folder an export folder is, or is inside (its GameData, or a folder under it): the nearest folder
    /// upward that has a base.zip, in a Mods folder that has ModData\base.zip. Null for any other folder.
    /// </summary>
    public static string? ModFolderOf(string exportFolder)
    {
        try
        {
            DirectoryInfo? at = new(Path.GetFullPath(exportFolder));
            for (int up = 0; up < 8 && at is not null; up++, at = at.Parent)
            {
                if (!File.Exists(Path.Combine(at.FullName, BaseFile)) || at.Parent is null) continue;
                return File.Exists(Path.Combine(at.Parent.FullName, "ModData", BaseFile)) ? at.FullName : null;
            }
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }

    /// <summary>The folder's state when it is a game mod folder whose base files are not the game's current ones; null otherwise.</summary>
    public static OutOfDate? Check(string exportFolder)
    {
        if (ModFolderOf(exportFolder) is not string modFolder) return null;
        string mods = Path.GetDirectoryName(modFolder)!;
        string own = Path.Combine(modFolder, BaseFile), current = Path.Combine(mods, "ModData", BaseFile);
        try
        {
            if (SameBytes(own, current)) return null;
            string script = Path.Combine(modFolder, "UpdateMod.bat");
            return new OutOfDate(modFolder, Version(ModGenVersion(modFolder, own), DateIn(own)), Version(LatestBuild(mods), DateIn(current)),
                File.Exists(script) ? script : null);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }

    /// <summary>Whether two files hold the same bytes: the game's own test of a mod folder.</summary>
    private static bool SameBytes(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        using FileStream a = File.OpenRead(first), b = File.OpenRead(second);
        byte[] one = new byte[1 << 20], other = new byte[1 << 20];
        while (true)
        {
            int read = a.ReadAtLeast(one, one.Length, throwOnEndOfStream: false);
            int readOther = b.ReadAtLeast(other, other.Length, throwOnEndOfStream: false);
            if (read != readOther || !one.AsSpan(0, read).SequenceEqual(other.AsSpan(0, readOther))) return false;
            if (read == 0) return true;
        }
    }

    /// <summary>"201602 (18 Aug 2026)", or the part of it that is known.</summary>
    private static string Version(string? build, DateTime? date)
    {
        string day = date?.ToString("d MMM yyyy", CultureInfo.CurrentCulture) ?? "";
        return build is null ? (day != "" ? day : "?") : day != "" ? build + " (" + day + ")" : build;
    }

    /// <summary>The date the base files in a base.zip were made: every file in it carries the same one.</summary>
    private static DateTime? DateIn(string zip)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zip);
            return archive.Entries.FirstOrDefault(entry => entry.Length > 0)?.LastWriteTime.DateTime;
        }
        catch (Exception problem) when (problem is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The game's current build: the newest numbered folder of its Data\PC.</summary>
    private static string? LatestBuild(string modsFolder)
    {
        string data = Path.Combine(Path.GetDirectoryName(modsFolder) ?? "", "Data", "PC");
        if (!Directory.Exists(data)) return null;
        return Directory.EnumerateDirectories(data).Select(Path.GetFileName).Where(name => name is not null && name.All(char.IsAsciiDigit) && name.Length > 0)
            .OrderBy(name => long.Parse(name!, CultureInfo.InvariantCulture)).LastOrDefault();
    }

    /// <summary>
    /// The build the mod folder was last generated on, which the game notes as "ModGenVersion" in the mod's Config.ini
    /// under Saved Games. Null when there is none, or when the folder's base files are newer than the note (the folder
    /// was updated since, and not generated yet).
    /// </summary>
    private static string? ModGenVersion(string modFolder, string ownBase)
    {
        string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "EugenSystems", "WARNO", "mod",
            Path.GetFileName(modFolder), "Config.ini");
        if (!File.Exists(config) || File.GetLastWriteTimeUtc(ownBase) > File.GetLastWriteTimeUtc(config)) return null;
        foreach (string line in File.ReadLines(config))
        {
            string[] parts = line.Split('=', 2);
            if (parts.Length != 2 || parts[0].Trim() != "ModGenVersion") continue;
            string value = parts[1].Split(';')[0].Trim();
            return value != "" && value.All(char.IsAsciiDigit) ? value : null;
        }
        return null;
    }
}
