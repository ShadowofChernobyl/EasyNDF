using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyNdf.App.Weapons;

/// <summary>
/// A weapon picture as the Armory draws it (<see cref="WeaponPicture"/>), made pixel by pixel: the game's shape (its
/// pixels' opacity) enlarged by a whole number, each of its pixels a square of the screen's, so its edges stay sharp;
/// filled with the theme's weapon picture colour, which darkens a little towards the bottom; with a shadow one pixel
/// below and to the right in the frame's colour, darkened. The pictures drawn are kept, as a list's cards are drawn
/// again and again while it scrolls.
/// </summary>
public static class WeaponArt
{
    /// <summary>How much darker the bottom of a shape is than its top, towards the frame's colour.</summary>
    private const double Shading = 0.3;

    /// <summary>The most pictures kept; past it they are all let go of, and made again as they are shown.</summary>
    private const int MostKept = 600;

    private static readonly Dictionary<(BitmapSource Picture, int Scale, Color Art, Color Frame), BitmapSource> Kept = [];

    /// <summary>A picture drawn at a scale (1 for its own size) in a colour, for a frame of a colour.</summary>
    public static BitmapSource Drawn(BitmapSource picture, int scale, Color art, Color frame)
    {
        if (Kept.TryGetValue((picture, scale, art, frame), out BitmapSource? kept)) return kept;
        if (Kept.Count >= MostKept) Kept.Clear();
        BitmapSource drawn = Draw(picture, scale, art, frame);
        Kept[(picture, scale, art, frame)] = drawn;
        return drawn;
    }

    private static BitmapSource Draw(BitmapSource picture, int scale, Color art, Color frame)
    {
        // the picture's opacity, a byte a pixel
        var bgra = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight;
        byte[] source = new byte[width * height * 4];
        bgra.CopyPixels(source, width * 4, 0);

        // one more pixel across and down, for the shadow
        int drawnWidth = width * scale + 1, drawnHeight = height * scale + 1;
        byte[] drawn = new byte[drawnWidth * drawnHeight * 4];
        Color shadow = Mix(frame, Colors.Black, 0.5);
        for (int y = 0; y < drawnHeight; y++)
        {
            Color fill = Mix(art, frame, Shading * y / drawnHeight);
            for (int x = 0; x < drawnWidth; x++)
            {
                byte shape = OpacityAt(source, width, height, x / scale, y / scale, x < width * scale && y < height * scale);
                byte shade = x >= 1 && y >= 1 ? OpacityAt(source, width, height, (x - 1) / scale, (y - 1) / scale, true) : (byte)0;
                Put(drawn, (y * drawnWidth + x) * 4, fill, shape, shadow, shade);
            }
        }
        BitmapSource result = BitmapSource.Create(drawnWidth, drawnHeight, 96, 96, PixelFormats.Bgra32, null, drawn, drawnWidth * 4);
        result.Freeze();
        return result;
    }

    /// <summary>The picture's opacity at a pixel; none outside it.</summary>
    private static byte OpacityAt(byte[] source, int width, int height, int x, int y, bool inside) =>
        inside && x < width && y < height ? source[(y * width + x) * 4 + 3] : (byte)0;

    /// <summary>A pixel: the shape's colour over its shadow, each as opaque as the picture is there.</summary>
    private static void Put(byte[] drawn, int at, Color fill, byte shape, Color shadow, byte shade)
    {
        double shapeOpacity = shape / 255.0, shadeOpacity = shade / 255.0 * (1 - shapeOpacity);
        double opacity = shapeOpacity + shadeOpacity;
        if (opacity <= 0) return;
        drawn[at] = (byte)Math.Round((fill.B * shapeOpacity + shadow.B * shadeOpacity) / opacity);
        drawn[at + 1] = (byte)Math.Round((fill.G * shapeOpacity + shadow.G * shadeOpacity) / opacity);
        drawn[at + 2] = (byte)Math.Round((fill.R * shapeOpacity + shadow.R * shadeOpacity) / opacity);
        drawn[at + 3] = (byte)Math.Round(opacity * 255);
    }

    /// <summary>A colour <paramref name="share"/> of the way from one to another.</summary>
    private static Color Mix(Color from, Color to, double share) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * share),
        (byte)Math.Round(from.G + (to.G - from.G) * share),
        (byte)Math.Round(from.B + (to.B - from.B) * share));
}
