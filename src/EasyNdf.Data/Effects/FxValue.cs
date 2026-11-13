using System.Numerics;

namespace EasyNdf.Data.Effects;

/// <summary>
/// A value an effect works out: a number, a vector of 2 to 4 numbers (positions, colours), a yes or no (as 1 or 0), or
/// something that is not a number (a text, an object, a list, a moving frame) carried as it is.
/// </summary>
public readonly record struct FxValue
{
    /// <summary>How many numbers it holds: 0 for none (nil or not a number), 1 for a number, 2 to 4 for a vector.</summary>
    public int Size { get; init; }

    /// <summary>Its numbers; those past <see cref="Size"/> are 0.</summary>
    public Vector4 Numbers { get; init; }

    /// <summary>What it is when it is not a number: a text, an <see cref="FxNode"/>, a list of values, a frame; null otherwise.</summary>
    public object? Thing { get; init; }

    /// <summary>nil.</summary>
    public static readonly FxValue Nil = default;

    /// <summary>A number.</summary>
    public static FxValue Of(double number) => new() { Size = 1, Numbers = new Vector4((float)number, 0, 0, 0) };

    /// <summary>A vector of three numbers.</summary>
    public static FxValue Of(Vector3 vector) => new() { Size = 3, Numbers = new Vector4(vector, 0) };

    /// <summary>A vector of so many numbers.</summary>
    public static FxValue Of(Vector4 numbers, int size) => new() { Size = size, Numbers = numbers };

    /// <summary>Something that is not a number.</summary>
    public static FxValue OfThing(object thing) => new() { Thing = thing };

    /// <summary>Whether it is nil.</summary>
    public bool IsNil => Size == 0 && Thing is null;

    /// <summary>Its first number (a vector's x); 0 for nil.</summary>
    public float Number => Numbers.X;

    /// <summary>As a three-number vector: a number given for a vector fills all three, as the game's effects do.</summary>
    public Vector3 Vector => Size == 1 ? new Vector3(Numbers.X) : new Vector3(Numbers.X, Numbers.Y, Numbers.Z);

    /// <summary>Whether it counts as true: a number not 0, or something that is not nil.</summary>
    public bool IsTrue => Size > 0 ? Numbers.X != 0 : Thing is not null && !(Thing is string text && text == "false");

    /// <summary>A yes or no as a number.</summary>
    public static FxValue Of(bool truth) => Of(truth ? 1 : 0);

    /// <summary>Two values worked together number by number; a single number is spread over the other's vector.</summary>
    public static FxValue Combine(FxValue a, FxValue b, Func<float, float, float> work)
    {
        if (a.Size == 0 || b.Size == 0) return a.Size == 0 ? b : a;
        int size = Math.Max(a.Size, b.Size);
        Vector4 x = Spread(a), y = Spread(b);
        return Of(new Vector4(work(x.X, y.X), work(x.Y, y.Y), work(x.Z, y.Z), work(x.W, y.W)), size);
    }

    /// <summary>A value as four numbers, a single number spread over all four.</summary>
    private static Vector4 Spread(FxValue value) => value.Size == 1 ? new Vector4(value.Numbers.X) : value.Numbers;

    /// <summary>A value worked number by number.</summary>
    public FxValue Map(Func<float, float> work) =>
        Size == 0 ? this : Of(new Vector4(work(Numbers.X), Size > 1 ? work(Numbers.Y) : 0, Size > 2 ? work(Numbers.Z) : 0, Size > 3 ? work(Numbers.W) : 0), Size);
}

/// <summary>
/// The place and axes effects are drawn in: a firing point of the model as it is posed now, or a frame placed inside
/// another (TMobileWithLocalRepereMatrixFactory), or one that moves (a projectile's).
/// </summary>
public sealed class FxFrame
{
    private readonly Func<Matrix4x4> where;

    /// <summary>A frame that is wherever <paramref name="where"/> says each time it is asked.</summary>
    /// <param name="where">Its place and axes: x is its first row, y its second, z its third, its place the fourth.</param>
    public FxFrame(Func<Matrix4x4> where) => this.where = where;

    /// <summary>A frame that stays put.</summary>
    public static FxFrame Fixed(Matrix4x4 matrix) => new(() => matrix);

    /// <summary>Where a moving action has taken the frame (a projectile's flight), in place of where it would be; null for not moved.</summary>
    public Matrix4x4? MovedTo { get; set; }

    /// <summary>Its place and axes now.</summary>
    public Matrix4x4 Matrix => MovedTo ?? where();

    /// <summary>Where it is now.</summary>
    public Vector3 Position => Matrix.Translation;

    /// <summary>Its x axis now (forward, along a gun).</summary>
    public Vector3 AxisX
    {
        get { Matrix4x4 m = Matrix; return Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13)); }
    }

    /// <summary>Its y axis now.</summary>
    public Vector3 AxisY
    {
        get { Matrix4x4 m = Matrix; return Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23)); }
    }

    /// <summary>Its z axis now (up).</summary>
    public Vector3 AxisZ
    {
        get { Matrix4x4 m = Matrix; return Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33)); }
    }

    /// <summary>
    /// A frame placed inside this one, following it as it moves: at a place, turned by the scripts' EulerAngles (radians),
    /// whose first is the turn about the up axis (z), the second about y and the third about x. The order is read from
    /// the effects themselves: a gun's smoke ring is turned (π, 0, 0) to blow forward, which only a turn about z does.
    /// </summary>
    public FxFrame Inside(Vector3 position, Vector3 turns)
    {
        Matrix4x4 local = Matrix4x4.CreateRotationX(turns.Z) * Matrix4x4.CreateRotationY(turns.Y) * Matrix4x4.CreateRotationZ(turns.X) * Matrix4x4.CreateTranslation(position);
        return new FxFrame(() => local * Matrix);
    }
}
