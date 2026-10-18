using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EasyNdf.Data.Game;

/// <summary>Writes a picture as a .png file, with nothing but .NET itself.</summary>
public static class PngFile
{
    /// <summary>The .png file's bytes: the signature, a header, the compressed pixel rows, and the end.</summary>
    public static byte[] Write(Picture picture)
    {
        using var file = new MemoryStream();
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), picture.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), picture.Height);
        header[8] = 8; // 8 bits a channel
        header[9] = 6; // red, green, blue and alpha
        WriteChunk(file, "IHDR", header);

        // each row starts with its filter (0: none), then its pixels
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            int rowLength = picture.Width * 4;
            for (int row = 0; row < picture.Height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(picture.Rgba, row * rowLength, rowLength);
            }
        }
        WriteChunk(file, "IDAT", compressed.ToArray());
        WriteChunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>A chunk: its length, its type, its data and a checksum of the type and data.</summary>
    private static void WriteChunk(Stream file, string type, byte[] data)
    {
        var number = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number);
        byte[] typeAndData = [.. Encoding.ASCII.GetBytes(type), .. data];
        file.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndData));
        file.Write(number);
    }

    /// <summary>The CRC-32 checksum .png files use (the same as zip's).</summary>
    private static uint Crc32(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        return ~crc;
    }
}
