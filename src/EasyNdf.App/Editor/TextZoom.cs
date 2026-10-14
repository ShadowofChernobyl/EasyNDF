namespace EasyNdf.App.Editor;

/// <summary>
/// The text size the File Editor and the Add Descriptor rule's text box share (the setting "edZoom", in pixels), and
/// the percentage the status strip shows for it: 12 pixels is 100%.
/// </summary>
public static class TextZoom
{
    /// <summary>The usual text size, 100%.</summary>
    public const double UsualSize = 12;

    /// <summary>The smallest and largest text sizes.</summary>
    public const double SmallestSize = 7, LargestSize = 36;

    /// <summary>The sizes the strip's dropdown offers, in pixels: 75% to 300%.</summary>
    public static readonly int[] OfferedSizes = [9, 10, 11, 12, 13, 14, 15, 18, 21, 24, 30, 36];

    /// <summary>Raised with the new size when either editor changes it, so the other takes it too.</summary>
    public static event Action<double>? Changed;

    /// <summary>The size saved, or the usual one.</summary>
    public static double Saved => AppData.Current.Settings["edZoom"]?.GetValue<double>() ?? UsualSize;

    /// <summary>A size rounded to whole pixels and kept between the smallest and largest.</summary>
    public static double Fit(double size) => Math.Clamp(Math.Round(size), SmallestSize, LargestSize);

    /// <summary>A size as the strip shows it, for example "125%".</summary>
    public static string Percent(double size) => Math.Round(size / UsualSize * 100) + "%";

    /// <summary>Saves a new size and tells both editors.</summary>
    public static void Set(double size)
    {
        double fitted = Fit(size);
        AppData.Current.Settings["edZoom"] = fitted;
        AppData.Current.SaveSettings();
        Changed?.Invoke(fitted);
    }
}
