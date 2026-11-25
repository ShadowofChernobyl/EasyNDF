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
        Panel.PreviewMouseDown += (_, _) => touched = true;
        Panel.PreviewKeyDown += (_, _) => touched = true;
        Panel.PreviewDrop += (_, _) => touched = true;
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
        Panel.AwaitsName = false;
        touched = false;
        Panel.Open(copied, source, current);
        Dispatcher.BeginInvoke(() => Box.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void PanelChanged()
    {
        HintText.Text = Panel.Hint;
        HintText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DoneButton.IsEnabled = !Panel.Blocks;
    }

    /// <summary>
    /// Whether the user did anything in the panel since it opened (a click, a key, a file dropped). Done with nothing
    /// done closes the window and writes nothing: it wrote a new copy of the model's files each time.
    /// </summary>
    private bool touched;

    private void DoneClicked(object sender, RoutedEventArgs e)
    {
        if (!DoneButton.IsEnabled) return;
        if (!touched)
        {
            Close();
            return;
        }
        OwnModel? chosen;
        // the files kept now go again if the change is refused: each try would otherwise leave another copy behind
        var keptNow = new List<string>();
        try
        {
            chosen = Panel.UsesOwn ? Panel.Keep(descriptor, (bytes, name) =>
            {
                string kept = keep(bytes, name);
                keptNow.Add(kept);
                return kept;
            }) : null;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            HintText.Text = Texts.Get("Units.OwnModel.NotKept", ("problem", problem.Message));
            HintText.SetResourceReference(TextBlock.ForegroundProperty, "warn");
            DeleteKept(keptNow);
            return;
        }
        if (done(chosen)) Close();
        else DeleteKept(keptNow);
    }

    /// <summary>Deletes the model files kept for a change that was not made after all.</summary>
    private static void DeleteKept(List<string> files)
    {
        foreach (string file in files)
        {
            try { File.Delete(file); }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
        }
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

    /// <summary>Where it is kept whether the window is as large as the app's own.</summary>
    private const string FullSizeKey = "ownModelFullSize";

    /// <summary>Whether the window fills the app's window, as last chosen with the button beside the close button.</summary>
    private static bool IsFullSize => AppData.Current.State[FullSizeKey] is System.Text.Json.Nodes.JsonValue kept && kept.TryGetValue(out bool full) && full;

    private void FullSizeClicked(object sender, RoutedEventArgs e)
    {
        AppData.Current.State[FullSizeKey] = !IsFullSize;
        AppData.Current.SaveState();
        FitSize();
    }

    /// <summary>1600 wide and the panel 560 high, or as much as a smaller window has.</summary>
    private void FitSize()
    {
        if (Backdrop.ActualWidth <= 0) return;
        FullSizeButton.Content = IsFullSize ? "\uE923" : "\uE922";
        FullSizeButton.ToolTip = Texts.Get(IsFullSize ? "Main.Restore.Tip" : "Main.Maximize.Tip");
        System.Windows.Automation.AutomationProperties.SetName(FullSizeButton, (string)FullSizeButton.ToolTip);
        if (IsFullSize)
        {
            // as large as the app's window: the panel takes all the height the title and foot leave
            Box.Width = Backdrop.ActualWidth;
            Box.MaxHeight = Backdrop.ActualHeight;
            Panel.Height = Math.Max(200, Backdrop.ActualHeight - 110);
            return;
        }
        Box.Width = Math.Min(1600, Backdrop.ActualWidth * 0.96);
        Box.MaxHeight = Backdrop.ActualHeight * 0.96;
        Panel.Height = Math.Max(200, Math.Min(640, Backdrop.ActualHeight * 0.96 - 110));
    }
}
