using EasyNdf.Data.Game;

namespace EasyNdf.Data.Models;

/// <summary>The pictures one material is painted with; null where the game has none.</summary>
/// <param name="Colour">Its colours.</param>
/// <param name="Normal">Its normal map: red and green give which way each pixel's surface leans.</param>
/// <param name="Surface">
/// Its surface map: red is how much light reaches into creases (ambient occlusion), green how rough it is, blue how
/// metallic.
/// </param>
public sealed record MaterialPictures(Picture? Colour, Picture? Normal, Picture? Surface);

/// <summary>
/// Finds a model's pictures: the atlas in its folder (PC/Atlas/&lt;model's folder&gt;/TextureSmall.atlas) places each texture
/// its materials name inside bigger textures (.tgv), with the material's colours in a "daComb" texture, its normal map in
/// an "NM" one and its surface map in one named after the colour texture with "_O$_$".
/// </summary>
public sealed class ModelTextures
{
    private readonly GameArchives archives;
    private readonly List<AtlasPart> atlas;
    private readonly Dictionary<string, Texture> opened = new(StringComparer.OrdinalIgnoreCase);

    private ModelTextures(GameArchives archives, List<AtlasPart> atlas)
    {
        this.archives = archives;
        this.atlas = atlas;
    }

    /// <summary>The textures for a model: its folder's atlas; none when the folder has no atlas.</summary>
    public static ModelTextures For(GameArchives archives, ModelMesh mesh)
    {
        string folder = mesh.Path[..Math.Max(0, mesh.Path.LastIndexOf('/'))];
        ArchiveFile? atlasFile = archives.Find("PC/Atlas/" + folder + "/TextureSmall.atlas");
        return new ModelTextures(archives, atlasFile is null ? [] : Textures.ReadAtlas(atlasFile.Read()));
    }

    /// <summary>
    /// Which archive paths these textures need (the atlas and the .tgv files it points into), so the viewer's list of
    /// archive files can be kept to what models use.
    /// </summary>
    public static bool IsModelTextureFile(string path) =>
        path.StartsWith("PC/Atlas/Assets/3D/", StringComparison.OrdinalIgnoreCase);

    /// <summary>A material's pictures, each cut out of its texture; each null when the atlas does not place it.</summary>
    public MaterialPictures PicturesOf(ModelMaterial material)
    {
        string stem = material.Colour is null ? "" : Path.GetFileNameWithoutExtension(material.Colour);
        AtlasPart? colour = Place(material.Colour, "daComb");
        AtlasPart? normal = Place(material.Normal, "NM");
        AtlasPart? surface = stem.Length == 0 ? null : atlas.FirstOrDefault(part => part.Source.Contains("/" + stem + "_O$_$", StringComparison.OrdinalIgnoreCase));
        return new MaterialPictures(Cut(colour), Cut(normal), Cut(surface));
    }

    /// <summary>
    /// Where the atlas places a texture the material names: the part of that name, or else the part of that name's
    /// stem in the texture of its kind ("daComb", "NM").
    /// </summary>
    private AtlasPart? Place(string? name, string kind)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string file = Path.GetFileName(name.Replace('\\', '/'));
        string stem = Path.GetFileNameWithoutExtension(file);
        return atlas.FirstOrDefault(part => part.Source.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase))
            ?? atlas.FirstOrDefault(part => part.Source.Contains("/" + stem, StringComparison.OrdinalIgnoreCase) && part.Source.Contains(kind, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A part cut out of its texture; null when there is no part, or its texture cannot be read.</summary>
    private Picture? Cut(AtlasPart? part)
    {
        if (part is null) return null;
        try
        {
            if (!opened.TryGetValue(part.Texture, out Texture? texture))
            {
                if (archives.Find(part.Texture) is not ArchiveFile file) return null;
                opened[part.Texture] = texture = Textures.ReadTexture(file.Read());
            }
            int width = Math.Min(part.Width, texture.Width - part.X), height = Math.Min(part.Height, texture.Height - part.Y);
            return width <= 0 || height <= 0 ? null : texture.Crop(part.X, part.Y, width, height);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
