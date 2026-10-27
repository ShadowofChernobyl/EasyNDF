using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyNdf.App.Divisions;

/// <summary>
/// Pictures of the user's own (a division's emblem) made into a .png the game's tools can read. The mod tools
/// (GenerateMod.bat's AssetCooker) refuse some .png files, such as interlaced ones ("ADAM7 interlacing isn't
/// supported"), and read no other format, so every picture is written again as a plain .png: 8-bit colour with
/// transparency, not interlaced. Windows reads the picture, so .jpg, .bmp, .gif and .tif files can be used too.
/// </summary>
public static class GameReadyPicture
{
    /// <summary>
    /// Writes a picture as a plain .png at a path, reading it first in full, so the path may be the picture's own.
    /// </summary>
    /// <returns>Whether it could be read and written.</returns>
    public static bool Write(string source, string target)
    {
        BitmapSource? picture = Read(source);
        if (picture is null) return false;
        var encoder = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0)));
        try
        {
            using var file = File.Create(target);
            encoder.Save(file);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a file is already a .png the game's tools read: a .png, not interlaced, with 8 bits a colour. Only its
    /// header is read.
    /// </summary>
    public static bool IsReady(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var header = new byte[29];
            if (file.Read(header, 0, header.Length) < header.Length) return false;
            // the PNG signature, then the IHDR chunk: its bit depth at byte 24 and its interlace method at byte 28
            byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            return header.AsSpan(0, 8).SequenceEqual(signature) && header[24] == 8 && header[28] == 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Makes a kept picture a plain .png in place when it is not one yet. Returns whether it is one now.</summary>
    public static bool MakeReady(string path) => IsReady(path) || Write(path, path);

    /// <summary>The picture, read in full and let go of; null when Windows cannot read it.</summary>
    private static BitmapSource? Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            BitmapFrame frame = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
