using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Weapons;

/// <summary>
/// The Weapons sub-tab's zoom, as the Units sub-tab's: Cards and Table each keep their own, chosen with Ctrl and the
/// wheel over the weapons, Ctrl+plus, Ctrl+minus and Ctrl+0, or the dropdown at the bottom right. Compare has none.
/// </summary>
public partial class WeaponsArmory
{
    /// <summary>The zooms offered, in percent, the Units sub-tab's; Ctrl and the wheel step from one to the next.</summary>
    private static readonly int[] ZoomSteps = [50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200];

    /// <summary>The usual zoom.</summary>
    private const int UsualZoom = 100;

    /// <summary>True while the dropdown is being filled, so that does not count as a choice.</summary>
    private bool showingZoom;

    /// <summary>The setting that keeps a view's zoom: "weaponsZoomCards" or "weaponsZoomTable".</summary>
    private static string ZoomSetting(WeaponView zoomed) => "weaponsZoom" + zoomed;

    /// <summary>A view's zoom in percent, as saved; 100 when none is.</summary>
    private static int ZoomOf(WeaponView zoomed)
    {
        int saved = AppData.Current.Settings[ZoomSetting(zoomed)]?.GetValue<int>() ?? UsualZoom;
        return Math.Clamp(saved, ZoomSteps[0], ZoomSteps[^1]);
    }

    /// <summary>How many times larger a view is drawn: its zoom as a scale, 1 at 100%.</summary>
    private static double ScaleOf(WeaponView zoomed) => ZoomOf(zoomed) / 100.0;

    /// <summary>Fills the dropdown and gives both views their saved zoom; done once, when the sub-tab is made.</summary>
    private void PrepareZoom()
    {
        var choices = new List<ComboBoxItem>();
        foreach (int percent in ZoomSteps)
            choices.Add(new ComboBoxItem { Content = percent + "%", Tag = percent });
        ZoomChoice.ItemsSource = choices;
        ZoomChoice.SelectionChanged += ZoomChosen;
        // heard here, before the lists' own scrolling boxes, which would take the wheel for scrolling
        PreviewMouseWheel += WheelTurnedOverList;
        PreviewKeyDown += ZoomKeys;
        ApplyZoom(WeaponView.Cards);
        ApplyZoom(WeaponView.Table);
    }

    /// <summary>Draws a view at its zoom: each row of cards scaled, or the table's headings and rows.</summary>
    private void ApplyZoom(WeaponView zoomed)
    {
        if (zoomed == WeaponView.Table)
        {
            Table.SetScale(ScaleOf(zoomed));
            return;
        }
        // a transform in a style cannot change once the style is in use, so each zoom gets a new style
        var scale = new ScaleTransform(ScaleOf(zoomed), ScaleOf(zoomed));
        scale.Freeze();
        CardsList.ItemContainerStyle = new Style(typeof(ContentPresenter)) { Setters = { new Setter(LayoutTransformProperty, scale) } };
    }

    /// <summary>Shows the zoom of the view shown in the dropdown; on Compare, which has none, the dropdown is hidden.</summary>
    private void ShowZoom()
    {
        ZoomChoice.Visibility = view == WeaponView.Compare ? Visibility.Collapsed : Visibility.Visible;
        if (view == WeaponView.Compare) return;
        showingZoom = true;
        int percent = ZoomOf(view);
        ZoomChoice.SelectedItem = ((List<ComboBoxItem>)ZoomChoice.ItemsSource).FirstOrDefault(item => (int)item.Tag == percent);
        showingZoom = false;
    }

    private void ZoomChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showingZoom || ZoomChoice.SelectedItem is not ComboBoxItem { Tag: int percent }) return;
        SetZoom(percent);
    }

    /// <summary>Ctrl and the wheel over Cards or Table: one step larger or smaller.</summary>
    private void WheelTurnedOverList(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !IsOverList(e.OriginalSource as DependencyObject)) return;
        e.Handled = true;
        StepZoom(e.Delta > 0 ? 1 : -1);
    }

    /// <summary>Whether an element is in the cards or the table.</summary>
    private bool IsOverList(DependencyObject? element)
    {
        for (DependencyObject? at = element; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at == CardsList || at == Table) return true;
        return false;
    }

    /// <summary>Ctrl+plus, Ctrl+minus and Ctrl+0 change the zoom of Cards or Table; not while typing in a box.</summary>
    private void ZoomKeys(object sender, KeyEventArgs e)
    {
        if (view == WeaponView.Compare || catalog is null || Keyboard.FocusedElement is TextBox) return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        if (e.Key is Key.OemPlus or Key.Add) { e.Handled = true; StepZoom(1); }
        else if (e.Key is Key.OemMinus or Key.Subtract) { e.Handled = true; StepZoom(-1); }
        else if (e.Key is Key.D0 or Key.NumPad0) { e.Handled = true; SetZoom(UsualZoom); }
    }

    /// <summary>The next zoom offered, larger (1) or smaller (-1); none past the largest or smallest.</summary>
    private void StepZoom(int direction)
    {
        if (view == WeaponView.Compare) return;
        int at = Array.IndexOf(ZoomSteps, ZoomOf(view));
        // a zoom saved by hand that is not one of the steps: the nearest step is taken first
        if (at < 0) at = Array.FindIndex(ZoomSteps, step => step >= ZoomOf(view));
        int next = Math.Clamp(at + direction, 0, ZoomSteps.Length - 1);
        SetZoom(ZoomSteps[next]);
    }

    /// <summary>Saves the shown view's zoom, draws it at that zoom, and lays the cards out again for the width they now have.</summary>
    private void SetZoom(int percent)
    {
        if (view == WeaponView.Compare || percent == ZoomOf(view)) return;
        AppData.Current.Settings[ZoomSetting(view)] = percent;
        AppData.Current.SaveSettings();
        ApplyZoom(view);
        ShowZoom();
        if (view == WeaponView.Cards) ShowCards();
    }

    // ---- for the workbench's checks ----

    /// <summary>Sets the zoom of the view shown, as the dropdown does.</summary>
    internal void SetZoomNow(int percent) => SetZoom(percent);

    /// <summary>How many cards a row of the Cards view has now.</summary>
    internal int CardColumnsNow => cardColumns;
}
