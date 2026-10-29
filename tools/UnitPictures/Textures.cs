using System.Text;

namespace UnitPictures;

/// <summary>A picture: its size and its pixels, 4 bytes each in the order red, green, blue, alpha.</summary>
internal sealed record Picture(int Width, int Height, byte[] Rgba)
{
    /// <summary>The part of the picture at x, y, of the given size.</summary>
    public Picture Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || x + width > Width || y + height > Height) throw new InvalidDataException("the crop runs past the picture");
        var pixels = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(Rgba, ((y + row) * Width + x) * 4, pixels, row * width * 4, width * 4);
        return new Picture(width, height, pixels);
    }
}

/// <summary>Where a small picture sits inside a bigger texture, as an atlas says.</summary>
/// <param name="Source">The small picture's own path, as the game's NDF names it: "GameData:/Assets/2D/....png".</param>
/// <param name="Texture">The texture it is a part of, inside the archives: "PC/Atlas/....tgv".</param>
internal sealed record AtlasPart(string Source, string Texture, int X, int Y, int Width, int Height);

/// <summary>The game's texture files (.tgv) and atlases (.atlas, which list the small pictures inside a texture).</summary>
internal static class Textures
{
    /// <summary>
    /// A .tgv texture's full-size pixels. The file holds several sizes ("mipmaps"), each stored on its own: packed
    /// with Zstandard ("ZSTD", WARNO), with zlib ("ZIPO", Steel Division 2) or not packed. The one whose size is the
    /// texture's full size is taken.
    /// </summary>
    public static Picture ReadTgv(byte[] data)
    {
        if (data.Length < 64 || BitConverter.ToInt32(data, 0) is not (2 or 3)) throw new InvalidDataException("a .tgv of a version not known");
        int width = BitConverter.ToInt32(data, 8);
        int height = BitConverter.ToInt32(data, 12);
        int sizes = BitConverter.ToUInt16(data, 24);
        // the format's name, such as "A8B8G8R8_SRGB" or "DXT5_SRGB", ends at its first zero byte
        int formatEnd = Array.IndexOf(data, (byte)0, 28, 32);
        int formatLength = (formatEnd < 0 ? 60 : formatEnd) - 28;
        string format = Encoding.ASCII.GetString(data, 28, formatLength);
        int fullSize = PixelBytes(format, width, height);

        // the table of where each size starts, then of how long each is: after the format's name and a 16-byte
        // checksum, though its place varies a little, so nearby places are tried too
        int expectedTable = 28 + (formatLength + 3) / 4 * 4 + 16;
        foreach (int table in new[] { expectedTable, 48, 44, 52, 56, 60, 64, 40, 68, 72 }.Distinct())
        {
            if (table + sizes * 8 > data.Length) continue;
            for (int i = 0; i < sizes; i++)
            {
                int at = BitConverter.ToInt32(data, table + i * 4);
                int length = BitConverter.ToInt32(data, table + sizes * 4 + i * 4);
                if (at < 0 || length < 8 || at + (long)length > data.Length) continue;
                byte[]? pixels = Unpack(data, at, length, fullSize);
                if (pixels is not null) return new Picture(width, height, ToRgba(format, width, height, pixels));
            }
        }
        throw new InvalidDataException("no full-size pixels found in a .tgv");
    }

    /// <summary>The first bytes of a .tgv, for the report when it cannot be read.</summary>
    public static string TgvHeader(byte[] data) => Convert.ToHexString(data, 0, Math.Min(96, data.Length));

    /// <summary>How many bytes a picture of this format and size takes before unpacking.</summary>
    private static int PixelBytes(string format, int width, int height)
    {
        int blocks = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4);
        if (format.StartsWith("DXT1", StringComparison.Ordinal) || format.StartsWith("BC1", StringComparison.Ordinal)) return blocks * 8;
        if (format.StartsWith("DXT5", StringComparison.Ordinal) || format.StartsWith("BC3", StringComparison.Ordinal)) return blocks * 16;
        if (IsFourBytesPerPixel(format)) return width * height * 4;
        throw new InvalidDataException("a .tgv in the format " + format + ", which this tool cannot read yet");
    }

    /// <summary>One size's bytes, unpacked; null when it is not the full size.</summary>
    private static byte[]? Unpack(byte[] data, int at, int length, int fullSize)
    {
        bool zstd = data.AsSpan(at, 4).SequenceEqual("ZSTD"u8);
        bool zlib = data.AsSpan(at, 4).SequenceEqual("ZIPO"u8);
        if (!zstd && !zlib) return length == fullSize ? data.AsSpan(at, length).ToArray() : null;
        if (BitConverter.ToInt32(data, at + 4) != fullSize) return null;

        byte[] pixels;
        if (zstd)
        {
            using var decompressor = new ZstdSharp.Decompressor();
            pixels = decompressor.Unwrap(data.AsSpan(at + 8, length - 8), fullSize).ToArray();
        }
        else
        {
            using var packed = new MemoryStream(data, at + 8, length - 8);
            using var unpacker = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionMode.Decompress);
            pixels = new byte[fullSize];
            unpacker.ReadExactly(pixels);
        }
        if (pixels.Length != fullSize) throw new InvalidDataException("a .tgv's pixels are the wrong length");
        return pixels;
    }

    /// <summary>Formats that keep each pixel in 4 bytes, unpacked.</summary>
    private static bool IsFourBytesPerPixel(string format) =>
        format.StartsWith("A8B8G8R8", StringComparison.Ordinal) || format.StartsWith("A8R8G8B8", StringComparison.Ordinal)
        || format.StartsWith("R8G8B8A8", StringComparison.Ordinal) || format.StartsWith("B8G8R8A8", StringComparison.Ordinal);

    /// <summary>The pixels in the order red, green, blue, alpha, whatever the format keeps them in.</summary>
    private static byte[] ToRgba(string format, int width, int height, byte[] pixels)
    {
        if (format.StartsWith("DXT1", StringComparison.Ordinal) || format.StartsWith("BC1", StringComparison.Ordinal)) return BlockTextures.Decode(pixels, width, height, hasAlphaBlock: false);
        if (format.StartsWith("DXT5", StringComparison.Ordinal) || format.StartsWith("BC3", StringComparison.Ordinal)) return BlockTextures.Decode(pixels, width, height, hasAlphaBlock: true);
        // these two keep blue before red in memory
        if (format.StartsWith("A8R8G8B8", StringComparison.Ordinal) || format.StartsWith("B8G8R8A8", StringComparison.Ordinal))
            for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        return pixels;
    }

    /// <summary>
    /// The small pictures an .atlas file places inside its textures. An atlas is a small binary NDF file ("EUG0",
    /// then "CNDF"), compressed with Zstandard after its 44-byte header, with tables of strings, class names,
    /// property names and objects.
    /// </summary>
    public static List<AtlasPart> ReadAtlas(byte[] packed)
    {
        if (packed.Length < 44 || !packed.AsSpan(0, 4).SequenceEqual("EUG0"u8) || !packed.AsSpan(8, 4).SequenceEqual("CNDF"u8))
            throw new InvalidDataException("not an atlas");
        int size = BitConverter.ToInt32(packed, 40);
        using var decompressor = new ZstdSharp.Decompressor();
        byte[] data = decompressor.Unwrap(packed.AsSpan(44), size).ToArray();
        // offsets in the file count from its start; the data starts 40 bytes in
        int tableOfContents = (int)BitConverter.ToInt64(packed, 16) - 40;

        using var memory = new MemoryStream(data);
        using var reader = new BinaryReader(memory);
        memory.Position = tableOfContents;
        if (reader.ReadUInt32() != 0x30434f54) throw new InvalidDataException("an atlas without its table of contents");
        var sections = new Dictionary<string, (int Start, int Length)>();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string key = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0');
            int start = (int)reader.ReadInt64() - 40;
            int length = (int)reader.ReadInt64();
            sections[key] = (start, length);
        }

        List<string> strings = ReadStrings(reader, sections["STRG"], withNumber: false);
        List<string> classes = ReadStrings(reader, sections["CLAS"], withNumber: false);
        List<string> properties = ReadStrings(reader, sections["PROP"], withNumber: true);
        List<(string Type, Dictionary<string, object> Fields)> objects = ReadObjects(reader, sections["OBJE"], strings, classes, properties);
        return PartsOf(objects);
    }

    /// <summary>A section of strings, each its length then its bytes; property names also have a number after them.</summary>
    private static List<string> ReadStrings(BinaryReader reader, (int Start, int Length) section, bool withNumber)
    {
        var list = new List<string>();
        reader.BaseStream.Position = section.Start;
        while (reader.BaseStream.Position < section.Start + section.Length)
        {
            int length = reader.ReadInt32();
            list.Add(Encoding.UTF8.GetString(reader.ReadBytes(length)).TrimEnd('\0'));
            if (withNumber) reader.ReadInt32();
        }
        return list;
    }

    /// <summary>The objects: each its class, then its properties until the end marker 0xABABABAB.</summary>
    private static List<(string Type, Dictionary<string, object> Fields)> ReadObjects(BinaryReader reader, (int Start, int Length) section, List<string> strings, List<string> classes, List<string> properties)
    {
        var objects = new List<(string, Dictionary<string, object>)>();
        reader.BaseStream.Position = section.Start;
        while (reader.BaseStream.Position < section.Start + section.Length)
        {
            string type = classes[reader.ReadInt32()];
            var fields = new Dictionary<string, object>();
            while (true)
            {
                uint property = reader.ReadUInt32();
                if (property == 0xabababab) break;
                int kind = reader.ReadInt32();
                // only the kinds an atlas uses: a string, a pair of whole numbers, a pair of decimals, a link to an object
                object value = kind switch
                {
                    28 => strings[reader.ReadInt32()],
                    31 => ((float)reader.ReadInt32(), (float)reader.ReadInt32()),
                    33 => (reader.ReadSingle(), reader.ReadSingle()),
                    9 => ReadLink(reader),
                    _ => throw new InvalidDataException("an atlas property of a kind not known: " + kind),
                };
                fields[properties[(int)property]] = value;
            }
            objects.Add((type, fields));
        }
        return objects;
    }

    private static int ReadLink(BinaryReader reader)
    {
        if (reader.ReadUInt32() != 0xbbbbbbbb) throw new InvalidDataException("an atlas link of a kind not known");
        int index = reader.ReadInt32();
        reader.ReadInt32();
        return index;
    }

    /// <summary>
    /// Each small picture (a TTextureSmall): its own path, the texture it is in (its "Container"), and where. The
    /// place is given as fractions of the texture's size, so it is turned into pixels.
    /// </summary>
    private static List<AtlasPart> PartsOf(List<(string Type, Dictionary<string, object> Fields)> objects)
    {
        var parts = new List<AtlasPart>();
        foreach ((string type, Dictionary<string, object> fields) in objects)
        {
            if (type != "TTextureSmall") continue;
            var container = objects[(int)fields["Container"]].Fields;
            var (textureWidth, textureHeight) = ((float, float))container["Size"];
            var (left, top) = fields.TryGetValue("MinUV", out object? min) ? ((float, float))min : (0f, 0f);
            var (width, height) = ((float, float))fields["SizeInPixels"];
            string texture = ((string)container["TextureFileName"]).Replace("ZZ:/", "", StringComparison.OrdinalIgnoreCase);
            parts.Add(new AtlasPart((string)fields["TexturePartFileName"], Path.ChangeExtension(texture, ".tgv").Replace('\\', '/'),
                (int)Math.Round(left * textureWidth), (int)Math.Round(top * textureHeight), (int)width, (int)height));
        }
        return parts;
    }
}
