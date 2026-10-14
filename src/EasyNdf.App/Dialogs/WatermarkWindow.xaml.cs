using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.Data;
using EasyNdf.Engine.Export;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The Watermark window (the HTML app's <c>wmOpen</c>, <c>wmDraw</c> and <c>wmClose</c>). <see cref="Edit"/> opens it
/// from the Mod box, where every change is saved as it is made; <see cref="AskBeforeExport"/> opens it before Run export,
/// where the values are a draft that Export saves, and Cancel stops the export.
/// </summary>
public partial class WatermarkWindow : UserControl
{
    private Mod? mod;
    private bool isAsking;
    private bool showing;
    private TaskCompletionSource<bool>? answer;

    /// <summary>Builds the window, closed.</summary>
    public WatermarkWindow()
    {
        InitializeComponent();
        PreviewKeyDown += KeyPressed;
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>The app's version, which the "Edited with EasyNDF" line names (set by the main view).</summary>
    public string AppVersion { get; set; } = "";

    /// <summary>"Watermark...": the open mod's watermark, saved as it is changed.</summary>
    public Task Edit(Mod openMod) => Open(openMod, asking: false);

    /// <summary>
    /// Before Run export: offers to update the watermark of a mod whose files are exported. Answers whether the export
    /// may go ahead.
    /// </summary>
    public Task<bool> AskBeforeExport(Mod exported) => Open(exported, asking: true);

    private Task<bool> Open(Mod openMod, bool asking)
    {
        mod = openMod;
        isAsking = asking;
        answer = new TaskCompletionSource<bool>();
        ModWatermark mark = WatermarkOf(openMod);

        Title.Text = asking ? "Update the Watermark?" : "Watermark: " + openMod.Name;
        Lead.Text = asking ? "Exporting with the watermark of " + openMod.Name + ". Change anything that is out of date:" : "";
        Lead.Visibility = Shown(asking);
        OnSwitch.Visibility = SavedNote.Visibility = AskTick.Visibility = DoneButton.Visibility = Shown(!asking);
        NeverTick.Visibility = ExportButton.Visibility = CancelButton.Visibility = Shown(asking);

        showing = true;
        OnSwitch.IsChecked = mark.On;
        AuthorBox.Text = mark.Author;
        VersionBox.Text = mark.Version;
        GameBox.Text = mark.Game;
        AskTick.IsChecked = mark.Ask;
        NeverTick.IsChecked = false;
        showing = false;

        Draw();
        Visibility = Visibility.Visible;
        // before an export, the mod's version is the likeliest to be out of date
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => (asking ? (Control)VersionBox : OnSwitch).Focus());
        return answer.Task;
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A mod's watermark, or an empty one, switched off, that asks on export.</summary>
    public static ModWatermark WatermarkOf(Mod mod) => mod.Watermark ?? new ModWatermark(false, "", "", "", Ask: true);

    /// <summary>The values in the window. Before an export the watermark is on, and asks again unless told not to.</summary>
    private ModWatermark Values() => ModWatermark.FromJson(new JsonObject
    {
        ["on"] = isAsking || OnSwitch.IsChecked == true,
        ["author"] = AuthorBox.Text,
        ["ver"] = VersionBox.Text,
        ["game"] = GameBox.Text,
        ["ask"] = isAsking ? NeverTick.IsChecked != true : AskTick.IsChecked == true,
    });

    /// <summary>
    /// Shows what the values mean: the boxes can be used only while the watermark is on, and the preview has only the
    /// lines an export would write.
    /// </summary>
    private void Draw()
    {
        if (mod is null) return;
        ModWatermark values = Values();
        AuthorBox.IsEnabled = VersionBox.IsEnabled = GameBox.IsEnabled = AskTick.IsEnabled = values.On;
        var lines = new List<string>();
        if (AppData.Current.Settings["stamp"]?.GetValue<bool>() != false) lines.Add("// Edited with EasyNDF " + AppVersion);
        if (values.On) lines.Add("// Mod: " + ExportFinishing.WatermarkLine(mod.Name, new Watermark(true, values.Author, values.Version, values.Game)));
        Preview.Text = string.Join("\n", lines);
        PreviewBox.Visibility = Shown(lines.Count > 0);
    }

    /// <summary>A change in the window: opened from the Mod box, it is saved at once.</summary>
    private void ValuesChanged(object sender, RoutedEventArgs e)
    {
        if (showing || mod is null) return;
        if (!isAsking) Save(Values());
        Draw();
    }

    private void Save(ModWatermark values)
    {
        if (mod is null) return;
        mod.Watermark = values;
        AppData.Current.Save(mod);
    }

    /// <summary>Done, Export, or Enter in a box: closes; before an export, the values are saved and the export goes ahead.</summary>
    private void DoneClicked(object sender, RoutedEventArgs e) => Close(go: true);

    /// <summary>Closes the window as × does: before an export, nothing is saved and the export stops.</summary>
    public void Cancel() => Close(go: false);

    /// <summary>×, Cancel, Escape or a click outside: closes; before an export, nothing is saved and the export stops.</summary>
    private void CloseClicked(object sender, RoutedEventArgs e) => Close(go: false);

    private void Close(bool go)
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        if (isAsking && go) Save(Values());
        answer?.TrySetResult(!isAsking || go);
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(go: false); }
        else if (e.Key == Key.Enter && e.OriginalSource is TextBox) { e.Handled = true; Close(go: true); }
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close(go: false);
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
