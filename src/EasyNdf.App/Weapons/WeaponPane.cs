using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// The Weapon pane beside or below the weapons, the Unit pane's twin, laid out as the mock's: the selected weapon's
/// picture (✎ gives it another weapon's), name and what it is, buttons to show it in the File Editor, make a rule for
/// it, compare it or copy it, the box saying how many units share it, then its sections, folding as the Unit pane's
/// do: every value of its ammunition (WeaponPane.Values.cs), Used By and Files. Where the tab lets it, a click on a
/// value changes it (<see cref="ClickToChange"/>), for every unit carrying the weapon, which the pane says first;
/// ticking units under Used By gives them a copy of their own instead.
/// </summary>
public static partial class WeaponPane
{
    /// <summary>What the folding of the pane's sections is kept under.</summary>
    private const string FoldsKey = "weaponPane";

    /// <summary>The widest the weapon's picture is drawn.</summary>
    private const double PictureWidth = 300;

    /// <summary>The pane's content for a weapon; for none, a line saying to click one.</summary>
    /// <param name="weapon">The weapon, as the open mod makes it.</param>
    /// <param name="units">The units, by descriptor, to name those carrying it.</param>
    /// <param name="actions">What the tab does with a weapon.</param>
    /// <param name="compare">Puts the weapon on the sub-tab's Compare.</param>
    public static FrameworkElement Build(Weapon? weapon, IReadOnlyDictionary<string, Unit> units, IWeaponActions actions, Action<Weapon> compare)
    {
        if (weapon is null) return UnitPane.Muted(Texts.Get("Weapons.Pane.ClickAWeapon"), new Thickness(10));
        var pane = new StackPanel();
        (FoldSection usedBy, TextBox? copyName) = UsedBy(weapon, units, actions);
        // Copy...: Used By opened, with the copy's name ready to type
        void OpenCopy()
        {
            usedBy.Open();
            if (copyName is null) return;
            copyName.Focus();
            copyName.SelectAll();
        }
        pane.Children.Add(Head(weapon, actions, compare, OpenCopy));
        foreach (ValueSection section in ValueSections) pane.Children.Add(ValuesSection(section, weapon, actions));
        pane.Children.Add(usedBy);
        pane.Children.Add(Files(weapon));
        return pane;
    }

    /// <summary>
    /// The head, as the mock's: the picture (with its ✎ where it can be changed), the name, what the weapon is ("Gun ·
    /// Kinetic [its traits' pictures] · its descriptor"), the buttons, and, when it can be changed and more than one
    /// unit carries it, a box in the caution colour saying a change reaches all of them; 10 pixels in.
    /// </summary>
    private static StackPanel Head(Weapon weapon, IWeaponActions actions, Action<Weapon> compare, Action openCopy)
    {
        var name = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        WeaponMarks.Show(name, weapon, WeaponField.Name, weapon.Name, each => each.Name);
        var nameCell = new Bevel { Child = name, Margin = new Thickness(0, 8, 0, 0) };
        // a copy of the mod's own is renamed here; the game's weapons keep the name made from their descriptor
        if (actions.IsCopy(weapon))
            ClickToChange.WithBox(nameCell, weapon.Name, typed => typed.Trim() != "", typed => actions.ChangeWeapon(weapon, WeaponField.Name, typed), Texts.Get("Weapons.Pane.Name.Tip"));
        var head = new StackPanel
        {
            Margin = new Thickness(10, 10, 10, 10),
            Children = { PictureWithPencil(weapon, actions), nameCell, What(weapon), Buttons(weapon, actions, compare, openCopy) },
        };
        if (actions.CanChangeWeapon(weapon) && weapon.Users.Count > 1) head.Children.Add(SharedBox(weapon, openCopy));
        return head;
    }

    /// <summary>
    /// The picture, at most 300 wide; for a weapon whose values can be changed, with a small ✎ on its bottom right
    /// corner, as a unit's picture has, whose menu gives it another weapon's picture or its own again.
    /// </summary>
    private static FrameworkElement PictureWithPencil(Weapon weapon, IWeaponActions actions)
    {
        var picture = new WeaponPicture { Source = weapon.PictureSource };
        var holder = new Grid { MaxWidth = PictureWidth, HorizontalAlignment = HorizontalAlignment.Left, Children = { picture } };
        // a weapon with no picture of its own has no texture for a rule to change
        if (!actions.CanChangeWeapon(weapon) || weapon.PictureTexture is null) return holder;
        var pencil = new Button
        {
            Content = "✎", Style = (Style)Application.Current.FindResource("HeadingButton"), Padding = new Thickness(0), Width = 18, MinHeight = 18, Height = 18,
            FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 4),
            ToolTip = Texts.Get("Weapons.Picture.Tip"),
        };
        pencil.Click += (_, _) => WeaponPicturePicker.OpenMenu(pencil, actions.WeaponPictures, actions.HasOtherPicture(weapon), texture => actions.ChangePicture(weapon, texture));
        holder.Children.Add(pencil);
        // another weapon's picture: the asterisk at the picture's top right, and the tooltip naming the mod and the game's picture
        if (WeaponMarks.Changes.Of(weapon) is WeaponChange change && change.Changed(WeaponField.Picture))
        {
            holder.ToolTip = WeaponMarks.TipFor(change, WeaponField.Picture, each => each.PictureTexture ?? UnitNumbers.None);
            if (ModMarks.AreShown)
                holder.Children.Add(new TextBlock { Inlines = { ModMarks.Star() }, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 6, 0) });
        }
        return holder;
    }

    /// <summary>
    /// What the weapon is, dim: its kind, its traits by name with their pictures, and its descriptor in the code font:
    /// "Gun · Kinetic [KE] · Ammo_Canon_AP_120_mm_L52_M68".
    /// </summary>
    private static WrapPanel What(Weapon weapon)
    {
        var line = new WrapPanel();
        TextElement.SetFontSize(line, 12);
        line.SetResourceReference(TextElement.ForegroundProperty, "muted");
        line.Children.Add(new TextBlock { Text = WeaponNames.Kind(weapon) + " · " });
        if (weapon.Traits.Count > 0)
        {
            line.Children.Add(new TextBlock { Text = TraitIcon.NamesOf(weapon) + " " });
            StackPanel icons = TraitIcon.RowFor(weapon, 16);
            if (icons.Children.Count > 0) line.Children.Add(icons);
            line.Children.Add(new TextBlock { Text = " · " });
        }
        var descriptor = new TextBlock { Text = weapon.Descriptor, TextWrapping = TextWrapping.Wrap };
        descriptor.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        line.Children.Add(descriptor);
        return line;
    }

    /// <summary>
    /// The box saying the weapon is shared: "⚠ 3 units carry it: a change here changes all of them. Copy... makes one
    /// of your own for chosen units.", with a border and a faint background in the caution colour; "Copy..." opens
    /// Used By with its copy's name.
    /// </summary>
    private static Border SharedBox(Weapon weapon, Action openCopy)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        string sentence = Texts.Get("Weapons.Pane.Shared", ("units", "\u0001"), ("copy", "\u0002"));
        // the sentence with its two places: the units in bold, and Copy... as a link
        string[] pieces = sentence.Split('\u0001', '\u0002');
        int at = 0;
        for (int index = 0; index < pieces.Length; index++)
        {
            text.Inlines.Add(new Run(pieces[index]));
            if (index == pieces.Length - 1) break;
            at += pieces[index].Length;
            char marker = sentence[at];
            at++;
            if (marker == '\u0001') text.Inlines.Add(new Bold(new Run(Texts.Count("Weapons.UnitCount", weapon.Users.Count))));
            else text.Inlines.Add(CopyLink(openCopy));
        }
        var sign = new TextBlock { Text = "⚠", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 8, 0) };
        sign.SetResourceReference(TextBlock.ForegroundProperty, "caution");
        DockPanel.SetDock(sign, Dock.Left);
        var box = new Border { Child = new DockPanel { Children = { sign, text } }, BorderThickness = new Thickness(1), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 8, 0, 0) };
        box.SetResourceReference(Border.BorderBrushProperty, "caution");
        box.Background = FaintCaution(box);
        return box;
    }

    /// <summary>"Copy...", underlined, as a link: it opens Used By with its copy's name.</summary>
    private static Hyperlink CopyLink(Action openCopy)
    {
        var link = new Hyperlink(new Run(Texts.Get("Weapons.Pane.Copy"))) { ToolTip = Texts.Get("Weapons.Pane.Copy.Tip") };
        link.SetResourceReference(TextElement.ForegroundProperty, "ink");
        link.Click += (_, _) => openCopy();
        return link;
    }

    /// <summary>The caution colour, faint, as the shared box's background; it follows the theme.</summary>
    private static Brush FaintCaution(FrameworkElement owner)
    {
        var brush = new SolidColorBrush { Opacity = 0.12 };
        void Recolour() => brush.Color = (owner.TryFindResource("caution") as SolidColorBrush)?.Color ?? Colors.Goldenrod;
        owner.Loaded += (_, _) => Recolour();
        Recolour();
        return brush;
    }

    /// <summary>Show in File Editor (dim while its file is not on the Files tab), New Rule…, Compare and Copy…, as the mock's.</summary>
    private static WrapRow Buttons(Weapon weapon, IWeaponActions actions, Action<Weapon> compare, Action openCopy)
    {
        var showInEditor = new Button { Content = Texts.Get("Units.Pane.ShowInEditor"), IsEnabled = actions.CanShowInEditor(weapon) };
        showInEditor.ToolTip = showInEditor.IsEnabled ? Texts.Get("Weapons.Pane.ShowInEditor.Tip") : Texts.Get("Units.Menu.NotLoaded.Tip", ("file", weapon.File));
        ToolTipService.SetShowOnDisabled(showInEditor, true);
        showInEditor.Click += (_, _) => actions.ShowInEditor(weapon);
        var newRule = new Button { Content = Texts.Get("Units.Pane.NewRule"), ToolTip = Texts.Get("Weapons.Pane.NewRule.Tip") };
        newRule.Click += (_, _) => actions.NewRuleFor(weapon);
        var compareButton = new Button { Content = Texts.Get("Units.Compare"), ToolTip = Texts.Get("Weapons.Pane.Compare.Tip") };
        compareButton.Click += (_, _) => compare(weapon);
        var row = new WrapRow { Gap = 4, Margin = new Thickness(0, 8, 0, 0), Children = { showInEditor, newRule, compareButton } };
        if (actions.CanChangeWeapon(weapon))
        {
            var copy = new Button { Content = Texts.Get("Weapons.Pane.Copy"), ToolTip = Texts.Get("Weapons.Pane.Copy.Tip") };
            copy.Click += (_, _) => openCopy();
            row.Children.Add(copy);
        }
        return row;
    }

    /// <summary>A folding section of the pane.</summary>
    private static FoldSection Section(string key, string titleKey, UIElement contents, string summary = "", bool foldedAtFirst = false) =>
        new(key, Texts.Get(titleKey), summary, contents, FoldsKey, foldedAtFirst);

    /// <summary>
    /// Used By: the units that carry the weapon, as links that show each on the Units sub-tab; folded, how many. Where
    /// the tab lets it, a tick before each loadout (the units sharing one go together), and what to do with the units
    /// ticked: give them a copy of their own, give them one of the mod's copies already made, or for a copy, give them
    /// the weapon it was copied from again. With the section, the box of the copy's name, which Copy... goes to.
    /// </summary>
    private static (FoldSection Section, TextBox? CopyName) UsedBy(Weapon weapon, IReadOnlyDictionary<string, Unit> units, IWeaponActions actions)
    {
        var contents = new StackPanel();
        TextBox? copyName = null;
        if (!actions.CanChangeWeapon(weapon) || weapon.Users.Count == 0)
        {
            contents.Children.Add(UserLinks(weapon.Users, units, actions));
        }
        else
        {
            var ticked = new List<string>();
            (StackPanel controls, Action sayHowMany, copyName) = CopyControls(weapon, ticked, actions);
            var ticks = new StackPanel();
            foreach ((string loadout, List<Unit> sharing) in Loadouts(weapon, units))
                ticks.Children.Add(LoadoutTick(loadout, sharing, ticked, sayHowMany, actions));
            contents.Children.Add(UnitPane.Muted(Texts.Get(actions.IsCopy(weapon) ? "Weapons.Pane.TickForCopy" : "Weapons.Pane.Tick"), new Thickness(0, 0, 0, 6)));
            contents.Children.Add(UnitPane.Boxed(ticks));
            contents.Children.Add(controls);
            List<string> withoutLoadout = [.. weapon.Users.Where(user => units.TryGetValue(user, out Unit? unit) && unit.WeaponSet is null)];
            if (withoutLoadout.Count > 0) contents.Children.Add(UserLinks(withoutLoadout, units, actions));
        }
        return (Section("users", "Weapons.Pane.CarriedBy", contents, Texts.Count("Weapons.UnitCount", weapon.Users.Count)), copyName);
    }

    /// <summary>The units carrying the weapon as links that show each on the Units sub-tab.</summary>
    private static FrameworkElement UserLinks(IEnumerable<string> users, IReadOnlyDictionary<string, Unit> units, IWeaponActions actions)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string descriptor in users)
            names.TryAdd(units.TryGetValue(descriptor, out Unit? unit) ? unit.Name : descriptor, descriptor);
        FrameworkElement tags = UnitPane.Tags(names.Keys, Texts.Get("Weapons.Pane.CarriedByNone"), clicked: name => actions.ShowUnit(names[name]));
        foreach (Bevel tag in (tags as Panel)?.Children.OfType<Bevel>() ?? [])
            if (tag.Child is TextBlock text) text.ToolTip = Texts.Get("Weapons.Pane.Unit.Tip", ("unit", text.Text));
        return tags;
    }

    /// <summary>The loadouts of the units carrying the weapon, each with the units that share it, in the order the units are listed.</summary>
    private static List<(string Loadout, List<Unit> Sharing)> Loadouts(Weapon weapon, IReadOnlyDictionary<string, Unit> units)
    {
        var loadouts = new List<(string, List<Unit>)>();
        foreach (string user in weapon.Users)
        {
            if (!units.TryGetValue(user, out Unit? unit) || unit.WeaponSet is not string loadout) continue;
            int at = loadouts.FindIndex(each => each.Item1 == loadout);
            if (at < 0) loadouts.Add((loadout, [unit]));
            else loadouts[at].Item2.Add(unit);
        }
        return loadouts;
    }

    /// <summary>
    /// A loadout's row: its tick, and the units sharing it, each a link to the Units sub-tab. Units sharing a loadout
    /// (a unit the mod adds shares its model's) can only carry the same weapons, so they are ticked together.
    /// </summary>
    /// <param name="loadout">The loadout, by descriptor.</param>
    /// <param name="sharing">The units with it.</param>
    /// <param name="ticked">The loadouts ticked, which the tick adds itself to or takes itself out of.</param>
    /// <param name="tickedChanged">Tells the buttons below how many are ticked now.</param>
    /// <param name="actions">What the tab does with a weapon.</param>
    private static Bevel LoadoutTick(string loadout, List<Unit> sharing, List<string> ticked, Action tickedChanged, IWeaponActions actions)
    {
        var tick = new CheckBox { Style = (Style)Application.Current.FindResource("Tick"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        tick.ToolTip = sharing.Count > 1 ? Texts.Get("Weapons.Pane.Tick.Shared.Tip", ("units", string.Join(", ", sharing.Select(unit => unit.Name)))) : Texts.Get("Weapons.Pane.Tick.Tip");
        tick.Click += (_, _) =>
        {
            ticked.Remove(loadout);
            if (tick.IsChecked == true) ticked.Add(loadout);
            tickedChanged();
        };
        var names = new WrapPanel();
        foreach (Unit unit in sharing)
        {
            var link = new TextBlock { Text = unit.Name, Margin = new Thickness(0, 0, 10, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = Texts.Get("Weapons.Pane.Unit.Tip", ("unit", unit.Name)) };
            link.MouseEnter += (_, _) => link.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            link.MouseLeave += (_, _) => link.ClearValue(TextBlock.ForegroundProperty);
            link.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                actions.ShowUnit(unit.Descriptor);
            };
            names.Children.Add(link);
        }
        // how many units the row stands for, at its right, as the nations' rows have
        var count = new TextBlock { Text = Texts.Number(sharing.Count), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        count.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var row = new Bevel { Child = new DockPanel { Children = { tick, count, names } }, Padding = new Thickness(7, 2, 7, 2), Edges = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(tick, Dock.Left);
        DockPanel.SetDock(count, Dock.Right);
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        return row;
    }

    /// <summary>
    /// Under the ticks: the new copy's name and "Copy for N units"; "Give to X" for each copy the mod has already made
    /// of the weapon; for a copy, "Give back X", the weapon it was copied from. Each is dim until a unit is ticked,
    /// but the copy, which can be made for none (to give units later). A note says what a copy is. Given back with
    /// what makes the buttons say how many units are ticked, which the ticks call.
    /// </summary>
    private static (StackPanel Controls, Action SayHowMany, TextBox NameBox) CopyControls(Weapon weapon, List<string> ticked, IWeaponActions actions)
    {
        var nameBox = new TextBox { Text = Texts.Get("Weapons.Copy.DefaultName", ("weapon", weapon.Name)), ToolTip = Texts.Get("Weapons.Copy.Name.Tip"), MinWidth = 160 };
        var copy = new Button { Style = (Style)Application.Current.FindResource("GoButton"), ToolTip = Texts.Get("Weapons.Copy.Tip") };
        copy.Click += (_, _) =>
        {
            if (nameBox.Text.Trim() != "") actions.CopyWeapon(weapon, nameBox.Text.Trim(), [.. ticked]);
        };
        var buttons = new WrapRow { Margin = new Thickness(0, 6, 0, 0) };
        var others = new List<(Button Button, string Key, string Name)>();
        foreach (Weapon made in actions.CopiesOf(weapon))
        {
            var give = new Button { ToolTip = Texts.Get("Weapons.GiveTo.Tip", ("weapon", made.Name)) };
            give.Click += (_, _) => actions.GiveCopy(made, weapon, [.. ticked]);
            others.Add((give, "Weapons.GiveTo", made.Name));
        }
        if (actions.OriginalOf(weapon) is Weapon original)
        {
            var giveBack = new Button { ToolTip = Texts.Get("Weapons.GiveBack.Tip", ("weapon", original.Name)) };
            giveBack.Click += (_, _) => actions.GiveCopy(original, weapon, [.. ticked]);
            others.Add((giveBack, "Weapons.GiveBack", original.Name));
        }
        void SayHowMany()
        {
            copy.Content = ticked.Count == 0 ? Texts.Get("Weapons.Copy.ForNone") : Texts.Count("Weapons.Copy.For", ticked.Count);
            foreach ((Button button, string key, string name) in others)
            {
                button.Content = Texts.Count(key, ticked.Count, ("weapon", name));
                button.IsEnabled = ticked.Count > 0;
            }
        }
        SayHowMany();
        buttons.Children.Add(copy);
        foreach ((Button button, _, _) in others) buttons.Children.Add(button);
        var nameRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        TextBlock label = UnitPane.Muted(Texts.Get("Weapons.Copy.Named"), new Thickness(0, 0, 6, 0));
        label.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(label, Dock.Left);
        nameRow.Children.Add(label);
        nameRow.Children.Add(nameBox);
        return (new StackPanel { Children = { nameRow, buttons, UnitPane.Muted(Texts.Get("Weapons.Copy.Note"), new Thickness(0, 6, 0, 0)) } }, SayHowMany, nameBox);
    }

    /// <summary>
    /// Where the weapon is in the game's files: its descriptor, its file, its GUID, and the keys of its texts in the
    /// game's dictionaries (its name, category, caliber and description). Folded at first.
    /// </summary>
    private static FoldSection Files(Weapon weapon)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Add(string nameKey, string? value)
        {
            if (value is null) return;
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock name = UnitPane.Muted(Texts.Get(nameKey), new Thickness(0, 1, 10, 1));
            var text = new TextBlock { Text = value, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
            text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(name);
            grid.Children.Add(text);
        }
        Add("Units.Pane.Descriptor", weapon.Descriptor);
        Add("Units.Pane.File", weapon.File);
        Add("Weapons.Pane.Guid", weapon.FileValue("DescriptorId"));
        Add("Weapons.Pane.NameText", Unquoted(weapon.FileValue("Name")));
        Add("Weapons.Pane.CategoryText", Unquoted(weapon.FileValue("TypeCategoryName")));
        Add("Weapons.Pane.CaliberText", Unquoted(weapon.FileValue("Caliber")));
        Add("Weapons.Pane.DescriptionText", Unquoted(weapon.FileValue("WeaponDescriptionToken")));
        return Section("files", "Units.Pane.Files", grid, foldedAtFirst: true);
    }

    /// <summary>A text as its file writes it, without its quotes; null for none.</summary>
    private static string? Unquoted(string? written) => written?.Trim('\'', '"');
}
