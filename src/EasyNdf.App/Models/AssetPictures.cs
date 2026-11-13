using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasyNdf.Data.Game;
using EasyNdf.Data.Models;

namespace EasyNdf.App.Models;

/// <summary>
/// A Steel Division 2 model's pictures, from its Modding Assets: each material names its colour and normal textures
/// (PNG files beside the model in its zip). Pictures larger than <see cref="LargestSide"/> are drawn smaller: the
/// assets' are up to 4096 pixels a side, more than the viewer's window needs.
/// </summary>
internal static class AssetPictures
{
    /// <summary>The most pixels a picture is kept at, along its longer side.</summary>
    private const int LargestSide = 2048;

    /// <summary>Each part's pictures, in the model's order; a material's are read once.</summary>
    public static List<MaterialPictures> Of(ModdingAssets assets, ModelMesh model)
    {
        var byMaterial = new Dictionary<ModelMaterial, MaterialPictures>();
        var pictures = new Dictionary<string, Picture?>(StringComparer.OrdinalIgnoreCase);
        Picture? Read(string? path)
        {
            if (path is null) return null;
            if (!pictures.TryGetValue(path, out Picture? picture)) pictures[path] = picture = Decode(assets.ReadFile(path));
            return picture;
        }
        List<MaterialPictures> each = [];
        foreach (ModelPart part in model.Parts)
        {
            if (!byMaterial.TryGetValue(part.Material, out MaterialPictures? maps))
                byMaterial[part.Material] = maps = new MaterialPictures(Read(part.Material.Colour), Read(part.Material.Normal), null);
            each.Add(maps);
        }
        return each;
    }

    /// <summary>A PNG file's pixels (red, green, blue, alpha), made smaller to <see cref="LargestSide"/>; null when it cannot be read.</summary>
    private static Picture? Decode(byte[]? png)
    {
        if (png is null) return null;
        try
        {
            BitmapSource frame = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            double scale = Math.Min(1, LargestSide / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
            if (scale < 1) frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth, height = bgra.PixelHeight;
            var pixels = new byte[width * height * 4];
            bgra.CopyPixels(pixels, width * 4, 0);
            // blue, green, red, alpha to red, green, blue, alpha
            for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            return new Picture(width, height, pixels);
        }
        catch (Exception problem) when (problem is NotSupportedException or FileFormatException or IOException or ArgumentException or OverflowException)
        {
            return null;
        }
    }
}
