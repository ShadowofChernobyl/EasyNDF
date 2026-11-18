using EasyNdf.Data.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EasyNdf.App.Models;

/// <summary>
/// Plays the 3D model viewer's sounds through the computer's speakers: the game's own sounds, unpacked when first
/// played and kept while the viewer is open, mixed together so shots, an engine and a turret can sound at once.
/// </summary>
internal sealed class ModelSounds : IDisposable
{
    internal const int OutputRate = 48000;
    private readonly WasapiOut? output;
    private readonly MixingSampleProvider mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(OutputRate, 2)) { ReadFully = true };
    private readonly VolumeSampleProvider master;
    private readonly Func<string, GameSound?> load;
    private readonly Dictionary<string, GameSound?> unpacked = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PlayingSound> playing = [];

    /// <summary>Gets ready to play; quiet, with no error, when the computer has no sound output.</summary>
    /// <param name="load">Unpacks a sound by its path in the archives; null when it cannot be.</param>
    public ModelSounds(Func<string, GameSound?> load)
    {
        this.load = load;
        master = new VolumeSampleProvider(mixer);
        try
        {
            output = new WasapiOut(AudioClientShareMode.Shared, 60);
            output.Init(master);
            output.Play();
        }
        catch (Exception missing) when (missing is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException)
        {
            output = null;
        }
    }

    /// <summary>Whether sounds are heard: the viewer's Sound switch. Off silences what is playing too.</summary>
    public bool IsOn { get; set; } = true;

    /// <summary>How loud everything plays, 0 (silent) to 1 (the game's own levels): the viewer's volume slider.</summary>
    public float Loudness
    {
        get => master.Volume;
        set => master.Volume = Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// Plays one of a sound's files, picked at random as the game picks, at its volume (the game's 2 is full) and with
    /// its pitch varied as its sound settings say; null when the sound is off or cannot be played.
    /// </summary>
    /// <param name="choice">The sound.</param>
    /// <param name="loop">Whether it loops until faded out.</param>
    /// <param name="fadeIn">How long it takes to rise to its volume, in seconds.</param>
    /// <param name="delay">How long to wait before it starts, in seconds.</param>
    public PlayingSound? Play(SoundChoice? choice, bool loop = false, double fadeIn = 0, double delay = 0)
    {
        if (!IsOn || output is null || choice is null || choice.Files.Count == 0) return null;
        string file = choice.Files[Random.Shared.Next(choice.Files.Count)];
        GameSound? sound = Unpacked(file);
        if (sound is null) return null;
        // RandomPitch read as the whole spread of the pitch, half above and half below
        double pitch = choice.Pitch * (1 + (Random.Shared.NextDouble() - 0.5) * choice.RandomPitch);
        var played = new PlayingSound(sound, (float)(choice.Volume / 2), pitch * sound.Rate / OutputRate, loop, fadeIn, (int)(delay * OutputRate));
        lock (playing)
        {
            playing.RemoveAll(each => each.IsFinished);
            playing.Add(played);
        }
        mixer.AddMixerInput(played);
        return played;
    }

    /// <summary>How long a sound playing plays once through, in seconds.</summary>
    public static double Seconds(PlayingSound sound) => sound.Samples / OutputRate;

    /// <summary>Unpacks the sounds a unit uses ahead of time, so the first shot is not late.</summary>
    public void Prepare(IEnumerable<SoundChoice?> choices)
    {
        foreach (SoundChoice? choice in choices)
            foreach (string file in choice?.Files ?? []) Unpacked(file);
    }

    private GameSound? Unpacked(string file)
    {
        lock (unpacked)
        {
            if (!unpacked.TryGetValue(file, out GameSound? sound)) unpacked[file] = sound = load(file);
            return sound;
        }
    }

    /// <summary>How much the sounds unpacked so far take, in bytes.</summary>
    public long HeldBytes
    {
        get
        {
            lock (unpacked) return unpacked.Values.Sum(sound => (sound?.Samples.Length ?? 0) * 2L);
        }
    }

    /// <summary>Fades out everything playing, quickly.</summary>
    public void Silence()
    {
        lock (playing)
        {
            foreach (PlayingSound each in playing) each.FadeOut(0.08);
            playing.Clear();
        }
    }

    /// <summary>Stops the output and lets go of the sounds.</summary>
    public void Dispose()
    {
        output?.Stop();
        output?.Dispose();
        lock (unpacked) unpacked.Clear();
    }
}

/// <summary>
/// One sound playing: its samples stepped through at its own rate and pitch, the same on both speakers, with its volume
/// rising at the start and falling when faded out. Its volume changes from the screen's thread while the speakers'
/// thread reads it.
/// </summary>
internal sealed class PlayingSound : ISampleProvider
{
    /// <summary>How long it plays once through, in samples of the output (see <see cref="ModelSounds.Seconds"/>).</summary>
    public double Samples { get; }

    /// <summary>The recording it plays.</summary>
    public GameSound Sound => sound;

    /// <summary>How much faster than recorded it plays (its pitch): its own times divided by this are the times heard.</summary>
    public double Speed => step * ModelSounds.OutputRate / Math.Max(1, sound.Rate);

    private readonly GameSound sound;
    private readonly float volume;
    private readonly double step;
    private readonly bool loop;
    private double position;
    private int waitLeft;
    private float level;
    private volatile float levelStep;
    private volatile bool finishing;
    private volatile bool finished;

    /// <summary>A sound ready to play.</summary>
    /// <param name="sound">The sound.</param>
    /// <param name="volume">Its volume, 1 for full.</param>
    /// <param name="step">How far through it each sample of the output moves, in its own frames.</param>
    /// <param name="loop">Whether it loops.</param>
    /// <param name="fadeIn">How long it takes to rise, in seconds.</param>
    /// <param name="wait">How many samples of the output to wait before starting.</param>
    public PlayingSound(GameSound sound, float volume, double step, bool loop, double fadeIn, int wait)
    {
        // how long it plays once, in output samples, at its pitch
        Samples = sound.Samples.Length / Math.Max(1, sound.Channels) / Math.Max(1e-6, step);
        this.sound = sound;
        this.volume = volume;
        this.step = step;
        this.loop = loop;
        waitLeft = wait;
        level = fadeIn > 0 ? 0 : 1;
        levelStep = fadeIn > 0 ? (float)(1 / (fadeIn * 48000)) : 0;
    }

    /// <inheritdoc/>
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    /// <summary>Whether it has ended, or faded out.</summary>
    public bool IsFinished => finished;

    /// <summary>Lowers it to nothing over this many seconds, then ends it.</summary>
    public void FadeOut(double seconds)
    {
        levelStep = -(float)(1 / (Math.Max(0.01, seconds) * 48000));
        finishing = true;
    }

    /// <inheritdoc/>
    public int Read(float[] buffer, int offset, int count)
    {
        if (finished) return 0;
        int frames = sound.Frames, channels = sound.Channels;
        int loopStart = Math.Clamp(sound.LoopStart, 0, Math.Max(0, frames - 1)), loopEnd = sound.LoopEnd > loopStart && sound.LoopEnd <= frames ? sound.LoopEnd : frames;
        int written = 0;
        while (written + 1 < count)
        {
            if (waitLeft > 0)
            {
                waitLeft--;
                buffer[offset + written++] = 0;
                buffer[offset + written++] = 0;
                continue;
            }
            int at = (int)position;
            if (at >= (loop ? loopEnd : frames) - 1)
            {
                if (!loop) break;
                position = loopStart + (position - (loopEnd - 1));
                at = (int)position;
            }
            // between two of its frames, mixed in proportion; both channels of a stereo sound mixed for each speaker
            double part = position - at;
            float Frame(int frame)
            {
                float sum = 0;
                for (int channel = 0; channel < channels; channel++) sum += sound.Samples[frame * channels + channel];
                return sum / channels / 32768f;
            }
            float value = (float)(Frame(at) * (1 - part) + Frame(Math.Min(at + 1, frames - 1)) * part);
            level = Math.Clamp(level + levelStep, 0, 1);
            if (finishing && level <= 0) break;
            float sample = value * volume * level;
            buffer[offset + written++] = sample;
            buffer[offset + written++] = sample;
            position += step;
        }
        if (written < count && (!loop || finishing)) finished = true;
        return written;
    }
}
