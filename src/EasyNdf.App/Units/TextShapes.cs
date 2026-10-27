using System.Windows;
using System.Windows.Media;

namespace EasyNdf.App.Units;

/// <summary>
/// The shapes of the numbers drawn outlined on the tiles (their cost, their units per card), each made once and shared
/// by every tile that shows it. Making a shape from text is slow, and was done for every tile each time it was laid out
/// and drawn: switching the Armory's sub-tabs spent much of its time on it, though the tiles show the same few numbers.
/// </summary>
internal static class TextShapes
{
    /// <summary>A shape made, and where it and its outline are drawn.</summary>
    public sealed record Shape(Geometry Geometry, Rect Ink);

    private static readonly Dictionary<ShapeKey, Shape> made = [];

    /// <summary>What a shape is made from: its text, its kind (two marks draw the same text differently), the font and the screen's scale.</summary>
    private sealed record ShapeKey(string Kind, string Text, FontFamily Family, FontWeight Weight, double PixelsPerDip);

    /// <summary>The shape of a text, made by <paramref name="make"/> the first time and shared from then on.</summary>
    /// <param name="kind">Which mark it is for, such as "cost".</param>
    /// <param name="text">The text.</param>
    /// <param name="family">The font.</param>
    /// <param name="weight">The font's weight.</param>
    /// <param name="pixelsPerDip">The screen's scale, which the shape's hinting follows.</param>
    /// <param name="outline">The outline it is drawn with, for where it is drawn.</param>
    /// <param name="make">Makes the shape.</param>
    public static Shape Of(string kind, string text, FontFamily family, FontWeight weight, double pixelsPerDip, Pen outline, Func<Geometry> make)
    {
        var key = new ShapeKey(kind, text, family, weight, pixelsPerDip);
        if (made.TryGetValue(key, out Shape? shape)) return shape;
        Geometry geometry = make();
        geometry.Freeze();
        shape = new Shape(geometry, geometry.IsEmpty() ? new Rect(0, 0, 0, 0) : geometry.GetRenderBounds(outline));
        made[key] = shape;
        return shape;
    }
}
