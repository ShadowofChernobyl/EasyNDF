using System.IO;
using EasyNdf.Data;

namespace EasyNdf.App.Mods;

/// <summary>
/// The pictures a mod's Armory changes name (emblems, units' card pictures), as mod files and packages carry them: read
/// for an export by their name from where the app keeps them, and kept again on import.
/// </summary>
public static class ModPictures
{
    /// <summary>
    /// A picture's bytes, by the path a mod names it by: found by its name where the app keeps pictures (Pictures, a
    /// mod's Armory Pictures), never anywhere else, so an export cannot carry another file of this computer. Null when
    /// it is not found or cannot be read.
    /// </summary>
    public static byte[]? Read(string path)
    {
        if (ModFolders.FindPicture(path) is not string found) return null;
        try { return File.ReadAllBytes(found); }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Keeps the pictures an imported mod carried in the app's Pictures folder, where the Armory finds them by name. A
    /// picture already kept with the same bytes is left as it is; one of the same name but different bytes is never
    /// replaced: the new one is kept under a free name, and the mod is pointed at it. True when the mod was changed.
    /// </summary>
    public static bool KeepCarried(Mod mod)
    {
        if (mod.CarriedPictures.Count == 0 || AppData.Current.Folders?.Pictures is not string folder) return false;
        Directory.CreateDirectory(folder);
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, byte[] bytes) in mod.CarriedPictures)
        {
            if (!ModFiles.IsPictureName(name)) continue;
            if (ModFolders.FindPicture(name) is string existing)
            {
                if (SameBytes(existing, bytes)) continue;
                string free = FreeName(folder, name);
                File.WriteAllBytes(Path.Combine(folder, free), bytes);
                moved[name] = Path.Combine(folder, free);
                continue;
            }
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
        }
        mod.CarriedPictures = new Dictionary<string, byte[]>();
        ModFiles.RepointPictures(mod, moved);
        return moved.Count > 0;
    }

    private static bool SameBytes(string path, byte[] bytes)
    {
        try { return new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes); }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>"Emblem (2).png", "Emblem (3).png"…: the first name no kept picture has.</summary>
    private static string FreeName(string folder, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
        for (int copy = 2; ; copy++)
        {
            string candidate = stem + " (" + copy + ")" + extension;
            if (ModFolders.FindPicture(candidate) is null && !File.Exists(Path.Combine(folder, candidate))) return candidate;
        }
    }
}
