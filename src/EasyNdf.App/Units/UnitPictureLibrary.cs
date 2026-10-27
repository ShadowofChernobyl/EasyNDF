using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// The pictures of the units shown on the Units tab, and their nations' flags, ready to draw. The first time a picture
/// is wanted it is taken out of the game's files (see <see cref="UnitPictureStore"/>), in the background; after that
/// it is read from the app's cache folder. The pictures drawn most recently are kept in memory, made only as big as
/// they are drawn, so scrolling back is instant without holding every picture.
/// </summary>
public sealed class UnitPictureLibrary(UnitPictureStore store, IReadOnlyDictionary<string, string> flagSources)
{
    /// <summary>
    /// How much memory the pictures kept may take: a few screens of cards. A picture takes 4 bytes a pixel, so a card's
    /// takes about 100 to 400 KB, by the screen's scaling.
    /// </summary>
    private const long BytesKept = 32L * 1024 * 1024;

    /// <summary>How much memory the pictures kept take now.</summary>
    private long bytesInUse;

    /// <summary>The pictures of the source the Units tab shows; null until one is read.</summary>
    public static UnitPictureLibrary? Current { get; set; }

    /// <summary>The pictures in memory, by their source and size; null for one the game does not have.</summary>
    private readonly Dictionary<(string Source, int Width), ImageSource?> kept = [];

    /// <summary>The order the kept pictures were last used in, oldest first, so the oldest is the one let go.</summary>
    private readonly LinkedList<(string Source, int Width)> lastUsed = [];

    /// <summary>The pictures asked for and not yet read, so two cards with the same picture read it once.</summary>
    private readonly Dictionary<(string Source, int Width), TaskCompletionSource<ImageSource?>> reading = [];

    /// <summary>
    /// The pictures waiting to be read, newest last. The newest is read first: while a list scrolls fast, the cards
    /// now on screen were asked for last, and those scrolled past can wait.
    /// </summary>
    private readonly List<(string Source, int Width)> waiting = [];

    /// <summary>How many pictures are being read at once, in the background.</summary>
    private int readers;

    /// <summary>At most two pictures are read at once, so reading them never keeps the computer busy.</summary>
    private const int MostReaders = 2;

    /// <summary>Whether pictures are still waiting to be read.</summary>
    internal bool IsBusy => readers > 0 || waiting.Count > 0;

    /// <summary>A nation's flag's source in the game, such as "GameData:/.../Flags/US.png"; null when the game has none.</summary>
    public string? FlagSourceOf(string nation) => flagSources.GetValueOrDefault(nation);

    /// <summary>A picture already in memory, so it can be drawn at once; false when it must be read first.</summary>
    public bool TryGetKept(string source, double width, out ImageSource? picture)
    {
        var key = (source, SizeFor(width));
        if (!kept.TryGetValue(key, out picture)) return false;
        MarkUsed(key);
        return true;
    }

    /// <summary>
    /// A picture, for drawing <paramref name="width"/> pixels wide: from memory, else read in the background. Null
    /// when the game has no such picture, or it cannot be read.
    /// </summary>
    public async Task<ImageSource?> GetAsync(string source, double width)
    {
        var key = (source, SizeFor(width));
        if (kept.TryGetValue(key, out ImageSource? picture))
        {
            MarkUsed(key);
            return picture;
        }
        if (reading.TryGetValue(key, out TaskCompletionSource<ImageSource?>? asked))
        {
            // asked for again: it moves to the front of the queue
            if (waiting.Remove(key)) waiting.Add(key);
        }
        else
        {
            asked = new TaskCompletionSource<ImageSource?>();
            reading[key] = asked;
            waiting.Add(key);
            if (readers < MostReaders) ReadWaiting();
        }
        return await asked.Task;
    }

    /// <summary>Reads the waiting pictures, newest first, until none are left; runs on the screen's thread between reads.</summary>
    private async void ReadWaiting()
    {
        readers++;
        int takenOutBefore = store.PicturesTakenOut;
        while (waiting.Count > 0)
        {
            (string Source, int Width) key = waiting[^1];
            waiting.RemoveAt(waiting.Count - 1);
            ImageSource? picture = await Task.Run(() => Read(key.Source, key.Width));
            Keep(key, picture);
            if (reading.Remove(key, out TaskCompletionSource<ImageSource?>? asked)) asked.SetResult(picture);
        }
        readers--;
        // taking pictures out of the game's textures leaves large blocks of memory behind; once the last reader is
        // done, they are given back, rather than left for .NET, which can let them pile up to gigabytes
        if (readers == 0 && store.PicturesTakenOut != takenOutBefore) Memory.GiveBack();
    }

    /// <summary>
    /// Takes the picture out of the game when needed, and makes it as wide as it is drawn, or its own width when that is
    /// smaller. Runs in the background, so
    /// the picture is frozen, to be drawn on the screen's thread.
    /// </summary>
    private ImageSource? Read(string source, int width)
    {
        try
        {
            // a card picture of the user's own, for a unit the mod adds, is a file on this computer
            string? file = Path.IsPathFullyQualified(source) ? source : store.FileFor(source);
            if (file is null) return null;
            byte[] bytes = File.ReadAllBytes(file);
            // never made wider than the file itself: stretched here and again on screen, it would blur twice
            int ownWidth = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0].PixelWidth;
            var picture = new BitmapImage();
            picture.BeginInit();
            // read now, so the file is not kept open
            picture.CacheOption = BitmapCacheOption.OnLoad;
            picture.StreamSource = new MemoryStream(bytes);
            if (width < ownWidth) picture.DecodePixelWidth = width;
            picture.EndInit();
            picture.Freeze();
            return picture;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            // a picture that cannot be read is drawn as its outline, like one the game does not have
            return null;
        }
    }

    /// <summary>Keeps a picture, letting go of the ones used longest ago while those kept take more than <see cref="BytesKept"/>.</summary>
    private void Keep((string, int) key, ImageSource? picture)
    {
        if (kept.ContainsKey(key)) return;
        kept[key] = picture;
        lastUsed.AddLast(key);
        bytesInUse += BytesOf(picture);
        while (bytesInUse > BytesKept && lastUsed.Count > 1)
        {
            (string, int) oldest = lastUsed.First!.Value;
            lastUsed.RemoveFirst();
            bytesInUse -= BytesOf(kept[oldest]);
            kept.Remove(oldest);
        }
    }

    /// <summary>The memory a picture takes: 4 bytes a pixel; none for a picture the game does not have.</summary>
    private static long BytesOf(ImageSource? picture) => picture is BitmapSource bitmap ? 4L * bitmap.PixelWidth * bitmap.PixelHeight : 0;

    private void MarkUsed((string, int) key)
    {
        lastUsed.Remove(key);
        lastUsed.AddLast(key);
    }

    /// <summary>
    /// The width a picture is made at: the next of a few sizes up from the width it is drawn at on this screen, so a
    /// card that is a little wider or narrower uses the picture already made.
    /// </summary>
    internal static int SizeFor(double width)
    {
        double onScreen = width * ScreenScale;
        foreach (int size in new[] { 48, 96, 160, 256, 384 })
            if (onScreen <= size) return size;
        return 512;
    }

    /// <summary>How many screen pixels Windows draws for each of the app's pixels (1.5 at 150% scaling).</summary>
    private static double ScreenScale =>
        System.Windows.Application.Current?.MainWindow is { } window ? VisualTreeHelper.GetDpi(window).DpiScaleX : 1;
}
