using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyNdf.App.Units;

/// <summary>
/// Makes a picture larger the way photo editors do (Lanczos), which keeps edges much sharper than WPF's own resizing.
/// The Unit pane shows the game's small unit pictures larger than they are, and WPF would blur them.
/// </summary>
public static class PictureResizer
{
    /// <summary>How many of the picture's pixels on each side of a point are blended into it.</summary>
    private const int Reach = 3;

    /// <summary>The picture at a new size in pixels, frozen.</summary>
    public static BitmapSource Resize(BitmapSource picture, int width, int height)
    {
        // premultiplied, so a see-through edge does not darken what it is blended with
        var source = new FormatConvertedBitmap(picture, PixelFormats.Pbgra32, null, 0);
        int sourceWidth = source.PixelWidth, sourceHeight = source.PixelHeight;
        byte[] pixels = new byte[sourceWidth * sourceHeight * 4];
        source.CopyPixels(pixels, sourceWidth * 4, 0);

        // across first, then down: two passes of one direction each are much quicker than one of both
        float[] across = ResizeRows(ToFloats(pixels), sourceWidth, sourceHeight, width);
        float[] down = ResizeColumns(across, width, sourceHeight, height);

        BitmapSource resized = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, ToBytes(down), width * 4);
        resized.Freeze();
        return resized;
    }

    /// <summary>The Lanczos weight of a pixel at a distance from the point: highest close by, a little below zero further out, which sharpens.</summary>
    private static double Weight(double distance)
    {
        if (distance == 0) return 1;
        if (Math.Abs(distance) >= Reach) return 0;
        double x = Math.PI * distance;
        return Reach * Math.Sin(x) * Math.Sin(x / Reach) / (x * x);
    }

    /// <summary>
    /// For each new position along a line of <paramref name="from"/> pixels made <paramref name="to"/> long: the first
    /// old pixel it blends and the weight of each, adding up to one.
    /// </summary>
    private static (int First, double[] Weights)[] Blends(int from, int to)
    {
        double ratio = (double)from / to;
        // when shrinking, each new pixel covers several old ones, so it reaches further
        double spread = Math.Max(1, ratio);
        var blends = new (int, double[])[to];
        for (int at = 0; at < to; at++)
        {
            double centre = (at + 0.5) * ratio - 0.5;
            int first = (int)Math.Floor(centre - Reach * spread) + 1;
            int last = (int)Math.Floor(centre + Reach * spread);
            var weights = new double[last - first + 1];
            double total = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = Weight((first + i - centre) / spread);
                total += weights[i];
            }
            for (int i = 0; i < weights.Length; i++) weights[i] /= total;
            blends[at] = (first, weights);
        }
        return blends;
    }

    private static float[] ResizeRows(float[] pixels, int width, int height, int newWidth)
    {
        (int First, double[] Weights)[] blends = Blends(width, newWidth);
        var result = new float[newWidth * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < newWidth; x++)
                for (int channel = 0; channel < 4; channel++)
                {
                    double sum = 0;
                    (int first, double[] weights) = blends[x];
                    for (int i = 0; i < weights.Length; i++)
                    {
                        // past the edge, the edge pixel is used again
                        int from = Math.Clamp(first + i, 0, width - 1);
                        sum += weights[i] * pixels[(y * width + from) * 4 + channel];
                    }
                    result[(y * newWidth + x) * 4 + channel] = (float)sum;
                }
        return result;
    }

    private static float[] ResizeColumns(float[] pixels, int width, int height, int newHeight)
    {
        (int First, double[] Weights)[] blends = Blends(height, newHeight);
        var result = new float[width * newHeight * 4];
        for (int y = 0; y < newHeight; y++)
            for (int x = 0; x < width; x++)
                for (int channel = 0; channel < 4; channel++)
                {
                    double sum = 0;
                    (int first, double[] weights) = blends[y];
                    for (int i = 0; i < weights.Length; i++)
                    {
                        int from = Math.Clamp(first + i, 0, height - 1);
                        sum += weights[i] * pixels[(from * width + x) * 4 + channel];
                    }
                    result[(y * width + x) * 4 + channel] = (float)sum;
                }
        return result;
    }

    private static float[] ToFloats(byte[] bytes)
    {
        var floats = new float[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) floats[i] = bytes[i];
        return floats;
    }

    /// <summary>Back to bytes; the sharpening can overshoot 0 and 255, and a colour is never more than its pixel's alpha.</summary>
    private static byte[] ToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length];
        for (int pixel = 0; pixel < floats.Length; pixel += 4)
        {
            byte alpha = (byte)Math.Clamp(Math.Round(floats[pixel + 3]), 0, 255);
            for (int channel = 0; channel < 3; channel++)
                bytes[pixel + channel] = (byte)Math.Clamp(Math.Round(floats[pixel + channel]), 0, alpha);
            bytes[pixel + 3] = alpha;
        }
        return bytes;
    }
}
