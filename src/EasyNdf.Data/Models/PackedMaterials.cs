using System.Text;

namespace EasyNdf.Data.Models;

/// <summary>
/// The mesh pack's materials (header words 19 and 20): a small binary NDF file packed with Zstandard after a 44-byte
/// head, with sections of strings (STRG), class names (CLAS), property names (PROP) and objects (OBJE). The first object
/// lists the materials; each material's TextureDico names its textures.
/// </summary>
internal sealed class PackedMaterials
{
    private readonly List<Dictionary<string, object>> objects = [];
    private readonly List<object> materialList;
    private readonly Dictionary<int, ModelMaterial> made = [];

    /// <summary>Reads the materials from their packed bytes.</summary>
    public PackedMaterials(byte[] packed)
    {
        byte[] data;
        using (var zstd = new ZstdSharp.Decompressor())
            data = zstd.Unwrap(packed.AsSpan(44), BitConverter.ToInt32(packed, 40)).ToArray();
        using var memory = new MemoryStream(data);
        using var reader = new BinaryReader(memory);
        // the sections' table, at a place counted from the start of the packed file (the 40 bytes before data)
        memory.Position = BitConverter.ToInt64(packed, 16) - 40;
        reader.ReadUInt32();
        var sections = new Dictionary<string, (int Start, int Length)>();
        int sectionCount = reader.ReadInt32();
        for (int i = 0; i < sectionCount; i++)
        {
            string key = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0');
            int start = (int)reader.ReadInt64() - 40, length = (int)reader.ReadInt64();
            sections[key] = (start, length);
        }
        List<string> strings = Strings(reader, sections["STRG"], withNumber: false);
        List<string> properties = Strings(reader, sections["PROP"], withNumber: true);
        ReadObjects(reader, sections["OBJE"], strings, properties);
        materialList = objects.Count > 0 && objects[0].TryGetValue("Materials", out object? list) && list is List<object> materials ? materials : [];
    }

    /// <summary>A material by its number in the pack; one with no textures when the pack has none of that number.</summary>
    public ModelMaterial Get(int number)
    {
        if (made.TryGetValue(number, out ModelMaterial? known)) return known;
        ModelMaterial material = new(null, null);
        if (number < materialList.Count && materialList[number] is int objectNumber && objectNumber < objects.Count
            && objects[objectNumber].TryGetValue("TextureDico", out object? dico) && dico is Dictionary<string, object> textures)
            material = new ModelMaterial(textures.GetValueOrDefault("DiffuseTextureNoAlpha") as string, textures.GetValueOrDefault("NormalTexture") as string);
        made[number] = material;
        return material;
    }

    /// <summary>A section's strings: each a length, then its bytes; property names also have a number after each.</summary>
    private static List<string> Strings(BinaryReader reader, (int Start, int Length) section, bool withNumber)
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

    /// <summary>
    /// The objects: each its class's number, then its properties (a property's number and its value) up to the marker
    /// 0xABABABAB. A value of a kind not read here ends the reading: the materials come first.
    /// </summary>
    private void ReadObjects(BinaryReader reader, (int Start, int Length) section, List<string> strings, List<string> properties)
    {
        reader.BaseStream.Position = section.Start;
        try
        {
            while (reader.BaseStream.Position < section.Start + section.Length)
            {
                reader.ReadInt32();
                var fields = new Dictionary<string, object>(StringComparer.Ordinal);
                while (true)
                {
                    uint property = reader.ReadUInt32();
                    if (property == 0xABABABAB) break;
                    fields[properties[(int)property]] = Value(reader, reader.ReadInt32(), strings);
                }
                objects.Add(fields);
            }
        }
        catch (Exception stop) when (stop is InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            // a kind of value not read here: the objects read so far hold the materials
        }
    }

    /// <summary>One value: a string (kinds 7 and 0x1C), a reference to an object (9), a list (0x11) or a map (0x12).</summary>
    private static object Value(BinaryReader reader, int kind, List<string> strings)
    {
        switch (kind)
        {
            case 7:
            case 0x1C:
                return strings[reader.ReadInt32()];
            case 9:
                {
                    reader.ReadUInt32();
                    int objectNumber = reader.ReadInt32();
                    reader.ReadInt32();
                    return objectNumber;
                }
            case 0x11:
                {
                    int count = reader.ReadInt32();
                    var list = new List<object>(count);
                    for (int i = 0; i < count; i++) list.Add(Value(reader, reader.ReadInt32(), strings));
                    return list;
                }
            case 0x12:
                {
                    int count = reader.ReadInt32();
                    var map = new Dictionary<string, object>(StringComparer.Ordinal);
                    for (int i = 0; i < count; i++)
                    {
                        string key = (string)Value(reader, reader.ReadInt32(), strings);
                        map[key] = Value(reader, reader.ReadInt32(), strings);
                    }
                    return map;
                }
            default:
                throw new InvalidDataException("a material value of kind " + kind);
        }
    }
}
