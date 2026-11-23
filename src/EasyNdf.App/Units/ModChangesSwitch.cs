using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Mods;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.Game;

namespace EasyNdf.App.Units;

/// <summary>
/// "Show mods' changes", under the Armory's Source: on, the Armory shows the values as the mods that are on make them,
/// run in the Mods tab's order; off, as the source has them, with no mod's changes. Only the mods of the source's game
/// run on its files, so only those count. "View", at the right of the switch, lists them in the order they run: many
/// mods would not fit under the switch. Under it, "Mod Being Edited": the mod the Armory's changes are saved in. It
/// starts on the mod the Armory picks itself (see <see cref="Pages.UnitsPage.ArmoryModFor"/>), and another can be chosen.
/// The Units and Weapons sub-tabs each have one; both show the same.
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
    private readonly TextBlock view = new() { VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Focusable = true, TextDecorations = TextDecorations.Underline };
    private readonly TextBlock asSource = new() { Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
    private readonly TextBlock editedLabel = new() { Margin = new Thickness(0, 8, 0, 2) };
    private readonly ComboBox edited = new();

    /// <summary>True while the list of mods is filled, so filling it is not taken for a choice.</summary>
    private bool filling;

    /// <summary>Builds the switch, "View" beside it, and the mod being edited under it.</summary>
    public ModChangesSwitch()
    {
        Margin = new Thickness(0, 6, 0, 0);
        toggle.Style = (Style)Application.Current.FindResource("Toggle");
        toggle.Content = Texts.Get("Units.ModChanges");
        toggle.ToolTip = Texts.Get("Units.ModChanges.Tip");
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Click += (_, _) => Switch(toggle.IsChecked == true);
        view.Text = Texts.Get("Units.ModChanges.View");
        view.ToolTip = Texts.Get("Units.ModChanges.Mods.Tip");
        view.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        view.FocusVisualStyle = Application.Current.TryFindResource("KeyboardFocusOutline") as Style;
        view.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenList();
        };
        view.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space) || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            OpenList();
        };
        var row = new DockPanel();
        DockPanel.SetDock(view, Dock.Right);
        row.Children.Add(view);
        row.Children.Add(toggle);
        asSource.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        asSource.Text = Texts.Get("Units.ModChanges.Off");
        editedLabel.Text = Texts.Get("Units.ModEdited");
        editedLabel.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        edited.ToolTip = Texts.Get("Units.ModEdited.Tip");
        edited.SelectionChanged += (_, _) => EditedChosen();
        Children.Add(row);
        Children.Add(asSource);
        Children.Add(editedLabel);
        Children.Add(edited);
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

    /// <summary>The mods that run on the source's files, in the order they run.</summary>
    private static IReadOnlyList<Mod> Running() => SourceGame() is GameTitle game ? ModStack.RunningFor(game) : ModStack.Running;

    /// <summary>
    /// The switch as the setting is, "View" while mods are shown, or the line that says none is; and the mod being
    /// edited, among the mods of the source's game.
    /// </summary>
    private void Show()
    {
        bool on = Pages.UnitsPage.ArmoryShowsModChanges;
        toggle.IsChecked = on;
        bool anyShown = on && Running().Count > 0;
        view.Visibility = anyShown ? Visibility.Visible : Visibility.Collapsed;
        asSource.Visibility = anyShown ? Visibility.Collapsed : Visibility.Visible;
        asSource.Text = on ? Texts.Get("Units.ModChanges.NoneOn") : Texts.Get("Units.ModChanges.Off");
        ShowEdited();
    }

    /// <summary>
    /// Fills "Mod Being Edited": the mods of the source's game that are on, in the order they run. One that is off is
    /// not listed: its changes neither show nor run. Then "New Mod…". The one the Armory changes now is chosen.
    /// </summary>
    private void ShowEdited()
    {
        GameTitle? game = SourceGame();
        editedLabel.Visibility = edited.Visibility = game is null ? Visibility.Collapsed : Visibility.Visible;
        if (game is null) return;
        filling = true;
        try
        {
            edited.Items.Clear();
            Mod now = Pages.UnitsPage.ArmoryModFor(game);
            foreach (Mod mod in ModStack.RunningFor(game))
            {
                var item = new ComboBoxItem { Content = mod.Name, Tag = mod };
                edited.Items.Add(item);
                if (ReferenceEquals(mod, now)) edited.SelectedItem = item;
            }
            if (edited.Items.Count > 0) edited.Items.Add(new Separator());
            var add = new ComboBoxItem { Content = Texts.Get("Units.ModEdited.New") };
            add.SetResourceReference(Control.ForegroundProperty, "accent");
            edited.Items.Add(add);
        }
        finally
        {
            filling = false;
        }
    }

    /// <summary>A mod was chosen to be edited: the Armory saves its changes there from now on. Or "New Mod…".</summary>
    private async void EditedChosen()
    {
        if (filling || edited.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is Mod mod)
        {
            if (SourceGame() is GameTitle game && ReferenceEquals(mod, Pages.UnitsPage.ArmoryModFor(game))) return;
            ModStack.ChooseForArmory(mod);
            AppData.Current.TellModsChanged(new ModsChange());
            return;
        }
        // "New Mod…": asked as on the Mods tab; made or not, the list then shows the mod being edited
        await MainView.Of(this).RulesTab.AskNewMod();
        Show();
    }

    /// <summary>
    /// The list "View" drops down: the mods that are on, numbered in the order they run, the one being edited marked;
    /// under it, a way to the Mods tab, where they are switched and ordered. Esc or a click elsewhere closes it.
    /// </summary>
    private void OpenList()
    {
        IReadOnlyList<Mod> mods = Running();
        if (mods.Count == 0) return;
        Mod? now = SourceGame() is GameTitle game ? Pages.UnitsPage.ArmoryModFor(game) : null;
        var lines = new StackPanel();
        Grid.SetIsSharedSizeScope(lines, true);
        for (int at = 0; at < mods.Count; at++) lines.Children.Add(ListLine(at + 1, mods[at], ReferenceEquals(mods[at], now)));
        // fifty mods are taller than the window: the list is held to part of its height and scrolls
        double windowHeight = Window.GetWindow(this)?.ActualHeight ?? SystemParameters.WorkArea.Height;
        var scroller = new ScrollViewer
        {
            Content = lines, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(120, Math.Min(windowHeight, SystemParameters.WorkArea.Height) * 0.6),
            MaxWidth = Math.Max(240, SystemParameters.WorkArea.Width * 0.6),
            FocusVisualStyle = null, Margin = new Thickness(0, 6, 0, 8),
        };
        var title = new TextBlock { Text = Texts.Get("Units.ModChanges.Shown"), FontWeight = FontWeights.Bold, FontSize = 14 };
        title.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        var note = new TextBlock
        {
            Text = Texts.CountWide("Units.ModChanges.Shown.Note", mods.Count), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 340,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0),
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var toMods = new TextBlock
        {
            Text = Texts.Get("Units.ModChanges.Shown.ToMods"), Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline, VerticalAlignment = VerticalAlignment.Center,
        };
        toMods.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        var close = new Button { Content = Texts.Get("Units.ModChanges.Shown.Close"), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 70 };
        var foot = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        foot.Children.Add(close);
        foot.Children.Add(toMods);
        var all = new StackPanel { MinWidth = 260 };
        all.Children.Add(title);
        all.Children.Add(note);
        all.Children.Add(scroller);
        all.Children.Add(foot);
        var box = new Bevel { Child = all, Padding = new Thickness(12, 8, 12, 10) };
        box.SetResourceReference(Bevel.TopLeftProperty, "hi");
        box.SetResourceReference(Bevel.BottomRightProperty, "lo");
        box.SetResourceReference(Bevel.BackgroundProperty, "panel");
        var popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = box };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            popup.IsOpen = false;
        };
        close.Click += (_, _) => popup.IsOpen = false;
        toMods.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            popup.IsOpen = false;
            MainView main = MainView.Of(this);
            main.Tabs.SelectedItem = main.RulesTabItem;
        };
        // the keyboard goes into the list, so Escape closes it and the arrow keys scroll it; it comes back to "View" after
        popup.Opened += (_, _) => scroller.Focus();
        popup.Closed += (_, _) =>
        {
            if (Keyboard.FocusedElement is null || box.IsKeyboardFocusWithin) view.Focus();
        };
        popup.IsOpen = true;
    }

    /// <summary>One mod in the list: "3  My New Units   changes saved here".</summary>
    private static Grid ListLine(int number, Mod mod, bool isEdited)
    {
        var line = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ModName" });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var place = new TextBlock { Text = Texts.Number(number) };
        place.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var name = new TextBlock { Text = mod.Name, Margin = new Thickness(0, 0, 16, 0) };
        Grid.SetColumn(name, 1);
        line.Children.Add(place);
        line.Children.Add(name);
        if (!isEdited) return line;
        var mark = new TextBlock { Text = Texts.Get("Units.ModChanges.Shown.Edited"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        Grid.SetColumn(mark, 2);
        line.Children.Add(mark);
        return line;
    }
}
