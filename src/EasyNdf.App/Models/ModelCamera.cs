using System.Numerics;

namespace EasyNdf.App.Models;

/// <summary>
/// The viewer's camera: it circles a point at the model's middle (moved by right-dragging) at three times the model's
/// size, turned (yaw) and tilted (pitch) by dragging. Zooming in narrows its view; zooming out past the start moves it
/// back instead, so nothing bends.
/// </summary>
internal sealed class ModelCamera
{
    /// <summary>The farthest out it zooms: the camera three times as far back as at the start.</summary>
    public const double LeastZoom = 1 / 3.0;

    /// <summary>The farthest in it zooms.</summary>
    public const double MostZoom = 6;

    /// <summary>Which way round the model it stands, in degrees.</summary>
    public double Yaw { get; set; } = 310;

    /// <summary>How far above the model it looks down, in degrees.</summary>
    public double Pitch { get; set; } = 18;

    /// <summary>How far it is zoomed: <see cref="LeastZoom"/> to <see cref="MostZoom"/>, 1 at the start.</summary>
    public double Zoom { get; set; } = 1;

    /// <summary>How much the view is narrowed so a tall or long model fits (1 for most).</summary>
    public double Fit { get; set; } = 1;

    /// <summary>How far the point it circles is moved from the model's middle, in the model's units.</summary>
    public Vector3 Pan { get; set; }

    /// <summary>Where it is, after <see cref="Aim"/>.</summary>
    public Vector3 Eye { get; private set; }

    private Vector3 forward, right, up;

    /// <summary>Which way it looks, after <see cref="Aim"/>.</summary>
    public Vector3 Forward => forward;

    /// <summary>Its right, as the picture shows it.</summary>
    public Vector3 Right => right;

    /// <summary>Its up, as the picture shows it.</summary>
    public Vector3 Up => up;
    private double focal, width, height, distance;

    /// <summary>The farthest distance drawn.</summary>
    public float Far { get; private set; }

    /// <summary>How far a pixel of the picture is across, at the point it circles, in the model's units.</summary>
    public double UnitsAPixel => focal > 0 ? distance / focal : 1;

    /// <summary>How far it is from the point it circles, in the model's units.</summary>
    public double Distance => distance;

    /// <summary>The camera as the shaders take it, after <see cref="Aim"/>.</summary>
    public Matrix4x4 ViewProjection { get; private set; }

    /// <summary>Points the camera at a model's middle, for a picture this wide and high (in any unit, the same for both).</summary>
    public void Aim(Vector3 centre, float radius, double pictureWidth, double pictureHeight)
    {
        width = pictureWidth;
        height = pictureHeight;
        centre += Pan;
        double yaw = Yaw * Math.PI / 180, pitch = Pitch * Math.PI / 180;
        distance = radius * 3 / Math.Min(1, Zoom);
        Eye = centre + new Vector3((float)(Math.Cos(yaw) * Math.Cos(pitch)), (float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch)) * (float)distance;
        forward = Vector3.Normalize(centre - Eye);
        right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, forward));
        up = Vector3.Cross(forward, right);
        // a long lens (2.15 times the picture's height), which keeps a model's lines nearly straight
        focal = height * 2.15 * Math.Max(1, Zoom) * Fit;
        float moved = Pan.Length();
        float near = (float)Math.Max(5, (distance - radius * 4 - moved) * 0.5);
        // far enough for a smoke screen laid ahead of the model (its clouds reach some 60 metres out)
        Far = (float)(distance + moved + Math.Max(radius * 8, 15000));
        float across = (float)(2 * focal / width), down = (float)(2 * focal / height);
        var view = new Matrix4x4(
            right.X, up.X, forward.X, 0,
            right.Y, up.Y, forward.Y, 0,
            right.Z, up.Z, forward.Z, 0,
            -Vector3.Dot(right, Eye), -Vector3.Dot(up, Eye), -Vector3.Dot(forward, Eye), 1);
        var projection = new Matrix4x4(
            across, 0, 0, 0,
            0, down, 0, 0,
            0, 0, Far / (Far - near), 1,
            0, 0, -Far * near / (Far - near), 0);
        ViewProjection = view * projection;
    }

    /// <summary>Where a point is on the picture (from its top left, in the unit <see cref="Aim"/> was given), and how far in front of the camera.</summary>
    public (double X, double Y, double Distance) Project(Vector3 point)
    {
        Vector3 offset = point - Eye;
        double depth = Vector3.Dot(offset, forward);
        if (depth <= 0.001) return (double.NaN, double.NaN, depth);
        return (width / 2 + Vector3.Dot(offset, right) / depth * focal, height / 2 - Vector3.Dot(offset, up) / depth * focal, depth);
    }
}
