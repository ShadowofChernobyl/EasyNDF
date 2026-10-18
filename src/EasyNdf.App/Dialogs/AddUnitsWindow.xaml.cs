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

namespace EasyNdf.App.Dialogs;

/// <summary>A unit's line in the Add Units window.</summary>
/// <param name="Unit">The unit.</param>
/// <param name="Window">The window, which knows which units are ticked.</param>
public sealed record AddUnitsLine(Unit Unit, AddUnitsWindow Window);

/// <summary>What the Add Units window's list says when no unit matches.</summary>
/// <param name="Text">The words.</param>
public sealed record AddUnitsEmptyLine(string Text);

/// <summary>
/// The Add Units window: a list of units to pick from, with a search and filters for side, type, nation and role. A
/// click ticks a unit or unticks it, a double-click adds it at once, and Add adds all those ticked. Units already
/// chosen are greyed. It closes with ×, Cancel, Escape or a click outside it.
/// </summary>
public partial class AddUnitsWindow : UserControl
{
    private IReadOnlyList<Unit> units = [];
    private GameTitle game = GameTitle.Warno;
    private IReadOnlySet<Unit> alreadyChosen = new HashSet<Unit>();
    private Action<IReadOnlyList<Unit>> add = _ => { };
    private string side = "";
    private bool fillingLists;

    /// <summary>The units ticked, in the order they were ticked.</summary>
    private readonly List<Unit> ticked = [];

    /// <summary>Builds the window, closed.</summary>
    public AddUnitsWindow()
    {
        InitializeComponent();
        UnitList.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(UnitPressed), handledEventsToo: true);
    }

    /// <summary>Raised when the ticks change, for the rows to show theirs.</summary>
    public event Action? TicksChanged;

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Whether a unit is ticked.</summary>
    public bool IsTicked(Unit unit) => ticked.Contains(unit);

    /// <summary>Whether a unit was chosen before, and cannot be picked again.</summary>
    public bool IsAlreadyChosen(Unit unit) => alreadyChosen.Contains(unit);

    /// <summary>Whether a unit is on the game's first side (NATO, the Allies), for its nation's mark.</summary>
    public bool IsFirstSide(Unit unit) => unit.Side == UnitNames.SidesOf(game)[0];

    /// <summary>Opens the window on some units, with nothing ticked and the filters as at first.</summary>
    /// <param name="title">The window's title, such as "Add Units to Compare".</param>
    /// <param name="choices">The units to pick from.</param>
    /// <param name="fromGame">The game they are from, for its sides, factories and nations.</param>
    /// <param name="chosen">The units already chosen, greyed.</param>
    /// <param name="addUnits">What Add does with the units ticked.</param>
    public void Open(string title, IReadOnlyList<Unit> choices, GameTitle fromGame, IReadOnlySet<Unit> chosen, Action<IReadOnlyList<Unit>> addUnits)
    {
        TitleText.Text = title;
        units = choices;
        game = fromGame;
        alreadyChosen = chosen;
        add = addUnits;
        side = "";
        ticked.Clear();
        fillingLists = true;
        SearchBox.Text = "";
        FillSideChoices();
        FillChoice(FactoryChoice, Texts.Get("Units.AddUnits.AllTypes"), UnitNames.FactoriesOf(game).Where(factory => units.Any(unit => unit.Factory == factory)).Select(factory => (factory, UnitNames.Factory(factory))));
        FillChoice(NationChoice, Texts.Get("Units.AddUnits.AllNations"), units.Select(unit => unit.Nation).Distinct().Select(nation => (nation, UnitNames.Nation(nation))).OrderBy(nation => nation.Item2, StringComparer.Create(Texts.Catalog.Culture, true)));
        FillChoice(RoleChoice, Texts.Get("Units.AllRoles"), units.Where(unit => unit.Role != "").GroupBy(unit => unit.Role).Select(group => (group.Key, UnitNames.RoleOf(group.First()))).OrderBy(role => role.Item2, StringComparer.Create(Texts.Catalog.Culture, true)));
        fillingLists = false;
        FillList();
        ShowAddButton();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => SearchBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>A filter's list: "All …" first, then its choices, each kept by its code.</summary>
    private static void FillChoice(ComboBox choice, string all, IEnumerable<(string Code, string Name)> items)
    {
        choice.Items.Clear();
        choice.Items.Add(new ComboBoxItem { Content = all, Tag = "" });
        foreach ((string code, string name) in items) choice.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        choice.SelectedIndex = 0;
    }

    /// <summary>Both, and each side, as buttons that stay pressed.</summary>
    private void FillSideChoices()
    {
        SideChoices.Children.Clear();
        foreach (string choice in new[] { "" }.Concat(UnitNames.SidesOf(game)))
        {
            var button = new ToggleButton
            {
                Content = choice == "" ? Texts.Get("Units.BothSides") : UnitNames.Side(choice), IsChecked = side == choice,
                Style = (Style)FindResource("PressedToggle"), Padding = new Thickness(9, 1, 9, 1), Margin = new Thickness(SideChoices.Children.Count == 0 ? 0 : -1, 0, 0, 0),
            };
            button.Click += (_, _) =>
            {
                side = choice;
                FillSideChoices();
                FillList();
            };
            SideChoices.Children.Add(button);
        }
    }

    private static string Chosen(ComboBox choice) => (choice.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>The list: the units that pass the search and the filters, in the game's order.</summary>
    private void FillList()
    {
        string text = SearchBox.Text.Trim(), factory = Chosen(FactoryChoice), nation = Chosen(NationChoice), role = Chosen(RoleChoice);
        List<Unit> matching = [.. units.Where(unit =>
            (side == "" || unit.Side == side) && (factory == "" || unit.Factory == factory) && (nation == "" || unit.Nation == nation)
            && (role == "" || unit.Role == role)
            && (text == "" || unit.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) || unit.Descriptor.Contains(text, StringComparison.OrdinalIgnoreCase)))];
        UnitList.ItemsSource = matching.Count == 0
            ? new object[] { new AddUnitsEmptyLine(Texts.Get("Units.NoneMatch")) }
            : matching.Select(unit => (object)new AddUnitsLine(unit, this)).ToList();
        CountText.Text = Texts.CountWide("Units.AddUnits.Count", matching.Count);
    }

    private void FilterChanged(object sender, TextChangedEventArgs e)
    {
        if (!fillingLists) FillList();
    }

    private void FilterChosen(object sender, SelectionChangedEventArgs e)
    {
        if (!fillingLists) FillList();
    }

    /// <summary>A click ticks a unit or unticks it; a double-click adds it, with any others ticked.</summary>
    private void UnitPressed(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e.OriginalSource) is not AddUnitsRow { Unit: Unit unit } || IsAlreadyChosen(unit)) return;
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            if (!ticked.Contains(unit)) ticked.Add(unit);
            AddTicked();
            return;
        }
        if (!ticked.Remove(unit)) ticked.Add(unit);
        TicksChanged?.Invoke();
        ShowAddButton();
    }

    private static AddUnitsRow? RowAt(object? source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is AddUnitsRow row) return row;
        return null;
    }

    /// <summary>Add, usable once a unit is ticked; "Add 3 Units" when several are.</summary>
    private void ShowAddButton()
    {
        AddButton.IsEnabled = ticked.Count > 0;
        AddButton.Content = ticked.Count > 1 ? Texts.Get("Units.AddUnits.AddSeveral", ("count", ticked.Count)) : Texts.Get("Units.AddUnits.Add");
    }

    private void AddClicked(object sender, RoutedEventArgs e) => AddTicked();

    private void AddTicked()
    {
        if (ticked.Count == 0) return;
        List<Unit> picked = [.. ticked];
        Close();
        add(picked);
    }

    private void Close() => Visibility = Visibility.Collapsed;

    private void CancelClicked(object sender, RoutedEventArgs e) => Close();

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && ticked.Count > 0)
        {
            e.Handled = true;
            AddTicked();
        }
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>760 pixels wide, or 96% of the window when it is narrower.</summary>
    private void BackdropResized(object sender, SizeChangedEventArgs e) => Box.Width = Math.Min(760, (e.NewSize.Width + 20) * 0.96);
}

/// <summary>
/// A unit's row in the Add Units window: its tick, picture, nation's mark, name, role and cost. Under the mouse it takes
/// the selection colour, ticked the hover colour; a unit already chosen is faded, with a tooltip saying so.
/// </summary>
public sealed class AddUnitsRow : Bevel
{
    private readonly Grid cells = new();
    private readonly CheckBox tick = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private AddUnitsLine? line;

    /// <summary>Builds the row when the list gives it its line.</summary>
    public AddUnitsRow()
    {
        Edges = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(6, 2, 6, 2);
        SetResourceReference(BottomRightProperty, "line");
        Child = cells;
        Cursor = Cursors.Hand;
        foreach (GridLength width in new[] { new GridLength(16), new GridLength(54), GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(130), new GridLength(44) })
            cells.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        DataContextChanged += (_, _) => Build();
        MouseEnter += (_, _) => ShowState();
        MouseLeave += (_, _) => ShowState();
        Loaded += (_, _) => { if (line is not null) line.Window.TicksChanged += ShowState; ShowState(); };
        Unloaded += (_, _) => { if (line is not null) line.Window.TicksChanged -= ShowState; };
    }

    /// <summary>The unit shown.</summary>
    public Unit? Unit => line?.Unit;

    private void Build()
    {
        if (line is not null && IsLoaded) line.Window.TicksChanged -= ShowState;
        cells.Children.Clear();
        line = DataContext as AddUnitsLine;
        if (line is null) return;
        if (IsLoaded) line.Window.TicksChanged += ShowState;
        tick.Style = (Style)FindResource("Tick");
        Unit unit = line.Unit;
        var name = new TextBlock { Text = unit.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var role = new TextBlock { Text = UnitNames.RoleOf(unit), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        role.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var cost = new TextBlock { Text = unit.Cost is null ? "" : UnitNumbers.Of(unit.Cost), FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        cost.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        Place(tick, 0, 0);
        Place(new UnitPicture { Source = unit.PictureSource, Factory = unit.Factory, Width = 54 }, 1, 8);
        Place(new NationMark(unit.Nation, line.Window.IsFirstSide(unit)), 2, 8);
        Place(name, 3, 8);
        Place(role, 4, 8);
        Place(cost, 5, 8);
        bool taken = line.Window.IsAlreadyChosen(unit);
        Opacity = taken ? 0.5 : 1;
        Cursor = taken ? Cursors.Arrow : Cursors.Hand;
        ToolTip = taken ? Texts.Get("Units.AddUnits.AlreadyChosen") : unit.Descriptor;
        ShowState();
    }

    /// <summary>Puts a cell in its column, 8 pixels after the one before.</summary>
    private void Place(FrameworkElement cell, int column, double gapBefore)
    {
        cell.Margin = new Thickness(gapBefore, 0, 0, 0);
        Grid.SetColumn(cell, column);
        cells.Children.Add(cell);
    }

    /// <summary>The hover colour when ticked, the selection colour under the mouse, else none.</summary>
    private void ShowState()
    {
        if (line is null) return;
        bool isTicked = line.Window.IsTicked(line.Unit);
        tick.IsChecked = isTicked;
        if (isTicked) SetResourceReference(BackgroundProperty, "hover");
        else if (IsMouseOver && !line.Window.IsAlreadyChosen(line.Unit)) SetResourceReference(BackgroundProperty, "sel");
        else Background = Brushes.Transparent;
    }
}
