using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace EasyNdf.Data.FileList;

/// <summary>
/// A file's bytes kept packed in memory, for files taken out of an archive. Game files are plain text that packs to
/// about a thirtieth of its size, so a whole game's 4,000 files take some 15 MB instead of 450 MB.
/// </summary>
public sealed class PackedBytes
{
    private readonly byte[] packed;

    private PackedBytes(byte[] packed, long length)
    {
        this.packed = packed;
        Length = length;
    }

    /// <summary>The size of the bytes before packing.</summary>
    public long Length { get; }

    /// <summary>Packs bytes. The quickest packing is used: it still makes game files about 30 times smaller.</summary>
    public static PackedBytes Pack(ReadOnlySpan<byte> bytes)
    {
        using var output = new MemoryStream();
        using (var packer = new BrotliStream(output, CompressionLevel.Fastest))
            packer.Write(bytes);
        return new PackedBytes(output.ToArray(), bytes.Length);
    }

    /// <summary>Packs a text exactly as it is, character for character.</summary>
    public static PackedBytes PackText(string text) => Pack(MemoryMarshal.AsBytes(text.AsSpan()));

    /// <summary>A text packed with <see cref="PackText"/>.</summary>
    public string UnpackText() => new(MemoryMarshal.Cast<byte, char>(Unpack()));

    /// <summary>The bytes as they were before packing.</summary>
    public byte[] Unpack()
    {
        var bytes = new byte[Length];
        using var unpacker = new BrotliStream(new MemoryStream(packed), CompressionMode.Decompress);
        unpacker.ReadExactly(bytes);
        return bytes;
    }
}
