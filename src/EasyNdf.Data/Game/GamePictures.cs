namespace EasyNdf.Data.Game;

/// <summary>
/// The game's 2D pictures, by the path its NDF files give them: "GameData:/Assets/2D/....png". A picture is either a
/// texture (.tgv) of its own in the archives, or a part of a bigger texture that an atlas (.atlas) places it in.
/// </summary>
public sealed class GamePictures(GameArchives archives)
{
    // where each small picture is inside the interface's atlases; read the first time a picture is not a texture of its own
    private Dictionary<string, AtlasPart>? atlasParts;
    // the last few atlas textures read, kept as stored (a quarter of their decoded size or less): pictures next to
    // each other share them, and only the part a picture needs is decoded
    private readonly Dictionary<string, Texture> recentTextures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The atlases that could not be read, each with what went wrong.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>
    /// The picture at a "GameData:/Assets/2D/....png" path, or null when the archives do not have it. Throws
    /// <see cref="InvalidDataException"/> when its texture is in a form that cannot be read.
    /// </summary>
    public Picture? Read(string source)
    {
        if (archives.Find(OwnTexturePath(source)) is ArchiveFile own) return Textures.ReadTgv(own.Read());
        atlasParts ??= ReadAtlases();
        if (!atlasParts.TryGetValue(Normalised(source), out AtlasPart? part)) return null;
        Texture? texture = TextureOf(part);
        return texture?.Crop(part.X, part.Y, part.Width, part.Height);
    }

    /// <summary>
    /// Where a picture's own texture would be: "GameData:/Assets/2D/X.png" is "PC/Texture/Assets/2D/X.tgv".
    /// </summary>
    public static string OwnTexturePath(string source) =>
        Path.ChangeExtension("PC/Texture/" + Normalised(source)["GameData:/".Length..], ".tgv");

    /// <summary>A picture's path with forward slashes and a "GameData:/" start, as the atlases write it.</summary>
    public static string Normalised(string source)
    {
        string path = source.Replace('\\', '/');
        if (path.StartsWith("GameData:", StringComparison.OrdinalIgnoreCase)) path = path["GameData:".Length..];
        return "GameData:/" + path.TrimStart('/');
    }

    /// <summary>An atlas's texture, kept with the last few read.</summary>
    private Texture? TextureOf(AtlasPart part)
    {
        if (recentTextures.TryGetValue(part.Texture, out Texture? kept)) return kept;
        if (archives.Find(part.Texture) is not ArchiveFile file) return null;
        if (recentTextures.Count >= 4) recentTextures.Clear();
        Texture texture = Textures.ReadTexture(file.Read());
        recentTextures[part.Texture] = texture;
        return texture;
    }

    /// <summary>Where every small picture is, from the interface's atlases (those of 3D models and effects hold none of them).</summary>
    private Dictionary<string, AtlasPart> ReadAtlases()
    {
        var parts = new Dictionary<string, AtlasPart>(StringComparer.OrdinalIgnoreCase);
        foreach (ArchiveFile atlas in archives.Files.Values.Where(IsInterfaceAtlas))
        {
            try
            {
                foreach (AtlasPart part in Textures.ReadAtlas(atlas.Read())) parts.TryAdd(Normalised(part.Source), part);
            }
            catch (Exception problem) when (problem is InvalidDataException or IOException or UnauthorizedAccessException
                or KeyNotFoundException or ArgumentException or InvalidCastException or IndexOutOfRangeException or EndOfStreamException or ZstdSharp.ZstdException)
            {
                Problems.Add(atlas.Name + ": " + problem.Message);
            }
        }
        return parts;
    }

    private static bool IsInterfaceAtlas(ArchiveFile file) =>
        file.Name.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase)
        && !file.Name.Contains("/Assets/3D/", StringComparison.OrdinalIgnoreCase)
        && !file.Name.Contains("/FX-", StringComparison.OrdinalIgnoreCase);
}
