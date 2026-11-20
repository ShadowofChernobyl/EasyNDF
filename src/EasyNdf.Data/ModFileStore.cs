using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace EasyNdf.Data;

/// <summary>
/// The files File rules carry into a mod. A rule names its file by a name made from its content and its extension
/// (such as "3f2a…9c.png"), so two rules with the same file share it and the mods themselves stay small. A file is
/// first kept in the app's data folder (<see cref="AppFolders.ModFiles"/>) under that name, then moved into the mod's
/// own folder (<see cref="ModFileViews"/>), where <see cref="Elsewhere"/> finds it. A mod file exported for sharing
/// carries the files' contents with it, and an import keeps them here.
/// </summary>
public sealed partial class ModFileStore(string folder)
{
    /// <summary>The folder a file is kept in until a mod's folder has it.</summary>
    public string Folder { get; } = folder;

    /// <summary>Where a file is when it is no longer in <see cref="Folder"/> (in a mod's folder); null when nowhere.</summary>
    public Func<string, string?>? Elsewhere { get; set; }

    /// <summary>
    /// A kept file's name: 32 letters and digits from its content, then its extension, if it has one. Nothing else is
    /// ever read or written, so a mod file cannot name any other file on the user's computer.
    /// </summary>
    [GeneratedRegex("^[0-9a-f]{32}(\\.[a-z0-9]{1,10})?\\z")]
    private static partial Regex StoredNamePattern();

    /// <summary>Whether a name is one this store gives its files.</summary>
    public static bool IsStoredName(string name) => StoredNamePattern().IsMatch(name);

    /// <summary>The name a file's content is kept under, with the extension of the file it was chosen as ("YSM.webm" gives ".webm").</summary>
    public static string NameFor(byte[] content, string chosenName)
    {
        string digest = Convert.ToHexStringLower(SHA256.HashData(content))[..32];
        string extension = Path.GetExtension(chosenName).ToLowerInvariant();
        return IsStoredName(digest + extension) ? digest + extension : digest;
    }

    /// <summary>Keeps a file's content, and returns the name it is kept under; a file kept before is left as it is.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <param name="chosenName">The name of the file it came from, for its extension.</param>
    public string Keep(byte[] content, string chosenName)
    {
        string name = NameFor(content, chosenName);
        string path = Path.Combine(Folder, name);
        if (PathOf(name) is not null) return name;
        Directory.CreateDirectory(Folder);
        string partial = path + ".partial";
        File.WriteAllBytes(partial, content);
        File.Move(partial, path, overwrite: true);
        return name;
    }

    /// <summary>
    /// Keeps the files a mod file carried, each under the name it gave: a file whose content does not give its name is
    /// left out, so a mod file cannot put one file in place of another. Returns how many were kept.
    /// </summary>
    public int KeepCarried(IReadOnlyDictionary<string, byte[]> carried)
    {
        int kept = 0;
        foreach ((string name, byte[] content) in carried)
        {
            if (!IsStoredName(name) || NameFor(content, name) != name) continue;
            Keep(content, name);
            kept++;
        }
        return kept;
    }

    /// <summary>Where a kept file is; null for a name this store does not give, or a file it no longer has.</summary>
    public string? PathOf(string name)
    {
        if (!IsStoredName(name)) return null;
        string path = Path.Combine(Folder, name);
        return File.Exists(path) ? path : Elsewhere?.Invoke(name);
    }

    /// <summary>A kept file's content; null when the store does not have it.</summary>
    public byte[]? Read(string name)
    {
        // a file can move into its mod's folder between finding and reading it: found again once
        for (int attempt = 0; ; attempt++)
        {
            if (PathOf(name) is not string path) return null;
            try { return File.ReadAllBytes(path); }
            catch (Exception problem) when (attempt == 0 && problem is FileNotFoundException or DirectoryNotFoundException) { }
        }
    }

    /// <summary>Copies a kept file to a place; false when the store does not have it.</summary>
    public bool CopyTo(string name, string target)
    {
        // a file can move into its mod's folder between finding and copying it: found again once
        for (int attempt = 0; ; attempt++)
        {
            if (PathOf(name) is not string path) return false;
            try
            {
                File.Copy(path, target, overwrite: true);
                return true;
            }
            catch (Exception problem) when (attempt == 0 && problem is FileNotFoundException or DirectoryNotFoundException) { }
        }
    }
}
