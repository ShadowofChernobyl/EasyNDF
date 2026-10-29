using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace UnitPictures;

/// <summary>A unit, as the game's UniteDescriptor.ndf describes it: what the extract needs of it.</summary>
/// <param name="Descriptor">The descriptor's name, such as "Descriptor_Unit_M1A1_Abrams_US".</param>
/// <param name="NameToken">The key of its name in the game's dictionary, such as "HMNGALJGAU".</param>
/// <param name="PictureKey">Its ButtonTexture, such as "Texture_Button_Unit_M1A1_Abrams_US".</param>
internal sealed record Unit(string Descriptor, string NameToken, string PictureKey);

/// <summary>
/// The game's own NDF text, from WARNO\Mods\ModData\base.zip: the clean copy of GameData that the game's mod tools
/// start from. Read here with simple patterns, as only a few fields are needed.
/// </summary>
internal static class GameData
{
    /// <summary>Every unit in UniteDescriptor.ndf.</summary>
    public static List<Unit> Units(ZipArchive baseZip)
    {
        string text = Read(baseZip, "Generated/Gameplay/Gfx/UniteDescriptor.ndf");
        var units = new List<Unit>();
        foreach (Match unit in Regex.Matches(text, @"(?m)^export (Descriptor_Unit_[A-Za-z0-9_]+) is TEntityDescriptor\n\((.*?)\n\)", RegexOptions.Singleline))
        {
            string body = unit.Groups[2].Value;
            string token = Regex.Match(body, @"NameToken\s*=\s*'([A-Za-z0-9]+)'").Groups[1].Value;
            string picture = Regex.Match(body, @"ButtonTexture\s*=\s*'([A-Za-z0-9_]+)'").Groups[1].Value;
            units.Add(new Unit(unit.Groups[1].Value, token, picture));
        }
        return units;
    }

    /// <summary>
    /// Each picture key's file, from ButtonTexturesUnites.ndf: "Texture_Button_Unit_M1A1_Abrams_US" to
    /// "GameData:/Assets/2D/Interface/Common/UnitsIcons/NATO/M1A1_Abrams_US.png". The two games write it differently.
    /// </summary>
    public static Dictionary<string, string> PictureFiles(ZipArchive baseZip)
    {
        string text = Read(baseZip, "/ButtonTexturesUnites.ndf");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        // WARNO: ("Texture_Button_Unit_X", MAP [( ~/ComponentState/Normal, TUIResourceTexture( FileName = "GameData:/....png"))])
        foreach (Match entry in Regex.Matches(text, @"\(\s*""([A-Za-z0-9_]+)""\s*,\s*MAP\s*\[\s*\(\s*~/ComponentState/Normal\s*,\s*TUIResourceTexture\s*\(\s*FileName\s*=\s*""([^""]+)"""))
            files[entry.Groups[1].Value] = entry.Groups[2].Value;
        // Steel Division 2: export Texture_Button_Unit_X is TUIResourceTextureWithUsefulnessMask ( ... FileName = 'Assets\...\X.png' )
        foreach (Match texture in Regex.Matches(text, @"(?m)^export (Texture_Button_Unit_[A-Za-z0-9_]+) is [A-Za-z0-9_]+\n\((.*?)\n\)", RegexOptions.Singleline))
        {
            Match file = Regex.Match(texture.Groups[2].Value, @"FileName\s*=\s*['""]([^'""]+)['""]");
            if (!file.Success) continue;
            string path = file.Groups[1].Value.Replace('\\', '/');
            files[texture.Groups[1].Value] = path.StartsWith("GameData:/", StringComparison.OrdinalIgnoreCase) ? path : "GameData:/" + path;
        }
        return files;
    }

    /// <summary>
    /// Each nation's flag, by the nation's code as units use it ("US", "SOV", "CZ"): the "..._FLAG.png" pictures of
    /// the interface's Flags folder. A nation with no such flag (Poland in Steel Division 2) gets the first picture of
    /// its animated division flag instead.
    /// </summary>
    public static Dictionary<string, string> FlagFiles(ZipArchive baseZip)
    {
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var divisionFlags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in baseZip.Entries.Where(IsTextureList))
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            string text = reader.ReadToEnd();
            foreach (Match flag in Regex.Matches(text, @"FileName\s*=\s*[""']([^""']*[\\/]Flags[\\/](?:Normal[\\/])?([A-Za-z]+)_FLAG\.png)[""']", RegexOptions.IgnoreCase))
                flags.TryAdd(flag.Groups[2].Value.ToUpperInvariant(), AsSource(flag.Groups[1].Value));
            foreach (Match flag in Regex.Matches(text, @"FileName\s*=\s*[""']([^""']*[\\/]Division[\\/]Flags[\\/]([A-Za-z]+)[\\/][A-Za-z]+_flag_00000\.png)[""']", RegexOptions.IgnoreCase))
                divisionFlags.TryAdd(flag.Groups[2].Value.ToUpperInvariant(), AsSource(flag.Groups[1].Value));
        }
        foreach (var (code, path) in divisionFlags) flags.TryAdd(code, path);
        return flags;
    }

    /// <summary>The NDF files that list the interface's pictures: under UserInterface, or the UI's Resources/Textures.</summary>
    private static bool IsTextureList(ZipArchiveEntry entry)
    {
        string path = entry.FullName.Replace('\\', '/');
        if (!path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)) return false;
        return path.Contains("/UserInterface/", StringComparison.OrdinalIgnoreCase) || path.Contains("/UI/Resources/Textures/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A picture's path as the archives' atlases name it: "GameData:/Assets/2D/....png", with forward slashes.</summary>
    private static string AsSource(string path)
    {
        path = path.Replace('\\', '/');
        if (path.StartsWith("GameData:", StringComparison.OrdinalIgnoreCase)) path = path["GameData:".Length..].TrimStart('/');
        return "GameData:/" + path;
    }

    /// <summary>A file from base.zip, found by the end of its path, wherever the zip's folders start.</summary>
    private static string Read(ZipArchive baseZip, string endOfPath)
    {
        ZipArchiveEntry entry = baseZip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith(endOfPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("base.zip has no " + endOfPath);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        // the patterns above expect plain line ends
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}

/// <summary>
/// The game's dictionaries (.dic files in the archives, "TRAD" inside): the text shown in the game, by a 64-bit key.
/// </summary>
internal static class Dictionaries
{
    /// <summary>A dictionary's entries: each key, then where its UTF-16 text is and how many characters long.</summary>
    public static Dictionary<ulong, string> Read(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual("TRAD"u8)) throw new InvalidDataException("not a TRAD dictionary");
        int count = BitConverter.ToInt32(data, 4);
        var entries = new Dictionary<ulong, string>();
        for (int i = 0; i < count; i++)
        {
            int at = 8 + i * 16;
            ulong key = BitConverter.ToUInt64(data, at);
            int textAt = BitConverter.ToInt32(data, at + 8);
            int length = BitConverter.ToInt32(data, at + 12);
            entries[key] = Encoding.Unicode.GetString(data, textAt, length * 2);
        }
        return entries;
    }

    /// <summary>
    /// The key of a name token such as "HMNGALJGAU": each character in 6 bits, digits 1 to 10 and letters 11 to 36,
    /// the first character highest. Null for a token that cannot be a key.
    /// </summary>
    public static ulong? KeyOf(string token)
    {
        if (token.Length == 0 || token.Length > 10) return null;
        ulong key = 0;
        foreach (char c in token.ToUpperInvariant())
        {
            int value = c is >= 'A' and <= 'Z' ? c - 'A' + 11 : c is >= '0' and <= '9' ? c - '0' + 1 : -1;
            if (value < 0) return null;
            key = (key << 6) | (uint)value;
        }
        return key;
    }
}
