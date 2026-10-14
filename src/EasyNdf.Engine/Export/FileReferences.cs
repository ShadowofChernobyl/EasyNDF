namespace EasyNdf.Engine.Export;

/// <summary>
/// How loaded files are named by rules and lists: by a key of their folder and name, in small letters, such as
/// "gameplay/constantes/strategic/gdconstants.ndf".
/// </summary>
public static class FileReferences
{
    /// <summary>A file's key: its folder (if it has one) and its name, in small letters.</summary>
    public static string Key(string? folder, string name) =>
        ((string.IsNullOrEmpty(folder) ? "" : folder + "/") + name).ToLowerInvariant();

    /// <summary>
    /// The file a rule's reference points to, such as a Lookup rule's "Strategic/GDConstants.ndf": the file whose key is
    /// the reference, otherwise the first whose key ends with "/" and the reference. Capitals do not matter. Null when none fits.
    /// </summary>
    public static string? Find(string? reference, IReadOnlyList<string> keys)
    {
        string r = (reference ?? "").Trim().ToLowerInvariant();
        if (r == "") return null;
        return keys.FirstOrDefault(k => k == r) ?? keys.FirstOrDefault(k => k.EndsWith("/" + r, StringComparison.Ordinal));
    }

    /// <summary>
    /// The shortest end of a path that no other file's path ends with: just the name when it is unique, otherwise with
    /// as many of its folders as needed to tell it apart. This is how a file is named in dropdowns and in rules.
    /// </summary>
    public static string ShortReference(string path, IReadOnlyList<string> allPaths)
    {
        string[] parts = path.Split('/');
        for (int n = 1; n <= parts.Length; n++)
        {
            string end = string.Join("/", parts[^n..]);
            string lower = end.ToLowerInvariant(), withSlash = "/" + lower;
            bool shared = allPaths.Any(o => o != path && (o.ToLowerInvariant() == lower || o.ToLowerInvariant().EndsWith(withSlash, StringComparison.Ordinal)));
            if (!shared) return end;
        }
        return path;
    }
}
