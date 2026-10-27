using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// What the weapons editor's buttons do (<see cref="LoadoutEditor"/>): the Unit pane makes each change in the open mod
/// at once; the New Unit window keeps them for the unit it makes.
/// </summary>
public interface ILoadoutActions
{
    /// <summary>Opens the Swap window for the weapon mounted somewhere (a place in the loadout as the game has it).</summary>
    void Swap(LoadoutMount mount);

    /// <summary>A copy of the weapon mounted somewhere, of the user's own, mounted there in its place, to change.</summary>
    void CopyAndChange(LoadoutMount mount);

    /// <summary>Takes the weapon mounted somewhere off, once asked (not yet tried in the game).</summary>
    void Remove(LoadoutMount mount);

    /// <summary>Takes back a change: a weapon taken off is put back, a swap undone, an added weapon taken away.</summary>
    void TakeBack(LoadoutChange change);

    /// <summary>Opens the window to pick a weapon to add to a turret (not yet tried in the game).</summary>
    void Add(LoadoutTurret turret);

    /// <summary>A right-click on a weapon: its menu (show it in the File Editor, a new rule for it...), where there is one.</summary>
    void OpenWeaponMenu(string ammunition, FrameworkElement at) { }

    /// <summary>A double-click on a weapon: it shown on the Weapons sub-tab, where it can be.</summary>
    void ShowWeapon(string ammunition) { }

    /// <summary>
    /// Puts a change in the place of another in the list of changes, as a number typed in the editor does: with no
    /// <paramref name="old"/> one, the new one is added; with no <paramref name="updated"/> one, the old one is taken
    /// back. The list's order is kept, as the weapons added are numbered in it.
    /// </summary>
    void Replace(LoadoutChange? old, LoadoutChange? updated);
}

/// <summary>What the editor's Checks say about the unit and where its changes are written.</summary>
/// <param name="ModelName">The unit whose loadout the changes are made on: the model unit, or the unit itself.</param>
/// <param name="IsNewUnit">Whether the unit is being made in the New Unit window.</param>
/// <param name="GameLoadout">The loadout the changes are made on, as the game names it.</param>
/// <param name="OwnLoadout">The loadout of its own the unit gets once changed.</param>
/// <param name="Files">The files the changes are written into.</param>
/// <param name="MissingFiles">Those of <paramref name="Files"/> not on the Files tab.</param>
public sealed record LoadoutFacts(string ModelName, bool IsNewUnit, string GameLoadout, string OwnLoadout, IReadOnlyList<string> Files, IReadOnlyList<string> MissingFiles);

/// <summary>
/// The weapons editor: a unit's loadout turret by turret, as the game's files lay it out, in a raised box each. A
/// weapon is a row: its picture, its name and kind, its ammo box with the box's salvos and how many of it are
/// mounted (both can be typed in), and Swap ▾, Copy and Change… and ✕. A changed row has a bar in the accent colour
/// at its left; a weapon taken off is faded and struck through, with Undo; each turret ends with a dashed
/// "+ Add to This Turret…". Beside it, the Checks: what the changes do, what has not been tried in the game, and how
/// they are written.
/// </summary>
public static class LoadoutEditor
{
    /// <summary>The picture's width in a row, as in the mock.</summary>
    private const double PictureWidth = 96;

    /// <summary>The highest salvos a box takes, and the highest number of a weapon at one place.</summary>
    private const int HighestSalvos = 9999, HighestCount = 99;

    /// <summary>A row narrower than this puts its buttons on a second line.</summary>
    private const double NarrowRow = 560;

    /// <summary>The turrets, each in a raised box, with the changes shown in place.</summary>
    /// <param name="original">The loadout as the game has it, which the changes' places are in.</param>
    /// <param name="changes">The unit's changes to it.</param>
    /// <param name="weapons">Every weapon by descriptor, for the names, pictures and values.</param>
    /// <param name="actions">What the buttons and boxes do.</param>
    public static FrameworkElement Build(Loadout original, IReadOnlyList<LoadoutChange> changes, IReadOnlyDictionary<string, Weapon> weapons, ILoadoutActions actions)
    {
        var editor = new StackPanel();
        foreach (LoadoutTurret turret in original.Turrets)
        {
            var rows = new StackPanel { Children = { TurretHeading(turret) } };
            foreach (LoadoutMount mount in turret.Mounts) rows.Children.Add(MountRow(original, mount, changes, weapons, actions));
            foreach (LoadoutChange add in changes.Where(change => change.Kind == LoadoutChange.Add && change.Turret == turret.Index))
                rows.Children.Add(AddedRow(original, add, changes, weapons, actions));
            rows.Children.Add(AddRow(turret, actions));
            var box = new Bevel { Child = rows, Margin = new Thickness(0, 0, 0, 7) };
            box.SetResourceReference(Bevel.TopLeftProperty, "hi");
            box.SetResourceReference(Bevel.BottomRightProperty, "lo");
            box.SetResourceReference(Bevel.BackgroundProperty, "panel");
            editor.Children.Add(box);
        }
        if (original.Turrets.Count == 0) editor.Children.Add(UnitPane.Muted(Texts.Get("Weapons.Loadout.NoTurrets"), new Thickness(0)));
        return editor;
    }

    /// <summary>A turret's heading: "Turret 1" in the accent colour, then the model's name for it, dim, with a line under it.</summary>
    private static Bevel TurretHeading(LoadoutTurret turret)
    {
        var name = new TextBlock { FontWeight = FontWeights.Bold, Text = Texts.Get("Weapons.Loadout.Turret", ("number", turret.Index + 1)) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Children = { name } };
        if (turret.Tag != "") heading.Children.Add(Small(turret.Tag, "muted", new Thickness(8, 0, 0, 0)));
        var bar = new Bevel { Child = heading, Padding = new Thickness(8, 3, 8, 3), Edges = new Thickness(0, 0, 0, 1), ToolTip = Texts.Get("Weapons.Loadout.Turret.Tip") };
        bar.SetResourceReference(Bevel.BottomRightProperty, "line");
        return bar;
    }

    // ---- the rows ----

    /// <summary>
    /// A weapon mounted in the game's loadout: what is mounted there now, with the weapon it replaced; its box and
    /// count, to type in; taken off, faded and struck through with Undo.
    /// </summary>
    private static FrameworkElement MountRow(Loadout original, LoadoutMount mount, IReadOnlyList<LoadoutChange> changes, IReadOnlyDictionary<string, Weapon> weapons, ILoadoutActions actions)
    {
        LoadoutChange? removed = LastAt(changes, LoadoutChange.Remove, mount);
        LoadoutChange? swap = LastAt(changes, LoadoutChange.Swap, mount);
        LoadoutChange? count = LastAt(changes, LoadoutChange.Count, mount);
        LoadoutChange? salvos = changes.LastOrDefault(change => change.Kind == LoadoutChange.Salvos && change.Mount == mount.AmmoBox);
        Weapon? before = weapons.GetValueOrDefault(mount.Ammunition);
        string ammunition = swap?.Ammunition ?? mount.Ammunition;
        Weapon? weapon = weapons.GetValueOrDefault(ammunition);
        string beforeName = before?.Name ?? Weapon.NameOf(mount.Ammunition);

        var name = new TextBlock { Text = weapon?.Name ?? Weapon.NameOf(ammunition), FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        var sub = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        if (removed is not null)
        {
            name.TextDecorations = TextDecorations.Strikethrough;
            sub.Text = What(weapon);
            sub.Inlines.Add(CautionRun(" · " + Texts.Get("Weapons.Loadout.Removed")));
            return Row(Picture(weapon), name, sub, null, null, Buttons(SmallButton(Texts.Get("Weapons.Loadout.PutBack"), Texts.Get("Weapons.Loadout.PutBack.Tip"), () => actions.TakeBack(removed))), changed: false, gone: true);
        }

        if (swap is not null)
        {
            MarkChanged(name, beforeName);
            sub.Text = KindOf(weapon) + " · " + Texts.Get("Weapons.Loadout.Was", ("weapon", beforeName));
            if (before is not null && weapon is not null && before.Kind != weapon.Kind)
                sub.Inlines.Add(CautionRun(" · " + Texts.Get("Weapons.Loadout.OtherKind", ("weapon", beforeName))));
        }
        else sub.Text = What(weapon);

        FrameworkElement box = BoxField(original, mount, salvos, actions);
        FrameworkElement number = CountField(mount.Count, count?.Number, updated =>
            actions.Replace(count, updated == mount.Count ? null : new LoadoutChange(LoadoutChange.Count, mount.Turret, mount.Index, Number: updated)));

        var swapButton = SmallButton(Texts.Get("Weapons.Loadout.SwapMenu"), Texts.Get("Weapons.Loadout.Swap.Tip"), () => { });
        swapButton.Click += (_, _) => OpenSwapMenu(swapButton, mount, swap, beforeName, actions);
        Panel buttons = Buttons(
            swapButton,
            SmallButton(Texts.Get("Weapons.Loadout.CopyAndChange"), Texts.Get("Weapons.Loadout.CopyAndChange.Tip"), () => actions.CopyAndChange(mount)),
            SmallButton("✕", Texts.Get("Weapons.Loadout.Remove.Tip"), () => actions.Remove(mount)));
        bool changed = swap is not null || count is not null || (salvos is not null && FirstInBox(original, mount));
        return WithMenu(Row(Picture(weapon), name, sub, box, number, buttons, changed, gone: false), ammunition, actions);
    }

    /// <summary>A weapon the user adds to a turret: its own ammo box and count, to type in, and ✕ to take it away again.</summary>
    private static FrameworkElement AddedRow(Loadout original, LoadoutChange add, IReadOnlyList<LoadoutChange> changes, IReadOnlyDictionary<string, Weapon> weapons, ILoadoutActions actions)
    {
        Weapon? weapon = weapons.GetValueOrDefault(add.Ammunition);
        var name = new TextBlock { Text = weapon?.Name ?? Weapon.NameOf(add.Ammunition), FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        if (ModMarks.AreShown) name.Inlines.Add(ModMarks.Star());
        name.ToolTip = Texts.Get("Units.Changed.By.Tip", ("mod", ModMarks.Changes.ModName));
        var sub = new TextBlock { Text = What(weapon), TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        sub.Inlines.Add(CautionRun(" · " + Texts.Get("Weapons.Loadout.Added")));

        // the added weapons' boxes come after the game's, in the order they were added
        int boxNumber = original.AmmoBoxes.Count + changes.Where(change => change.Kind == LoadoutChange.Add).TakeWhile(change => change != add).Count();
        int count = LoadoutRules.AddedCount(add);
        FrameworkElement box = Field(
            Texts.Get("Weapons.Loadout.BoxLabel", ("box", boxNumber + 1)), add.Number, null, 1, HighestSalvos, Texts.Get("Weapons.Loadout.Salvos"),
            Texts.Get("Weapons.Loadout.OwnBox.Tip"), salvosTyped => actions.Replace(add, add with { Number = salvosTyped }));
        FrameworkElement number = CountField(count, null, countTyped =>
            actions.Replace(add, add with { MountText = WithCount(add.MountText, count, countTyped) }));
        Panel buttons = Buttons(SmallButton("✕", Texts.Get("Weapons.Loadout.TakeAway.Tip"), () => actions.TakeBack(add)));
        return WithMenu(Row(Picture(weapon), name, sub, box, number, buttons, changed: true, gone: false), add.Ammunition, actions);
    }

    /// <summary>The dashed "+ Add to This Turret…" at a turret's end, with what it means beside it.</summary>
    private static FrameworkElement AddRow(LoadoutTurret turret, ILoadoutActions actions)
    {
        var label = new TextBlock { Text = Texts.Get("Weapons.Loadout.Add") };
        label.SetResourceReference(TextBlock.ForegroundProperty, "caution");
        label.Margin = new Thickness(8, 1, 8, 1);
        // a dashed outline in the caution colour, as the mock's, drawn round the words
        var outline = new System.Windows.Shapes.Rectangle { StrokeThickness = 1, StrokeDashArray = [3, 2], SnapsToDevicePixels = true };
        outline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "caution");
        var dashed = new Grid { Children = { outline, label }, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = Texts.Get("Weapons.Loadout.Add.Tip") };
        dashed.MouseLeftButtonUp += (_, _) => actions.Add(turret);
        var line = new WrapRow { Children = { dashed, Small(Texts.Get("Weapons.Loadout.Add.Note"), "muted", new Thickness(0, 2, 0, 0)) } };
        var row = new Border { Child = line, Padding = new Thickness(8, 4, 8, 4) };
        row.SetResourceReference(Border.BackgroundProperty, "bg");
        return row;
    }

    /// <summary>
    /// A row laid out as the mock's: picture, name and kind, box, count, buttons; narrow, the box, count and buttons
    /// go under the name, wrapping as the room needs. A changed row has a 3-pixel bar in the accent colour at its left;
    /// one taken off is faded.
    /// </summary>
    private static FrameworkElement Row(FrameworkElement picture, TextBlock name, TextBlock sub, FrameworkElement? box, FrameworkElement? count, Panel buttons, bool changed, bool gone)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PictureWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var names = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Children = { name, sub } };
        Place(grid, picture, 0);
        Place(grid, names, 1);
        // what goes under the name when the row is narrow (the fields keep their 8 pixels at the left, as the name's)
        var under = new WrapPanel { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        Grid.SetRow(under, 1);
        Grid.SetColumn(under, 1);
        Grid.SetColumnSpan(under, 4);
        grid.Children.Add(under);
        // each with its column at the row's right: box, count, then the buttons
        var sideParts = new List<(FrameworkElement Part, int Column)>();
        if (box is not null) sideParts.Add((box, 2));
        if (count is not null) sideParts.Add((count, 3));
        sideParts.Add((buttons, 4));
        PutSideParts(grid, under, sideParts, narrow: false);
        grid.SizeChanged += (_, _) => PutSideParts(grid, under, sideParts, grid.ActualWidth < NarrowRow);
        if (gone) grid.Opacity = 0.45;

        // the bar is drawn over the row's padding, as the mock's inset shadow, so the row's content does not move
        var row = new Bevel { Child = grid, Padding = new Thickness(changed ? 5 : 8, 4, 8, 4), Edges = new Thickness(changed ? 3 : 0, 0, 0, 1) };
        row.SetResourceReference(Bevel.BackgroundProperty, "bg");
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        if (changed) row.SetResourceReference(Bevel.TopLeftProperty, "accent");
        return row;
    }

    /// <summary>
    /// A row whose right-click opens its weapon's menu, as the weapons' list in the Unit pane did, and whose
    /// double-click shows the weapon on the Weapons sub-tab.
    /// </summary>
    private static FrameworkElement WithMenu(FrameworkElement row, string ammunition, ILoadoutActions actions)
    {
        row.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            actions.OpenWeaponMenu(ammunition, row);
        };
        row.MouseLeftButtonDown += (_, e) =>
        {
            // a double-click on the row itself, not on its buttons or numbers
            if (e.ClickCount != 2 || IsOnAControl(e.OriginalSource as DependencyObject, row)) return;
            e.Handled = true;
            actions.ShowWeapon(ammunition);
        };
        return row;
    }

    /// <summary>Whether a click is on one of a row's controls (a button, or a number's field), which take their own clicks.</summary>
    private static bool IsOnAControl(DependencyObject? clicked, FrameworkElement row)
    {
        for (DependencyObject? at = clicked; at is not null && at != row; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is ButtonBase or TextBox || at is FrameworkElement { Cursor: var cursor } && cursor == Cursors.IBeam) return true;
        return false;
    }

    private static void Place(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>
    /// The box, count and buttons: in the row's own columns at its right, or, when the row is narrow, under the name,
    /// wrapping to more lines as the room needs. They stay where they are while the row's width stays on one side.
    /// </summary>
    private static void PutSideParts(Grid grid, WrapPanel under, List<(FrameworkElement Part, int Column)> parts, bool narrow)
    {
        FrameworkElement buttons = parts[^1].Part;
        bool isNarrowNow = buttons.Parent == under;
        if (isNarrowNow == narrow && buttons.Parent is not null) return;
        foreach ((FrameworkElement part, int column) in parts)
        {
            if (part.Parent is Panel holder) holder.Children.Remove(part);
            part.VerticalAlignment = VerticalAlignment.Center;
            if (narrow)
            {
                part.Margin = new Thickness(8, 0, 0, 4);
                under.Children.Add(part);
            }
            else
            {
                part.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetRow(part, 0);
                Grid.SetColumn(part, column);
                grid.Children.Add(part);
            }
        }
        under.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The weapon's picture, 96 pixels wide, in its sunken frame.</summary>
    private static FrameworkElement Picture(Weapon? weapon) =>
        new WeaponPicture { Source = weapon?.PictureSource, Width = PictureWidth, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The buttons of a row, side by side.</summary>
    private static Panel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (Button button in buttons)
        {
            button.Margin = new Thickness(panel.Children.Count == 0 ? 0 : 4, 0, 0, 0);
            panel.Children.Add(button);
        }
        return panel;
    }

    /// <summary>Swap ▾: another weapon in this place (the Swap window), and, once swapped, the game's weapon again.</summary>
    private static void OpenSwapMenu(Button owner, LoadoutMount mount, LoadoutChange? swap, string beforeName, ILoadoutActions actions)
    {
        var menu = new ContextMenu { PlacementTarget = owner, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var other = new MenuItem { Header = Texts.Get("Weapons.Loadout.SwapOther"), ToolTip = Texts.Get("Weapons.Loadout.Swap.Tip") };
        other.Click += (_, _) => actions.Swap(mount);
        menu.Items.Add(other);
        if (swap is not null)
        {
            var back = new MenuItem { Header = Texts.Get("Weapons.Loadout.Unswap"), ToolTip = Texts.Get("Weapons.Loadout.Unswap.Tip", ("weapon", beforeName)) };
            back.Click += (_, _) => actions.TakeBack(swap);
            menu.Items.Add(back);
        }
        menu.IsOpen = true;
    }

    // ---- the box and count fields ----

    /// <summary>
    /// The ammo box of a game's mount: "Box 1 · [40] salvos" on the first weapon of the box, where its salvos are typed
    /// in; "Box 1 (shared)" on the others firing from it.
    /// </summary>
    private static FrameworkElement BoxField(Loadout original, LoadoutMount mount, LoadoutChange? salvos, ILoadoutActions actions)
    {
        string label = Texts.Get("Weapons.Loadout.BoxLabel", ("box", mount.AmmoBox + 1));
        if (!FirstInBox(original, mount))
            return Well(new TextBlock { Text = Texts.Get("Weapons.Loadout.SharedBox", ("box", mount.AmmoBox + 1)) }, Texts.Get("Weapons.Loadout.SharedBox.Tip"));
        int game = mount.AmmoBox < original.AmmoBoxes.Count ? original.AmmoBoxes[mount.AmmoBox] : 0;
        return Field(label, salvos?.Number ?? game, salvos is null ? null : game, 1, HighestSalvos, Texts.Get("Weapons.Loadout.Salvos"), Texts.Get("Weapons.Loadout.Salvos.Tip"),
            typed => actions.Replace(salvos, typed == game ? null : new LoadoutChange(LoadoutChange.Salvos, 0, mount.AmmoBox, Number: typed)));
    }

    /// <summary>"Count · ×1": how many of the weapon are mounted, typed in.</summary>
    /// <param name="game">The game's number.</param>
    /// <param name="changed">The number the mod gives, or null for the game's.</param>
    /// <param name="keep">Keeps a number typed.</param>
    private static FrameworkElement CountField(int game, int? changed, Action<int> keep) =>
        Field(Texts.Get("Weapons.Loadout.CountLabel"), changed ?? game, changed is null ? null : game, 1, HighestCount, "", Texts.Get("Weapons.Loadout.Count.Tip"), keep);

    /// <summary>
    /// A field as the mock's: a well holding a label, a number and a unit after it. The number is text until the field
    /// is clicked, then a box to type in: Enter or a click elsewhere keeps it, Escape puts it back. A changed number has
    /// the asterisk after it, with the game's value in its tooltip; a number the game does not take is put back.
    /// </summary>
    /// <param name="label">What comes before the number.</param>
    /// <param name="value">The number shown.</param>
    /// <param name="gameValue">The game's number, when the mod changes it; null when it does not.</param>
    /// <param name="lowest">The lowest number taken.</param>
    /// <param name="highest">The highest number taken.</param>
    /// <param name="unit">What comes after the number, or nothing.</param>
    /// <param name="tip">The field's tooltip.</param>
    /// <param name="keep">Keeps another number typed.</param>
    private static FrameworkElement Field(string label, int value, int? gameValue, int lowest, int highest, string unit, string tip, Action<int> keep)
    {
        string shown = value.ToString(CultureInfo.InvariantCulture);
        var number = new TextBlock { Text = shown, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 1, 0) };
        number.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        var parts = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, number } };
        if (gameValue is int game)
        {
            var star = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            if (ModMarks.AreShown) star.Inlines.Add(ModMarks.Star());
            tip += "\n" + ChangedTip(game.ToString(CultureInfo.InvariantCulture));
            parts.Children.Add(star);
        }
        if (unit != "") parts.Children.Add(new TextBlock { Text = " " + unit, VerticalAlignment = VerticalAlignment.Center });
        Border well = Well(parts, tip, new Thickness(8, 0, 0, 0));
        well.Cursor = Cursors.IBeam;
        well.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (parts.Children.OfType<TextBox>().Any()) return;
            TypeIn(parts, number, shown, lowest, highest, typed =>
            {
                if (typed != value) keep(typed);
            });
        };
        return well;
    }

    /// <summary>
    /// The number of a field swapped for a box to type in, focused with its text chosen. Enter, or the focus going
    /// elsewhere, keeps a number the game takes (the editor is then drawn again); Escape, or a number it does not take,
    /// puts the text back.
    /// </summary>
    private static void TypeIn(Panel parts, TextBlock number, string shown, int lowest, int highest, Action<int> keep)
    {
        var box = new TextBox
        {
            Text = shown, MinWidth = 34, Padding = new Thickness(1, 0, 1, 0), BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center, FontSize = 11.5,
        };
        box.SetResourceReference(Control.BackgroundProperty, "panel");
        box.SetResourceReference(Control.ForegroundProperty, "ink");
        int at = parts.Children.IndexOf(number);
        parts.Children.RemoveAt(at);
        parts.Children.Insert(at, box);
        bool done = false;
        void Finish(bool keepIt)
        {
            if (done) return;
            done = true;
            bool taken = int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int typed) && typed >= lowest && typed <= highest;
            parts.Children.Remove(box);
            parts.Children.Insert(at, number);
            if (keepIt && taken) keep(typed);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            // the window's own Enter (Create) and Escape (close) wait for the box
            e.Handled = true;
            Finish(keepIt: e.Key == Key.Enter);
        };
        box.LostKeyboardFocus += (_, _) => Finish(keepIt: true);
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
    }

    /// <summary>The mock's field: 20 pixels high, a thin line round a well, dim 11.5-pixel text.</summary>
    private static Border Well(UIElement content, string tip, Thickness? margin = null)
    {
        var well = new Border
        {
            Child = content, Height = 20, Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(1), ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center, Margin = margin ?? new Thickness(8, 0, 0, 0),
        };
        well.SetResourceReference(Border.BackgroundProperty, "well");
        well.SetResourceReference(Border.BorderBrushProperty, "line");
        well.SetResourceReference(TextElement.ForegroundProperty, "muted");
        TextElement.SetFontSize(well, 11.5);
        return well;
    }

    /// <summary>A mount's text with another number of the weapon: "NbWeapons = 1" becomes "NbWeapons = 2".</summary>
    private static string WithCount(string mountText, int count, int newCount) =>
        mountText.Replace("NbWeapons = " + count.ToString(CultureInfo.InvariantCulture), "NbWeapons = " + newCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>Whether a mount is the first of the loadout firing from its ammo box: where the box's salvos are typed in.</summary>
    private static bool FirstInBox(Loadout loadout, LoadoutMount mount) =>
        loadout.Mounts.First(each => each.AmmoBox == mount.AmmoBox) == mount;

    // ---- the Checks ----

    /// <summary>
    /// The Checks beside the turrets: a line for each thing the changes do, ✓ when the game takes it, ⚠ for what works
    /// differently or has not been tried in the game, ✕ for what stops it; then how the changes are written.
    /// </summary>
    public static FrameworkElement Checks(Loadout original, IReadOnlyList<LoadoutChange> changes, IReadOnlyDictionary<string, Weapon> weapons, LoadoutFacts facts)
    {
        var panel = new StackPanel { Children = { Heading(Texts.Get("Weapons.Checks")) } };
        Loadout changed = LoadoutRules.Changed(original, changes, facts.OwnLoadout);
        string NameOf(string ammunition) => weapons.GetValueOrDefault(ammunition)?.Name ?? Weapon.NameOf(ammunition);

        foreach (IGrouping<int, LoadoutMount> box in changed.Mounts.GroupBy(mount => mount.AmmoBox).Where(group => group.Count() > 1))
        {
            List<Weapon?> sharing = [.. box.Select(mount => weapons.GetValueOrDefault(mount.Ammunition))];
            string names = string.Join(", ", box.Select(mount => NameOf(mount.Ammunition)).Distinct());
            double? shots = sharing.FirstOrDefault()?.ShotsPerSalvo;
            bool same = sharing.All(weapon => weapon?.ShotsPerSalvo == shots);
            panel.Children.Add(same
                ? Check("✓", "ok", Texts.Get("Weapons.Checks.SharedBox", ("weapons", names), ("box", box.Key + 1), ("shots", UnitNumbers.Of(shots))))
                : Check("✕", "warn", Texts.Get("Weapons.Checks.SharedBoxDiffers", ("weapons", names), ("box", box.Key + 1))));
        }
        bool adds = changes.Any(change => change.Kind == LoadoutChange.Add);
        if (!adds) panel.Children.Add(Check("✓", "ok", Texts.Get("Weapons.Checks.Places", ("model", facts.ModelName))));
        foreach (LoadoutChange swap in changes.Where(change => change.Kind == LoadoutChange.Swap))
        {
            if (original.Mounts.FirstOrDefault(mount => mount.Turret == swap.Turret && mount.Index == swap.Mount) is not LoadoutMount place) continue;
            Weapon? was = weapons.GetValueOrDefault(place.Ammunition), now = weapons.GetValueOrDefault(swap.Ammunition);
            if (was is not null && now is not null && was.Kind != now.Kind)
                panel.Children.Add(Check("⚠", "caution", Texts.Get("Weapons.Checks.OtherKind", ("weapon", now.Name), ("was", was.Name))));
        }
        int addedBox = original.AmmoBoxes.Count;
        foreach (LoadoutChange add in changes.Where(change => change.Kind == LoadoutChange.Add))
            panel.Children.Add(Check("⚠", "caution", Texts.Get("Weapons.Checks.Added", ("weapon", NameOf(add.Ammunition)), ("turret", add.Turret + 1), ("box", ++addedBox))));
        foreach (LoadoutChange remove in changes.Where(change => change.Kind == LoadoutChange.Remove))
        {
            string ammunition = original.Mounts.FirstOrDefault(mount => mount.Turret == remove.Turret && mount.Index == remove.Mount)?.Ammunition ?? "";
            panel.Children.Add(Check("⚠", "caution", Texts.Get("Weapons.Checks.Removed", ("weapon", NameOf(ammunition)), ("turret", remove.Turret + 1))));
        }
        string files = string.Join(", ", facts.Files);
        panel.Children.Add(facts.MissingFiles.Count == 0
            ? Check("✓", "ok", Texts.Get("Weapons.Checks.Files", ("files", files)))
            : Check("✕", "warn", Texts.Get("Weapons.Checks.FilesMissing", ("files", string.Join(", ", facts.MissingFiles)))));

        TextBlock heading = Heading(Texts.Get("Weapons.Checks.WrittenAs"));
        heading.Margin = new Thickness(0, 12, 0, 6);
        panel.Children.Add(heading);
        panel.Children.Add(WrittenAs(changes.Count > 0, facts));
        return panel;
    }

    /// <summary>
    /// How the changes are written: "A loadout of its own, WeaponDescriptor_EZ_…, copied from the M1A1 ABRAMS's. The
    /// M1A1 ABRAMS keeps its own."; with none, the loadout the unit carries.
    /// </summary>
    private static TextBlock WrittenAs(bool anyChange, LoadoutFacts facts)
    {
        string key = !anyChange ? (facts.IsNewUnit ? "Weapons.Checks.NoChangeNew" : "Weapons.Checks.NoChange")
            : facts.IsNewUnit ? "Weapons.Checks.OwnCopy" : "Weapons.Checks.OwnLoadout";
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        string sentence = Texts.Get(key, ("loadout", "\u0001"), ("from", "\u0002"), ("model", facts.ModelName));
        // the loadouts' names in the code font, as the game writes them
        foreach (string part in sentence.Split('\u0001'))
        {
            if (text.Inlines.Count > 0) text.Inlines.Add(Code(facts.OwnLoadout));
            string[] pieces = part.Split('\u0002');
            for (int index = 0; index < pieces.Length; index++)
            {
                if (index > 0) text.Inlines.Add(Code(facts.GameLoadout));
                text.Inlines.Add(new Run(pieces[index]));
            }
        }
        return text;
    }

    private static Run Code(string text)
    {
        var run = new Run(text);
        run.SetResourceReference(TextElement.FontFamilyProperty, "mono");
        return run;
    }

    /// <summary>A heading of the Checks, as the panes' headings: bold, in the accent colour.</summary>
    private static TextBlock Heading(string text)
    {
        var heading = new TextBlock { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        return heading;
    }

    /// <summary>A line of the Checks: its sign in its colour, then the text.</summary>
    private static FrameworkElement Check(string sign, string colour, string text)
    {
        var mark = new TextBlock { Text = sign, Width = 16, FontWeight = FontWeights.Bold };
        mark.SetResourceReference(TextBlock.ForegroundProperty, colour);
        var words = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
        DockPanel.SetDock(mark, Dock.Left);
        return new DockPanel { Margin = new Thickness(0, 0, 0, 5), Children = { mark, words } };
    }

    // ---- small parts ----

    /// <summary>The last change of a kind at a place in the game's loadout; null when there is none.</summary>
    private static LoadoutChange? LastAt(IReadOnlyList<LoadoutChange> changes, string kind, LoadoutMount mount) =>
        changes.LastOrDefault(change => change.Kind == kind && change.Turret == mount.Turret && change.Mount == mount.Index);

    /// <summary>Puts the asterisk after a swapped weapon's name, with a tooltip naming the mod and the game's weapon.</summary>
    private static void MarkChanged(TextBlock name, string gameWeapon)
    {
        if (ModMarks.AreShown) name.Inlines.Add(ModMarks.Star());
        name.ToolTip = ChangedTip(gameWeapon);
    }

    /// <summary>"Changed by WARNO Tactical Overhaul", then "Game value: 40".</summary>
    private static string ChangedTip(string gameValue) =>
        Texts.Get("Units.Changed.By.Tip", ("mod", ModMarks.Changes.ModName)) + "\n" + Texts.Get("Units.Changed.Game.Tip", ("value", gameValue));

    /// <summary>A weapon's kind, damage and range: "Gun · AP 32 · range 2,275".</summary>
    private static string What(Weapon? weapon)
    {
        if (weapon is null) return UnitNumbers.None;
        string range = WeaponNames.Range(weapon.LongestRange);
        return WeaponNames.KindAndDamage(weapon) + (range == UnitNumbers.None ? "" : " · " + Texts.Get("Weapons.Loadout.Range", ("range", range)));
    }

    private static string KindOf(Weapon? weapon) => weapon is null ? UnitNumbers.None : WeaponNames.Kind(weapon);

    /// <summary>Text in the caution colour, inside a line.</summary>
    private static Run CautionRun(string text)
    {
        var run = new Run(text);
        run.SetResourceReference(TextElement.ForegroundProperty, "caution");
        return run;
    }

    private static TextBlock Small(string text, string colour, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = 11.5, Margin = margin, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, colour);
        return block;
    }

    /// <summary>A small button, as Compare's: 20 pixels high, 11.5-pixel text.</summary>
    private static Button SmallButton(string text, string tip, Action click)
    {
        var button = new Button { Content = text, ToolTip = tip, MinHeight = 20, Padding = new Thickness(7, 0, 7, 0), FontSize = 11.5 };
        button.Click += (_, _) => click();
        return button;
    }
}
