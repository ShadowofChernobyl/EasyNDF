using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// The Armory's Weapons sub-tab (see WeaponsArmory.xaml): the source's weapons, filtered, as cards or a table, and the
/// one selected in the Weapon pane; or Compare, weapons side by side (WeaponsArmory.Compare.cs). Cards and Table each
/// keep their own zoom (WeaponsArmory.Zoom.cs). The Armory tab gives it the units it has read (<see cref="Show"/>), as the open mod
/// makes them; the weapons are their ammunition.
/// </summary>
public partial class WeaponsArmory : UserControl
{
    private readonly WeaponFilter filter = new();
    private IWeaponActions? actions;
    private UnitCatalog? catalog;
    private GameTitle game = GameTitle.Warno;
    private Dictionary<string, Unit> unitsByDescriptor = new(StringComparer.Ordinal);
    private List<Weapon> shown = [];
    private string? selected;
    private WeaponView view;
    private int cardColumns = 1;

    /// <summary>The sub-tab's views: the weapons as cards or a table, or the weapons compared.</summary>
    private enum WeaponView { Cards, Table, Compare }

    /// <summary>Builds the sub-tab, empty until <see cref="Show"/> gives it the units.</summary>
    public WeaponsArmory()
    {
        InitializeComponent();
        Panes.FoldedPlace = PaneSlot;
        Panes.SecondTitle = Texts.Get("Weapons.Pane.Title");
        Table.WeaponClicked += Select;
        Table.WeaponRightClicked += OpenWeaponMenu;
        view = Enum.TryParse((string?)AppData.Current.State["weaponView"], out WeaponView remembered) ? remembered : WeaponView.Cards;
        PrepareZoom();
        PreviewKeyDown += DeleteKey;
    }

    /// <summary>
    /// Shows the weapons of the units read, keeping the filters and the weapon selected when the source is the same.
    /// </summary>
    /// <param name="units">The units as the open mod makes them, with their weapons (<see cref="UnitCatalog.Weapons"/>).</param>
    /// <param name="sourceName">Where they come from: "WARNO", or a mod's name.</param>
    /// <param name="weaponActions">What the tab does with a weapon.</param>
    /// <param name="vanillaWeapons">The vanilla game's weapons, by descriptor: any other is modded.</param>
    public void Show(UnitCatalog units, string sourceName, IWeaponActions weaponActions, IReadOnlySet<string> vanillaWeapons)
    {
        filter.VanillaWeapons = vanillaWeapons;
        bool otherGame = catalog is not null && catalog.Game != units.Game;
        catalog = units;
        game = units.Game;
        actions = weaponActions;
        unitsByDescriptor = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in units.Units) unitsByDescriptor.TryAdd(unit.Descriptor, unit);
        TraitIcon.Known = units.Traits ?? new Dictionary<string, WeaponTrait>();
        if (otherGame)
        {
            filter.Side = "";
            filter.Nations.Clear();
            filter.DamageFamily = "";
        }
        TitleText.Text = Texts.Get("Weapons.Title", ("source", sourceName));
        SavedInText.Text = Texts.Get("Weapons.SavedIn", ("mod", weaponActions.OpenModName));
        FillSideChoices();
        FillDamageChoice();
        ShowAll();
    }

    /// <summary>The Source list shows the main window's sources, as the Units sub-tab's list does.</summary>
    public void UseSources(SourceSelection shared) => SourceChoice.Selection = shared;

    /// <summary>The chosen source's folder, under the Source list.</summary>
    public void ShowWhere(string folder) => WhereText.Text = folder;

    /// <summary>
    /// What the Armory says while there are no weapons to show: no game found, choose a source, or why it could not be
    /// read. The weapons shown before are let go of.
    /// </summary>
    public void ShowMessage(string text)
    {
        catalog = null;
        CardsList.ItemsSource = null;
        CardsList.Visibility = Visibility.Collapsed;
        Table.Visibility = Visibility.Collapsed;
        CompareBox.Visibility = Visibility.Collapsed;
        Panes.Visibility = Visibility.Visible;
        KindTabs.Children.Clear();
        NationList.Children.Clear();
        PaneContent.Content = null;
        EmptyText.Text = text;
        EmptyText.Visibility = Visibility.Visible;
        CountText.Text = "";
    }

    /// <summary>The weapons of the units shown; none before any are read.</summary>
    private IReadOnlyList<Weapon> AllWeapons => catalog?.Weapons ?? [];

    // ---- the filters ----

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (filling) return;
        filter.Text = SearchBox.Text;
        ShowAll();
    }

    private void OnlyInUseClicked(object sender, RoutedEventArgs e)
    {
        filter.OnlyInUse = OnlyInUse.IsChecked == true;
        ShowAll();
    }

    /// <summary>"Only modded weapons", as the Units sub-tab's "Only modded units": those vanilla WARNO doesn't have.</summary>
    private void OnlyModdedClicked(object sender, RoutedEventArgs e)
    {
        filter.OnlyModded = OnlyModded.IsChecked == true;
        ShowAll();
    }

    /// <summary>Both, and each side by its name, as buttons that stay pressed, as on the Units sub-tab.</summary>
    private void FillSideChoices()
    {
        SideChoices.Children.Clear();
        foreach (string side in new[] { "" }.Concat(UnitNames.SidesOf(game)))
        {
            var button = new ToggleButton
            {
                Content = side == "" ? Texts.Get("Units.BothSides") : UnitNames.Side(side), IsChecked = filter.Side == side,
                Style = (Style)FindResource("PressedToggle"), Padding = new Thickness(9, 1, 9, 1), Margin = new Thickness(SideChoices.Children.Count == 0 ? 0 : -1, 0, 0, 0),
                ToolTip = side == "" ? Texts.Get("Weapons.BothSides.Tip") : Texts.Get("Weapons.Side.Tip", ("side", UnitNames.Side(side))),
            };
            button.Click += (_, _) =>
            {
                filter.Side = side;
                FillSideChoices();
                ShowAll();
            };
            SideChoices.Children.Add(button);
        }
    }

    /// <summary>Every kind of damage the weapons have, by name, after "All damage types".</summary>
    private void FillDamageChoice()
    {
        filling = true;
        DamageChoice.Items.Clear();
        DamageChoice.Items.Add(new ComboBoxItem { Content = Texts.Get("Weapons.AllDamage"), Tag = "" });
        foreach (string family in AllWeapons.Select(weapon => weapon.DamageFamily).Where(family => family != "").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var item = new ComboBoxItem { Content = family.Replace('_', ' ').ToUpperInvariant(), Tag = family };
            DamageChoice.Items.Add(item);
            if (family == filter.DamageFamily) DamageChoice.SelectedItem = item;
        }
        if (DamageChoice.SelectedItem is null) DamageChoice.SelectedIndex = 0;
        filling = false;
    }

    /// <summary>Whether the lists are being filled, so their choices made in code are not taken as the user's.</summary>
    private bool filling;

    private void DamageChosen(object sender, SelectionChangedEventArgs e)
    {
        if (filling) return;
        filter.DamageFamily = (DamageChoice.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        ShowAll();
    }

    /// <summary>
    /// The nations of the units that carry the weapons, under their side's name, each with a tick and how many weapons
    /// its units carry with the other filters.
    /// </summary>
    private void FillNationList()
    {
        NationList.Children.Clear();
        if (catalog is null) return;
        Dictionary<string, int> counts = filter.NationCounts(AllWeapons, unitsByDescriptor);
        IReadOnlyList<string> sides = UnitNames.SidesOf(game);
        foreach (string side in sides.Where(side => filter.Side == "" || filter.Side == side))
        {
            NationList.Children.Add(SideHeading(side, side == sides[0]));
            IEnumerable<string> nations = catalog.Units.Where(unit => unit.Side == side).Select(unit => unit.Nation).Distinct()
                .OrderBy(UnitNames.Nation, StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
            foreach (string nation in nations) NationList.Children.Add(NationRow(nation, counts.GetValueOrDefault(nation)));
        }
    }

    /// <summary>A side's heading, bold on the panel colour with the side's stripe, as on the Units sub-tab.</summary>
    private static Bevel SideHeading(string side, bool firstSide)
    {
        var stripe = new System.Windows.Shapes.Rectangle { Width = 3, Margin = new Thickness(0, 0, 6, 0) };
        stripe.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, firstSide ? "side-a" : "side-b");
        var name = new TextBlock { Text = UnitNames.Side(side), FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new Bevel { Child = new DockPanel { Children = { stripe, name } }, Padding = new Thickness(8, 3, 8, 3), Edges = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        row.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return row;
    }

    /// <summary>A nation's row: its tick, name and count; a click ticks or unticks it.</summary>
    private Bevel NationRow(string nation, int count)
    {
        var tick = new CheckBox { Style = (Style)FindResource("Tick"), IsChecked = filter.Nations.Contains(nation), IsHitTestVisible = false, Margin = new Thickness(0, 0, 6, 0) };
        var countText = new TextBlock { Text = Texts.Number(count), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        countText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(countText, Dock.Right);
        DockPanel.SetDock(tick, Dock.Left);
        var name = new TextBlock { Text = UnitNames.Nation(nation), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var row = new Bevel
        {
            Child = new DockPanel { Children = { tick, countText, name } }, Padding = new Thickness(8, 3, 8, 3), Edges = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = Texts.Get("Weapons.Nation.Tip", ("nation", UnitNames.Nation(nation))),
        };
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        row.MouseEnter += (_, _) => row.SetResourceReference(Bevel.BackgroundProperty, "sel");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (!filter.Nations.Remove(nation)) filter.Nations.Add(nation);
            ShowAll();
        };
        return row;
    }

    /// <summary>ALL, then each category, each with how many weapons it would show with the other filters.</summary>
    private void FillKindTabs()
    {
        KindTabs.Children.Clear();
        Dictionary<WeaponCategory, int> counts = filter.CategoryCounts(AllWeapons, unitsByDescriptor);
        KindTabs.Children.Add(KindTab(null, Texts.Get("Units.Factory.All.Short"), Texts.Get("Weapons.AllKinds"), counts.Values.Sum()));
        foreach (WeaponCategory category in Enum.GetValues<WeaponCategory>())
            KindTabs.Children.Add(KindTab(category, WeaponNames.CategoryTab(category), WeaponNames.CategoryHeading(category), counts.GetValueOrDefault(category)));
    }

    private ToggleButton KindTab(WeaponCategory? category, string shortName, string name, int count)
    {
        var countText = new System.Windows.Documents.Run(" " + Texts.Number(count)) { FontWeight = FontWeights.Normal, FontSize = 11 };
        countText.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "muted");
        var label = new TextBlock { Inlines = { new System.Windows.Documents.Run(shortName), countText } };
        var tab = new ToggleButton { Content = label, Style = (Style)FindResource("FactoryTab"), IsChecked = filter.Category == category, ToolTip = name };
        tab.Click += (_, _) =>
        {
            filter.Category = category;
            ShowAll();
        };
        return tab;
    }

    // ---- the views ----

    private void NewWeaponClicked(object sender, RoutedEventArgs e) => actions?.NewWeapon();

    private void ViewClicked(object sender, RoutedEventArgs e) =>
        ShowView(sender == TableButton ? WeaponView.Table : sender == CompareButton ? WeaponView.Compare : WeaponView.Cards);

    /// <summary>Shows a view, remembered for next time.</summary>
    private void ShowView(WeaponView shownView)
    {
        view = shownView;
        AppData.Current.State["weaponView"] = view.ToString();
        AppData.Current.SaveState();
        ShowAll();
    }

    /// <summary>Shows everything again: the lists' counts, the weapons the filters leave, and the Weapon pane.</summary>
    private void ShowAll()
    {
        if (catalog is null) return;
        FillNationList();
        FillKindTabs();
        shown = filter.Apply(AllWeapons, unitsByDescriptor);
        bool scrollToSelected = false;
        if (selectWhenShown is not null && AllWeapons.FirstOrDefault(weapon => weapon.Descriptor == selectWhenShown) is Weapon wanted)
        {
            selected = selectWhenShown;
            selectWhenShown = null;
            if (revealWanted && !shown.Contains(wanted)) ShowOnly(wanted);
            scrollToSelected = revealWanted;
            revealWanted = false;
        }
        if (selected is not null && !AllWeapons.Any(weapon => weapon.Descriptor == selected)) selected = null;
        CardsButton.IsChecked = view == WeaponView.Cards;
        TableButton.IsChecked = view == WeaponView.Table;
        CompareButton.IsChecked = view == WeaponView.Compare;
        // Compare takes the place of the weapons and the pane, as on the Units sub-tab
        bool compare = view == WeaponView.Compare;
        CompareBox.Visibility = compare ? Visibility.Visible : Visibility.Collapsed;
        Panes.Visibility = compare ? Visibility.Collapsed : Visibility.Visible;
        ShowZoom();
        if (compare)
        {
            ShowCompare();
            CountText.Text = Texts.CountWide("Weapons.Compared", compared.Count);
            FootnoteText.Text = Texts.Get("Weapons.Footnote");
            return;
        }
        CardsList.Visibility = view == WeaponView.Cards && shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Table.Visibility = view == WeaponView.Table && shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Texts.Get("Weapons.NoneShown");
        if (view == WeaponView.Table) Table.Show(shown, selected);
        else ShowCards();
        CountText.Text = Texts.CountWide("Weapons.Shown", shown.Count);
        FootnoteText.Text = Texts.Get(view == WeaponView.Table ? "Weapons.Footnote.Table" : "Weapons.Footnote");
        ShowPane();
        // once the list is laid out, so its rows' places are known
        if (scrollToSelected) Dispatcher.BeginInvoke(ScrollToSelected, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The filters set so a weapon asked for shows: its category's tab, and every other filter off (the search, the
    /// side, the nations, the damage and the toggles), as the filters hid it.
    /// </summary>
    private void ShowOnly(Weapon weapon)
    {
        filling = true;
        filter.Text = "";
        SearchBox.Text = "";
        filter.Side = "";
        filter.Nations.Clear();
        filter.DamageFamily = "";
        filter.OnlyInUse = false;
        filter.OnlyModded = false;
        OnlyInUse.IsChecked = false;
        OnlyModded.IsChecked = false;
        filter.Category = weapon.Category;
        filling = false;
        FillSideChoices();
        FillDamageChoice();
        FillNationList();
        FillKindTabs();
        shown = filter.Apply(AllWeapons, unitsByDescriptor);
    }

    /// <summary>The weapon selected brought into view: its row of cards, or its row of the table.</summary>
    private void ScrollToSelected()
    {
        if (selected is null) return;
        if (view == WeaponView.Table)
        {
            Table.BringIntoView(selected);
            return;
        }
        int at = shown.FindIndex(weapon => weapon.Descriptor == selected);
        if (at >= 0 && VisualsIn<VirtualizingStackPanel>(CardsList).FirstOrDefault() is VirtualizingStackPanel rows)
            rows.BringIndexIntoViewPublic(at / Math.Max(1, cardColumns));
    }

    /// <summary>
    /// The Cards view: the weapons shown in rows of as many cards as fit across, each card an equal share of the
    /// width, as the units' tiles are.
    /// </summary>
    private void ShowCards()
    {
        WeaponCardRowView.Selected = selected;
        cardColumns = WeaponCardRowView.ColumnsFor(CardsRoom());
        var rows = new List<WeaponCardRow>();
        for (int start = 0; start < shown.Count; start += cardColumns)
            rows.Add(new WeaponCardRow([.. shown.Skip(start).Take(cardColumns)], cardColumns, IsLast: start + cardColumns >= shown.Count));
        CardsList.ItemsSource = rows;
    }

    /// <summary>The width the cards have across, beside the scrollbar, in their own units: less when zoomed in.</summary>
    private double CardsRoom() => (CardsList.ActualWidth - SystemParameters.VerticalScrollBarWidth) / ScaleOf(WeaponView.Cards);

    /// <summary>The rows are made again when a different number of cards fits across.</summary>
    private void CardsListResized(object sender, SizeChangedEventArgs e)
    {
        if (view != WeaponView.Cards || WeaponCardRowView.ColumnsFor(CardsRoom()) == cardColumns) return;
        ShowCards();
    }

    /// <summary>A click on a card selects its weapon.</summary>
    private void CardsClicked(object sender, MouseButtonEventArgs e)
    {
        if (CardAt(e.OriginalSource) is { Weapon: Weapon weapon }) Select(weapon);
    }

    /// <summary>A right-click on a card selects its weapon and opens its menu.</summary>
    private void CardsRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (CardAt(e.OriginalSource) is not { Weapon: Weapon weapon } card) return;
        e.Handled = true;
        Select(weapon);
        OpenWeaponMenu(weapon, card);
    }

    /// <summary>The card an element clicked is on; null for none.</summary>
    private static WeaponCard? CardAt(object clicked)
    {
        for (DependencyObject? at = clicked as DependencyObject; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is WeaponCard card) return card;
        return null;
    }

    /// <summary>Selects a weapon: its card or row shows selected, and the Weapon pane shows it.</summary>
    private void Select(Weapon weapon)
    {
        selected = weapon.Descriptor;
        // only the cards or rows on screen are drawn again, in place: making the list again moved it under the mouse
        if (view == WeaponView.Table) Table.ShowSelected(selected);
        else ShowSelectedCard();
        ShowPane();
    }

    /// <summary>The cards on screen drawn again, the one selected sunk; the list itself stays as it is.</summary>
    private void ShowSelectedCard()
    {
        WeaponCardRowView.Selected = selected;
        foreach (WeaponCard card in VisualsIn<WeaponCard>(CardsList))
            if (card.Weapon is Weapon shownWeapon) card.Show(shownWeapon, shownWeapon.Descriptor == selected);
    }

    /// <summary>Every element of a type drawn inside another.</summary>
    internal static IEnumerable<T> VisualsIn<T>(DependencyObject inside) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(inside); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(inside, i);
            if (child is T found) yield return found;
            foreach (T deeper in VisualsIn<T>(child)) yield return deeper;
        }
    }

    private void ShowPane()
    {
        if (actions is null) return;
        Weapon? weapon = selected is null ? null : AllWeapons.FirstOrDefault(each => each.Descriptor == selected);
        PaneContent.Content = WeaponPane.Build(weapon, unitsByDescriptor, actions, Compare);
    }

    /// <summary>The Delete key, outside a box: the weapon selected is deleted, or its changes taken back, once asked.</summary>
    private void DeleteKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None || actions is null || view == WeaponView.Compare) return;
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        if (selected is null || AllWeapons.FirstOrDefault(weapon => weapon.Descriptor == selected) is not Weapon weapon) return;
        e.Handled = true;
        _ = actions.DeleteWeapon(weapon);
    }

    /// <summary>A weapon just made, by descriptor, to select once the weapons shown have it.</summary>
    private string? selectWhenShown;

    /// <summary>
    /// Selects a weapon as soon as it is shown: a copy just made appears once the mod's weapons are worked out again,
    /// and is then selected, so the user can change it straight away.
    /// </summary>
    public void SelectWhenShown(string descriptor) => selectWhenShown = descriptor;

    /// <summary>Whether the weapon to select is also to be shown when the filters hide it, and brought into view.</summary>
    private bool revealWanted;

    /// <summary>
    /// "Show in Weapons", from a unit's weapons: the weapon selected as soon as it is shown, with the filters set so it
    /// shows, and brought into view.
    /// </summary>
    public void Reveal(string descriptor)
    {
        selectWhenShown = descriptor;
        revealWanted = true;
        if (catalog is not null) ShowAll();
    }

    // ---- for the workbench's checks ----

    /// <summary>The weapons the filters leave now.</summary>
    internal IReadOnlyList<Weapon> ShownNow => shown;

    /// <summary>Selects a weapon, as a click on it does.</summary>
    internal void SelectNow(Weapon weapon) => Select(weapon);

    /// <summary>Chooses a category's tab; null for ALL.</summary>
    internal void ChooseCategoryNow(WeaponCategory? category)
    {
        filter.Category = category;
        ShowAll();
    }

    /// <summary>Shows the Cards view, or the Table view.</summary>
    internal void ShowTableNow(bool table) => ShowView(table ? WeaponView.Table : WeaponView.Cards);

    /// <summary>Adds weapons to Compare, which is shown.</summary>
    internal void CompareNow(IEnumerable<Weapon> weapons)
    {
        foreach (Weapon weapon in weapons) Compare(weapon);
    }

    /// <summary>The cards on Compare, the pinned weapon's first.</summary>
    internal IEnumerable<WeaponCompareCard> CompareCardsNow => CompareList.Children.OfType<WeaponCompareCard>();
}
