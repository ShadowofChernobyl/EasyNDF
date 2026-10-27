using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasyNdf.App.Units;

namespace EasyNdf.App.Weapons;

/// <summary>
/// A weapon's picture from the game (InterfaceWeaponTexture) in a sunken frame. The game's pictures are small black
/// shapes (132 by 50 pixels) that it draws in a light colour; here they are drawn in the theme's weapon picture colour
/// ("weapon-art", set in the Theme Designer), a little darker towards the bottom, with a shadow one pixel below and to
/// the right. So they stay sharp, each of the picture's pixels becomes a whole number of the screen's pixels (one,
/// two, three…), as many as fit; the shape is centred in the frame.
/// </summary>
public sealed class WeaponPicture : FrameworkElement
{
    /// <summary>The picture's path in the game (<see cref="Data.Weapons.Weapon.PictureSource"/>); null for none.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(WeaponPicture), new FrameworkPropertyMetadata(null, (element, _) => ((WeaponPicture)element).Load()));

    // the theme's colours, kept as properties so the frame redraws when the theme changes
    private static readonly DependencyProperty WellProperty = Colour("Well");
    private static readonly DependencyProperty LoProperty = Colour("Lo");
    private static readonly DependencyProperty HiProperty = Colour("Hi");
    private static readonly DependencyProperty ArtProperty = Colour("Art");

    /// <summary>The game's pictures' size, whose proportions the frame keeps.</summary>
    private const double PictureWidth = 132, PictureHeight = 50;

    /// <summary>The frame's edge and the room between it and the shape.</summary>
    private const double Inset = 3;

    private BitmapSource? picture;

    /// <summary>Builds an empty frame in the theme's colours.</summary>
    public WeaponPicture()
    {
        SetResourceReference(WellProperty, "well");
        SetResourceReference(LoProperty, "lo");
        SetResourceReference(HiProperty, "hi");
        SetResourceReference(ArtProperty, "weapon-art");
        // the frame starts on a whole screen pixel, so the shape's pixels fall on the screen's
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        SnapsToDevicePixels = true;
    }

    /// <inheritdoc cref="SourceProperty"/>
    public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>The frame's height for its width, as the game's pictures are: 50 for 132, with room for the frame.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? PictureWidth + 2 * Inset : availableSize.Width;
        return new Size(width, Math.Round(width * PictureHeight / PictureWidth + 2 * Inset));
    }

    /// <summary>Shows the picture of <see cref="Source"/>: at once when it is in memory, else when it has been read.</summary>
    private async void Load()
    {
        string? source = Source;
        UnitPictureLibrary? library = UnitPictureLibrary.Current;
        if (source is null || library is null)
        {
            Show(null);
            return;
        }
        if (library.TryGetKept(source, PictureWidth, out ImageSource? kept))
        {
            Show(kept);
            return;
        }
        Show(null);
        ImageSource? read = await library.GetAsync(source, PictureWidth);
        // the frame may show another weapon by now, as a list's rows are reused while it scrolls
        if (Source == source) Show(read);
    }

    private void Show(ImageSource? shown)
    {
        if (ReferenceEquals(picture, shown)) return;
        picture = shown as BitmapSource;
        InvalidateVisual();
    }

    /// <summary>The sunken frame, then the weapon's shape, drawn pixel for pixel at a whole-number scale.</summary>
    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 2 || height <= 2) return;
        drawing.DrawRectangle(Brush(WellProperty), null, new Rect(0, 0, width, height));
        // a sunken edge: dark above and at the left, light below and at the right
        drawing.DrawRectangle(Brush(LoProperty), null, new Rect(0, 0, width, 1));
        drawing.DrawRectangle(Brush(LoProperty), null, new Rect(0, 0, 1, height));
        drawing.DrawRectangle(Brush(HiProperty), null, new Rect(0, height - 1, width, 1));
        drawing.DrawRectangle(Brush(HiProperty), null, new Rect(width - 1, 0, 1, height));
        if (picture is null || Brush(ArtProperty) is not SolidColorBrush art || Brush(WellProperty) is not SolidColorBrush well) return;

        // the room inside the frame, in the screen's own pixels
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double roomWidth = (width - 2 * Inset) * dpi, roomHeight = (height - 2 * Inset) * dpi;
        int scale = (int)Math.Min(roomWidth / picture.PixelWidth, roomHeight / picture.PixelHeight);
        if (scale < 1)
        {
            // smaller than the picture's own size, as in the table's rows: it is shrunk to fit, smoothly
            BitmapSource small = WeaponArt.Drawn(picture, 1, art.Color, well.Color);
            drawing.DrawImage(small, Fitted(new Rect(Inset, Inset, width - 2 * Inset, height - 2 * Inset), small.PixelWidth, small.PixelHeight));
            return;
        }
        BitmapSource shape = WeaponArt.Drawn(picture, scale, art.Color, well.Color);
        // where it goes, centred, on whole screen pixels; its own size in the screen's pixels, so nothing is stretched
        double left = Math.Round(Inset * dpi + (roomWidth - shape.PixelWidth) / 2) / dpi;
        double top = Math.Round(Inset * dpi + (roomHeight - shape.PixelHeight) / 2) / dpi;
        drawing.DrawImage(shape, new Rect(left, top, shape.PixelWidth / dpi, shape.PixelHeight / dpi));
    }

    /// <summary>The largest rectangle of a picture's proportions inside <paramref name="room"/>, centred.</summary>
    private static Rect Fitted(Rect room, double pictureWidth, double pictureHeight)
    {
        double scale = Math.Min(room.Width / pictureWidth, room.Height / pictureHeight);
        double width = pictureWidth * scale, height = pictureHeight * scale;
        return new Rect(room.X + (room.Width - width) / 2, room.Y + (room.Height - height) / 2, width, height);
    }

    private Brush? Brush(DependencyProperty colour) => (Brush?)GetValue(colour);

    private static DependencyProperty Colour(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(WeaponPicture), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}
