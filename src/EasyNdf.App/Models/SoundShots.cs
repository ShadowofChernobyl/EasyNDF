using System.Runtime.CompilerServices;
using EasyNdf.Data.Models;

namespace EasyNdf.App.Models;

/// <summary>
/// The shots heard in a recording of a burst of fire: when the first sounds, how far apart they come, and how many. A
/// machine gun's recording runs on well after its last shot (a .50 cal's 2.6 seconds hold six shots in the first 0.8,
/// then their echo), so its muzzle flashes follow the shots heard, not the recording's length.
/// </summary>
public static class SoundShots
{
    /// <summary>A recording's shots: the first's time, the time between them, and how many, in the recording's own seconds.</summary>
    public sealed record Shots(double First, double Between, int Count);

    // Tuned for speed: a recording is gone through once, the first time it is fired
    private static readonly ConditionalWeakTable<GameSound, Shots> found = new();

    /// <summary>The shots of a recording; null when fewer than two can be told apart.</summary>
    public static Shots? Of(GameSound sound)
    {
        if (found.TryGetValue(sound, out Shots? known)) return known.Count >= 2 ? known : null;
        Shots shots = Find(sound);
        found.AddOrUpdate(sound, shots);
        return shots.Count >= 2 ? shots : null;
    }

    /// <summary>
    /// Goes through the recording 5 ms at a time: a shot is a step up to at least a quarter of the loudest, twice as
    /// loud as the 30 ms before it, and 40 ms or more after the last; the first is where it first gets that loud.
    /// </summary>
    private static Shots Find(GameSound sound)
    {
        int channels = Math.Max(1, sound.Channels), frames = sound.Samples.Length / channels, window = Math.Max(1, sound.Rate / 200);
        var level = new List<double>();
        for (int at = 0; at + window <= frames; at += window)
        {
            double sum = 0;
            for (int i = at; i < at + window; i++)
            {
                double value = sound.Samples[i * channels] / 32768.0;
                sum += value * value;
            }
            level.Add(Math.Sqrt(sum / window));
        }
        if (level.Count == 0) return new Shots(0, 0, 0);
        double loudest = level.Max();
        const double Step = 0.005;
        var heard = new List<double>();
        int first = level.FindIndex(each => each >= 0.25 * loudest);
        if (first >= 0) heard.Add(first * Step);
        for (int k = Math.Max(6, first + 1); k < level.Count; k++)
        {
            double before = 0;
            for (int j = k - 6; j < k; j++) before += level[j];
            before /= 6;
            if (level[k] >= 0.25 * loudest && level[k] >= 2 * before && k * Step - heard[^1] >= 0.04) heard.Add(k * Step);
        }
        return heard.Count < 2 ? new Shots(heard.FirstOrDefault(), 0, heard.Count) : new Shots(heard[0], (heard[^1] - heard[0]) / (heard.Count - 1), heard.Count);
    }
}
