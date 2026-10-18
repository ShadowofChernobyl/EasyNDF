using System.Windows;
using System.Windows.Media;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// The simple drawings the Units tab uses: a unit's outline by its factory, shown where the game has no picture of it,
/// and the small icon of each sort of weapon. The outlines are drawn on a 100 by 50 grid, the icons on 20 by 18.
/// </summary>
public static class UnitShapes
{
    private static readonly Dictionary<string, Geometry> Outlines = new(StringComparer.Ordinal)
    {
        ["Tanks"] = Shape("M8,30 h84 l-6,12 H14 Z M22,20 h44 l6,10 H18 Z M58,21 h40 v4 h-40 Z"),
        ["Recons"] = Shape("M10,30 h80 l-5,10 H15 Z M28,20 h36 l6,10 H24 Z" + Circle(22, 42, 6) + Circle(50, 42, 6) + Circle(78, 42, 6)),
        ["Infantry"] = Shape(Circle(30, 10, 5) + " M25,16 h10 l3,16 h-4 l-2,16 h-4 l-2,-16 h-4 Z" + Circle(55, 10, 5)
            + " M50,16 h10 l3,16 h-4 l-2,16 h-4 l-2,-16 h-4 Z" + Circle(80, 12, 5) + " M75,18 h10 l3,15 h-4 l-2,15 h-4 l-2,-15 h-4 Z"),
        ["Art"] = Group(Shape("M10,34 h60 l-4,8 H14 Z M30,26 h26 v8 H30 Z" + Circle(20, 43, 5) + Circle(58, 43, 5)), Bar(48, 12, 48, 4, -18)),
        ["DCA"] = Group(Shape("M10,34 h80 l-5,8 H15 Z M34,24 h30 v10 H34 Z"), Bar(48, 8, 40, 3, -35), Bar(48, 14, 40, 3, -35)),
        ["Helis"] = Shape("M8,10 h84 v2 H8 Z M47,11 h4 v8 h-4 Z M28,20 c10,-4 30,-4 38,2 l28,-2 v5 l-28,4 c-10,6 -30,6 -38,0 Z"),
        ["Planes"] = Shape("M6,26 l20,-3 l30,-12 h6 l-14,14 l34,-2 l10,-10 h4 l-4,14 l4,14 h-4 l-10,-10 l-34,-2 l14,14 h-6 l-30,-12 l-20,-3 Z"),
        ["Logistic"] = Shape("M8,18 h56 v22 H8 Z M64,24 h18 l10,10 v6 H64 Z" + Circle(22, 42, 6) + Circle(50, 42, 6) + Circle(80, 42, 6)),
    };

    private static readonly Dictionary<WeaponKind, Geometry> WeaponIcons = new()
    {
        [WeaponKind.AntiArmor] = Shape("M3,12 h11 l4,-3 l-4,-3 H3 Z M6,6 v6"),
        [WeaponKind.HighExplosive] = Shape("M3,12 h10 l5,-3 l-5,-3 H3 Z M9,6 v6 M11,6 v6"),
        [WeaponKind.Missile] = Shape("M2,9 h13 l3,0 l-3,-2 H5 L2,5 M2,13 l3,-2 h10 l3,-2 M7,7 l-2,-3 M7,11 l-2,3"),
        [WeaponKind.Rocket] = Shape("M3,9 h12 l3,0 M3,9 l2,-2 v4 Z M15,7.5 l3,1.5 l-3,1.5"),
        [WeaponKind.SmallArms] = Shape("M3,6 h7 l2,1.5 l-2,1.5 H3 Z M5,11 h7 l2,1.5 l-2,1.5 H5 Z"),
        [WeaponKind.Smoke] = Shape("M5,13 A3,3 0 1 1 6,7.2 A4,4 0 0 1 14,7 A3,3 0 1 1 14,13 Z"),
    };

    /// <summary>
    /// The outline drawn for a unit of a factory, on a 100 by 50 grid. Steel Division 2's Support uses the truck, and
    /// its anti-tank guns and defences the gun.
    /// </summary>
    public static Geometry OutlineOf(string factory) => factory switch
    {
        "Support" => Outlines["Logistic"],
        "AT" or "Defense" => Outlines["Art"],
        _ => Outlines.GetValueOrDefault(factory) ?? Outlines["Logistic"],
    };

    /// <summary>The icon of a sort of weapon, drawn as lines on a 20 by 18 grid.</summary>
    public static Geometry IconOf(WeaponKind kind) => WeaponIcons[kind];

    /// <summary>A circle in the path language: two half arcs.</summary>
    private static string Circle(double x, double y, double radius) =>
        FormattableString.Invariant($" M{x - radius},{y} a{radius},{radius} 0 1 0 {radius * 2},0 a{radius},{radius} 0 1 0 {-radius * 2},0 Z");

    /// <summary>A gun barrel: a bar turned about its left end's middle.</summary>
    private static Geometry Bar(double x, double y, double length, double thickness, double angle)
    {
        var bar = new RectangleGeometry(new Rect(x, y, length, thickness), 0, 0, new RotateTransform(angle, x, y + thickness / 2));
        bar.Freeze();
        return bar;
    }

    private static Geometry Group(params Geometry[] parts)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (Geometry part in parts) group.Children.Add(part);
        group.Freeze();
        return group;
    }

    private static Geometry Shape(string path)
    {
        // "F1": parts that overlap are filled, as an SVG fills them, rather than cut out of each other
        Geometry shape = Geometry.Parse("F1 " + path);
        shape.Freeze();
        return shape;
    }
}
