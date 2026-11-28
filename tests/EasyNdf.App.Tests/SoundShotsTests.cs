using EasyNdf.App.Models;
using EasyNdf.Data.Models;

namespace EasyNdf.App.Tests;

/// <summary>A recording of a burst of fire: the shots heard in it, not its length, set its muzzle flashes.</summary>
public class SoundShotsTests
{
    [Fact]
    public void The_shots_of_a_burst_are_found_and_its_echo_is_not_counted()
    {
        const int rate = 48000;
        var samples = new short[rate * 2];
        // six sharp shots 0.125 s apart from 0.1 s, each fading over 30 ms, then a long quiet echo
        for (int shot = 0; shot < 6; shot++)
        {
            int at = (int)((0.1 + shot * 0.125) * rate);
            for (int i = 0; i < rate * 3 / 100; i++) samples[at + i] = (short)(20000 * (1 - i / (rate * 0.03)) * (i % 2 == 0 ? 1 : -1));
        }
        for (int i = (int)(0.9 * rate); i < 2 * rate; i++) samples[i] = (short)(1500 * (i % 2 == 0 ? 1 : -1));

        SoundShots.Shots shots = SoundShots.Of(new GameSound(rate, 1, samples, 0, 0))!;

        Assert.Equal(6, shots.Count);
        Assert.Equal(0.1, shots.First, 2);
        Assert.Equal(0.125, shots.Between, 2);
    }
}
