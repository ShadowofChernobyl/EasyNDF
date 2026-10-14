using System.Globalization;
using System.Windows.Data;

namespace EasyNdf.App.Controls;

/// <summary>
/// Keeps a scrollbar's thumb long enough to grab and to show its grip, however long the list or the text is. WPF's track
/// sizes the thumb from the share of the content in view, and lets it shrink to a few pixels; it takes no notice of the
/// thumb's least height. So the track is told the view is a little larger than it is, just enough for a thumb of
/// <see cref="ShortestThumb"/> pixels; the thumb still runs from top to bottom as the content does.
/// </summary>
public sealed class ThumbViewport : IMultiValueConverter
{
    /// <summary>The shortest a thumb may be: room for the grip, and some to spare.</summary>
    public const double ShortestThumb = 40;

    /// <summary>The one converter the scrollbar styles share.</summary>
    public static readonly ThumbViewport Instance = new();

    /// <summary>From the view's size, the scroll range and the track's length, the view size the track is given.</summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [double viewport, double minimum, double maximum, double trackLength]) return values.Length > 0 ? values[0] : 0.0;
        double range = maximum - minimum;
        if (range <= 0 || trackLength <= ShortestThumb * 2) return viewport;
        // the thumb is trackLength × view ÷ (range + view): the view that makes it exactly ShortestThumb long
        double viewForShortest = ShortestThumb * range / (trackLength - ShortestThumb);
        return Math.Max(viewport, viewForShortest);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
