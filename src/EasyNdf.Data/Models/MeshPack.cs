using System.IO.Compression;
using System.Numerics;
using System.Text;
using EasyNdf.Data.Game;

namespace EasyNdf.Data.Models;

/// <summary>
/// WARNO's 3D models: the mesh pack PC/Mesh/Pack/Base.spk inside the game's archives ("MESHPCPC", version 11), some
/// 600 MB. Its header points to tables: the models' names, the kinds of vertex, the materials, then for each model its
/// parts with their packed vertices and triangles, and its nodes. Only what one model needs is read, straight from the
/// archive, so opening the pack takes a moment and little memory.
/// </summary>
/// <remarks>
/// The layout was worked out by reading the pack: nothing about it is published. Steel Division 2's pack has the same
/// tables, but its vertices are packed in a way not yet read.
/// </remarks>
public sealed class MeshPack : IModelPack
{
    /// <summary>One model in the pack's name tree: its path, its mesh's number and its nodes' number.</summary>
    private sealed record Entry(string Path, int Mesh, int Nodes);

    private readonly FileStream stream;
    private readonly long packStart;
    private readonly uint[] header;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> vertexKinds;
    private readonly PackedMaterials materials;

    private MeshPack(ArchiveFile pack)
    {
        stream = File.OpenRead(pack.Archive);
        packStart = pack.Offset;
        byte[] head = Read(0, 512);
        if (!head.AsSpan(0, 8).SequenceEqual("MESHPCPC"u8)) throw new InvalidDataException("not a mesh pack");
        header = new uint[128];
        for (int i = 0; i < header.Length; i++) header[i] = BitConverter.ToUInt32(head, i * 4);
        ReadNameTree();
        vertexKinds = ReadVertexKinds();
        materials = new PackedMaterials(Read(header[19], (int)header[20]));
    }

    /// <summary>Opens the mesh pack in the game's archives; null when the archives have none.</summary>
    public static MeshPack? Open(GameArchives archives) =>
        PackPaths.Select(archives.Find).OfType<ArchiveFile>().FirstOrDefault() is ArchiveFile pack ? new MeshPack(pack) : null;

    /// <summary>
    /// Opens the packs a game mod adds (its Gen folder's MeshPack/&lt;name&gt;/Mesh_All.spk, added to the archives as
    /// loose files), which hold its own models, the most recently built first; those that cannot be read are left out.
    /// </summary>
    public static List<MeshPack> OpenModPacks(GameArchives archives)
    {
        var packs = new List<MeshPack>();
        // a mod can hold more than one (WTO x WiF keeps an older World in Flames build beside its own): the latest built is read first
        foreach (ArchiveFile file in archives.Files.Values.Where(file => IsModPackFile(file.Name)).OrderByDescending(file => File.GetLastWriteTimeUtc(file.Archive)))
        {
            try
            {
                packs.Add(new MeshPack(file));
            }
            catch (Exception problem) when (problem is InvalidDataException or IOException or EndOfStreamException or IndexOutOfRangeException or ArgumentException)
            {
                archives.Problems.Add(file.Name + ": " + problem.Message);
            }
        }
        return packs;
    }

    /// <summary>Whether an archive path is a pack a mod adds: under MeshPack/, not the game's own.</summary>
    private static bool IsModPackFile(string path) =>
        path.StartsWith("MeshPack/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".spk", StringComparison.OrdinalIgnoreCase) && !PackPaths.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where the pack is inside the archives, newest first: WARNO's later updates add MeshPack/Base.spk and leave an older
    /// PC/Mesh/Pack/Base.spk, laid out a little differently, in an older archive.
    /// </summary>
    public static readonly IReadOnlyList<string> PackPaths = ["MeshPack/Base.spk", "PC/Mesh/Pack/Base.spk"];

    /// <summary>Whether an archive path is a mesh pack the viewer may open: the game's (see <see cref="PackPaths"/>) or a mod's.</summary>
    public static bool IsPackFile(string path) => PackPaths.Contains(path, StringComparer.OrdinalIgnoreCase) || IsModPackFile(path);

    /// <summary>Whether the pack has a model of this path, such as "Assets/3D/Units/US/Char/M1A1_Abrams/M1A1_Abrams.fbx".</summary>
    public bool Has(string path) => entries.ContainsKey(Normalised(path));

    /// <summary>
    /// A model by its path, as the game's NDF names it ("GameData:/Assets/3D/....fbx" or "Assets/3D/....fbx"); null when
    /// the pack has none of that path.
    /// </summary>
    public ModelMesh? Read(string path)
    {
        if (!entries.TryGetValue(Normalised(path), out Entry? entry)) return null;
        (List<ModelPart> parts, Vector3 low, Vector3 high) = ReadParts(entry.Mesh);
        List<ModelNode> nodes = ReadNodes(entry.Nodes);
        MarkTrackStrips(parts, nodes);
        return new ModelMesh(entry.Path, parts, nodes, low, high);
    }

    /// <summary>A path as the pack lists it: "Assets/3D/...", with forward slashes.</summary>
    private static string Normalised(string path)
    {
        string slashed = path.Replace('\\', '/');
        return slashed.StartsWith("GameData:/", StringComparison.OrdinalIgnoreCase) ? slashed["GameData:/".Length..] : slashed.TrimStart('/');
    }

    /// <summary>How many models the pack lists.</summary>
    public int ModelCount => entries.Count;

    /// <summary>Closes the archive.</summary>
    public void Dispose() => stream.Dispose();

    // ---- the tables ----

    private byte[] Read(long at, int count)
    {
        var bytes = new byte[count];
        stream.Position = packStart + at;
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// The name tree (header words 13 and 14): folders hold folders or models, each entry a jump to its children, its
    /// length and its name; a model's entry also holds its bounding box, its mesh's number and its nodes' number.
    /// </summary>
    private void ReadNameTree()
    {
        byte[] tree = Read(header[13], (int)header[14]);
        void Walk(int start, int end, string parent)
        {
            while (start + 8 <= end)
            {
                int childrenAt = BitConverter.ToInt32(tree, start);
                int length = BitConverter.ToInt32(tree, start + 4);
                int stop = length == 0 ? end : Math.Min(start + length, end);
                int nameAt = start + (childrenAt != 0 ? 8 : 40);
                int nameEnd = Array.IndexOf(tree, (byte)0, nameAt, stop - nameAt);
                string name = parent + Encoding.ASCII.GetString(tree, nameAt, nameEnd - nameAt);
                if (childrenAt != 0) Walk(start + childrenAt, stop, name);
                else
                {
                    string path = name.Replace('\\', '/');
                    entries[path] = new Entry(path, BitConverter.ToUInt16(tree, start + 36), BitConverter.ToUInt16(tree, start + 38));
                }
                if (length == 0) return;
                start = stop;
            }
        }
        Walk(tree[0], tree.Length, "");
    }

    /// <summary>The kinds of vertex (header words 16 to 18), such as "Position_3f__TexCoord0_2wn__...": what each vertex holds, in order.</summary>
    private List<string> ReadVertexKinds()
    {
        byte[] bytes = Read(header[16], (int)header[17]);
        var kinds = new List<string>();
        for (int i = 0; i < header[18]; i++)
            kinds.Add(Encoding.ASCII.GetString(bytes, 4 + i * 256, 252).TrimEnd('\0').Replace("$/M3D/System/VertexType/TVertex__", ""));
        return kinds;
    }

    /// <summary>
    /// A block of vertices or triangles: "VBUF" blocks are packed with Zstandard after a 16-byte head; the others start
    /// with their unpacked length and are packed with zlib.
    /// </summary>
    private byte[] Unpack(long at, int length)
    {
        byte[] block = Read(at, length);
        if (block.AsSpan(0, 4).SequenceEqual("VBUF"u8))
        {
            using var zstd = new ZstdSharp.Decompressor();
            return zstd.Unwrap(block.AsSpan(16)).ToArray();
        }
        int full = BitConverter.ToInt32(block, 0);
        using var zlib = new ZLibStream(new MemoryStream(block, 4, length - 4), CompressionMode.Decompress);
        var bytes = new byte[full];
        zlib.ReadExactly(bytes);
        return bytes;
    }

    // ---- one model ----

    /// <summary>
    /// A model's parts: its mesh (header word 22) names its first part and how many; each part (word 25) names its
    /// material, its triangles' head (word 28, into the stream at word 31) and its vertices' head (word 33, into word 36).
    /// </summary>
    private (List<ModelPart> Parts, Vector3 Low, Vector3 High) ReadParts(int mesh)
    {
        byte[] meshEntry = Read(header[22] + mesh * 4L, 4);
        int first = BitConverter.ToUInt16(meshEntry, 0), count = BitConverter.ToUInt16(meshEntry, 2);
        byte[] partEntries = Read(header[25] + first * 8L, count * 8);
        var parts = new List<ModelPart>();
        Vector3 low = new(float.MaxValue), high = new(float.MinValue);
        for (int p = 0; p < count; p++)
        {
            int material = BitConverter.ToUInt16(partEntries, p * 8 + 2);
            int indexHead = BitConverter.ToUInt16(partEntries, p * 8 + 4), vertexHead = BitConverter.ToUInt16(partEntries, p * 8 + 6);
            byte[] indexEntry = Read(header[28] + indexHead * 16L, 16), vertexEntry = Read(header[33] + vertexHead * 16L, 16);
            ModelVertex[] vertices = ReadVertices(vertexEntry);
            int[] indexes = ReadIndexes(indexEntry, vertices.Length);
            foreach (ModelVertex vertex in vertices)
            {
                low = Vector3.Min(low, vertex.Position);
                high = Vector3.Max(high, vertex.Position);
            }
            parts.Add(new ModelPart(vertices, indexes, materials.Get(material), IsTrackStrip: false));
        }
        return (parts, low, high);
    }

    /// <summary>
    /// A part's vertices, laid out as its kind of vertex says: "Position_3f" takes 12 bytes, every other value 4.
    /// Texture places are two 16-bit numbers over 0 to 8; directions a byte each; BlIdx's four bytes are the nodes the
    /// vertex follows and BlW's how much it follows each (a vehicle's follow one node fully; a soldier's skin bends
    /// between up to four bones).
    /// </summary>
    private ModelVertex[] ReadVertices(byte[] entry)
    {
        int count = BitConverter.ToInt32(entry, 8);
        string kind = vertexKinds[(int)(BitConverter.ToUInt32(entry, 12) & 0xFFFF)];
        byte[] bytes = Unpack(header[36] + BitConverter.ToUInt32(entry, 0), BitConverter.ToInt32(entry, 4));
        int stride = bytes.Length / Math.Max(1, count);
        int offset = 0, position = -1, texture = -1, tangent = -1, binormal = -1, normal = -1, node = -1, weight = -1;
        foreach (string attribute in kind.Split("__"))
        {
            string[] bits = attribute.Split('_');
            if (bits[0] == "Position") position = offset;
            else if (bits[0] == "TexCoord0") texture = offset;
            else if (bits[0] == "BlIdx") node = offset;
            else if (bits[0] == "BlW") weight = offset;
            else if (bits[0].StartsWith("Tangent", StringComparison.Ordinal)) tangent = offset;
            else if (bits[0].StartsWith("Binormal", StringComparison.Ordinal)) binormal = offset;
            else if (bits[0].StartsWith("Normal", StringComparison.Ordinal)) normal = offset;
            offset += bits[^1] == "3f" ? 12 : 4;
        }
        if (offset != stride || position < 0) throw new InvalidDataException($"a vertex of {offset} bytes in a block of {stride}");

        var vertices = new ModelVertex[count];
        for (int v = 0; v < count; v++)
        {
            int at = v * stride;
            ref ModelVertex vertex = ref vertices[v];
            vertex.Position = new Vector3(BitConverter.ToSingle(bytes, at + position), BitConverter.ToSingle(bytes, at + position + 4), BitConverter.ToSingle(bytes, at + position + 8));
            if (texture >= 0)
                vertex.TexturePlace = new Vector2(BitConverter.ToUInt16(bytes, at + texture) / 65535f * 8, BitConverter.ToUInt16(bytes, at + texture + 2) / 65535f * 8);
            Vector3 along = tangent >= 0 ? Direction(bytes, at + tangent) : Vector3.UnitX;
            Vector3 across = binormal >= 0 ? Direction(bytes, at + binormal) : Vector3.UnitY;
            Vector3 facing = normal >= 0 ? Direction(bytes, at + normal) : Vector3.Cross(along, across);
            vertex.Tangent = ModelVertex.Pack(along);
            vertex.Binormal = ModelVertex.Pack(across);
            vertex.Normal = ModelVertex.Pack(facing.LengthSquared() > 0 ? Vector3.Normalize(facing) : Vector3.UnitZ);
            if (node >= 0)
            {
                vertex.Node = bytes[at + node];
                vertex.MoreNodes = bytes[at + node + 1] | (uint)bytes[at + node + 2] << 8 | (uint)bytes[at + node + 3] << 16;
            }
            if (weight >= 0) vertex.Weights = BitConverter.ToUInt32(bytes, at + weight);
        }
        return vertices;
    }

    /// <summary>A direction stored as three bytes (0 is -1, 255 is +1).</summary>
    private static Vector3 Direction(byte[] bytes, int at) =>
        new(bytes[at] / 255f * 2 - 1, bytes[at + 1] / 255f * 2 - 1, bytes[at + 2] / 255f * 2 - 1);

    /// <summary>
    /// A part's triangles: stored as differences from the vertex number before, 16 bits each (wrapping round past
    /// 65535) or 32.
    /// </summary>
    private int[] ReadIndexes(byte[] entry, int vertexCount)
    {
        int count = BitConverter.ToInt32(entry, 8);
        byte[] bytes = Unpack(header[31] + BitConverter.ToUInt32(entry, 0), BitConverter.ToInt32(entry, 4));
        int bytesEach = bytes.Length / Math.Max(1, count), running = 0;
        var indexes = new int[count];
        for (int i = 0; i < count; i++)
        {
            running += bytesEach == 4 ? BitConverter.ToInt32(bytes, i * 4) : BitConverter.ToInt16(bytes, i * 2);
            if (bytesEach == 2) running &= 0xFFFF;
            if (running < 0 || running >= vertexCount) throw new InvalidDataException($"a triangle names vertex {running} of {vertexCount}");
            indexes[i] = running;
        }
        return indexes;
    }

    /// <summary>
    /// A model's nodes (header words 38 and 41): how many, their names, their parents and three sets of matrices, of
    /// which the third is each node's place in the model's own space (3 rows of 4 numbers, the move in the last column).
    /// </summary>
    private List<ModelNode> ReadNodes(int nodesNumber)
    {
        byte[] entry = Read(header[38] + nodesNumber * 8L, 8);
        byte[] block = Read(header[41] + BitConverter.ToInt32(entry, 0), BitConverter.ToInt32(entry, 4));
        int count = BitConverter.ToInt32(block, 4), matrices = BitConverter.ToInt32(block, 8), parents = BitConverter.ToInt32(block, 12);
        int nameAt = BitConverter.ToInt32(block, 16);
        var nodes = new List<ModelNode>(count);
        for (int n = 0; n < count; n++)
        {
            int length = BitConverter.ToUInt16(block, 36 + n * 2);
            string name = Encoding.ASCII.GetString(block, nameAt, length);
            nameAt += length;
            int at = matrices + 2 * count * 48 + n * 48;
            float F(int row, int column) => BitConverter.ToSingle(block, at + (row * 4 + column) * 4);
            var world = new Matrix4x4(
                F(0, 0), F(1, 0), F(2, 0), 0,
                F(0, 1), F(1, 1), F(2, 1), 0,
                F(0, 2), F(1, 2), F(2, 2), 0,
                F(0, 3), F(1, 3), F(2, 3), 1);
            nodes.Add(new ModelNode(name, BitConverter.ToInt32(block, parents + n * 4), world));
        }
        return nodes;
    }

    /// <summary>Marks the parts that are track strips (see <see cref="ModelPart.IsTrackStrip"/>), and their vertices.</summary>
    private static void MarkTrackStrips(List<ModelPart> parts, List<ModelNode> nodes)
    {
        bool IsTrackNode(int node) => node < nodes.Count && (nodes[node].Name.StartsWith("roue_elev", StringComparison.Ordinal) || nodes[node].Name.StartsWith("chenille", StringComparison.Ordinal));
        for (int p = 0; p < parts.Count; p++)
        {
            ModelPart part = parts[p];
            if (part.Vertices.Length == 0 || !part.Vertices.All(vertex => IsTrackNode(vertex.Node))) continue;
            for (int v = 0; v < part.Vertices.Length; v++) part.Vertices[v].IsTrack = 1;
            parts[p] = part with { IsTrackStrip = true };
        }
    }
}
