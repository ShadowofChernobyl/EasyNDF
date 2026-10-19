namespace EasyNdf.Data.Game;

/// <summary>
/// Block-compressed textures (DXT1 and DXT5, also called BC1 and BC3): each 4×4 block of pixels is stored as two
/// colours and a 2-bit choice per pixel between them; DXT5 adds an alpha block before each colour block.
/// </summary>
public static class BlockTextures
{
    /// <summary>The picture's pixels in the order red, green, blue, alpha.</summary>
    public static byte[] Decode(byte[] blocks, int width, int height, bool hasAlphaBlock) =>
        DecodePart(blocks, width, height, hasAlphaBlock, 0, 0, width, height);

    /// <summary>
    /// The pixels of one part of the picture, in the order red, green, blue, alpha: only the blocks it covers are
    /// decoded, so a small picture cut from a large atlas costs little.
    /// </summary>
    /// <param name="blocks">The whole picture's blocks.</param>
    /// <param name="width">The whole picture's width.</param>
    /// <param name="height">The whole picture's height.</param>
    /// <param name="hasAlphaBlock">Whether each block has an alpha block first (DXT5).</param>
    /// <param name="left">The part's left edge.</param>
    /// <param name="top">The part's top edge.</param>
    /// <param name="partWidth">The part's width.</param>
    /// <param name="partHeight">The part's height.</param>
    public static byte[] DecodePart(byte[] blocks, int width, int height, bool hasAlphaBlock, int left, int top, int partWidth, int partHeight)
    {
        var rgba = new byte[partWidth * partHeight * 4];
        int blockBytes = hasAlphaBlock ? 16 : 8;
        int blocksAcross = Math.Max(1, (width + 3) / 4);
        int right = Math.Min(width, left + partWidth), bottom = Math.Min(height, top + partHeight);
        for (int blockY = top / 4; blockY * 4 < bottom; blockY++)
        {
            for (int blockX = left / 4; blockX * 4 < right; blockX++)
            {
                int at = (blockY * blocksAcross + blockX) * blockBytes;
                byte[] alphas = hasAlphaBlock ? AlphaBlock(blocks, at) : [];
                byte[][] colours = ColourBlock(blocks, hasAlphaBlock ? at + 8 : at, out uint choices, out bool lastIsClear);
                for (int pixel = 0; pixel < 16; pixel++)
                {
                    int x = blockX * 4 + pixel % 4, y = blockY * 4 + pixel / 4;
                    if (x < left || y < top || x >= right || y >= bottom) continue;
                    int choice = (int)(choices >> (pixel * 2)) & 3;
                    int to = ((y - top) * partWidth + (x - left)) * 4;
                    rgba[to] = colours[choice][0];
                    rgba[to + 1] = colours[choice][1];
                    rgba[to + 2] = colours[choice][2];
                    rgba[to + 3] = hasAlphaBlock ? alphas[pixel] : (byte)(lastIsClear && choice == 3 ? 0 : 255);
                }
            }
        }
        return rgba;
    }

    /// <summary>A colour block's four colours (red, green, blue) and its 2-bit choices.</summary>
    private static byte[][] ColourBlock(byte[] blocks, int at, out uint choices, out bool lastIsClear)
    {
        ushort first = BitConverter.ToUInt16(blocks, at);
        ushort second = BitConverter.ToUInt16(blocks, at + 2);
        choices = BitConverter.ToUInt32(blocks, at + 4);
        byte[] a = FromRgb565(first), b = FromRgb565(second);
        // when the first colour is not the larger, the block has three colours and a clear one
        lastIsClear = first <= second;
        byte[] third = first > second ? Mix(a, b, 2, 1) : Mix(a, b, 1, 1);
        byte[] fourth = first > second ? Mix(a, b, 1, 2) : [0, 0, 0];
        return [a, b, third, fourth];
    }

    /// <summary>A DXT5 alpha block's 16 alpha values: two end values and a 3-bit choice per pixel.</summary>
    private static byte[] AlphaBlock(byte[] blocks, int at)
    {
        byte first = blocks[at], second = blocks[at + 1];
        var values = new int[8];
        values[0] = first;
        values[1] = second;
        if (first > second)
            for (int i = 1; i < 7; i++) values[i + 1] = ((7 - i) * first + i * second) / 7;
        else
        {
            for (int i = 1; i < 5; i++) values[i + 1] = ((5 - i) * first + i * second) / 5;
            values[6] = 0;
            values[7] = 255;
        }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)blocks[at + 2 + i] << (8 * i);
        var alphas = new byte[16];
        for (int pixel = 0; pixel < 16; pixel++) alphas[pixel] = (byte)values[(int)(bits >> (pixel * 3)) & 7];
        return alphas;
    }

    /// <summary>A 16-bit colour (5 bits red, 6 green, 5 blue) as 8-bit red, green, blue.</summary>
    private static byte[] FromRgb565(ushort colour)
    {
        int red = colour >> 11 & 31, green = colour >> 5 & 63, blue = colour & 31;
        return [(byte)(red * 255 / 31), (byte)(green * 255 / 63), (byte)(blue * 255 / 31)];
    }

    /// <summary>Two colours mixed in the given proportions.</summary>
    private static byte[] Mix(byte[] a, byte[] b, int partsOfA, int partsOfB)
    {
        int total = partsOfA + partsOfB;
        return [(byte)((a[0] * partsOfA + b[0] * partsOfB) / total), (byte)((a[1] * partsOfA + b[1] * partsOfB) / total), (byte)((a[2] * partsOfA + b[2] * partsOfB) / total)];
    }
}
