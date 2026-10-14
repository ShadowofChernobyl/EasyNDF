using System.Text;

namespace EasyNdf.Data;

/// <summary>Turning names (of mods, themes, filters) into file names Windows accepts.</summary>
public static class FileNames
{
    /// <summary>Names Windows keeps for devices; a file cannot be called this, whatever its extension.</summary>
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// A file name for a name: characters Windows does not allow become "_", and so do spaces and dots at the end.
    /// The real name is always kept inside the file, so this only has to be safe and readable.
    /// </summary>
    public static string ForName(string name)
    {
        var safe = new StringBuilder();
        foreach (char ch in name)
            safe.Append(ch < ' ' || "<>:\"/\\|?*".Contains(ch) ? '_' : ch);
        string result = safe.ToString().TrimEnd(' ', '.');
        if (result.Length > 100) result = result[..100];
        if (result == "" || DeviceNames.Contains(result)) result = "_" + result;
        return result;
    }

    /// <summary>
    /// A file path in <paramref name="folder"/> for <paramref name="name"/> that no other name uses: "WTO.mod.json", or
    /// "WTO (2).mod.json" when two names give the same file name (such as "A?" and "A*").
    /// </summary>
    public static string FreePath(string folder, string name, string extension, ICollection<string> pathsInUse)
    {
        string baseName = ForName(name);
        string path = Path.Combine(folder, baseName + extension);
        for (int i = 2; pathsInUse.Contains(path, StringComparer.OrdinalIgnoreCase); i++)
            path = Path.Combine(folder, baseName + " (" + i + ")" + extension);
        return path;
    }
}
