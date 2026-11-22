using System.IO.Compression;
using System.Text;

namespace EasyNdf.Data.Models.Fbx;

/// <summary>
/// One record of an FBX file: its name ("Objects", "Model", "P"...), its values, and the records inside it. A value is
/// a short, bool, int, float, double, long, string, raw bytes (byte[]) or an array (short[] is not used; arrays are
/// float[], double[], long[], int[], bool[]).
/// </summary>
public sealed class FbxNode(string name, List<object> values, List<FbxNode> children)
{
    public string Name { get; set; } = name;
    public List<object> Values { get; } = values;
    public List<FbxNode> Children { get; } = children;

    public FbxNode(string name, params object[] values) : this(name, [.. values], []) { }

    /// <summary>The first record inside of this name; null when there is none.</summary>
    public FbxNode? Child(string name) => Children.FirstOrDefault(child => child.Name == name);

    /// <summary>The records inside of this name.</summary>
    public IEnumerable<FbxNode> All(string name) => Children.Where(child => child.Name == name);

    /// <summary>A value as a long, for ids (a long) and indexes (an int).</summary>
    public long Long(int index) => Values[index] switch { long l => l, int i => i, short s => s, _ => 0 };

    /// <summary>A value as text; "" when it is not text.</summary>
    public string Text(int index) => index < Values.Count && Values[index] is string text ? text : "";

    /// <summary>A value as a number; 0 when it is not one.</summary>
    public double Number(int index) => index >= Values.Count ? 0 : Values[index] switch
    {
        double d => d, float f => f, int i => i, long l => l, short s => s, bool b => b ? 1 : 0, _ => 0,
    };

    /// <summary>The first value as an array of doubles, whatever kind of number array it is; empty when it is none.</summary>
    public double[] Doubles() => Values.Count == 0 ? [] : Values[0] switch
    {
        double[] d => d, float[] f => [.. f.Select(x => (double)x)], int[] i => [.. i.Select(x => (double)x)], long[] l => [.. l.Select(x => (double)x)], _ => [],
    };

    /// <summary>The first value as an array of ints; empty when it is none.</summary>
    public int[] Ints() => Values.Count == 0 ? [] : Values[0] switch
    {
        int[] i => i, long[] l => [.. l.Select(x => (int)x)], _ => [],
    };
}

/// <summary>
/// A binary FBX file, as Blender, 3ds Max and Maya write it: its version and its top records. Read whole into records
/// and written back the same way, so a copy can be changed (a part renamed, a point added, the model scaled) and saved.
/// Text (ASCII) FBX files are not read.
/// </summary>
public sealed class FbxFile
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0");

    /// <summary>The file's version, such as 7400 for FBX 2014 (Blender's).</summary>
    public int Version { get; set; }

    /// <summary>Its top records: FBXHeaderExtension, GlobalSettings, Definitions, Objects, Connections...</summary>
    public List<FbxNode> Nodes { get; } = [];

    /// <summary>The first top record of this name; null when there is none.</summary>
    public FbxNode? Node(string name) => Nodes.FirstOrDefault(node => node.Name == name);

    /// <summary>Whether bytes start as a text FBX file does (one the app does not read).</summary>
    public static bool IsText(ReadOnlySpan<byte> start) =>
        start.Length >= 2 && !start.StartsWith(Magic.AsSpan(0, 18)) && Encoding.ASCII.GetString(start[..Math.Min(start.Length, 200)]).Contains("FBX", StringComparison.Ordinal);

    /// <summary>Reads a binary FBX file.</summary>
    /// <exception cref="InvalidDataException">It is not a binary FBX file, or it is cut short.</exception>
    public static FbxFile Read(byte[] data)
    {
        if (data.Length < 27 || !data.AsSpan(0, 21).SequenceEqual(Magic))
            throw new InvalidDataException(IsText(data) ? "a text FBX file; save it as binary FBX" : "not an FBX file");
        var file = new FbxFile { Version = BitConverter.ToInt32(data, 23) };
        bool wide = file.Version >= 7500;
        int pos = 27;
        try
        {
            while (pos < data.Length)
            {
                FbxNode? node = ReadNode(data, ref pos, wide);
                if (node is null) break;
                file.Nodes.Add(node);
            }
        }
        catch (Exception problem) when (problem is ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            throw new InvalidDataException("the FBX file is damaged or cut short", problem);
        }
        return file;
    }

    private static FbxNode? ReadNode(byte[] d, ref int pos, bool wide)
    {
        long end, count;
        if (wide)
        {
            end = BitConverter.ToInt64(d, pos);
            count = BitConverter.ToInt64(d, pos + 8);
            pos += 24;
        }
        else
        {
            end = BitConverter.ToUInt32(d, pos);
            count = BitConverter.ToUInt32(d, pos + 4);
            pos += 12;
        }
        int nameLength = d[pos++];
        if (end == 0) return null;
        if (end > d.Length) throw new InvalidDataException("record past the end");
        string name = Encoding.ASCII.GetString(d, pos, nameLength);
        pos += nameLength;
        var values = new List<object>((int)Math.Min(count, 64));
        for (long i = 0; i < count; i++) values.Add(ReadValue(d, ref pos));
        var children = new List<FbxNode>();
        while (pos < end)
        {
            FbxNode? child = ReadNode(d, ref pos, wide);
            if (child is null) break;
            children.Add(child);
        }
        pos = (int)end;
        return new FbxNode(name, values, children);
    }

    private static object ReadValue(byte[] d, ref int pos)
    {
        char type = (char)d[pos++];
        switch (type)
        {
            case 'Y': pos += 2; return BitConverter.ToInt16(d, pos - 2);
            case 'C': pos += 1; return d[pos - 1] != 0;
            case 'I': pos += 4; return BitConverter.ToInt32(d, pos - 4);
            case 'F': pos += 4; return BitConverter.ToSingle(d, pos - 4);
            case 'D': pos += 8; return BitConverter.ToDouble(d, pos - 8);
            case 'L': pos += 8; return BitConverter.ToInt64(d, pos - 8);
            case 'S':
            case 'R':
            {
                int length = BitConverter.ToInt32(d, pos);
                pos += 4;
                byte[] bytes = d.AsSpan(pos, length).ToArray();
                pos += length;
                return type == 'S' ? Encoding.UTF8.GetString(bytes) : bytes;
            }
            case 'f': case 'd': case 'l': case 'i': case 'b': case 'c':
            {
                int length = BitConverter.ToInt32(d, pos), encoding = BitConverter.ToInt32(d, pos + 4), stored = BitConverter.ToInt32(d, pos + 8);
                pos += 12;
                byte[] raw = d.AsSpan(pos, stored).ToArray();
                pos += stored;
                if (encoding == 1)
                {
                    using var inflate = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
                    using var plain = new MemoryStream();
                    inflate.CopyTo(plain);
                    raw = plain.ToArray();
                }
                return type switch
                {
                    'f' => Cast<float>(raw, length),
                    'd' => Cast<double>(raw, length),
                    'l' => Cast<long>(raw, length),
                    'i' => Cast<int>(raw, length),
                    _ => raw.Take(length).Select(b => b != 0).ToArray(),
                };
            }
            default: throw new InvalidDataException("unknown value type '" + type + "'");
        }
    }

    private static T[] Cast<T>(byte[] raw, int length) where T : struct
    {
        var values = new T[length];
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, T>(raw.AsSpan(0, Math.Min(raw.Length, length * System.Runtime.InteropServices.Marshal.SizeOf<T>()))).CopyTo(values);
        return values;
    }

    // ---- writing ----

    /// <summary>The file's bytes, in its own version's layout; arrays of more than 64 values are compressed, as the FBX SDK does.</summary>
    public byte[] Write()
    {
        bool wide = Version >= 7500;
        using var out_ = new MemoryStream();
        var w = new BinaryWriter(out_);
        w.Write(Magic);
        w.Write((byte)0x1A);
        w.Write((byte)0x00);
        w.Write(Version);
        foreach (FbxNode node in Nodes) WriteNode(w, node, wide);
        WriteEnd(w, wide);
        // the footer: an id, padding to 16 bytes, the version, 120 zeros and the closing bytes, as the SDK writes it
        w.Write(new byte[] { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e });
        w.Write(new byte[4]);
        int pad = (int)((16 - (out_.Position % 16)) % 16);
        if (pad == 0) pad = 16;
        w.Write(new byte[pad]);
        w.Write(Version);
        w.Write(new byte[120]);
        w.Write(new byte[] { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b });
        w.Flush();
        return out_.ToArray();
    }

    private static void WriteEnd(BinaryWriter w, bool wide) => w.Write(new byte[wide ? 25 : 13]);

    private static void WriteNode(BinaryWriter w, FbxNode node, bool wide)
    {
        long start = w.BaseStream.Position;
        // the header, filled in once the values' length and the end are known
        w.Write(new byte[wide ? 24 : 12]);
        byte[] name = Encoding.ASCII.GetBytes(node.Name);
        w.Write((byte)name.Length);
        w.Write(name);
        long valuesStart = w.BaseStream.Position;
        foreach (object value in node.Values) WriteValue(w, value);
        long valuesLength = w.BaseStream.Position - valuesStart;
        foreach (FbxNode child in node.Children) WriteNode(w, child, wide);
        // a record with records inside, or none and no values, ends with an empty record, as the SDK writes it
        if (node.Children.Count > 0 || node.Values.Count == 0) WriteEnd(w, wide);
        long end = w.BaseStream.Position;
        w.BaseStream.Position = start;
        if (wide)
        {
            w.Write(end);
            w.Write((long)node.Values.Count);
            w.Write(valuesLength);
        }
        else
        {
            w.Write((uint)end);
            w.Write((uint)node.Values.Count);
            w.Write((uint)valuesLength);
        }
        w.BaseStream.Position = end;
    }

    private static void WriteValue(BinaryWriter w, object value)
    {
        switch (value)
        {
            case short s: w.Write((byte)'Y'); w.Write(s); break;
            case bool b: w.Write((byte)'C'); w.Write((byte)(b ? 1 : 0)); break;
            case int i: w.Write((byte)'I'); w.Write(i); break;
            case float f: w.Write((byte)'F'); w.Write(f); break;
            case double d: w.Write((byte)'D'); w.Write(d); break;
            case long l: w.Write((byte)'L'); w.Write(l); break;
            case string s:
            {
                byte[] bytes = Encoding.UTF8.GetBytes(s);
                w.Write((byte)'S');
                w.Write(bytes.Length);
                w.Write(bytes);
                break;
            }
            case byte[] raw: w.Write((byte)'R'); w.Write(raw.Length); w.Write(raw); break;
            case float[] f: WriteArray(w, 'f', f.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(f.AsSpan()).ToArray()); break;
            case double[] d: WriteArray(w, 'd', d.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(d.AsSpan()).ToArray()); break;
            case long[] l: WriteArray(w, 'l', l.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(l.AsSpan()).ToArray()); break;
            case int[] i: WriteArray(w, 'i', i.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(i.AsSpan()).ToArray()); break;
            case bool[] b: WriteArray(w, 'b', b.Length, [.. b.Select(x => (byte)(x ? 1 : 0))]); break;
            default: throw new InvalidDataException("cannot write a value of type " + value.GetType().Name);
        }
    }

    private static void WriteArray(BinaryWriter w, char type, int length, byte[] raw)
    {
        w.Write((byte)type);
        w.Write(length);
        if (length > 64)
        {
            using var packed = new MemoryStream();
            using (var deflate = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(raw);
            byte[] bytes = packed.ToArray();
            w.Write(1);
            w.Write(bytes.Length);
            w.Write(bytes);
        }
        else
        {
            w.Write(0);
            w.Write(raw.Length);
            w.Write(raw);
        }
    }
}
