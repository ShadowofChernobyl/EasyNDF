using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// A weapon trait's small picture from the game (WeaponTraits.ndf), such as Kinetic's: a black shape the game draws
/// light, drawn here in the theme's weapon picture colour ("weapon-art"), as the weapons' pictures are. Its tooltip
/// is the trait's name.
/// </summary>
public sealed class TraitIcon : FrameworkElement
{
    /// <summary>The traits the game describes, by token; set by the Weapons sub-tab when it is given the units read.</summary>
    public static IReadOnlyDictionary<string, WeaponTrait> Known { get; set; } = new Dictionary<string, WeaponTrait>();

    /// <summary>The size the game's trait pictures are read at, enough for the few sizes they are drawn at.</summary>
    private const double ReadWidth = 96;

    private static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        "Art", typeof(Brush), typeof(TraitIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly string source;
    private ImageSource? picture;

    /// <summary>
    /// A trait's picture, as high as <paramref name="size"/> and twice as wide, as the game's are (96 by 48); null when
    /// the game has no picture for it.
    /// </summary>
    public static TraitIcon? For(string token, double size)
    {
        if (!Known.TryGetValue(token, out WeaponTrait? trait) || trait.PictureSource is not string source) return null;
        return new TraitIcon(source, size) { ToolTip = trait.Name };
    }

    /// <summary>A weapon's traits' pictures in a row, each a few pixels apart; those without a picture are left out.</summary>
    public static StackPanel RowFor(Weapon weapon, double size)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (string token in weapon.Traits)
        {
            if (For(token, size) is not TraitIcon icon) continue;
            icon.Margin = new Thickness(row.Children.Count == 0 ? 0 : 2, 0, 0, 0);
            row.Children.Add(icon);
        }
        return row;
    }

    /// <summary>A weapon's traits by name: "Kinetic, Motion"; the token for a trait the game does not name.</summary>
    public static string NamesOf(Weapon weapon) =>
        string.Join(", ", weapon.Traits.Select(token => Known.GetValueOrDefault(token)?.Name ?? token));

    private TraitIcon(string source, double size)
    {
        this.source = source;
        Width = size * 2;
        Height = size;
        SetResourceReference(ArtProperty, "weapon-art");
        Loaded += (_, _) => Load();
    }

    private async void Load()
    {
        if (UnitPictureLibrary.Current is not UnitPictureLibrary library) return;
        if (library.TryGetKept(source, ReadWidth, out ImageSource? kept)) picture = kept;
        else picture = await library.GetAsync(source, ReadWidth);
        InvalidateVisual();
    }

    /// <summary>The picture's shape, filled with the weapon picture colour.</summary>
    protected override void OnRender(DrawingContext drawing)
    {
        if (picture is null || GetValue(ArtProperty) is not Brush art) return;
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawing.PushOpacityMask(new ImageBrush(picture) { Stretch = Stretch.Uniform });
        drawing.DrawRectangle(art, null, bounds);
        drawing.Pop();
    }
}
