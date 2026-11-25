using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasyNdf.Data.Game;
using EasyNdf.Data.Models;
using EasyNdf.Data.Models.Fbx;

namespace EasyNdf.App.Models;

/// <summary>
/// A material of a picked .fbx file and its pictures beside it: its colour picture (the one its texture names, else
/// one named after the material), and the maps named after that picture: _NM (normal), _R (roughness), _M (metal),
/// _O (occlusion), as the game's own models and its modding examples have them; _A (see-through) when there is one.
/// </summary>
/// <param name="Material">The material's name.</param>
/// <param name="Maps">Each map's file, by its suffix ("" for the colour picture), null where it is missing.</param>
/// <param name="Alpha">Its see-through map, when there is one.</param>
internal sealed record MaterialMaps(string Material, IReadOnlyList<(string Suffix, string? File)> Maps, string? Alpha)
{
    /// <summary>The maps every material has, by suffix: colour, normal, roughness, metal, occlusion.</summary>
    public static readonly IReadOnlyList<string> Suffixes = ["", "_NM", "_R", "_M", "_O"];

    /// <summary>The maps missing.</summary>
    public IEnumerable<string> Missing => Maps.Where(map => map.File is null).Select(map => map.Suffix);

    /// <summary>The files found, to carry into the mod.</summary>
    public IEnumerable<string> Found => Maps.Select(map => map.File).Append(Alpha).OfType<string>();
}

/// <summary>
/// An .fbx file picked for a unit, read: its bytes (kept, as the copy the mod carries is made from them), its model,
/// its materials' pictures, and its model and pictures as the viewer draws them.
/// </summary>
internal sealed class PickedModel
{
    /// <summary>The longest side a picture is drawn at; the game's are 2048 pixels.</summary>
    private const int LargestSide = 1024;

    public required string File { get; init; }
    public required byte[] Bytes { get; init; }
    public required FbxScene Scene { get; init; }
    public required ModelMesh Mesh { get; init; }
    public required List<MaterialMaps> Materials { get; init; }

    /// <summary>Each material's pictures as the viewer draws them, by its name.</summary>
    public required Dictionary<string, MaterialPictures> Pictures { get; init; }

    /// <summary>Reads an .fbx file and its pictures; slow, run it in the background.</summary>
    /// <exception cref="InvalidDataException">It is not a binary FBX file.</exception>
    /// <exception cref="IOException">It cannot be read.</exception>
    /// <param name="known">Pictures found already, by the name the model gives them (a unit's kept files); others are looked for beside the file.</param>
    public static PickedModel Read(string file, IReadOnlyDictionary<string, string>? known = null)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(file);
        FbxScene scene = FbxScene.Read(bytes);
        string folder = Path.GetDirectoryName(file) ?? "";
        List<MaterialMaps> materials = [.. scene.Materials.DistinctBy(material => material.Name).Select(material => MapsOf(material, folder, known))];
        var pictures = new Dictionary<string, MaterialPictures>(StringComparer.Ordinal);
        foreach (MaterialMaps maps in materials)
        {
            string? Map(string suffix) => maps.Maps.FirstOrDefault(map => map.Suffix == suffix).File;
            pictures[maps.Material] = new MaterialPictures(Decode(Map("")), Decode(Map("_NM")), Surface(Map("_O"), Map("_R"), Map("_M")));
        }
        return new PickedModel
        {
            File = file, Bytes = bytes, Scene = scene, Materials = materials, Pictures = pictures,
            Mesh = scene.ToMesh(Path.GetFileName(file), MaterialOf),
        };
    }

    /// <summary>A material as the drawn model names it: by its name, which <see cref="Pictures"/> is keyed by.</summary>
    public static ModelMaterial MaterialOf(FbxMaterial material) => new(material.Name, null);

    /// <summary>The model as drawn from a copy of the file's bytes (renamed, scaled, with points added), with this file's pictures.</summary>
    public (ModelMesh Mesh, List<MaterialPictures> Pictures) Drawn(byte[] copy)
    {
        ModelMesh mesh = FbxScene.Read(copy).ToMesh(Path.GetFileName(File), MaterialOf);
        return (mesh, [.. mesh.Parts.Select(part => Pictures.GetValueOrDefault(part.Material.Colour ?? "") ?? new MaterialPictures(null, null, null))]);
    }

    /// <summary>An .fbx file's materials' maps found beside it, without reading the pictures (for a detail level's file).</summary>
    /// <exception cref="InvalidDataException">It is not a binary FBX file.</exception>
    public static List<MaterialMaps> MapsBeside(string file, IReadOnlyDictionary<string, string>? known = null)
    {
        FbxScene scene = FbxScene.Read(System.IO.File.ReadAllBytes(file));
        string folder = Path.GetDirectoryName(file) ?? "";
        return [.. scene.Materials.DistinctBy(material => material.Name).Select(material => MapsOf(material, folder, known))];
    }

    /// <summary>A material's maps, found beside the file.</summary>
    private static MaterialMaps MapsOf(FbxMaterial material, string folder, IReadOnlyDictionary<string, string>? known)
    {
        string colour = material.Files.TryGetValue("DiffuseColor", out string? named) && named.Trim() != "" ? Path.GetFileName(named.Replace('\\', '/').Split('/')[^1]) : material.Name + ".png";
        string stem = Path.GetFileNameWithoutExtension(colour);
        string? Beside(string name)
        {
            if (known?.GetValueOrDefault(name) is string found && System.IO.File.Exists(found)) return found;
            string path = Path.Combine(folder, name);
            return System.IO.File.Exists(path) ? path : null;
        }
        List<(string, string?)> maps = [.. MaterialMaps.Suffixes.Select(suffix => (suffix, Beside(suffix == "" ? colour : stem + suffix + ".png")))];
        return new MaterialMaps(material.Name, maps, Beside(stem + "_A.png"));
    }

    /// <summary>The surface map the viewer draws with: occlusion in red, roughness in green, metal in blue; null without all three of one size.</summary>
    private static Picture? Surface(string? occlusion, string? roughness, string? metal)
    {
        Picture? o = Decode(occlusion), r = Decode(roughness), m = Decode(metal);
        if (o is null || r is null || m is null || o.Width != r.Width || o.Width != m.Width || o.Height != r.Height || o.Height != m.Height) return null;
        var rgba = new byte[o.Rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = o.Rgba[i];
            rgba[i + 1] = r.Rgba[i];
            rgba[i + 2] = m.Rgba[i];
            rgba[i + 3] = 255;
        }
        return new Picture(o.Width, o.Height, rgba);
    }

    /// <summary>A picture file's pixels (red, green, blue, alpha), made smaller to <see cref="LargestSide"/>; null when it cannot be read.</summary>
    private static Picture? Decode(string? file)
    {
        if (file is null) return null;
        try
        {
            using FileStream stream = System.IO.File.OpenRead(file);
            BitmapSource frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            double scale = Math.Min(1, LargestSide / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
            if (scale < 1) frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth, height = bgra.PixelHeight;
            var pixels = new byte[width * height * 4];
            bgra.CopyPixels(pixels, width * 4, 0);
            for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            return new Picture(width, height, pixels);
        }
        catch (Exception problem) when (problem is NotSupportedException or FileFormatException or IOException or ArgumentException or OverflowException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
