using System.Windows;
using System.Windows.Media;
using EasyNdf.App.Controls;

namespace EasyNdf.App.Units;

/// <summary>
/// A unit's picture in a sunken frame, 8 wide for 5 high, filling the frame as a thumbnail in an old list does. Until
/// the picture is read, and where the game has none, the unit's outline is drawn instead, faint, by its factory.
/// </summary>
public sealed class UnitPicture : FrameworkElement
{
    /// <summary>The picture's path in the game (<see cref="EasyNdf.Data.Units.Unit.PictureSource"/>); null for none.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(UnitPicture), new FrameworkPropertyMetadata(null, (element, _) => ((UnitPicture)element).Load()));

    /// <summary>The unit's factory, which chooses the outline drawn without a picture.</summary>
    public static readonly DependencyProperty FactoryProperty = DependencyProperty.Register(
        nameof(Factory), typeof(string), typeof(UnitPicture), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    // the theme's colours, kept as properties so the frame redraws when the theme changes
    private static readonly DependencyProperty PanelProperty = Colour("Panel");
    private static readonly DependencyProperty LoProperty = Colour("Lo");
    private static readonly DependencyProperty HiProperty = Colour("Hi");
    private static readonly DependencyProperty MutedProperty = Colour("Muted");

    private readonly PixelSnapping snapping;
    private ImageSource? picture;

    /// <summary>Builds an empty frame in the theme's colours.</summary>
    public UnitPicture()
    {
        snapping = new PixelSnapping(this);
        SetResourceReference(PanelProperty, "panel");
        SetResourceReference(LoProperty, "lo");
        SetResourceReference(HiProperty, "hi");
        SetResourceReference(MutedProperty, "muted");
        // a picture is read when the frame is first drawn, and again if it was made smaller than now needed
        SizeChanged += (_, _) => Load();
    }

    /// <inheritdoc cref="SourceProperty"/>
    public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <inheritdoc cref="FactoryProperty"/>
    public string Factory { get => (string)GetValue(FactoryProperty); set => SetValue(FactoryProperty, value); }

    /// <summary>As wide as it is given, or its own Width, and five eighths of that high.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 128 : availableSize.Width;
        return new Size(width, width * 5 / 8);
    }

    /// <summary>Shows the picture of <see cref="Source"/>: at once when it is in memory, else when it has been read.</summary>
    private async void Load()
    {
        string? source = Source;
        UnitPictureLibrary? library = UnitPictureLibrary.Current;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        if (source is null || library is null || double.IsNaN(width) || width <= 0)
        {
            Show(null);
            return;
        }
        if (library.TryGetKept(source, width, out ImageSource? kept))
        {
            Show(kept);
            return;
        }
        Show(null);
        ImageSource? read = await library.GetAsync(source, width);
        // the frame may show another unit by now, as the list's rows are reused while it scrolls
        if (Source == source) Show(read);
    }

    private void Show(ImageSource? shown)
    {
        if (ReferenceEquals(picture, shown)) return;
        picture = shown;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 2 || height <= 2) return;
        drawing.DrawRectangle((Brush?)GetValue(PanelProperty), null, snapping.Snap(0, 0, width, height));
        var inside = new Rect(1, 1, width - 2, height - 2);
        if (picture is not null) DrawFilling(drawing, picture, inside);
        else DrawOutline(drawing, inside);
        // the sunken edge: dark top and left, light bottom and right
        drawing.DrawRectangle((Brush?)GetValue(LoProperty), null, snapping.Snap(0, 0, width, 1));
        drawing.DrawRectangle((Brush?)GetValue(LoProperty), null, snapping.Snap(0, 0, 1, height));
        drawing.DrawRectangle((Brush?)GetValue(HiProperty), null, snapping.Snap(0, height - 1, width, 1));
        drawing.DrawRectangle((Brush?)GetValue(HiProperty), null, snapping.Snap(width - 1, 0, 1, height));
    }

    /// <summary>The picture scaled to fill the frame, its middle kept and the overhang cut off (CSS's object-fit: cover).</summary>
    private static void DrawFilling(DrawingContext drawing, ImageSource image, Rect frame)
    {
        double scale = Math.Max(frame.Width / image.Width, frame.Height / image.Height);
        double width = image.Width * scale, height = image.Height * scale;
        drawing.PushClip(new RectangleGeometry(frame));
        drawing.DrawImage(image, new Rect(frame.X + (frame.Width - width) / 2, frame.Y + (frame.Height - height) / 2, width, height));
        drawing.Pop();
    }

    /// <summary>The factory's outline, faint, in the middle of the frame: 16% in from each side, 18% from the top, 22% from the bottom.</summary>
    private void DrawOutline(DrawingContext drawing, Rect frame)
    {
        var area = new Rect(frame.X + frame.Width * 0.16, frame.Y + frame.Height * 0.18, frame.Width * 0.68, frame.Height * 0.60);
        // the outline keeps its shape (100 by 50), as large as the area allows, in its middle
        double scale = Math.Min(area.Width / 100, area.Height / 50);
        var place = new TransformGroup();
        place.Children.Add(new ScaleTransform(scale, scale));
        place.Children.Add(new TranslateTransform(area.X + (area.Width - 100 * scale) / 2, area.Y + (area.Height - 50 * scale) / 2));
        drawing.PushTransform(place);
        drawing.PushOpacity(0.55);
        drawing.DrawGeometry((Brush?)GetValue(MutedProperty), null, UnitShapes.OutlineOf(Factory));
        drawing.Pop();
        drawing.Pop();
    }

    private static DependencyProperty Colour(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(UnitPicture), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}
