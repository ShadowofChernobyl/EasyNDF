using System.Buffers.Binary;
using System.Text;

namespace EasyNdf.Data.Game;

/// <summary>One file inside a game archive: where its bytes are.</summary>
/// <param name="Archive">The archive's path on disk.</param>
/// <param name="Name">The file's path inside the archive, with forward slashes, such as "PC/Atlas/....atlas".</param>
/// <param name="Offset">Where its bytes start in the archive.</param>
/// <param name="Length">How many bytes it has.</param>
public sealed record ArchiveFile(string Archive, string Name, long Offset, long Length)
{
    /// <summary>Reads the file's bytes from the archive.</summary>
    public byte[] Read()
    {
        using FileStream stream = File.OpenRead(Archive);
        stream.Position = Offset;
        var bytes = new byte[Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}

/// <summary>
/// Eugen's "EDat" archives (the game's ZZ_*.dat files): a header, a tree of folder and file names, then the files'
/// bytes. Only the tree is read here; a file's bytes are read when they are needed.
/// </summary>
public static class EdatArchive
{
    /// <summary>
    /// Every file in an archive, by its path inside it. Some Steel Division 2 archives have tree entries that run past
    /// their parent's end; those are cut at the parent's end, counted in <paramref name="entriesCut"/>, and the tree's
    /// bytes are given back in <paramref name="tree"/> so they can be looked at.
    /// </summary>
    public static List<ArchiveFile> ListFiles(string path, out int entriesCut, out byte[] tree) =>
        ListFiles(path, 0, out entriesCut, out tree);

    /// <summary>
    /// Every file in an archive that starts <paramref name="start"/> bytes into the file at <paramref name="path"/>:
    /// Steel Division 2's texture packs (.ppk) are archives of the same kind, kept inside the game's archives.
    /// </summary>
    public static List<ArchiveFile> ListFiles(string path, long start, out int entriesCut, out byte[] tree)
    {
        using FileStream stream = File.OpenRead(path);
        stream.Position = start;
        var header = new byte[80];
        stream.ReadExactly(header);
        if (!header.AsSpan(0, 4).SequenceEqual("edat"u8)) throw new InvalidDataException("not an EDat archive");
        int version = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (version is not (2 or 3)) throw new InvalidDataException("EDat version " + version + " is not known");

        // where the tree is, how long it is, and what the files' offsets are counted from: version 2 and 3 keep
        // them at different places in the header
        uint treeAt = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(version == 2 ? 25 : 8));
        uint treeLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(version == 2 ? 29 : 12));
        uint filesStart = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(version == 2 ? 33 : 16));
        var files = new List<ArchiveFile>();
        entriesCut = 0;
        tree = [];
        if (treeLength == 0) return files;
        if (start + treeAt + (long)treeLength > stream.Length) throw new InvalidDataException("the file tree runs past the end");

        tree = new byte[treeLength];
        stream.Position = start + treeAt;
        stream.ReadExactly(tree);
        // the tree starts with a short header whose first byte is its length: 9 in WARNO, 10 in Steel Division 2
        int firstEntry = tree[0] is 9 or 10 ? tree[0] : 9;
        var cut = new int[1];
        try
        {
            ReadNodes(tree, firstEntry, tree.Length, "", path, start + filesStart, files, depth: 0, cut);
        }
        catch (Exception problem) when (problem is ArgumentException or InvalidDataException)
        {
            // keep the files read so far; the report says the tree was cut short
            cut[0]++;
        }
        entriesCut = cut[0];
        return files;
    }

    /// <summary>
    /// One file of an archive, by its path inside it in any capitals; null when the archive does not have it. Only the
    /// tree's branch towards it is read, rather than the whole list: a game's main archive lists some 100,000 files.
    /// </summary>
    public static ArchiveFile? FindFile(string path, string wanted)
    {
        using FileStream stream = File.OpenRead(path);
        var header = new byte[80];
        stream.ReadExactly(header);
        if (!header.AsSpan(0, 4).SequenceEqual("edat"u8)) throw new InvalidDataException("not an EDat archive");
        int version = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (version is not (2 or 3)) throw new InvalidDataException("EDat version " + version + " is not known");
        uint treeAt = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(version == 2 ? 25 : 8));
        uint treeLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(version == 2 ? 29 : 12));
        uint filesStart = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(version == 2 ? 33 : 16));
        if (treeLength == 0 || treeAt + (long)treeLength > stream.Length) return null;
        var tree = new byte[treeLength];
        stream.Position = treeAt;
        stream.ReadExactly(tree);
        int firstEntry = tree[0] is 9 or 10 ? tree[0] : 9;
        try
        {
            return FindNode(tree, firstEntry, tree.Length, "", wanted.Replace('\\', '/'), path, filesStart, depth: 0);
        }
        catch (Exception problem) when (problem is ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Looks for a file among the tree's entries from one place to another, as <see cref="ReadNodes"/> reads them, going
    /// into a folder only when the file's path starts with the folder's name.
    /// </summary>
    private static ArchiveFile? FindNode(byte[] tree, int start, int end, string parentName, string wanted, string archive, long filesStart, int depth)
    {
        if (depth > 64) throw new InvalidDataException("the file tree is too deep");
        while (start + 8 <= end)
        {
            int childrenAt = BitConverter.ToInt32(tree, start);
            int length = BitConverter.ToInt32(tree, start + 4);
            int stop = length == 0 ? end : Math.Min(start + length, end);
            if (stop <= start) throw new InvalidDataException("a tree entry has no length");

            bool isFolder = childrenAt != 0;
            int nameAt = start + (isFolder ? 8 : 40);
            int nameEnd = Array.IndexOf(tree, (byte)0, nameAt, stop - nameAt);
            if (nameEnd < 0) throw new InvalidDataException("a name has no end");
            string name = (parentName + Encoding.UTF8.GetString(tree, nameAt, nameEnd - nameAt)).Replace('\\', '/');

            if (isFolder && wanted.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                if (FindNode(tree, start + childrenAt, stop, name, wanted, archive, filesStart, depth + 1) is ArchiveFile found) return found;
            }
            else if (!isFolder && string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return new ArchiveFile(archive, name, filesStart + BitConverter.ToInt64(tree, start + 8), BitConverter.ToInt64(tree, start + 16));
            }
            start = stop;
        }
        return null;
    }

    /// <summary>
    /// Reads the tree's entries from one place to another. Each entry starts with two numbers: where its children
    /// start (0 for a file) and its own length (0 for the last one). A name is only the part after its parent's.
    /// </summary>
    private static void ReadNodes(byte[] tree, int start, int end, string parentName, string archive, long filesStart, List<ArchiveFile> files, int depth, int[] cut)
    {
        if (depth > 64) throw new InvalidDataException("the file tree is too deep");
        while (start + 8 <= end)
        {
            int childrenAt = BitConverter.ToInt32(tree, start);
            int length = BitConverter.ToInt32(tree, start + 4);
            int stop = length == 0 ? end : start + length;
            if (stop <= start) throw new InvalidDataException("a tree entry has no length");
            if (stop > end)
            {
                // cut at the parent's end rather than give up on the whole archive
                stop = end;
                cut[0]++;
            }

            bool isFolder = childrenAt != 0;
            // a folder's name follows its two numbers; a file's follows its offset, size and checksum
            int nameAt = start + (isFolder ? 8 : 40);
            int nameEnd = Array.IndexOf(tree, (byte)0, nameAt, stop - nameAt);
            if (nameEnd < 0) throw new InvalidDataException("a name has no end");
            string name = parentName + Encoding.UTF8.GetString(tree, nameAt, nameEnd - nameAt);

            if (isFolder)
            {
                ReadNodes(tree, start + childrenAt, stop, name, archive, filesStart, files, depth + 1, cut);
            }
            else
            {
                long offset = filesStart + BitConverter.ToInt64(tree, start + 8);
                long size = BitConverter.ToInt64(tree, start + 16);
                files.Add(new ArchiveFile(archive, name.Replace('\\', '/'), offset, size));
            }
            start = stop;
        }
    }
}
