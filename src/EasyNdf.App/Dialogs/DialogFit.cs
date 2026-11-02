using System.Windows;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// Keeps a window drawn over the app inside it: at its usual width while there is room, narrower in a small app window,
/// and never taller than the app, so its buttons always show (what does not fit scrolls inside it). At 150% scale a
/// 600 by 640 window leaves about 400 by 427 to draw in, which the fixed sizes did not fit.
/// </summary>
internal static class DialogFit
{
    /// <summary>The room kept between the window and each edge of the app.</summary>
    private const double EdgeRoom = 8;

    /// <summary>Sizes a window's box to the room it has.</summary>
    /// <param name="box">The window's raised box.</param>
    /// <param name="room">What the box is centred in, the size of the app.</param>
    /// <param name="width">The box's usual width.</param>
    public static void Keep(FrameworkElement box, FrameworkElement room, double width)
    {
        if (room.ActualWidth <= 0 || room.ActualHeight <= 0) return;
        box.Width = Math.Max(160, Math.Min(width, room.ActualWidth - 2 * EdgeRoom));
        box.MaxHeight = Math.Max(120, room.ActualHeight - 2 * EdgeRoom);
    }
}
