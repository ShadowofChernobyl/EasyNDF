using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Files;
using EasyNdf.App.Mods;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.Game;

namespace EasyNdf.App.Units;

/// <summary>
/// "Show mods' changes", under the Armory's Source: on, the Armory shows the values as the mods that are on make them,
/// run in the Mods tab's order; off, as the source has them, with no mod's changes. Only the mods of the source's game
/// run on its files, so only those are shown. Under the switch, those mods, whole names only, then "2 more" (as the
/// Files tab's Mods column); a click lists them all. The Units and Weapons sub-tabs each have one; both show the same
/// setting.
/// </summary>
public sealed class ModChangesSwitch : StackPanel
{
    /// <summary>Raised when the switch is turned on or off, in any of the Armory's sub-tabs.</summary>
    public static event Action? Changed;

    /// <summary>The game of the Armory's source, set by the main view; null while none is chosen.</summary>
    public static Func<GameTitle?> SourceGame { get; set; } = () => null;

    /// <summary>Raised when another source is chosen: the mods listed are those of its game.</summary>
    private static event Action? SourceChanged;

    /// <summary>Another source was chosen: every switch lists the mods of its game.</summary>
    public static void ShowSourceChanged() => SourceChanged?.Invoke();

    private readonly CheckBox toggle = new();
    private readonly ModsCell names = new() { Padding = new Thickness(0, 3, 0, 0) };
    private readonly TextBlock asSource = new() { Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };

    /// <summary>Builds the switch, with the mods that are on under it.</summary>
    public ModChangesSwitch()
    {
        Margin = new Thickness(0, 6, 0, 0);
        toggle.Style = (Style)Application.Current.FindResource("Toggle");
        toggle.Content = Texts.Get("Units.ModChanges");
        toggle.ToolTip = Texts.Get("Units.ModChanges.Tip");
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Click += (_, _) => Switch(toggle.IsChecked == true);
        asSource.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        // small and dim, as the line under the switch in the mock
        names.SetValue(TextElement.FontSizeProperty, 11.5);
        names.SetResourceReference(TextElement.ForegroundProperty, "muted");
        asSource.Text = Texts.Get("Units.ModChanges.Off");
        Children.Add(toggle);
        Children.Add(names);
        Children.Add(asSource);
        // the tabs live as long as the app, so the switch listens for as long
        AppData.Current.ModsChanged += _ => Show();
        Changed += Show;
        SourceChanged += Show;
        Show();
    }

    /// <summary>Keeps the choice in the settings, and tells the Armory to show its values again.</summary>
    private static void Switch(bool on)
    {
        AppData.Current.Settings["armoryModChanges"] = on;
        AppData.Current.SaveSettings();
        Changed?.Invoke();
    }

    /// <summary>The switch as the setting is, and the mods of the source's game that are on under it, or "As the source has them".</summary>
    private void Show()
    {
        bool on = Pages.UnitsPage.ArmoryShowsModChanges;
        toggle.IsChecked = on;
        IReadOnlyList<Mod> mods = SourceGame() is GameTitle game ? ModStack.RunningFor(game) : ModStack.Running;
        List<string> running = [.. mods.Select(mod => mod.Name)];
        names.Show(running, Texts.Get("Units.ModChanges.Mods.Tip"));
        names.Visibility = on && running.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        asSource.Visibility = names.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        asSource.Text = on ? Texts.Get("Units.ModChanges.NoneOn") : Texts.Get("Units.ModChanges.Off");
    }
}
