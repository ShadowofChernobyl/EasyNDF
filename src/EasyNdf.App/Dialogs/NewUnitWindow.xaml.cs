using System.Globalization;
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
using EasyNdf.App.Weapons;

namespace EasyNdf.App.Dialogs;

/// <summary>A unit's line in the New Unit window's Table view of units.</summary>
/// <param name="Unit">The unit whose model unit it offers.</param>
/// <param name="Window">The window, which knows which model unit is chosen.</param>
public sealed record ModelLine(Unit Unit, NewUnitWindow Window);

/// <summary>
/// The New Unit window, in four steps listed at the top. 1, Model: the unit the new one is a copy of, picked as on the
/// Armory's Units sub-tab, with what comes with it at the right. 2, Name and Nation: its name, nation, role and card
/// picture, with its tile as it will look. 3, Stats: a box for each of its values, where an empty box keeps the model
/// unit's value. 4, Weapons: the model unit's weapons turret by turret, as the Unit pane's editor shows them, to swap,
/// change, take off or add; left as they are, the unit keeps its model's. Nothing of the copy is taken away unless the
/// user takes it off, as a unit missing what its model expects can stop the game loading.
/// It closes with ×, Cancel, Escape or a click outside it.
/// </summary>
public partial class NewUnitWindow : UserControl
{
    /// <summary>The steps, in order.</summary>
    private enum Step { Model, NameAndNation, Stats, Weapons }

    /// <summary>
    /// A value the Stats step asks for: its name, how the model unit's is read, whether it is a whole number (as the game
    /// writes it), what it is in (km/h), and where the new unit keeps it.
    /// </summary>
    /// <param name="Family">For an armour side, its armour family, which sets the levels the game allows; null for other values.</param>
    private sealed record StatField(string Name, Func<Unit, double?> Read, bool IsWhole, string? In, Action<ModUnit, double> Give, Func<UnitArmorFamilies, string?>? Family = null);

    private IReadOnlyList<Unit> units = [];
    private GameTitle game = GameTitle.Warno;
    private IReadOnlyCollection<string> takenDescriptors = [];

    /// <summary>How many armour levels each armour family has, from the source's DamageResistance.ndf.</summary>
    private IReadOnlyDictionary<string, int>? armorLevels;
    private Func<string?> choosePicture = () => null;
    private Func<ModUnit, IReadOnlyList<ModWeapon>, bool> create = (_, _) => false;

    /// <summary>What the Weapons step asks of the Armory tab: the model unit's loadout and the weapons editor.</summary>
    private INewUnitWeapons? weaponsSource;

    /// <summary>The Weapons step's changes and copies, kept until the unit is created.</summary>
    private readonly PendingLoadout pendingWeapons = new();

    /// <summary>The model unit the Weapons step's changes are for; they are let go of when another is chosen.</summary>
    private Unit? weaponsFor;

    private Step step;
    private Step furthest;
    private readonly UnitFilter filter = new();
    private bool fillingLists;

    /// <summary>The model unit chosen; null until one is.</summary>
    private Unit? modelUnit;

    /// <summary>The model unit chosen, for the Cards view's tiles, which show it selected.</summary>
    private readonly UnitSelection chosenTile = new();

    /// <summary>Whether the units show as the Armory's tiles rather than as rows; kept from one opening to the next.</summary>
    private bool showsCards = (string?)AppData.Current.State["newUnitView"] != "table";

    /// <summary>How many tiles fit across the list when it was last filled in the Cards view.</summary>
    private int cardColumns;

    /// <summary>A card picture of the user's own, kept with the app's data; null for the model unit's.</summary>
    private string? pictureFile;

    /// <summary>The Stats step's boxes, each with the value it asks for.</summary>
    private readonly List<(StatField Field, TextBox Box)> statBoxes = [];

    /// <summary>The model unit the Name and Nation step was last filled for, whose role chosen stays.</summary>
    private Unit? nameStepFilledFor;

    /// <summary>The new unit's tile, as it will model unit, beside the Name and Stats steps.</summary>
    private UnitTile? preview;

    /// <summary>Builds the window, closed.</summary>
    public NewUnitWindow()
    {
        InitializeComponent();
        ModelList.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(ModelPressed), handledEventsToo: true);
    }

    /// <summary>Raised when the model unit chosen changes, for the rows to show it.</summary>
    public event Action? ModelChanged;

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Whether a unit is the model unit chosen.</summary>
    public bool IsChosen(Unit unit) => ReferenceEquals(unit, modelUnit);

    /// <summary>Whether a unit is on the game's first side (NATO, the Allies), for its nation's mark.</summary>
    public bool IsFirstSide(Unit unit) => unit.Side == UnitNames.SidesOf(game)[0];

    /// <summary>Opens the window on its first step, with nothing chosen and every box empty.</summary>
    /// <param name="units">The units whose units can be taken: the source's own.</param>
    /// <param name="fromGame">The game they are from, for its sides, types and nations.</param>
    /// <param name="taken">The unit descriptors in use, so the new one gets a name of its own.</param>
    /// <param name="pickPicture">Asks for a card picture of the user's own and keeps it; null when none was chosen or it could not be kept.</param>
    /// <param name="createUnit">Makes the unit, with the copies of weapons it mounts; false when it was refused (a file missing), which keeps the window open.</param>
    /// <param name="levels">How many armour levels each armour family has, for the armour boxes (<see cref="UnitCatalog.ArmorLevels"/>).</param>
    /// <param name="weapons">The model unit's loadout and the weapons editor, for the Weapons step.</param>
    public void Open(IReadOnlyList<Unit> models, GameTitle fromGame, IReadOnlyCollection<string> taken, Func<string?> pickPicture,
        Func<ModUnit, IReadOnlyList<ModWeapon>, bool> createUnit, IReadOnlyDictionary<string, int>? levels, INewUnitWeapons weapons)
    {
        weaponsSource = weapons;
        pendingWeapons.Clear();
        weaponsFor = null;
        armorLevels = levels;
        units = models;
        game = fromGame;
        takenDescriptors = taken;
        choosePicture = pickPicture;
        create = createUnit;
        modelUnit = null;
        chosenTile.SetAll([]);
        pictureFile = null;
        // nothing of the unit made before is kept: a new unit starts from scratch
        nameStepFilledFor = null;
        NationChoice.Items.Clear();
        RoleChoice.Items.Clear();
        statBoxes.Clear();
        StatsPanel.Children.Clear();
        fillingLists = true;
        SearchBox.Text = "";
        filter.Text = "";
        filter.Side = "";
        filter.Nations.Clear();
        filter.Role = "";
        // every type at first (Paul's choice): the model may be of any type, whatever the Armory shows
        filter.Factory = "";
        FillRoleFilter();
        NameBox.Text = "";
        ModelPictureChoice.IsChecked = true;
        OwnPictureChoice.IsChecked = false;
        fillingLists = false;
        furthest = Step.Model;
        ShowStep(Step.Model);
        FillModels();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => SearchBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    // ---- the steps ----

    /// <summary>Shows a step: its part of the window, the steps at the top, the hint and the buttons at the foot.</summary>
    private void ShowStep(Step shown)
    {
        step = shown;
        if (shown > furthest) furthest = shown;
        ModelStep.Visibility = shown == Step.Model ? Visibility.Visible : Visibility.Collapsed;
        DetailsSteps.Visibility = shown == Step.Model ? Visibility.Collapsed : Visibility.Visible;
        NameStep.Visibility = shown == Step.NameAndNation ? Visibility.Visible : Visibility.Collapsed;
        StatsStep.Visibility = shown == Step.Stats ? Visibility.Visible : Visibility.Collapsed;
        WeaponsStep.Visibility = shown == Step.Weapons ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = shown == Step.Model ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Visibility = shown == Step.Weapons ? Visibility.Collapsed : Visibility.Visible;
        CreateButton.Visibility = shown == Step.Weapons ? Visibility.Visible : Visibility.Collapsed;
        FitWidth();
        FillStepStrip();
        if (shown != Step.Model) ShowPreview();
        ShowButtons();
        if (shown == Step.NameAndNation) Dispatcher.BeginInvoke(() => NameBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        if (shown == Step.Stats && statBoxes.Count > 0) Dispatcher.BeginInvoke(() => statBoxes[0].Box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// The steps at the top: a numbered circle and the step's name, joined by a line. The step shown is filled in the
    /// accent colour; one done shows a tick, and a click on it goes back to it; one still to come is dim.
    /// </summary>
    private void FillStepStrip()
    {
        StepStrip.Children.Clear();
        (Step Step, string Name)[] steps =
        [
            (Step.Model, Texts.Get("Units.New.Step.Model")),
            (Step.NameAndNation, Texts.Get("Units.New.Step.NameAndNation")),
            (Step.Stats, Texts.Get("Units.New.Step.Stats")),
            (Step.Weapons, Texts.Get("Units.New.Step.Weapons")),
        ];
        foreach ((Step each, string name) in steps)
        {
            if (StepStrip.Children.Count > 0)
            {
                var join = new Border { Width = 28, Height = 1, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                join.SetResourceReference(Border.BackgroundProperty, "line");
                StepStrip.Children.Add(join);
            }
            StepStrip.Children.Add(StepMark(each, name, steps.Length));
        }
    }

    /// <summary>One step at the top: its circle and its name.</summary>
    private FrameworkElement StepMark(Step each, string name, int count)
    {
        bool isShown = each == step, isDone = each < step || (each <= furthest && each != step);
        var number = new TextBlock
        {
            Text = isDone ? "✓" : ((int)each + 1).ToString(Texts.Catalog.Culture), FontSize = 11, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var circle = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Child = number, Margin = new Thickness(0, 0, 6, 0) };
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        if (isShown)
        {
            circle.SetResourceReference(Border.BackgroundProperty, "accent");
            circle.SetResourceReference(Border.BorderBrushProperty, "accent");
            number.SetResourceReference(TextBlock.ForegroundProperty, "panel");
        }
        else
        {
            string colour = isDone ? "accent" : "muted";
            circle.SetResourceReference(Border.BorderBrushProperty, colour);
            number.SetResourceReference(TextBlock.ForegroundProperty, colour);
            label.SetResourceReference(TextBlock.ForegroundProperty, isDone ? "ink" : "muted");
        }
        var mark = new StackPanel
        {
            Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Children = { circle, label },
            ToolTip = Texts.Get("Units.New.Step.Tip", ("number", (int)each + 1), ("count", count), ("step", name)),
        };
        // a step done can be gone back to with a click
        if (isDone && CanShow(each))
        {
            mark.Cursor = Cursors.Hand;
            mark.MouseLeftButtonUp += (_, _) => GoTo(each);
        }
        return mark;
    }

    /// <summary>
    /// Whether a step can be shown: the ones after Model need a model unit; Stats needs a name too; Weapons, every
    /// value typed one the game takes.
    /// </summary>
    private bool CanShow(Step each) => each switch
    {
        Step.Model => true,
        Step.NameAndNation => modelUnit is not null,
        Step.Stats => modelUnit is not null && NameBox.Text.Trim() != "",
        _ => modelUnit is not null && NameBox.Text.Trim() != "" && StatsAreValid(),
    };

    /// <summary>Next, usable once the step shown is done; Create, once every stat is given; the hint says what is left.</summary>
    private void ShowButtons()
    {
        NextButton.IsEnabled = step != Step.Weapons && CanShow(step + 1);
        CreateButton.IsEnabled = step == Step.Weapons && StatsAreValid();
        HintText.Text = step switch
        {
            Step.Model => modelUnit is null ? Texts.Get("Units.New.Hint.Model") : Texts.Get("Units.New.Hint.ModelChosen", ("unit", modelUnit.Name)),
            Step.NameAndNation => NameBox.Text.Trim() == "" ? Texts.Get("Units.New.Hint.Name") : "",
            Step.Weapons => Texts.Get("Units.New.Hint.Weapons", ("unit", modelUnit?.Name ?? "")),
            _ => StatsAreValid() ? Texts.Get("Units.New.Hint.Stats", ("unit", modelUnit?.Name ?? "")) : WrongStatHint(),
        };
        // a value the game does not take is the one thing stopping Create: the hint says so in the caution colour
        if (step == Step.Stats && !StatsAreValid()) HintText.SetResourceReference(TextBlock.ForegroundProperty, "caution");
        else HintText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
    }

    /// <summary>The first value typed that the game does not take, by name, and what it takes: "Front: helicopter armor takes 1 to 3."</summary>
    private string WrongStatHint()
    {
        (StatField field, TextBox _) = statBoxes.First(each => each.Box.Text.Trim() != "" && ValueOf(each.Box.Text, each.Field) is null);
        return Texts.Get("Units.New.Hint.StatWrong", ("stat", Texts.Get(field.Name)), ("takes", WhatItTakes(field)));
    }

    /// <summary>What a stat's box takes, in a few words: "helicopter armor takes 1 to 3", "a whole number, 0 or more".</summary>
    private string WhatItTakes(StatField field)
    {
        if (FamilyOf(field) is string family && ArmorLevels.RangeOf(armorLevels, family) is (int lowest, int highest))
            return Texts.Get("Units.New.Takes.Armor", ("family", UnitNames.ArmorFamily(family)), ("lowest", lowest), ("highest", highest));
        return Texts.Get(field.IsWhole ? "Units.New.Takes.Whole" : "Units.New.Takes.Decimal");
    }

    private void NextClicked(object sender, RoutedEventArgs e) => GoOn();

    /// <summary>The next step, when the one shown is done.</summary>
    private void GoOn()
    {
        if (!NextButton.IsEnabled || step == Step.Weapons) return;
        GoTo(step + 1);
    }

    /// <summary>
    /// Shows a step, made for the model unit chosen: leaving the Model step fills the Name and Nation step, and the Stats step
    /// is made each time it shows, as another model unit may have other values.
    /// </summary>
    private void GoTo(Step target)
    {
        if (target != Step.Model && step == Step.Model) FillNameStep();
        if (target == Step.Stats) FillStatsStep();
        if (target == Step.Weapons) FillWeaponsStep();
        ShowStep(target);
    }

    private void BackClicked(object sender, RoutedEventArgs e)
    {
        if (step != Step.Model) ShowStep(step - 1);
    }

    // ---- step 1: the model unit ----

    /// <summary>The roles the units have, by name, with "All roles" first.</summary>
    private void FillRoleFilter()
    {
        RoleFilter.Items.Clear();
        RoleFilter.Items.Add(new ComboBoxItem { Content = Texts.Get("Units.AllRoles"), Tag = "" });
        foreach ((string code, string name) in RolesOf(units)) RoleFilter.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        RoleFilter.SelectedIndex = 0;
    }

    /// <summary>The roles some units have, each once, by name.</summary>
    private static IEnumerable<(string Code, string Name)> RolesOf(IEnumerable<Unit> units) =>
        units.Where(unit => unit.Role != "").GroupBy(unit => unit.Role).Select(group => (group.Key, UnitNames.RoleOf(group.First())))
            .OrderBy(role => role.Item2, StringComparer.Create(Texts.Catalog.Culture, true));

    /// <summary>The filters and the units, as the filters leave them.</summary>
    private void FillModels()
    {
        FillSideChoices();
        FillNationList();
        FillFactoryTabs();
        FillModelList();
        ShowModelDetails();
        ShowButtons();
    }

    /// <summary>Both, and each side, as buttons that stay pressed.</summary>
    private void FillSideChoices()
    {
        SideChoices.Children.Clear();
        foreach (string choice in new[] { "" }.Concat(UnitNames.SidesOf(game)))
        {
            var button = new ToggleButton
            {
                Content = choice == "" ? Texts.Get("Units.BothSides") : UnitNames.Side(choice), IsChecked = filter.Side == choice,
                Style = (Style)FindResource("PressedToggle"), Padding = new Thickness(9, 1, 9, 1), Margin = new Thickness(SideChoices.Children.Count == 0 ? 0 : -1, 0, 0, 0),
            };
            button.Click += (_, _) =>
            {
                filter.Side = choice;
                FillModels();
            };
            SideChoices.Children.Add(button);
        }
    }

    /// <summary>The nations of the side chosen, each with its tick, mark, name and how many units it has; ticking nations shows only them.</summary>
    private void FillNationList()
    {
        NationList.Children.Clear();
        foreach (string side in UnitNames.SidesOf(game).Where(side => filter.Side == "" || filter.Side == side))
        {
            Dictionary<string, int> counts = filter.NationCounts(units, side);
            IEnumerable<string> nations = units.Where(unit => unit.Side == side).Select(unit => unit.Nation).Distinct()
                .OrderBy(UnitNames.Nation, StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
            foreach (string nation in nations) NationList.Children.Add(NationRow(nation, side, counts.GetValueOrDefault(nation)));
        }
    }

    /// <summary>A nation's row: its tick, mark, name and count; a click ticks or unticks it.</summary>
    private Bevel NationRow(string nation, string side, int count)
    {
        var tick = new CheckBox { Style = (Style)FindResource("Tick"), IsChecked = filter.Nations.Contains(nation), IsHitTestVisible = false, Margin = new Thickness(0, 0, 6, 0) };
        var mark = new NationMark(nation, side == UnitNames.SidesOf(game)[0]) { Margin = new Thickness(0, 0, 6, 0) };
        var countText = new TextBlock { Text = Texts.Number(count), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        countText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var name = new TextBlock { Text = UnitNames.Nation(nation), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(tick, Dock.Left);
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(countText, Dock.Right);
        var row = new Bevel { Child = new DockPanel { Children = { tick, mark, countText, name } }, Padding = new Thickness(8, 3, 8, 3), Edges = new Thickness(0, 0, 0, 1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        row.MouseEnter += (_, _) => row.SetResourceReference(Bevel.BackgroundProperty, "sel");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (!filter.Nations.Remove(nation)) filter.Nations.Add(nation);
            FillModels();
        };
        return row;
    }

    /// <summary>The type tabs, as the Armory's: ALL, then each type of the game, each with how many units it would show.</summary>
    private void FillFactoryTabs()
    {
        FactoryTabs.Children.Clear();
        Dictionary<string, int> counts = filter.FactoryCounts(units);
        FactoryTabs.Children.Add(FactoryTab("", Texts.Get("Units.Factory.All.Short"), Texts.Get("Units.Factory.All"), counts.Values.Sum()));
        foreach (string factory in UnitNames.FactoriesOf(game))
            FactoryTabs.Children.Add(FactoryTab(factory, UnitNames.FactoryShort(factory), UnitNames.Factory(factory), counts.GetValueOrDefault(factory)));
    }

    private ToggleButton FactoryTab(string factory, string shortName, string name, int count)
    {
        var countText = new System.Windows.Documents.Run(" " + Texts.Number(count)) { FontWeight = FontWeights.Normal, FontSize = 11 };
        countText.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "muted");
        var label = new TextBlock { Inlines = { new System.Windows.Documents.Run(shortName), countText } };
        var tab = new ToggleButton { Content = label, Style = (Style)FindResource("FactoryTab"), IsChecked = filter.Factory == factory, ToolTip = name };
        tab.Click += (_, _) =>
        {
            filter.Factory = factory;
            FillModels();
        };
        return tab;
    }

    /// <summary>The units that pass the filters, in the game's order: as rows of tiles in the Cards view, as many across as fit, or as rows.</summary>
    private void FillModelList()
    {
        CardsViewButton.IsChecked = showsCards;
        TableViewButton.IsChecked = !showsCards;
        List<Unit> matching = filter.Apply(units);
        if (matching.Count == 0) ModelList.ItemsSource = new object[] { new EmptyLine(Texts.Get("Units.NoneMatch")) };
        else if (showsCards) ModelList.ItemsSource = CardRows(matching);
        else ModelList.ItemsSource = matching.Select(unit => (object)new ModelLine(unit, this)).ToList();
        ModelCountText.Text = Texts.CountWide("Units.New.Models", matching.Count);
    }

    /// <summary>The units as rows of tiles, as many across as fit.</summary>
    private List<object> CardRows(List<Unit> matching)
    {
        // the room beside the scrollbar, which takes 14 pixels when it shows
        cardColumns = CardRowView.ColumnsFor(Math.Max(0, ModelList.ActualWidth - 14));
        var rows = new List<object>();
        for (int start = 0; start < matching.Count; start += cardColumns)
        {
            List<Unit> row = matching.GetRange(start, Math.Min(cardColumns, matching.Count - start));
            rows.Add(new CardRowLine(row, cardColumns, IsLast: start + cardColumns >= matching.Count, chosenTile, IsFirstSide));
        }
        return rows;
    }

    /// <summary>
    /// The model unit chosen, at the right: its picture larger, its name, nation and role, then what comes with the model unit and
    /// what the new unit starts without, so nobody expects the model unit's values.
    /// </summary>
    private void ShowModelDetails()
    {
        if (modelUnit is null)
        {
            var none = new TextBlock { Text = Texts.Get("Units.New.ClickAModel"), TextWrapping = TextWrapping.Wrap };
            none.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            ModelDetails.Content = none;
            return;
        }
        var name = new TextBlock { Text = modelUnit.Name, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        var what = new TextBlock { Text = UnitNames.Nation(modelUnit.Nation) + " · " + UnitNames.RoleOf(modelUnit), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        what.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var nationLine = new DockPanel { Children = { new NationMark(modelUnit.Nation, IsFirstSide(modelUnit)) { Margin = new Thickness(0, 0, 6, 0) }, what } };
        var details = new StackPanel
        {
            Children =
            {
                new UnitPicture { Source = modelUnit.PictureSource, Factory = modelUnit.Factory, EnlargesSharply = true },
                name,
                nationLine,
                ListHeading(Texts.Get("Units.New.ComesWith")),
            },
        };
        foreach (string comes in ComesWithModel(modelUnit)) details.Children.Add(ListLine("✓", comes, "ok"));
        details.Children.Add(ListHeading(Texts.Get("Units.New.Kept")));
        details.Children.Add(ListLine("✓", Texts.Get("Units.New.Kept.Values"), "ok"));
        details.Children.Add(ListLine("✓", Texts.Get("Units.New.Kept.Weapons"), "ok"));
        details.Children.Add(ListHeading(Texts.Get("Units.New.NotCopied")));
        details.Children.Add(ListLine("–", Texts.Get("Units.New.NotCopied.Divisions"), "muted"));
        ModelDetails.Content = details;
    }

    /// <summary>What a new unit takes from its model unit: its model, showroom view, card picture, type, and its row in the deck builder when it has one.</summary>
    private List<string> ComesWithModel(Unit chosen)
    {
        List<string> comes =
        [
            Texts.Get("Units.New.ComesWith.Model"),
            Texts.Get("Units.New.ComesWith.Showroom"),
            Texts.Get("Units.New.ComesWith.Picture"),
            Texts.Get("Units.New.ComesWith.Type", ("type", UnitNames.Factory(chosen.Factory))),
        ];
        if (chosen.UpgradesFrom is string upgrade)
            comes.Add(Texts.Get("Units.New.ComesWith.Upgrade", ("unit", units.FirstOrDefault(unit => unit.Descriptor == upgrade)?.Name ?? upgrade)));
        return comes;
    }

    /// <summary>A heading of the model unit's details: bold, in the accent colour, with a line under it.</summary>
    private static Bevel ListHeading(string text)
    {
        var words = new TextBlock { Text = text, FontWeight = FontWeights.Bold };
        words.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        var heading = new Bevel { Child = words, Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 2), Margin = new Thickness(0, 12, 0, 4) };
        heading.SetResourceReference(Bevel.BottomRightProperty, "line");
        return heading;
    }

    /// <summary>A line of the model unit's details: a mark (✓ or –) in its colour, then the words.</summary>
    private static DockPanel ListLine(string mark, string text, string markColour)
    {
        var markText = new TextBlock { Text = mark, Width = 14 };
        markText.SetResourceReference(TextBlock.ForegroundProperty, markColour);
        var words = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (markColour == "muted") words.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return new DockPanel { Margin = new Thickness(0, 1, 0, 1), Children = { markText, words } };
    }

    /// <summary>A click on a model unit chooses it; a double-click chooses it and goes on.</summary>
    private void ModelPressed(object sender, MouseButtonEventArgs e)
    {
        if (ModelAt(e.OriginalSource) is not Unit unit) return;
        e.Handled = true;
        ChooseModel(unit);
        if (e.ClickCount == 2) GoOn();
    }

    /// <summary>Chooses a model unit: the list shows it chosen, and the details show it.</summary>
    private void ChooseModel(Unit unit)
    {
        modelUnit = unit;
        chosenTile.SetAll([unit]);
        ModelChanged?.Invoke();
        ShowModelDetails();
        ShowButtons();
    }

    /// <summary>The unit of the row or tile under the mouse; null elsewhere.</summary>
    private static Unit? ModelAt(object? source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is ModelRow { Unit: Unit inRow }) return inRow;
            if (at is UnitTile tile) return tile.Unit;
        }
        return null;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (fillingLists) return;
        filter.Text = SearchBox.Text.Trim();
        FillModels();
    }

    private void RoleFilterChosen(object sender, SelectionChangedEventArgs e)
    {
        if (fillingLists) return;
        filter.Role = (RoleFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        FillModels();
    }

    /// <summary>Cards or Table clicked: the units are shown that way, and are the next time the window opens.</summary>
    private void ViewClicked(object sender, RoutedEventArgs e)
    {
        showsCards = ReferenceEquals(sender, CardsViewButton);
        AppData.Current.State["newUnitView"] = showsCards ? "cards" : "table";
        AppData.Current.SaveState();
        FillModelList();
    }

    /// <summary>The list was resized: in the Cards view, the rows are made again when a different number of tiles fits across.</summary>
    private void ModelListResized(object sender, SizeChangedEventArgs e)
    {
        if (showsCards && IsOpen && CardRowView.ColumnsFor(Math.Max(0, ModelList.ActualWidth - 14)) != cardColumns) FillModelList();
    }

    // ---- step 2: name and nation ----

    /// <summary>
    /// The Name and Nation step, for the model unit chosen: the nations of the game, the model unit's chosen at first (a name
    /// typed and a picture chosen stay); the roles, the model unit's chosen at first.
    /// </summary>
    private void FillNameStep()
    {
        if (modelUnit is null) return;
        fillingLists = true;
        string nationBefore = ChosenNation() ?? modelUnit.Nation;
        NationChoice.Items.Clear();
        foreach (string side in UnitNames.SidesOf(game))
            foreach (string nation in units.Where(unit => unit.Side == side).Select(unit => unit.Nation).Distinct().OrderBy(UnitNames.Nation, StringComparer.Create(Texts.Catalog.Culture, true)))
            {
                // the nation's mark shows on the tile beside it
                var item = new ComboBoxItem { Tag = nation, Content = UnitNames.Nation(nation) + " (" + UnitNames.Side(side) + ")" };
                NationChoice.Items.Add(item);
                if (nation == nationBefore) NationChoice.SelectedItem = item;
            }
        // the role chosen stays while the model unit does; another model unit starts on its own role
        string roleBefore = ReferenceEquals(nameStepFilledFor, modelUnit) ? ChosenRole() ?? modelUnit.Role : modelUnit.Role;
        nameStepFilledFor = modelUnit;
        RoleChoice.Items.Clear();
        foreach ((string code, string name) in RolesOf(units))
        {
            var item = new ComboBoxItem { Content = name, Tag = code };
            RoleChoice.Items.Add(item);
            if (code == roleBefore) RoleChoice.SelectedItem = item;
        }
        fillingLists = false;
        // named after the unit, so it is plain whose picture it is
        ModelPictureChoice.Content = Texts.Get("Units.New.Picture.Model", ("unit", modelUnit.Name));
        ModelOfText.Text = Texts.Get("Units.New.ModelOf", ("type", UnitNames.Factory(modelUnit.Factory)), ("unit", modelUnit.Name));
    }

    private string? ChosenNation() => (NationChoice.SelectedItem as ComboBoxItem)?.Tag as string;

    private string? ChosenRole() => (RoleChoice.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>A nation's side, as its units have it.</summary>
    private string SideOf(string nation) => units.FirstOrDefault(unit => unit.Nation == nation)?.Side ?? modelUnit?.Side ?? "";

    private void NameChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsOpen) return;
        ShowPreview();
        ShowButtons();
    }

    private void DetailChosen(object sender, SelectionChangedEventArgs e)
    {
        if (!fillingLists && IsOpen) ShowPreview();
    }

    /// <summary>"The model unit's": the model unit's card picture again.</summary>
    private void ModelPictureClicked(object sender, RoutedEventArgs e)
    {
        pictureFile = null;
        ModelPictureChoice.IsChecked = true;
        OwnPictureChoice.IsChecked = false;
        ShowPreview();
    }

    /// <summary>"My own…": a picture of the user's own, kept as a .png the game's tools read; when none is chosen, the model unit's stays.</summary>
    private void OwnPictureClicked(object sender, RoutedEventArgs e)
    {
        string? kept = choosePicture();
        if (kept is not null) pictureFile = kept;
        OwnPictureChoice.IsChecked = pictureFile is not null;
        ModelPictureChoice.IsChecked = pictureFile is null;
        ShowPreview();
    }

    /// <summary>The new unit's tile as it will model unit: its picture, name, nation and, once given, cost; and the picture file chosen.</summary>
    private void ShowPreview()
    {
        if (modelUnit is null) return;
        string nation = ChosenNation() ?? modelUnit.Nation;
        var unit = new Unit
        {
            Descriptor = ModUnit.DescriptorPrefix,
            Name = NameBox.Text.Trim() == "" ? Texts.Get("Units.New.Unnamed") : NameBox.Text.Trim(),
            Nation = nation,
            Side = SideOf(nation),
            Factory = modelUnit.Factory,
            Role = ChosenRole() ?? modelUnit.Role,
            // an empty box keeps the model unit's cost
            Cost = StatGiven("Units.Stat.Cost") ?? modelUnit.Cost,
            PictureSource = pictureFile ?? modelUnit.PictureSource,
        };
        bool firstSide = IsFirstSide(unit);
        if (preview is null) preview = new UnitTile(unit, firstSide, new UnitSelection()) { Cursor = Cursors.Arrow };
        else preview.Show(unit, firstSide);
        PreviewSlot.Content = preview;
        PictureFileText.Text = pictureFile is null ? "" : System.IO.Path.GetFileName(pictureFile);
        PictureFileText.ToolTip = pictureFile;
    }

    // ---- step 3: the stats ----

    /// <summary>The values the Stats step asks for, by category, as the Unit pane shows them.</summary>
    private static IReadOnlyList<(string Category, StatField[] Fields)> StatCategories(GameTitle game)
    {
        string? speedIn = game == GameTitle.Warno ? Texts.Get("Units.New.KilometresPerHour") : null;
        return
        [
            (Texts.Get("Units.Pane.Overview"),
            [
                new("Units.Stat.Cost", unit => unit.Cost, true, null, (made, value) => made.Cost = (int)value),
                new("Units.Stat.Strength", unit => unit.Strength, true, null, (made, value) => made.Strength = (int)value),
                new("Units.Stat.Speed", unit => unit.Speed, true, speedIn, (made, value) => made.Speed = (int)value),
                new("Units.Stat.RoadSpeed", unit => unit.RoadSpeed, true, Texts.Get("Units.New.KilometresPerHour"), (made, value) => made.RoadSpeed = (int)value),
                new("Units.Stat.Optics", unit => unit.Optics, false, null, (made, value) => made.Optics = value),
                new("Units.Stat.Stealth", unit => unit.Stealth, false, null, (made, value) => made.Stealth = value),
                new("Units.Pane.Fuel", unit => unit.Fuel, true, null, (made, value) => made.Fuel = (int)value),
                new("Units.Pane.Vision", unit => unit.Vision, false, null, (made, value) => made.Vision = value),
            ]),
            (Texts.Get("Units.Pane.Armor"),
            [
                new("Units.Pane.Front", unit => unit.Armor.Front, true, null, (made, value) => made.FrontArmor = (int)value, families => families.Front),
                new("Units.Pane.Sides", unit => unit.Armor.Sides, true, null, (made, value) => made.SideArmor = (int)value, families => families.Sides),
                new("Units.Pane.Rear", unit => unit.Armor.Rear, true, null, (made, value) => made.RearArmor = (int)value, families => families.Rear),
                new("Units.Pane.Top", unit => unit.Armor.Top, true, null, (made, value) => made.TopArmor = (int)value, families => families.Top),
            ]),
        ];
    }

    /// <summary>
    /// The Stats step, for the model unit chosen: a box for each value the model unit has (a unit without fuel, say, is not asked
    /// for it), under the Unit pane's categories, each empty, with the model unit's value in it, dim: an empty box
    /// keeps that value. Values already typed stay when the model unit is the same.
    /// </summary>
    private void FillStatsStep()
    {
        if (modelUnit is null) return;
        Dictionary<string, string> typed = statBoxes.ToDictionary(each => each.Field.Name, each => each.Box.Text);
        statBoxes.Clear();
        StatsPanel.Children.Clear();
        foreach ((string category, StatField[] fields) in StatCategories(game))
        {
            StatField[] asked = [.. fields.Where(field => field.Read(modelUnit) is not null)];
            if (asked.Length == 0) continue;
            var heading = new Bevel { Style = (Style)FindResource("Heading"), Child = new TextBlock { Text = category }, Margin = new Thickness(0, StatsPanel.Children.Count == 0 ? 0 : 10, 0, 0) };
            StatsPanel.Children.Add(heading);
            var grid = new Grid();
            foreach (GridLength width in new[] { new GridLength(130), new GridLength(110), new GridLength(50) })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            foreach (StatField field in asked) AddStatRow(grid, field, typed.GetValueOrDefault(field.Name) ?? "");
            StatsPanel.Children.Add(grid);
        }
    }

    /// <summary>A value's row: its name, and its box, with the model unit's value in it, dim, while it is empty; then what it is in.</summary>
    private void AddStatRow(Grid grid, StatField field, string text)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = new TextBlock { Text = Texts.Get(field.Name), VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        string modelValue = field.IsWhole ? UnitNumbers.Of(field.Read(modelUnit!), 0) : UnitNumbers.Of(field.Read(modelUnit!), 2);
        // the text box's hint (its Tag) shows the value an empty box keeps
        var box = new TextBox { Text = text, Tag = modelValue, Margin = new Thickness(0, 3, 0, 3), ToolTip = StatTip(field) };
        box.TextChanged += (_, _) => StatTyped(box, field);
        var unitText = new TextBlock { Text = field.In ?? "", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        unitText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        Place(grid, name, row, 0);
        Place(grid, box, row, 1);
        Place(grid, unitText, row, 2);
        statBoxes.Add((field, box));
        ShowStatBox(box, field);
    }

    private static void Place(Grid grid, UIElement cell, int row, int column)
    {
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    /// <summary>A value typed: its box shows whether it is one the game takes, and Create follows.</summary>
    private void StatTyped(TextBox box, StatField field)
    {
        ShowStatBox(box, field);
        if (field.Name == "Units.Stat.Cost") ShowPreview();
        ShowButtons();
    }

    /// <summary>A box with text that is not a value the game takes is outlined in the caution colour.</summary>
    private void ShowStatBox(TextBox box, StatField field)
    {
        bool wrong = box.Text.Trim() != "" && ValueOf(box.Text, field) is null;
        // two pixels wide, so the box stands out among the others
        if (wrong)
        {
            box.SetResourceReference(BorderBrushProperty, "caution");
            box.BorderThickness = new Thickness(2);
        }
        else
        {
            box.ClearValue(BorderBrushProperty);
            box.ClearValue(BorderThicknessProperty);
        }
    }

    /// <summary>
    /// A value typed, when the game takes it: 0 or more, and for a value the game writes as a whole number, a whole
    /// number; for an armour side, a level its armour family allows (<see cref="ArmorLevels"/>). Typed as the app
    /// writes numbers in its language, or with a "." for the decimals.
    /// </summary>
    private double? ValueOf(string text, StatField field)
    {
        string trimmed = text.Trim();
        bool read = double.TryParse(trimmed, NumberStyles.Float, Texts.Catalog.Culture, out double value)
            || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (!read || value < 0 || double.IsInfinity(value)) return null;
        if (field.IsWhole && (value != Math.Floor(value) || value > int.MaxValue)) return null;
        if (FamilyOf(field) is string family && !ArmorLevels.Allows(armorLevels, family, (int)value)) return null;
        return value;
    }

    /// <summary>The model unit's armour family for an armour side's box; null for other values.</summary>
    private string? FamilyOf(StatField field) => field.Family is null || modelUnit is null ? null : field.Family(modelUnit.ArmorFamilies);

    /// <summary>
    /// A box's tooltip: what it takes; for an armour side, the levels its armour family allows, as the game's
    /// DamageResistance.ndf gives them.
    /// </summary>
    private string StatTip(StatField field)
    {
        if (FamilyOf(field) is string family && ArmorLevels.RangeOf(armorLevels, family) is (int lowest, int highest))
            return Texts.Get("Units.New.Stat.Armor.Tip", ("family", UnitNames.ArmorFamily(family)), ("lowest", lowest), ("highest", highest));
        return Texts.Get(field.IsWhole ? "Units.New.Stat.Whole.Tip" : "Units.New.Stat.Decimal.Tip");
    }

    /// <summary>Whether every value typed is one the game takes; an empty box keeps the model unit's value.</summary>
    private bool StatsAreValid() => statBoxes.All(each => each.Box.Text.Trim() == "" || ValueOf(each.Box.Text, each.Field) is not null);

    /// <summary>A value given in the Stats step, by its name; null when it is not (yet).</summary>
    private double? StatGiven(string name) =>
        statBoxes.Where(each => each.Field.Name == name).Select(each => ValueOf(each.Box.Text, each.Field)).FirstOrDefault();

    // ---- step 4: the weapons ----

    /// <summary>
    /// The Weapons step: the model unit's loadout in the weapons editor, with the changes made so far; another model
    /// unit chosen since starts it again, as the changes are by places in its model's loadout.
    /// </summary>
    private void FillWeaponsStep()
    {
        if (modelUnit is null || weaponsSource is null) return;
        if (!ReferenceEquals(weaponsFor, modelUnit))
        {
            pendingWeapons.Clear();
            weaponsFor = modelUnit;
        }
        pendingWeapons.UnitName = NameBox.Text;
        pendingWeapons.UnitDescriptor = ModUnit.NewDescriptor(NameBox.Text.Trim(), ChosenNation() ?? modelUnit.Nation, takenDescriptors);
        WeaponsPanel.Content = weaponsSource.WeaponsEditorFor(modelUnit, pendingWeapons, FillWeaponsStep)
            ?? (object)Units.UnitPane.Muted(Texts.Get("Units.New.Weapons.NotKnown", ("unit", modelUnit.Name)), new Thickness(0));
        WeaponChecksPanel.Content = weaponsSource.WeaponChecksFor(modelUnit, pendingWeapons);
    }

    // ---- making it ----

    private void CreateClicked(object sender, RoutedEventArgs e) => Create();

    /// <summary>The unit made from what the steps give; the window closes unless it was refused.</summary>
    private void Create()
    {
        if (!CreateButton.IsEnabled || modelUnit is null) return;
        string name = NameBox.Text.Trim();
        string nation = ChosenNation() ?? modelUnit.Nation;
        string role = ChosenRole() ?? modelUnit.Role;
        // only what the user changed is kept: the rest stays the model unit's
        var made = new ModUnit
        {
            Descriptor = ModUnit.NewDescriptor(name, nation, takenDescriptors),
            MadeFrom = modelUnit.Descriptor,
            Name = name,
            Nation = nation == modelUnit.Nation ? null : nation,
            Side = nation == modelUnit.Nation ? null : SideOf(nation),
            Role = role == modelUnit.Role ? null : role,
            PictureFile = pictureFile,
        };
        foreach ((StatField field, TextBox box) in statBoxes)
            if (ValueOf(box.Text, field) is double value) field.Give(made, value);
        // the weapons changed in the Weapons step, for this model unit; none when it was not shown for it
        if (ReferenceEquals(weaponsFor, modelUnit) && pendingWeapons.Changes.Count > 0)
        {
            made.LoadoutChanges = [.. pendingWeapons.Changes];
            made.LoadoutFrom = weaponsSource?.LoadoutOfModel(modelUnit)?.Descriptor;
        }
        List<ModWeapon> copies = ReferenceEquals(weaponsFor, modelUnit) ? [.. pendingWeapons.Copies] : [];
        if (create(made, copies)) Close();
    }

    private void Close() => Visibility = Visibility.Collapsed;

    // ---- for the workbench's checks, which drive the window without a mouse ----

    /// <summary>Chooses a model unit, as a click on it does.</summary>
    internal void ChooseModelNow(Unit unit) => ChooseModel(unit);

    /// <summary>Next, as its button does.</summary>
    internal void GoOnNow() => GoOn();

    /// <summary>Types the unit's name.</summary>
    internal void TypeNameNow(string name) => NameBox.Text = name;

    /// <summary>Types the same value into every box of the Stats step; how many boxes there are.</summary>
    internal int GiveEveryStatNow(string value)
    {
        foreach ((_, TextBox box) in statBoxes) box.Text = value;
        return statBoxes.Count;
    }

    /// <summary>Whether Next and Create can be clicked now.</summary>
    internal (bool Next, bool Create) ButtonsNow => (NextButton.IsEnabled, CreateButton.IsEnabled);

    /// <summary>Create, as its button does; from the Stats step, through the Weapons step, its weapons left as they are.</summary>
    internal void CreateNow()
    {
        if (step == Step.Stats) GoOn();
        Create();
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => Close();

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && step == Step.Weapons && CreateButton.IsEnabled)
        {
            e.Handled = true;
            Create();
        }
        else if (e.Key == Key.Enter && step != Step.Weapons && NextButton.IsEnabled)
        {
            e.Handled = true;
            GoOn();
        }
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>1000 pixels wide, or 96% of the window when it is narrower.</summary>
    private void BackdropResized(object sender, SizeChangedEventArgs e) => FitWidth();

    /// <summary>
    /// The widest the window is made on the steps with lists to look through (Model, Weapons), about twice the first
    /// window's size; on the steps with a few boxes (Name and Nation, Stats), as wide as the first; and the tallest its
    /// steps are.
    /// </summary>
    private const double WidestWindow = 1600, WidestForBoxes = 1000, TallestStep = 760;

    /// <summary>The room the title, the steps and the foot take above and below a step.</summary>
    private const double TitleAndFoot = 130;

    /// <summary>
    /// The window's size: 1600 pixels wide for Model and Weapons, 1000 for Name and Nation and Stats, which have only a
    /// few boxes; steps up to 760 tall; less in a smaller main window (96% of its width, and of its height with the
    /// title and foot).
    /// </summary>
    private void FitWidth()
    {
        if (Backdrop.ActualWidth <= 0) return;
        double widest = step is Step.Model or Step.Weapons ? WidestWindow : WidestForBoxes;
        Box.Width = Math.Min(widest, (Backdrop.ActualWidth + 20) * 0.96);
        double stepHeight = Math.Max(300, Math.Min(TallestStep, Backdrop.ActualHeight * 0.96 - TitleAndFoot));
        ModelStep.Height = stepHeight;
        StatsStep.MaxHeight = stepHeight;
        WeaponsStep.Height = stepHeight;
    }
}

/// <summary>
/// A model unit's row in the New Unit window's Table view: its picture, nation's mark, name, role and cost. Under the mouse
/// it takes the selection colour; the model unit chosen, the hover colour.
/// </summary>
public sealed class ModelRow : Bevel
{
    private readonly Grid cells = new();
    private ModelLine? line;

    /// <summary>Builds the row when the list gives it its line.</summary>
    public ModelRow()
    {
        Edges = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(6, 2, 6, 2);
        SetResourceReference(BottomRightProperty, "line");
        Child = cells;
        Cursor = Cursors.Hand;
        foreach (GridLength width in new[] { new GridLength(54), GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(130), new GridLength(44) })
            cells.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        DataContextChanged += (_, _) => Build();
        MouseEnter += (_, _) => ShowState();
        MouseLeave += (_, _) => ShowState();
        Loaded += (_, _) => { if (line is not null) line.Window.ModelChanged += ShowState; ShowState(); };
        Unloaded += (_, _) => { if (line is not null) line.Window.ModelChanged -= ShowState; };
    }

    /// <summary>The unit shown.</summary>
    public Unit? Unit => line?.Unit;

    private void Build()
    {
        if (line is not null && IsLoaded) line.Window.ModelChanged -= ShowState;
        cells.Children.Clear();
        line = DataContext as ModelLine;
        if (line is null) return;
        if (IsLoaded) line.Window.ModelChanged += ShowState;
        Unit unit = line.Unit;
        var name = new TextBlock { Text = unit.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var role = new TextBlock { Text = UnitNames.RoleOf(unit), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        role.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var cost = new TextBlock { Text = unit.Cost is null ? "" : UnitNumbers.Of(unit.Cost), FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        cost.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        Place(new UnitPicture { Source = unit.PictureSource, Factory = unit.Factory, Width = 54 }, 0, 0);
        Place(new NationMark(unit.Nation, line.Window.IsFirstSide(unit)), 1, 8);
        Place(name, 2, 8);
        Place(role, 3, 8);
        Place(cost, 4, 8);
        ToolTip = unit.Descriptor;
        ShowState();
    }

    /// <summary>Puts a cell in its column, some pixels after the one before.</summary>
    private void Place(FrameworkElement cell, int column, double gapBefore)
    {
        cell.Margin = new Thickness(gapBefore, 0, 0, 0);
        Grid.SetColumn(cell, column);
        cells.Children.Add(cell);
    }

    /// <summary>The hover colour when chosen, the selection colour under the mouse, else none.</summary>
    private void ShowState()
    {
        if (line is null) return;
        if (line.Window.IsChosen(line.Unit)) SetResourceReference(BackgroundProperty, "hover");
        else if (IsMouseOver) SetResourceReference(BackgroundProperty, "sel");
        else Background = Brushes.Transparent;
    }
}
