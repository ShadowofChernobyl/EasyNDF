using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// A division's emblem, read from the game in the background. Until it is read, and when it cannot be, it shows
/// nothing: in a list it keeps its room, so the names stay in line; elsewhere it takes none.
/// </summary>
public sealed class DivisionEmblem : Image
{
    /// <summary>Builds the emblem and starts reading its picture.</summary>
    /// <param name="source">The emblem's picture, as the game names it; null for none.</param>
    /// <param name="size">How wide and high it is drawn.</param>
    /// <param name="keepsRoom">Whether it keeps its room while it shows nothing, as in the list of divisions.</param>
    /// <param name="file">A picture of the user's own on this computer, shown instead of the game's; null for none.</param>
    public DivisionEmblem(string? source, double size, bool keepsRoom, string? file = null)
    {
        Width = size;
        Height = size;
        Stretch = Stretch.Uniform;
        Visibility = keepsRoom ? Visibility.Hidden : Visibility.Collapsed;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        if (file is not null) ReadFile(file, size);
        else if (source is not null && UnitPictureLibrary.Current is { } library) Read(library, source, size);
    }

    /// <summary>Shows a picture of the user's own; nothing when it cannot be read (moved, or not a picture).</summary>
    private void ReadFile(string file, double size)
    {
        try
        {
            var picture = new System.Windows.Media.Imaging.BitmapImage();
            picture.BeginInit();
            picture.UriSource = new Uri(file);
            picture.DecodePixelWidth = (int)Math.Ceiling(size * 2);
            // read at once, so the file is not kept open
            picture.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            picture.EndInit();
            picture.Freeze();
            Source = picture;
            Visibility = Visibility.Visible;
        }
        catch (Exception problem) when (problem is System.IO.IOException or NotSupportedException or UriFormatException or UnauthorizedAccessException)
        {
            // left empty, as for a division with no emblem
        }
    }

    /// <summary>Shows the picture once it is read; it stays empty when the picture cannot be read.</summary>
    private async void Read(UnitPictureLibrary library, string source, double size)
    {
        Source = await library.GetAsync(source, size);
        if (Source is not null) Visibility = Visibility.Visible;
    }
}
