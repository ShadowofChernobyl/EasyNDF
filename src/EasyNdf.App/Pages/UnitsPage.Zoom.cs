using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyNdf.App.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Units tab's zoom: Cards and Table each keep their own, chosen with Ctrl and the wheel over the list, Ctrl+plus,
/// Ctrl+minus and Ctrl+0, or the dropdown at the bottom right, as in the File Editor.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The zooms offered, in percent; Ctrl and the wheel step from one to the next.</summary>
    private static readonly int[] ZoomSteps = [50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200];

    /// <summary>The usual zoom.</summary>
    private const int UsualZoom = 100;

    /// <summary>True while the dropdown is being filled, so that does not count as a choice.</summary>
    private bool showingZoom;

    /// <summary>The setting that keeps a view's zoom: "unitsZoomCards" or "unitsZoomTable".</summary>
    private static string ZoomSetting(UnitView zoomed) => "unitsZoom" + zoomed;

    /// <summary>A view's zoom in percent, as saved; 100 when none is.</summary>
    private static int ZoomOf(UnitView zoomed)
    {
        int saved = AppData.Current.Settings[ZoomSetting(zoomed)]?.GetValue<int>() ?? UsualZoom;
        return Math.Clamp(saved, ZoomSteps[0], ZoomSteps[^1]);
    }

    /// <summary>How many times larger a view is drawn: its zoom as a scale, 1 at 100%.</summary>
    private static double ScaleOf(UnitView zoomed) => ZoomOf(zoomed) / 100.0;

    /// <summary>Fills the dropdown and gives both lists their saved zoom; done once, when the page is made.</summary>
    private void PrepareZoom()
    {
        var choices = new List<ComboBoxItem>();
        foreach (int percent in ZoomSteps)
            choices.Add(new ComboBoxItem { Content = percent + "%", Tag = percent });
        ZoomChoice.ItemsSource = choices;
        ZoomChoice.SelectionChanged += ZoomChosen;
        CardsList.PreviewMouseWheel += WheelTurnedOverList;
        TableList.PreviewMouseWheel += WheelTurnedOverList;
        ApplyZoom(UnitView.Cards);
        ApplyZoom(UnitView.Table);
    }

    /// <summary>
    /// Draws a list at its zoom: each of its rows (and the table's headings) is scaled. A transform in a style cannot
    /// change once the style is in use, so each zoom gets a new style.
    /// </summary>
    private void ApplyZoom(UnitView zoomed)
    {
        if (zoomed == UnitView.Table)
        {
            TableList.SetScale(ScaleOf(zoomed));
            return;
        }
        var scale = new ScaleTransform(ScaleOf(zoomed), ScaleOf(zoomed));
        scale.Freeze();
        CardsList.ItemContainerStyle = new Style(typeof(ContentPresenter)) { Setters = { new Setter(LayoutTransformProperty, scale) } };
    }

    /// <summary>Shows the zoom of the view shown in the dropdown; on Compare, which has none, the dropdown is hidden.</summary>
    private void ShowZoom()
    {
        ZoomChoice.Visibility = view == UnitView.Compare ? Visibility.Collapsed : Visibility.Visible;
        if (view == UnitView.Compare) return;
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
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        StepZoom(e.Delta > 0 ? 1 : -1);
    }

    /// <summary>Ctrl+plus, Ctrl+minus and Ctrl+0 change the zoom of Cards or Table, as they change the File Editor's text size.</summary>
    private void ZoomKeys(object sender, KeyEventArgs e)
    {
        if (view == UnitView.Compare || loaded is null || Keyboard.FocusedElement is TextBox) return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        if (e.Key is Key.OemPlus or Key.Add) { e.Handled = true; StepZoom(1); }
        else if (e.Key is Key.OemMinus or Key.Subtract) { e.Handled = true; StepZoom(-1); }
        else if (e.Key is Key.D0 or Key.NumPad0) { e.Handled = true; SetZoom(UsualZoom); }
    }

    /// <summary>The next zoom offered, larger (1) or smaller (-1); none past the largest or smallest.</summary>
    private void StepZoom(int direction)
    {
        if (view == UnitView.Compare) return;
        int at = Array.IndexOf(ZoomSteps, ZoomOf(view));
        // a zoom saved by hand that is not one of the steps: the nearest step is taken first
        if (at < 0) at = Array.FindIndex(ZoomSteps, step => step >= ZoomOf(view));
        int next = Math.Clamp(at + direction, 0, ZoomSteps.Length - 1);
        SetZoom(ZoomSteps[next]);
    }

    /// <summary>Saves the shown view's zoom, draws it at that zoom, and lays its rows out again for the width they now have.</summary>
    private void SetZoom(int percent)
    {
        if (view == UnitView.Compare || percent == ZoomOf(view)) return;
        AppData.Current.Settings[ZoomSetting(view)] = percent;
        AppData.Current.SaveSettings();
        ApplyZoom(view);
        ShowZoom();
        if (loaded is null) return;
        if (view == UnitView.Cards) ShowCards();
        else ShowTable();
        // once laid out, the pictures in view are read again at the size they are now drawn, so none is blurred
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ReloadPictures(view == UnitView.Cards ? CardsList : TableList));
    }

    /// <summary>Reads again every picture in a list, for the size it is drawn at.</summary>
    private static void ReloadPictures(DependencyObject inside)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(inside); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(inside, i);
            if (child is UnitPicture picture) picture.Reload();
            else ReloadPictures(child);
        }
    }
}
