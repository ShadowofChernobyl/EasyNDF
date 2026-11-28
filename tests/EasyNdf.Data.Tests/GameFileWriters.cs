using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Small made-up game files for the tests: an EDat archive, a .tgv texture and a TRAD dictionary, in the layouts the
/// app reads. No game file is used.
/// </summary>
internal static class GameFileWriters
{
    /// <summary>
    /// An EDat archive (version 2) holding the given files, each as an entry of its own in a flat tree: the header,
    /// then the tree, then the files' bytes.
    /// </summary>
    public static byte[] Archive(IReadOnlyList<(string Name, byte[] Bytes)> files)
    {
        // the tree: a 9-byte header whose first byte is its length, then one entry per file
        var tree = new MemoryStream();
        tree.Write([9, 0, 0, 0, 0, 0, 0, 0, 0]);
        long offset = 0;
        for (int i = 0; i < files.Count; i++)
        {
            byte[] name = Encoding.UTF8.GetBytes(files[i].Name + "\0");
            int length = 40 + name.Length + name.Length % 2;
            var entry = new byte[length];
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(0), 0); // 0: a file, not a folder
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), i == files.Count - 1 ? 0 : length); // 0: the last entry
            BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(8), offset);
            BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(16), files[i].Bytes.Length);
            name.CopyTo(entry, 40);
            tree.Write(entry);
            offset += files[i].Bytes.Length;
        }
        byte[] treeBytes = tree.ToArray();

        var header = new byte[80];
        "edat"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 2);
        int treeAt = header.Length;
        int filesStart = treeAt + treeBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(25), (uint)treeAt);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(29), (uint)treeBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(33), (uint)filesStart);

        var archive = new MemoryStream();
        archive.Write(header);
        archive.Write(treeBytes);
        foreach (var (_, bytes) in files) archive.Write(bytes);
        return archive.ToArray();
    }

    /// <summary>
    /// A .tgv texture (version 2) of one size, its pixels in A8B8G8R8 (red, green, blue, alpha in memory), unpacked
    /// or packed with zlib ("ZIPO").
    /// </summary>
    public static byte[] Texture(int width, int height, byte[] rgba, bool zlib = false)
    {
        byte[] stored = rgba;
        if (zlib)
        {
            var packed = new MemoryStream();
            packed.Write("ZIPO"u8);
            var size = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(size, rgba.Length);
            packed.Write(size);
            using (var zip = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) zip.Write(rgba);
            stored = packed.ToArray();
        }
        // header, the format's name padded to 4 bytes, a 16-byte checksum, the table of where the size starts and how long it is
        const int table = 28 + 8 + 16;
        var texture = new byte[table + 8 + stored.Length];
        BinaryPrimitives.WriteInt32LittleEndian(texture.AsSpan(0), 2);
        BinaryPrimitives.WriteInt32LittleEndian(texture.AsSpan(8), width);
        BinaryPrimitives.WriteInt32LittleEndian(texture.AsSpan(12), height);
        BinaryPrimitives.WriteUInt16LittleEndian(texture.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(texture.AsSpan(26), 8);
        "A8B8G8R8"u8.CopyTo(texture.AsSpan(28));
        BinaryPrimitives.WriteInt32LittleEndian(texture.AsSpan(table), table + 8);
        BinaryPrimitives.WriteInt32LittleEndian(texture.AsSpan(table + 4), stored.Length);
        stored.CopyTo(texture, table + 8);
        return texture;
    }

    /// <summary>A TRAD dictionary with the given texts, by their keys.</summary>
    public static byte[] Dictionary(IReadOnlyList<(ulong Key, string Text)> texts)
    {
        int tableEnd = 8 + texts.Count * 16;
        var dictionary = new MemoryStream();
        var head = new byte[tableEnd];
        "TRAD"u8.CopyTo(head);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(4), texts.Count);
        var body = new MemoryStream();
        for (int i = 0; i < texts.Count; i++)
        {
            byte[] text = Encoding.Unicode.GetBytes(texts[i].Text);
            BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(8 + i * 16), texts[i].Key);
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(8 + i * 16 + 8), tableEnd + (int)body.Length);
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(8 + i * 16 + 12), texts[i].Text.Length);
            body.Write(text);
        }
        dictionary.Write(head);
        dictionary.Write(body.ToArray());
        return dictionary.ToArray();
    }

    /// <summary>A zip like a game's base.zip, with the given files under its GameData folder.</summary>
    public static void BaseZip(string path, IReadOnlyDictionary<string, string> filesUnderGameData)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in filesUnderGameData)
        {
            using var writer = new StreamWriter(zip.CreateEntry("GameData/" + name).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
    }

    /// <summary>A picture of one colour.</summary>
    public static byte[] Pixels(int width, int height, byte red, byte green, byte blue) =>
        Enumerable.Range(0, width * height).SelectMany(_ => new[] { red, green, blue, (byte)255 }).ToArray();
}
