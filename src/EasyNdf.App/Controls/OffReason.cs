using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace EasyNdf.App.Controls;

/// <summary>
/// Why a control is switched off, shown as its tooltip while it is: <c>controls:OffReason.Text="{t:Text …}"</c>, or set
/// in code when the reason changes. While the control is on, its own tooltip shows. Tooltips show on switched-off
/// buttons, boxes and dropdowns app-wide (<see cref="Register"/>), so the reason can be read.
/// </summary>
public static class OffReason
{
    /// <summary>Why the control is off; empty or null for no reason given (its own tooltip then shows).</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(OffReason),
        new PropertyMetadata(null, (target, _) => { if (target is FrameworkElement element) Follow(element); }));

    /// <summary>The control's own tooltip, kept while the reason is shown in its place.</summary>
    private static readonly DependencyProperty OwnTipProperty = DependencyProperty.RegisterAttached("OwnTip", typeof(object), typeof(OffReason));

    /// <summary>Whether the control is watched for being switched on and off already.</summary>
    private static readonly DependencyProperty IsWatchedProperty = DependencyProperty.RegisterAttached("IsWatched", typeof(bool), typeof(OffReason));

    public static string? GetText(DependencyObject target) => (string?)target.GetValue(TextProperty);

    public static void SetText(DependencyObject target, string? value) => target.SetValue(TextProperty, value);

    /// <summary>Tooltips show on switched-off buttons, boxes and dropdowns, so a reason can be read; called once at start-up.</summary>
    public static void Register()
    {
        foreach (Type type in new[] { typeof(ButtonBase), typeof(ComboBox), typeof(TextBoxBase) })
            ToolTipService.ShowOnDisabledProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(true));
    }

    /// <summary>Shows the reason as the tooltip while the control is off, and its own tooltip while it is on.</summary>
    private static void Follow(FrameworkElement element)
    {
        if (!(bool)element.GetValue(IsWatchedProperty))
        {
            element.SetValue(IsWatchedProperty, true);
            element.IsEnabledChanged += (_, _) => Show(element);
        }
        Show(element);
    }

    /// <summary>
    /// Off with a reason: the reason in place of the own tooltip, which is kept. On: the own tooltip back. The own one is
    /// taken when the reason goes in, as XAML or code may set it after the reason.
    /// </summary>
    private static void Show(FrameworkElement element)
    {
        string? reason = GetText(element);
        bool showsReason = element.ReadLocalValue(OwnTipProperty) != DependencyProperty.UnsetValue;
        if (!element.IsEnabled && !string.IsNullOrEmpty(reason))
        {
            if (!showsReason) element.SetValue(OwnTipProperty, element.ToolTip);
            element.ToolTip = reason;
        }
        else if (showsReason)
        {
            element.ToolTip = element.GetValue(OwnTipProperty);
            element.ClearValue(OwnTipProperty);
        }
    }
}
