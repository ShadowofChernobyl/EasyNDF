using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The Armory's Change 3D Model window: a unit's own model, or one of the user's own for it (see
/// <see cref="OwnModel"/>), chosen in the 3D Model panel. Done keeps the choice as the unit's, written as rules in the
/// Mods tab; Cancel closes without keeping it.
/// </summary>
public partial class OwnModelWindow : UserControl
{
    private Func<OwnModel?, bool> done = _ => true;
    private Func<byte[], string, string> keep = (_, name) => name;
    private string descriptor = "";

    public OwnModelWindow()
    {
        InitializeComponent();
    }

    /// <summary>Opens the window for a unit.</summary>
    /// <param name="unit">The unit changed, as the Armory shows it.</param>
    /// <param name="copied">The unit whose look it has (itself, or for a unit the mod adds, the unit it is made from): its model's parts are the ones a file must have.</param>
    /// <param name="source">Where its model is read from.</param>
    /// <param name="current">The model of the user's own it has; null for its own.</param>
    /// <param name="keepFile">Keeps a model's file with the app's pictures, under a name; returns where.</param>
    /// <param name="onDone">Gives the unit the model chosen (null for the copied unit's own); false when it could not be kept, which keeps the window open.</param>
    public void Open(Unit unit, Unit copied, UnitSource source, OwnModel? current, Func<byte[], string, string> keepFile, Func<OwnModel?, bool> onDone)
    {
        descriptor = unit.Descriptor;
        keep = keepFile;
        done = onDone;
        UnitText.Text = unit.Name;
        Visibility = Visibility.Visible;
        FitSize();
        Panel.Descriptor = unit.Descriptor;
        Panel.Open(copied, source, current);
        Dispatcher.BeginInvoke(() => Box.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void PanelChanged()
    {
        HintText.Text = Panel.Hint;
        HintText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DoneButton.IsEnabled = !Panel.Blocks;
    }

    private void DoneClicked(object sender, RoutedEventArgs e)
    {
        if (!DoneButton.IsEnabled) return;
        OwnModel? chosen;
        try
        {
            chosen = Panel.UsesOwn ? Panel.Keep(descriptor, keep) : null;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            HintText.Text = Texts.Get("Units.OwnModel.NotKept", ("problem", problem.Message));
            HintText.SetResourceReference(TextBlock.ForegroundProperty, "warn");
            return;
        }
        if (done(chosen)) Close();
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => Close();

    private void Close()
    {
        Visibility = Visibility.Collapsed;
        Panel.Close();
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e) => Close();

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void BackdropResized(object sender, SizeChangedEventArgs e) => FitSize();

    /// <summary>1600 wide and the panel 560 high, or as much as a smaller window has.</summary>
    private void FitSize()
    {
        if (Backdrop.ActualWidth <= 0) return;
        Box.Width = Math.Min(1600, Backdrop.ActualWidth * 0.96);
        Box.MaxHeight = Backdrop.ActualHeight * 0.96;
        Panel.Height = Math.Max(200, Math.Min(640, Backdrop.ActualHeight * 0.96 - 110));
    }
}
