namespace EasyNdf.Data.Models;

/// <summary>A sound, unpacked: its samples (channels interleaved), how many a second, and where it loops.</summary>
/// <param name="Rate">Samples a second for each channel, such as 48000.</param>
/// <param name="Channels">1 for mono, 2 for stereo.</param>
/// <param name="Samples">The samples, the channels interleaved, as 16-bit numbers.</param>
/// <param name="LoopStart">Where a looping sound starts its loop, in frames (a frame is one sample of each channel).</param>
/// <param name="LoopEnd">Where it ends its loop, in frames.</param>
public sealed record GameSound(int Rate, int Channels, short[] Samples, int LoopStart, int LoopEnd)
{
    /// <summary>How many frames it has.</summary>
    public int Frames => Samples.Length / Math.Max(1, Channels);
}

/// <summary>
/// Eugen's packed sounds (.ess in the archives, which the game's NDF names as .wav): an "ESCR" head with the loop
/// points, then an "ESSH" part with the channels, the rate, the length and a table of blocks, each of 1024 samples
/// across the channels, packed with an adaptive run and bit coding and a predictor.
/// </summary>
/// <remarks>
/// The unpacking follows decode_ess.py of wgrd-cons-tools (github.com/ev1313/wgrd-cons-tools, GNU AGPL v3, the same
/// licence as EasyNDF), itself based on a write-up on zenhax.com; see THIRD-PARTY-NOTICES.txt. WARNO's files differ
/// from Wargame's only in their head. Some stereo sounds (music, menus) do not unpack yet: those throw.
/// </remarks>
public static class EssSound
{
    /// <summary>Unpacks a sound; throws <see cref="InvalidDataException"/> when it is not one, or cannot be unpacked.</summary>
    public static GameSound Decode(byte[] bytes)
    {
        if (bytes.Length < 32 || bytes[0] != 'E' || bytes[3] != 'R') throw new InvalidDataException("not an ESCR sound");
        int head = BigEndian(bytes, 4);
        if (head + 16 > bytes.Length || bytes[head] != 'E' || bytes[head + 3] != 'H') throw new InvalidDataException("no ESSH part");
        int channels = bytes[head + 5], rate = bytes[head + 6] << 8 | bytes[head + 7], frames = BigEndian(bytes, head + 8);
        if (channels is < 1 or > 2) throw new InvalidDataException("a sound of " + channels + " channels");
        int framesABlock = 1024 / channels, blocks = (frames + framesABlock - 1) / framesABlock, dataAt = head + 16 + 4 * blocks;
        if (blocks == 0 || dataAt + BigEndian(bytes, dataAt - 4) != bytes.Length) throw new InvalidDataException("the blocks' table does not match the file's length");
        var samples = new short[frames * channels];
        var work = new int[framesABlock];
        int left = frames, written = 0;
        for (int block = 0; block < blocks; block++)
        {
            int start = dataAt + (block == 0 ? 0 : BigEndian(bytes, head + 16 + (block - 1) * 4)), end = dataAt + BigEndian(bytes, head + 16 + block * 4);
            int count = Math.Min(left, framesABlock), bitsAt = start + 20 * channels, bit = 0;
            // each channel's predictor starts from 20 bytes of its own; the channels' bits follow one another
            for (int channel = 0; channel < channels; channel++)
                bit = DecodeChannel(bytes, start + channel * 20, bitsAt, end, bit, work, samples, written + channel, channels, count);
            written += count * channels;
            left -= count;
        }
        return new GameSound(rate, channels, samples, BitConverter.ToInt32(bytes, 24), BitConverter.ToInt32(bytes, 28));
    }

    private static int BigEndian(byte[] bytes, int at) => bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3];

    /// <summary>Division that rounds down, as Python's // does for negative numbers too.</summary>
    private static int FloorDivide(int value, int by) => value >= 0 ? value / by : -((-value + by - 1) / by);

    /// <summary>
    /// One channel of one block: first the values, each read from the gap since the last set bit (a run) and, for wide
    /// steps, two more bits; then the predictor turns them into samples. Gives back where the channel's bits ended.
    /// </summary>
    private static int DecodeChannel(byte[] bytes, int stateAt, int bitsAt, int end, int bit, int[] values, short[] samples, int to, int stride, int count)
    {
        short State(int index) => (short)(bytes[stateAt + index * 2] << 8 | bytes[stateAt + index * 2 + 1]);
        int lineGain = State(0), predictGain = State(1), last1 = State(2), last2 = State(3), predicted0 = State(4), predicted1 = State(5), predicted2 = State(6);
        int stepA = State(7), stepC = State(8), stepD = State(9);
        int Bit(int index)
        {
            int at = bitsAt + (index >> 3);
            if (at >= end) throw new InvalidDataException("a sound block runs past its end");
            return (bytes[at] >> (7 - (index & 7))) & 1;
        }
        int lastSet = bit - 1, made = 0;
        while (made < count)
        {
            if (Bit(bit) > 0)
            {
                int low = 0, run = bit - lastSet - 1, high;
                lastSet = bit;
                if (run >= 2)
                {
                    if (run >= 4)
                    {
                        if (run >= 6)
                        {
                            if (run == 24)
                            {
                                int extra = 0;
                                for (int k = 0; k < 16; k++)
                                {
                                    bit++;
                                    extra = extra * 2 + Bit(bit);
                                    lastSet++;
                                }
                                run = extra + 24;
                            }
                            low = stepA + stepC + (stepD + 1) * ((run >> 1) - 2);
                            high = stepD + low;
                            stepA += 6 * FloorDivide(stepA + 2048, 2048);
                            stepC += 6 * FloorDivide(stepC + 1024, 1024);
                            stepD += 6 * FloorDivide(stepD + 512, 512);
                        }
                        else
                        {
                            low = stepC + stepA;
                            high = stepD + low;
                            stepA += 6 * FloorDivide(stepA + 2048, 2048);
                            stepC += 6 * FloorDivide(stepC + 1024, 1024);
                            stepD -= 2 * FloorDivide(stepD + 510, 512);
                        }
                    }
                    else
                    {
                        low = stepA;
                        high = stepC + stepA;
                        stepA += 6 * FloorDivide(stepA + 2048, 2048);
                        stepC -= 2 * FloorDivide(stepC + 1022, 1024);
                    }
                }
                else
                {
                    high = stepA;
                    stepA -= 2 * FloorDivide(stepA + 2046, 2048);
                }
                int value = (high + low) >> 1;
                if (value > stepA)
                {
                    if (value > stepC)
                    {
                        if (value <= stepD) stepD -= 2 * FloorDivide(stepD + 510, 512);
                    }
                    else stepC -= 2 * FloorDivide(stepC + 1022, 1024);
                }
                else stepA -= 2 * FloorDivide(stepA + 2046, 2048);
                int span = high - low;
                if (span > 64)
                {
                    int step = FloorDivide(span, 4) + 1;
                    bit++;
                    int top = Bit(bit);
                    bit++;
                    value = low + step * (top * 2 + Bit(bit));
                    lastSet += 2;
                }
                if ((run & 1) > 0) value = ~value;
                values[made++] = value;
            }
            bit++;
        }
        for (int i = 0; i < count; i++)
        {
            int value = values[i], previous = last1, prediction = 2 * (2 * predicted0 - predicted2) - 5 * predicted1, line = 2 * last1 - last2, older = predicted1;
            predicted1 = predicted0;
            predicted0 = value + ((predictGain * prediction + 128) >> 8);
            if ((prediction | value) != 0) predictGain += (((prediction ^ value) & -536870913) | 0x40000000) >> 29;
            int sample = Math.Clamp(predicted0 + ((lineGain * line + 128) >> 8), -32768, 32767);
            if ((predicted0 | line) != 0) lineGain += (((predicted0 ^ line) & -536870913) | 0x40000000) >> 29;
            last2 = previous;
            samples[to + i * stride] = (short)sample;
            last1 = sample;
            predicted2 = older;
        }
        return bit;
    }
}
