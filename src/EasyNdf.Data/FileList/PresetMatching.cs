namespace EasyNdf.Data.FileList;

/// <summary>
/// Which preset a file gets when it is added (the HTML app's <c>matchPreset</c>): the open mod's preset whose name holds the
/// file's name, such as "WTO (Ammunition.ndf)" for Ammunition.ndf. When two presets hold the name, the one that also names
/// more of the file's folders wins, such as "WTO (Strategic/GDConstants.ndf)" for Strategic/GDConstants.ndf. A tie gives none.
/// </summary>
public static class PresetMatching
{
    /// <summary>The preset for a file, among the open mod's presets, or null.</summary>
    /// <param name="fileName">The file's name, such as "GDConstants.ndf". Capitals must match.</param>
    /// <param name="folder">The folders the file sat in, such as "Gameplay/Constantes/Strategic"; "" for none.</param>
    /// <param name="presets">The open mod's presets.</param>
    public static ModPreset? Match(string fileName, string folder, IEnumerable<ModPreset> presets)
    {
        string[] folders = folder.ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
        int best = -1;
        var bestPresets = new List<ModPreset>();
        foreach (ModPreset preset in presets)
        {
            int fit = Fit(preset.Name, fileName, folders);
            if (fit < 0) continue;
            if (fit > best) { best = fit; bestPresets = [preset]; }
            else if (fit == best) bestPresets.Add(preset);
        }
        return bestPresets.Count == 1 ? bestPresets[0] : null;
    }

    /// <summary>
    /// How well a preset name fits a file (the HTML app's <c>presetFit</c>): -1 when it does not hold the file's name as a
    /// whole name; otherwise how many of the file's folders, nearest first, are written before the name with "/".
    /// A folder written that the file is not in means no fit.
    /// </summary>
    public static int Fit(string presetName, string fileName, string[] folders)
    {
        string raw = presetName.Replace('\\', '/');
        int at = NameAt(raw, fileName);
        if (at < 0) return -1;
        string lower = raw.ToLowerInvariant();

        int matched = 0, end = at;
        while (end > 0 && lower[end - 1] == '/')
        {
            if (folders.Length == 0) break; // a file added on its own has no folder to check against
            int index = folders.Length - 1 - matched;
            if (index < 0) return -1;
            string wanted = folders[index];
            int start = end - 1 - wanted.Length;
            if (start < 0 || lower[start..(end - 1)] != wanted || (start > 0 && IsNameCharacter(lower[start - 1]))) return -1;
            matched++;
            end = start;
        }
        return matched;
    }

    /// <summary>
    /// Where the last whole copy of the file's name starts in a preset name: one without a letter, digit, "_", "." or "-"
    /// just before or after it. -1 when there is none.
    /// </summary>
    private static int NameAt(string raw, string fileName)
    {
        if (fileName == "") return -1;
        for (int at = raw.Length - fileName.Length; at >= 0; at--)
        {
            if (string.CompareOrdinal(raw, at, fileName, 0, fileName.Length) != 0) continue;
            int after = at + fileName.Length;
            bool clearBefore = at == 0 || !IsNameCharacter(raw[at - 1]);
            bool clearAfter = after >= raw.Length || !IsNameCharacter(raw[after]);
            if (clearBefore && clearAfter) return at;
        }
        return -1;
    }

    /// <summary>The characters a file or folder name is made of, as the HTML app's <c>[\w.-]</c>.</summary>
    private static bool IsNameCharacter(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-';
}
