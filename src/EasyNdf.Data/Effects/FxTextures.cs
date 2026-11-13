using System.Text.RegularExpressions;
using EasyNdf.Data.Game;

namespace EasyNdf.Data.Effects;

/// <summary>The pictures of an effect's texture: one, or every frame of its animation, and which one the script names.</summary>
/// <param name="Frames">The frames, in order; all the same size.</param>
/// <param name="Named">Which frame the script names, shown when the particle does not animate.</param>
public sealed record FxTextureSet(IReadOnlyList<Texture> Frames, int Named);

/// <summary>
/// The effects' textures in the game's archives (PC/Texture/Assets/2D/Fx/*.tgv). A script names one file
/// ('GameData:/Assets/2D/Fx/anim_explosion_03_002.png'); an animated particle plays every file of the same name with
/// another number (anim_explosion_03_001 to _037).
/// </summary>
/// <param name="archives">The game's archives.</param>
public sealed partial class FxTextures(GameArchives archives)
{
    private List<string>? effectFiles;

    /// <summary>Whether an archive path is an effect's texture, so the viewer's list of archive files can keep them.</summary>
    public static bool IsEffectTextureFile(string path) => path.StartsWith("PC/Texture/Assets/2D/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".tgv", StringComparison.OrdinalIgnoreCase);

    /// <summary>A texture's frames: all of its animation, or only the one named; null when the archives do not have it.</summary>
    /// <param name="textureName">The name the script gives.</param>
    /// <param name="animated">Whether to read every frame of its animation.</param>
    public FxTextureSet? Read(string textureName, bool animated)
    {
        string path = ArchivePathOf(textureName);
        if (archives.Find(path) is not ArchiveFile named) return null;
        Match numbered = Numbered().Match(path);
        if (!animated || !numbered.Success) return new FxTextureSet([Textures.ReadTexture(named.Read())], 0);
        string start = numbered.Groups[1].Value;
        int digits = numbered.Groups[2].Length;
        effectFiles ??= [.. archives.Files.Keys.Where(IsEffectTextureFile).Order(StringComparer.OrdinalIgnoreCase)];
        List<string> frames = [.. effectFiles.Where(file => file.Length == start.Length + digits + 4 && file.StartsWith(start, StringComparison.OrdinalIgnoreCase) && file[start.Length..^4].All(char.IsAsciiDigit))];
        Texture first = Textures.ReadTexture(named.Read());
        var read = new List<Texture>(frames.Count);
        int namedAt = 0;
        foreach (string frame in frames)
        {
            if (string.Equals(frame, path, StringComparison.OrdinalIgnoreCase)) namedAt = read.Count;
            Texture texture = string.Equals(frame, path, StringComparison.OrdinalIgnoreCase) ? first : Textures.ReadTexture(archives.Find(frame)!.Read());
            // frames of another size or kind cannot share the animation's texture
            if (texture.Width == first.Width && texture.Height == first.Height && texture.Format == first.Format) read.Add(texture);
        }
        return read.Count == 0 ? new FxTextureSet([first], 0) : new FxTextureSet(read, namedAt);
    }

    /// <summary>Where the archives keep a texture a script names: 'GameData:/Assets/2D/Fx/spot.png' is PC/Texture/Assets/2D/Fx/spot.tgv.</summary>
    public static string ArchivePathOf(string textureName)
    {
        string path = textureName.Replace('\\', '/');
        int colon = path.IndexOf(":/", StringComparison.Ordinal);
        if (colon >= 0) path = path[(colon + 2)..];
        int dot = path.LastIndexOf('.');
        if (dot > path.LastIndexOf('/')) path = path[..dot];
        return "PC/Texture/" + path + ".tgv";
    }

    [GeneratedRegex(@"^(.*?)(\d+)\.tgv$", RegexOptions.IgnoreCase)]
    private static partial Regex Numbered();
}
