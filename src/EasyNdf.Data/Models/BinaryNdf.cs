using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace EasyNdf.Data.Models;

/// <summary>A reference from one object of a <see cref="BinaryNdf"/> file to another, by the other's place in the file.</summary>
public readonly record struct NdfReference(int Object);

/// <summary>One object of a <see cref="BinaryNdf"/> file: its class's name and its properties' values, by name.</summary>
/// <param name="Class">Its class, such as "TMemASEGeomObject".</param>
/// <param name="Fields">
/// Its properties' values: a bool, byte, short, ushort, int, uint, float, double, string, <see cref="NdfReference"/>,
/// <see cref="Vector3"/>, an int[3], a <see cref="Matrix4x4"/>, a colour's four bytes as a uint, a Guid, a list
/// (List&lt;object?&gt;) or a map (Dictionary&lt;string, object?&gt;, its keys written as text). A property the file leaves out
/// takes its class's default, which the reader does not know: the caller supplies it.
/// </param>
public sealed record NdfObject(string Class, IReadOnlyDictionary<string, object?> Fields)
{
    /// <summary>A property's value as <typeparamref name="T"/>; <paramref name="otherwise"/> when it is missing or of another kind.</summary>
    public T Get<T>(string name, T otherwise) => Fields.TryGetValue(name, out object? value) && value is T typed ? typed : otherwise;

    /// <summary>A property that is a list, as its items; none when it is missing.</summary>
    public IReadOnlyList<object?> List(string name) => Fields.TryGetValue(name, out object? value) && value is List<object?> list ? list : [];
}

/// <summary>
/// A binary NDF file as Eugen's tools write it, such as the .Ase2NdfBin models of Steel Division 2's Modding Assets: an
/// "EUG0" head, then a body (packed with zlib when the head's flags say so) whose table lists its sections: the objects
/// (OBJE), the class names (CLAS), the property names (PROP) and the strings (STRG). An object is its class's number,
/// then its properties, each a property number, a kind and a value, up to the marker 0xABABABAB.
/// </summary>
public sealed class BinaryNdf
{
    private const uint ObjectEnd = 0xABABABAB;

    /// <summary>The objects, in the file's order: a <see cref="NdfReference"/> names one by its place here.</summary>
    public IReadOnlyList<NdfObject> Objects { get; }

    private BinaryNdf(IReadOnlyList<NdfObject> objects) => Objects = objects;

    /// <summary>The object a reference names; null when the reference is not one or names no object.</summary>
    public NdfObject? At(object? reference) =>
        reference is NdfReference { Object: int place } && place >= 0 && place < Objects.Count ? Objects[place] : null;

    /// <summary>Reads a file's objects; throws <see cref="InvalidDataException"/> when it is not a binary NDF file this reads.</summary>
    public static BinaryNdf Read(byte[] file)
    {
        if (file.Length < 44 || Encoding.ASCII.GetString(file, 0, 4) != "EUG0" || Encoding.ASCII.GetString(file, 8, 4) != "CNDF")
            throw new InvalidDataException("not a binary NDF file");
        uint flags = BitConverter.ToUInt32(file, 12);
        long table = BitConverter.ToInt64(file, 16), head = BitConverter.ToInt64(file, 24);
        int bodySize = BitConverter.ToInt32(file, 40);
        byte[] body;
        if ((flags & 0x80) != 0)
        {
            // packed: the body's size, then zlib from byte 44
            body = new byte[bodySize];
            using var unpacking = new ZLibStream(new MemoryStream(file, 44, file.Length - 44), CompressionMode.Decompress);
            unpacking.ReadExactly(body);
        }
        else body = file[(int)head..];

        using var reader = new BinaryReader(new MemoryStream(body), Encoding.Latin1);
        // the sections' table: places counted from the start of the file, before its head
        reader.BaseStream.Position = table - head;
        reader.ReadUInt32();
        var sections = new Dictionary<string, (long Start, long Length)>(StringComparer.Ordinal);
        int sectionCount = reader.ReadInt32();
        for (int i = 0; i < sectionCount; i++)
        {
            string key = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0');
            long sectionStart = reader.ReadInt64() - head, sectionLength = reader.ReadInt64();
            sections[key] = (sectionStart, sectionLength);
        }
        foreach (string needed in new[] { "OBJE", "CLAS", "PROP", "STRG" })
            if (!sections.ContainsKey(needed)) throw new InvalidDataException("no " + needed + " section");
        List<string> strings = Names(reader, sections["STRG"], withClass: false);
        List<string> classes = Names(reader, sections["CLAS"], withClass: false);
        List<string> properties = Names(reader, sections["PROP"], withClass: true);

        var objects = new List<NdfObject>();
        (long start, long length) = sections["OBJE"];
        reader.BaseStream.Position = start;
        while (reader.BaseStream.Position < start + length)
        {
            int classNumber = reader.ReadInt32();
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            while (true)
            {
                uint property = reader.ReadUInt32();
                if (property == ObjectEnd) break;
                if (property >= properties.Count) throw new InvalidDataException("property " + property + " out of range");
                fields[properties[(int)property]] = Value(reader, reader.ReadInt32(), strings);
            }
            objects.Add(new NdfObject(classNumber >= 0 && classNumber < classes.Count ? classes[classNumber] : "", fields));
        }
        return new BinaryNdf(objects);
    }

    /// <summary>A section of names: each a length, then its bytes; property names also have their class's number after each.</summary>
    private static List<string> Names(BinaryReader reader, (long Start, long Length) section, bool withClass)
    {
        var names = new List<string>();
        reader.BaseStream.Position = section.Start;
        while (reader.BaseStream.Position < section.Start + section.Length)
        {
            names.Add(Encoding.Latin1.GetString(reader.ReadBytes(reader.ReadInt32())));
            if (withClass) reader.ReadInt32();
        }
        return names;
    }

    /// <summary>One value of a kind (see <see cref="NdfObject.Fields"/>); throws <see cref="InvalidDataException"/> for a kind not read here.</summary>
    private static object? Value(BinaryReader reader, int kind, List<string> strings)
    {
        switch (kind)
        {
            case 0x00: return reader.ReadByte() != 0;
            case 0x01: return reader.ReadByte();
            case 0x02: return reader.ReadInt32();
            case 0x03: return reader.ReadUInt32();
            case 0x05: return reader.ReadSingle();
            case 0x06: return reader.ReadDouble();
            case 0x07:
            case 0x1C:
                {
                    int place = reader.ReadInt32();
                    return place >= 0 && place < strings.Count ? strings[place] : null;
                }
            case 0x08: return Encoding.Unicode.GetString(reader.ReadBytes(reader.ReadInt32()));
            case 0x09:
                {
                    reader.ReadUInt32();
                    int place = reader.ReadInt32();
                    reader.ReadInt32();
                    return new NdfReference(place);
                }
            case 0x0B: return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            case 0x0C: return reader.ReadUInt32();
            case 0x0E: return new[] { reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() };
            case 0x0F:
                return new Matrix4x4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            case 0x11:
                {
                    int count = reader.ReadInt32();
                    var list = new List<object?>(count);
                    for (int i = 0; i < count; i++) list.Add(Value(reader, reader.ReadInt32(), strings));
                    return list;
                }
            case 0x12:
                {
                    int count = reader.ReadInt32();
                    var map = new Dictionary<string, object?>(count, StringComparer.Ordinal);
                    for (int i = 0; i < count; i++)
                    {
                        string key = Value(reader, reader.ReadInt32(), strings)?.ToString() ?? "";
                        map[key] = Value(reader, reader.ReadInt32(), strings);
                    }
                    return map;
                }
            case 0x18: return reader.ReadInt16();
            case 0x19: return reader.ReadUInt16();
            case 0x1A: return new Guid(reader.ReadBytes(16));
            default: throw new InvalidDataException("a value of kind 0x" + kind.ToString("X"));
        }
    }
}
