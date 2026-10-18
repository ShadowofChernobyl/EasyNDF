using System.Text;

namespace EasyNdf.Data.Game;

/// <summary>
/// The games' dictionaries (.dic files in the archives, "TRAD" inside): the text shown in the game, by a 64-bit key
/// made from a text key such as "HMNGALJGAU".
/// </summary>
public static class Dictionaries
{
    /// <summary>A dictionary's entries: each key, then where its UTF-16 text is and how many characters long.</summary>
    public static Dictionary<ulong, string> Read(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual("TRAD"u8)) throw new InvalidDataException("not a TRAD dictionary");
        int count = BitConverter.ToInt32(data, 4);
        var entries = new Dictionary<ulong, string>();
        for (int i = 0; i < count; i++)
        {
            int at = 8 + i * 16;
            if (at + 16 > data.Length) throw new InvalidDataException("a dictionary's table runs past its end");
            ulong key = BitConverter.ToUInt64(data, at);
            int textAt = BitConverter.ToInt32(data, at + 8);
            int length = BitConverter.ToInt32(data, at + 12);
            if (textAt < 0 || length < 0 || textAt + (long)length * 2 > data.Length) throw new InvalidDataException("a dictionary's text runs past its end");
            entries[key] = Encoding.Unicode.GetString(data, textAt, length * 2);
        }
        return entries;
    }

    /// <summary>
    /// The key of a text key such as "HMNGALJGAU": each character in 6 bits, digits 1 to 10 and letters 11 to 36, the
    /// first character highest. Null for a text that cannot be a key.
    /// </summary>
    public static ulong? KeyOf(string token)
    {
        if (token.Length == 0 || token.Length > 10) return null;
        ulong key = 0;
        foreach (char c in token.ToUpperInvariant())
        {
            int value = c is >= 'A' and <= 'Z' ? c - 'A' + 11 : c is >= '0' and <= '9' ? c - '0' + 1 : -1;
            if (value < 0) return null;
            key = (key << 6) | (uint)value;
        }
        return key;
    }
}
