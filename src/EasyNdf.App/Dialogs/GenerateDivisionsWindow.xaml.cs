using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Dialogs;

/// <summary>A division the Division Generator makes: how it is made, and its name.</summary>
/// <param name="Generation">How it is made.</param>
/// <param name="Name">Its name, from the window's name pattern.</param>
public sealed record GeneratedDivisionChoice(DivisionGeneration Generation, string Name);

/// <summary>
/// The Division Generator, opened from ⚙ beside the Divisions heading: one division for each nation and side selected
/// in step 1, times each kind selected in step 2, each listed in the Generation Preview, where a row's box leaves that
/// division out. Opened from a generated division's "Change…", it changes how that division is made, with one choice
/// in each step. Generate (or Done) hands back the choice; Cancel, ×, Escape or a click outside change nothing.
/// </summary>
public partial class GenerateDivisionsWindow : UserControl
{
    /// <summary>The name pattern the window starts with: "YSM SOV – Limited".</summary>
    private const string DefaultNames = "{mod} {nation} – {kind}";

    /// <summary>The bytes a unit's line takes in the exported DivisionRules.ndf, about, when no list could be measured.</summary>
    private const double DefaultBytesPerUnit = 3000;

    /// <summary>What Generate does with the divisions made; null in change mode.</summary>
    private Action<List<GeneratedDivisionChoice>>? generate;

    /// <summary>What Done does with the one division's new making; null in generate mode.</summary>
    private Action<DivisionGeneration>? change;

    /// <summary>The units the divisions are made from, for the counts, sizes and sample card shown.</summary>
    private DivisionRosters? rosters;

    /// <summary>The game, for its sides.</summary>
    private GameTitle game = GameTitle.Warno;

    /// <summary>The open mod's name, for the name pattern.</summary>
    private string modName = "";

    /// <summary>The name of the division being changed, for the preview; empty in generate mode.</summary>
    private string changedName = "";

    /// <summary>The names of the open mod's divisions: a division of the same name is marked ⚠ in the preview.</summary>
    private HashSet<string> takenNames = [];

    /// <summary>The bytes a unit's line takes in the exported file, measured on one list as the window opens.</summary>
    private double bytesPerUnit = DefaultBytesPerUnit;

    /// <summary>Builds the window, closed, with the veterancy marks under Limited's numbers and the names' suggestions.</summary>
    public GenerateDivisionsWindow()
    {
        InitializeComponent();
        for (int level = 0; level < 4; level++)
        {
            var place = new Border
            {
                Width = 38, Margin = new Thickness(level == 0 ? 0 : 4, 0, 0, 0), Background = Brushes.Transparent,
                Child = VeterancyMarks.Mark(level), ToolTip = Texts.Get("Divisions.Generate.Level.Tip", ("level", Texts.Number(level + 1))),
            };
            ((FrameworkElement)place.Child).HorizontalAlignment = HorizontalAlignment.Center;
            CountMarks.Children.Add(place);
        }
        // Steel Division 2's: a phase's letter under each of its boxes, and a level's mark under each share
        TextBox[] phaseBoxes = PhaseBoxes(), shareBoxes = ShareBoxes();
        for (int phase = 0; phase < phaseBoxes.Length; phase++)
        {
            string letter = ((char)('A' + phase)).ToString();
            phaseBoxes[phase].ToolTip = Texts.Get("Divisions.Generate.Phase.Tip", ("phase", letter));
            var mark = new TextBlock { Text = letter, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            mark.SetResourceReference(TextBlock.ForegroundProperty, "ink");
            PhaseMarks.Children.Add(new Border { Width = 38, Margin = new Thickness(phase == 0 ? 0 : 4, 0, 0, 0), Child = mark, ToolTip = phaseBoxes[phase].ToolTip });
        }
        for (int level = 0; level < shareBoxes.Length; level++)
        {
            string tip = Texts.Get("Divisions.Generate.Share.Tip", ("level", Texts.Number(level + 1)));
            shareBoxes[level].ToolTip = tip;
            var place = new Border { Width = 38, Margin = new Thickness(level == 0 ? 0 : 4, 0, 0, 0), Background = Brushes.Transparent, Child = VeterancyMarks.Mark(level), ToolTip = tip };
            ((FrameworkElement)place.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ShareMarks.Children.Add(place);
        }
        Suggestions.Attach(NamesBox, NameSuggestions);
    }

    /// <summary>
    /// Whether the divisions' cards hold units by phase, as Steel Division 2's do (<see cref="DivisionGeneration.ByPhase"/>):
    /// step 2 then asks for the units in each phase and their share at each veterancy, and step 3 for the income choices.
    /// </summary>
    private bool ByPhase => game == GameTitle.SteelDivision2;

    /// <summary>The income choices offered in step 3 (Steel Division 2's DivisionEconomy lists); none for WARNO.</summary>
    private IReadOnlyList<string> economies = [];

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Whether the window changes one division's making, rather than making new divisions.</summary>
    private bool IsChanging => change is not null;

    /// <summary>Opens the window to make new divisions, with nothing selected but Limited.</summary>
    /// <param name="units">The units the divisions are made from (as the Armory shows them).</param>
    /// <param name="title">The game, for its sides.</param>
    /// <param name="mod">The open mod's name, for the name pattern.</param>
    /// <param name="divisionNames">The names of the open mod's divisions, to mark a name made again.</param>
    /// <param name="modNations">The nations the mods add of their own (no unit of the game is of them).</param>
    /// <param name="incomes">The game's income choices (Steel Division 2's DivisionEconomy lists); none for WARNO.</param>
    /// <param name="whenDone">What Generate does with the divisions made.</param>
    public void Open(DivisionRosters units, GameTitle title, string mod, IEnumerable<string> divisionNames, IReadOnlyList<string> modNations,
        IReadOnlyList<string> incomes, Action<List<GeneratedDivisionChoice>> whenDone)
    {
        generate = whenDone;
        change = null;
        changedName = "";
        takenNames = [.. divisionNames];
        Title.Text = Texts.Get("Divisions.Generate.Title");
        Lead.Text = Texts.Get("Divisions.Generate.Lead");
        // any reason shown from before is taken away first, so the button's own tooltip is the one it keeps (OffReason)
        OffReason.SetText(GoButton, null);
        GoButton.Content = Texts.Get("Divisions.Generate.Go");
        GoButton.ToolTip = Texts.Get("Divisions.Generate.Go.Tip");
        game = title;
        economies = incomes;
        ShowNames(true);
        NamesBox.Text = DefaultNames;
        Prepare(units, title, mod, new DivisionGeneration { ByPhase = ByPhase, Economy = FirstEconomy(incomes) }, modNations);
        SameTick.IsChecked = true;
        Show();
    }

    /// <summary>The income choice a new set of divisions starts with: the game's Default one, else the first.</summary>
    private static string FirstEconomy(IReadOnlyList<string> incomes) =>
        incomes.FirstOrDefault(economy => DivisionDetailsEditor.EconomyName(economy) == "Default") ?? incomes.FirstOrDefault() ?? "";

    /// <summary>Opens the window to change how one generated division is made, its choices selected.</summary>
    /// <param name="division">The division's name, for the title.</param>
    /// <param name="current">How it is made now.</param>
    /// <param name="units">The units it is made from (as the Armory shows them).</param>
    /// <param name="title">The game, for its sides.</param>
    /// <param name="modNations">The nations the mods add of their own.</param>
    /// <param name="incomes">The game's income choices (Steel Division 2's DivisionEconomy lists); none for WARNO.</param>
    /// <param name="whenDone">What Done does with its new making.</param>
    public void OpenToChange(string division, DivisionGeneration current, DivisionRosters units, GameTitle title, IReadOnlyList<string> modNations,
        IReadOnlyList<string> incomes, Action<DivisionGeneration> whenDone)
    {
        generate = null;
        change = whenDone;
        changedName = division;
        takenNames = [];
        Title.Text = Texts.Get("Divisions.Generate.ChangeTitle", ("division", division));
        Lead.Text = Texts.Get("Divisions.Generate.ChangeLead");
        // any reason shown from before is taken away first, so the button's own tooltip is the one it keeps (OffReason)
        OffReason.SetText(GoButton, null);
        GoButton.Content = Texts.Get("Divisions.Generate.Done");
        GoButton.ToolTip = Texts.Get("Divisions.Generate.Done.Tip");
        game = title;
        economies = incomes;
        ShowNames(false);
        // a division made before it was by phase (or with no income choices) is shown as a new one starts
        if (title == GameTitle.SteelDivision2 && !current.ByPhase) current = current with { ByPhase = true };
        if (current.ByPhase && current.Economy == "") current = current with { Economy = FirstEconomy(incomes) };
        Prepare(units, title, "", current, modNations);
        SelectWhatMakes(current);
        Show();
    }

    /// <summary>Step 3 with the names (making divisions) or without them (changing one division's making).</summary>
    private void ShowNames(bool shown)
    {
        Visibility visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        NamesLabel.Visibility = NamesRow.Visibility = NamesHint.Visibility = visibility;
        NamesHeading.Text = Texts.Get((shown, ByPhase) switch
        {
            (true, false) => "Divisions.Generate.Step.Names",
            (false, false) => "Divisions.Generate.Step.Points",
            (true, true) => "Divisions.Generate.Step.NamesIncome",
            _ => "Divisions.Generate.Step.PointsIncome",
        });
    }

    /// <summary>
    /// What the game asks for: WARNO's four numbers by veterancy; or Steel Division 2's numbers by phase with their
    /// shares by veterancy, its notes saying "in every phase" and "in each phase", and its income choices, those of a
    /// generation selected.
    /// </summary>
    private void ShowGameParts(DivisionGeneration start)
    {
        VeterancyCounts.Visibility = ByPhase ? Visibility.Collapsed : Visibility.Visible;
        PhaseSettings.Visibility = IncomeLabel.Visibility = IncomeRow.Visibility = ByPhase ? Visibility.Visible : Visibility.Collapsed;
        UnlimitedNote.Text = Texts.Get(ByPhase ? "Divisions.Generate.UnlimitedByPhase.Note" : "Divisions.Generate.Unlimited.Note");
        UnlimitedTick.ToolTip = Texts.Get(ByPhase ? "Divisions.Generate.UnlimitedByPhase.Tip" : "Divisions.Generate.Unlimited.Tip");
        SameTick.ToolTip = Texts.Get(ByPhase ? "Divisions.Generate.SameByPhase.Tip" : "Divisions.Generate.Same.Tip");
        AsTheGameNote.Text = Texts.Get(ByPhase ? "Divisions.Generate.AsTheGameByPhase.Note" : "Divisions.Generate.AsTheGame.Note");
        TextBox[] phaseBoxes = PhaseBoxes(), shareBoxes = ShareBoxes();
        for (int phase = 0; phase < phaseBoxes.Length; phase++) phaseBoxes[phase].Text = start.PhaseCounts[phase].ToString(Texts.Catalog.Culture);
        for (int level = 0; level < shareBoxes.Length; level++) shareBoxes[level].Text = start.VeterancyShares[level].ToString("0.0##", Texts.Catalog.Culture);
        IncomeChoice.Items.Clear();
        foreach (string economy in economies.Append(start.Economy).Where(economy => economy != "").Distinct(StringComparer.Ordinal))
            IncomeChoice.Items.Add(new ComboBoxItem { Content = DivisionDetailsEditor.EconomyName(economy), Tag = economy, ToolTip = economy });
        IncomeChoice.SelectedItem = IncomeChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == start.Economy);
        IncomeChoice.IsEnabled = IncomeChoice.Items.Count > 0;
        ShowIncomeNote();
    }

    private TextBox[] PhaseBoxes() => [PhaseA, PhaseB, PhaseC];

    private TextBox[] ShareBoxes() => [ShareLowest, ShareMiddle, ShareHighest];

    /// <summary>The income choice selected; "" when there is none to choose.</summary>
    private string EconomyChosen() => IncomeChoice.SelectedItem is ComboBoxItem { Tag: string economy } ? economy : "";

    /// <summary>An income choice chosen: its note follows.</summary>
    private void IncomeChosen(object sender, SelectionChangedEventArgs e)
    {
        ShowIncomeNote();
        ShowSummary();
    }

    /// <summary>The note beside the income choices: whose phases and income they bring.</summary>
    private void ShowIncomeNote() =>
        IncomeNote.Text = EconomyChosen() is { Length: > 0 } economy ? Texts.Get("Divisions.Generate.Income.Note", ("economy", DivisionDetailsEditor.EconomyName(economy))) : "";

    /// <summary>
    /// The nations offered for every division: the mods' own, and those the division being changed has already. Empty
    /// when there are none, and the tick is then off and greyed.
    /// </summary>
    private List<string> everyDivisionNations = [];

    /// <summary>The mods' own nations, marked "mod" in step 1's list.</summary>
    private HashSet<string> modNationCodes = [];

    /// <summary>Fills the lists and boxes for the units given, with a generation's counts, slots and other settings.</summary>
    private void Prepare(DivisionRosters units, GameTitle title, string mod, DivisionGeneration start, IReadOnlyList<string> modNations)
    {
        rosters = units;
        game = title;
        modName = mod;
        scopeTicks.Clear();
        unitCounts.Clear();
        leftOut.Clear();
        sortColumn = null;
        sampleUnit = null;
        sampleKind = null;
        SampleSlot.Content = null;
        FindBox.Text = "";
        modNationCodes = [.. modNations];
        everyDivisionNations = [.. start.EveryDivisionNations.Union(modNations, StringComparer.Ordinal)];
        FillNations(units);
        FillSides();
        UnlimitedTick.IsChecked = SameTick.IsChecked = AsTheGameTick.IsChecked = false;
        // wrapped: the nations named can be many, and the step is narrow
        ModNationsTick.Content = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = everyDivisionNations.Count == 0
                ? Texts.Get("Divisions.Generate.ModNations.None")
                : Texts.Get("Divisions.Generate.ModNations", ("nations", string.Join(", ", everyDivisionNations.Select(UnitNames.Nation)))),
        };
        ModNationsTick.IsEnabled = everyDivisionNations.Count > 0;
        // a new set of divisions takes the mods' own nations unless told not to; a division being changed keeps its choice
        ModNationsTick.IsChecked = everyDivisionNations.Count > 0 && (!IsChanging || start.EveryDivisionNations.Count > 0);
        IgnoredDivisionsBox.Text = string.Join(", ", start.IgnoredDivisions);
        IgnoredRulesBox.Text = start.IgnoredRulesFrom?.ToString(Texts.Catalog.Culture) ?? "";
        TextBox[] countBoxes = CountBoxes();
        for (int level = 0; level < countBoxes.Length; level++) countBoxes[level].Text = start.Counts[level].ToString(Texts.Catalog.Culture);
        SlotsBox.Text = start.Slots.ToString(Texts.Catalog.Culture);
        SlotsNote.Text = Texts.Get("Divisions.Generate.Slots.Note", ("categories", Categories));
        PointsBox.ToolTip = Texts.Get("Divisions.Generate.Points.Tip", ("categories", Categories));
        ShowGameParts(start);
        bytesPerUnit = MeasureBytesPerUnit(units, ByPhase);
    }

    private void Show()
    {
        ShowSummary();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => GoButton.Focus());
    }

    private TextBox[] CountBoxes() => [CountLowest, CountSecond, CountThird, CountHighest];

    /// <summary>The categories a division has cards in, each with as many slots as typed: nine, in either game.</summary>
    private int Categories => new DivisionGeneration { ByPhase = ByPhase }.CostCategories.Count;

    // ---- step 1: the nations and sides ----

    /// <summary>
    /// A tick for each nation whose units a deck can hold, those with the most units first, with their count; the mods'
    /// own nations among them, marked "mod".
    /// </summary>
    private void FillNations(DivisionRosters units)
    {
        NationTicks.Children.Clear();
        // each nation with the units its division gets, as the Generation Preview counts them (Steel Division 2's leave out
        // those only DLC divisions carry); a nation left with none is not offered
        var byNation = units.AllUnits.Where(DivisionRosters.IsDeckable)
            .GroupBy(unit => unit.Nation)
            .Select(nation => new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = nation.Key, Side = nation.First().Side, ByPhase = ByPhase })
            .Select(generation => (Generation: generation, Count: UnitsIn(generation)))
            .Where(nation => nation.Count > 0)
            .OrderByDescending(nation => nation.Count)
            .ThenBy(nation => UnitNames.Nation(nation.Generation.Nation), StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
        foreach ((DivisionGeneration generation, int count) in byNation)
            NationTicks.Children.Add(ScopeTick(UnitNames.Nation(generation.Nation), count, generation, isModNation: modNationCodes.Contains(generation.Nation)));
    }

    /// <summary>A tick for each side's nations, then one for every unit under each side's flag.</summary>
    private void FillSides()
    {
        SideTicks.Children.Clear();
        FlagTicks.Children.Clear();
        IReadOnlyList<string> sides = UnitNames.SidesOf(game);
        foreach (string side in sides)
        {
            var generation = new DivisionGeneration { Units = GeneratedUnits.Side, Side = side, ByPhase = ByPhase };
            SideTicks.Children.Add(ScopeTick(Texts.Get("Divisions.Generate.Side", ("side", UnitNames.Side(side))), UnitsIn(generation), generation));
        }
        foreach (string side in sides)
        {
            var generation = new DivisionGeneration { Units = GeneratedUnits.Everything, Side = side, ByPhase = ByPhase };
            FlagTicks.Children.Add(ScopeTick(Texts.Get("Divisions.Generate.Everything", ("side", UnitNames.Side(side))), UnitsIn(generation),
                generation, Texts.Get("Divisions.Generate.Everything.Tip", ("side", UnitNames.Side(side)))));
        }
    }

    /// <summary>
    /// A nation's or side's tick: its name, a "mod" tag for a mod's own nation, and its units at the right. Its tag is
    /// the generation it stands for (which units), so the choices can be read back.
    /// </summary>
    private CheckBox ScopeTick(string name, int units, DivisionGeneration generation, string tip = "", bool isModNation = false)
    {
        var label = new DockPanel();
        var countText = new TextBlock { Text = Texts.Number(units), Margin = new Thickness(8, 0, 0, 0) };
        countText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(countText, Dock.Right);
        label.Children.Add(countText);
        var nameText = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        nameText.Inlines.Add(name);
        if (isModNation)
        {
            var tag = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(5, 0, 0, 0), Child = new TextBlock { Text = Texts.Get("Divisions.Generate.ModTag"), FontSize = 10.5 } };
            tag.SetResourceReference(Border.BorderBrushProperty, "caution");
            ((TextBlock)tag.Child).SetResourceReference(TextBlock.ForegroundProperty, "caution");
            nameText.Inlines.Add(new System.Windows.Documents.InlineUIContainer(tag) { BaselineAlignment = BaselineAlignment.Center });
            tip = Texts.Get("Divisions.Generate.ModNation.Tip");
        }
        label.Children.Add(nameText);
        var tick = new CheckBox { Style = (Style)FindResource("Tick"), Content = label, Tag = generation, Margin = new Thickness(0, 2, 0, 2) };
        if (tip != "") tick.ToolTip = tip;
        tick.Click += TickClicked;
        scopeTicks.Add(tick);
        return tick;
    }

    /// <summary>Every nation's and side's tick, in the order shown.</summary>
    private readonly List<CheckBox> scopeTicks = [];

    private IEnumerable<CheckBox> ScopeTicks() => scopeTicks;

    private IEnumerable<CheckBox> KindTicks() => [UnlimitedTick, SameTick, AsTheGameTick];

    /// <summary>
    /// "Find a nation": only the nations whose name holds what is typed; the sides' lists are hidden while something is
    /// typed. What is selected stays selected.
    /// </summary>
    private void FindChanged(object sender, TextChangedEventArgs e)
    {
        string typed = FindBox.Text.Trim();
        foreach (UIElement child in NationTicks.Children)
        {
            if (child is not CheckBox { Tag: DivisionGeneration generation }) continue;
            child.Visibility = typed == "" || UnitNames.Nation(generation.Nation).Contains(typed, StringComparison.CurrentCultureIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
        }
        SideLists.Visibility = typed == "" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Selects the nation or side and the kind a generation is made with, for Change.</summary>
    private void SelectWhatMakes(DivisionGeneration current)
    {
        foreach (CheckBox tick in ScopeTicks())
        {
            var stands = (DivisionGeneration)tick.Tag;
            tick.IsChecked = stands.Units == current.Units && stands.Side == current.Side && (current.Units != GeneratedUnits.Nation || stands.Nation == current.Nation);
        }
        UnlimitedTick.IsChecked = current.Numbers == GeneratedNumbers.Unlimited;
        SameTick.IsChecked = current.Numbers == GeneratedNumbers.Same;
        AsTheGameTick.IsChecked = current.Numbers == GeneratedNumbers.AsTheGame;
    }

    // ---- the choices ----

    /// <summary>
    /// A tick clicked. Changing one division's making takes one nation or side and one kind: the tick clicked replaces
    /// the others of its step, and stays ticked.
    /// </summary>
    private void TickClicked(object sender, RoutedEventArgs e)
    {
        if (IsChanging && sender is CheckBox clicked && !ReferenceEquals(clicked, ModNationsTick))
        {
            IEnumerable<CheckBox> group = KindTicks().Contains(clicked) ? KindTicks() : ScopeTicks();
            foreach (CheckBox other in group) other.IsChecked = ReferenceEquals(other, clicked);
        }
        ShowSummary();
    }

    private void ValueChanged(object sender, TextChangedEventArgs e) => ShowSummary();

    /// <summary>"Leave out…": the pop-up with the game divisions and rules "As the game's divisions" does not learn from.</summary>
    private void LeaveOutClicked(object sender, RoutedEventArgs e) => LeaveOutPopup.IsOpen = !LeaveOutPopup.IsOpen;

    /// <summary>The units a card holds at each veterancy, as typed; null when one is not a whole number above nothing.</summary>
    private List<int>? CountsTyped()
    {
        var counts = new List<int>();
        foreach (TextBox box in CountBoxes())
        {
            if (!int.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Integer, Texts.Catalog.Culture, out int count) || count < 0) return null;
            counts.Add(count);
        }
        return counts.Max() > 0 ? counts : null;
    }

    /// <summary>The units a card holds in each phase, as typed; null when one is not a whole number above nothing.</summary>
    private List<int>? PhasesTyped()
    {
        var counts = new List<int>();
        foreach (TextBox box in PhaseBoxes())
        {
            if (!int.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Integer, Texts.Catalog.Culture, out int count) || count < 0) return null;
            counts.Add(count);
        }
        return counts.Max() > 0 ? counts : null;
    }

    /// <summary>The share at each veterancy, as typed; null when one is not a number from 0, or none is above it.</summary>
    private List<double>? SharesTyped()
    {
        var shares = new List<double>();
        foreach (TextBox box in ShareBoxes())
        {
            if (!double.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Float, Texts.Catalog.Culture, out double share) || share < 0) return null;
            shares.Add(share);
        }
        return shares.Max() > 0 ? shares : null;
    }

    /// <summary>Whether Limited's numbers are typed right for the game: four whole numbers, or three by phase and three shares.</summary>
    private bool LimitedTypedRight() => ByPhase ? PhasesTyped() is not null && SharesTyped() is not null : CountsTyped() is not null;

    /// <summary>The parts of division names typed in "not from divisions named", one between each comma.</summary>
    private List<string> IgnoredDivisionsTyped() =>
        [.. IgnoredDivisionsBox.Text.Split(',').Select(part => part.Trim()).Where(part => part != "")];

    /// <summary>The number typed in "nor from rules of … or more"; null when empty or not a whole number above nothing.</summary>
    private int? IgnoredRulesTyped() =>
        int.TryParse(IgnoredRulesBox.Text.Trim(), System.Globalization.NumberStyles.Integer | System.Globalization.NumberStyles.AllowThousands,
            Texts.Catalog.Culture, out int from) && from > 0 ? from : null;

    /// <summary>The card slots typed; null when not a whole number from 1 to 999.</summary>
    private int? SlotsTyped() =>
        int.TryParse(SlotsBox.Text.Trim(), System.Globalization.NumberStyles.Integer, Texts.Catalog.Culture, out int slots) && slots is > 0 and < 1000 ? slots : null;

    /// <summary>The kinds selected.</summary>
    private List<GeneratedNumbers> KindsSelected()
    {
        List<GeneratedNumbers> kinds = [];
        if (UnlimitedTick.IsChecked == true) kinds.Add(GeneratedNumbers.Unlimited);
        if (SameTick.IsChecked == true) kinds.Add(GeneratedNumbers.Same);
        if (AsTheGameTick.IsChecked == true) kinds.Add(GeneratedNumbers.AsTheGame);
        return kinds;
    }

    /// <summary>The nations and sides selected, as the generations they stand for, in the list's order.</summary>
    private List<DivisionGeneration> ScopesSelected() => [.. ScopeTicks().Where(tick => tick.IsChecked == true).Select(tick => (DivisionGeneration)tick.Tag)];

    /// <summary>
    /// Every division the choices make, with the counts, slots and choice of mod nations typed, the preview's left-out
    /// rows among them; empty when a number is wrong.
    /// </summary>
    private List<DivisionGeneration> Generations()
    {
        if (!LimitedTypedRight() || SlotsTyped() is not int slots) return [];
        DivisionGeneration typed = new();
        if (ByPhase) typed = typed with { PhaseCounts = PhasesTyped()!, VeterancyShares = SharesTyped()!, Economy = EconomyChosen() };
        else typed = typed with { Counts = CountsTyped()! };
        return [.. ScopesSelected().SelectMany(scope => KindsSelected().Select(kind => scope with
        {
            Numbers = kind,
            Counts = typed.Counts,
            PhaseCounts = typed.PhaseCounts,
            VeterancyShares = typed.VeterancyShares,
            Economy = typed.Economy,
            Slots = slots,
            EveryDivisionNations = ModNationsTick.IsChecked == true ? everyDivisionNations : [],
            IgnoredDivisions = IgnoredDivisionsTyped(),
            IgnoredRulesFrom = IgnoredRulesTyped(),
        }))];
    }

    /// <summary>The divisions made: those of <see cref="Generations"/> the preview does not leave out.</summary>
    private List<DivisionGeneration> GenerationsKept() => [.. Generations().Where(generation => !leftOut.Contains(RowKey(generation)))];

    /// <summary>A preview row's key: its nation or side and its kind, which stay the same while numbers are typed.</summary>
    private static string RowKey(DivisionGeneration generation) => $"{generation.Units}|{generation.Nation}|{generation.Side}|{generation.Numbers}";

    /// <summary>The rows whose box is cleared in the preview: those divisions are not made.</summary>
    private readonly HashSet<string> leftOut = [];

    /// <summary>A kind's name, as step 2 and the names give it: "Unlimited", "Limited", "As the game's divisions".</summary>
    private static string KindName(GeneratedNumbers kind) => kind switch
    {
        GeneratedNumbers.Unlimited => Texts.Get("Divisions.Generate.Unlimited"),
        GeneratedNumbers.Same => Texts.Get("Divisions.Generate.Same"),
        _ => Texts.Get("Divisions.Generate.AsTheGame"),
    };

    /// <summary>A division's name from the pattern: {mod}, {nation} (its code, or its side's) and {kind}.</summary>
    private string NameOf(DivisionGeneration generation)
    {
        if (IsChanging) return changedName;
        string nation = generation.Units switch
        {
            GeneratedUnits.Nation => generation.Nation,
            GeneratedUnits.Side => generation.Side,
            _ => Texts.Get("Divisions.Generate.Everything", ("side", generation.Side)),
        };
        string name = NamesBox.Text.Replace("{mod}", modName, StringComparison.Ordinal)
            .Replace("{nation}", nation, StringComparison.Ordinal)
            .Replace("{kind}", KindName(generation.Numbers), StringComparison.Ordinal);
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// What Names suggests at the caret, as the rule window's boxes do: after "{", the names that can go there, with
    /// what each stands for and an example.
    /// </summary>
    private SuggestionsAt? NameSuggestions(string text, int caret)
    {
        if (caret == 0) return null;
        int open = text.LastIndexOf('{', caret - 1);
        if (open < 0 || text[open..caret].Contains('}')) return null;
        string nation = ScopesSelected().FirstOrDefault()?.Nation is { Length: > 0 } code ? code : "BEL";
        return new SuggestionsAt(open,
        [
            new Suggestion("{mod}", "s-const", Texts.Get("Divisions.Generate.Var.Mod", ("example", modName)), Tip: Texts.Get("Divisions.Generate.Names.Tip")),
            new Suggestion("{nation}", "s-const", Texts.Get("Divisions.Generate.Var.Nation", ("example", nation)), Tip: Texts.Get("Divisions.Generate.Names.Tip")),
            new Suggestion("{kind}", "s-const", Texts.Get("Divisions.Generate.Var.Kind", ("example", KindName(GeneratedNumbers.Same))), Tip: Texts.Get("Divisions.Generate.Names.Tip")),
        ]);
    }

    // ---- activation points ----

    /// <summary>Set while the slots are being filled in from the activation points typed, so they do not fill those in back.</summary>
    private bool fillingSlots;

    /// <summary>
    /// Activation points typed: the card slots follow, the points divided by the categories, rounded down; while the
    /// box has the focus a note says what they were rounded to.
    /// </summary>
    private void PointsTyped(object sender, TextChangedEventArgs e)
    {
        if (!PointsBox.IsKeyboardFocusWithin) return;
        if (!int.TryParse(PointsBox.Text.Trim(), System.Globalization.NumberStyles.Integer, Texts.Catalog.Culture, out int points) || points < 0) return;
        int slots = points / Categories;
        fillingSlots = true;
        SlotsBox.Text = slots.ToString(Texts.Catalog.Culture);
        fillingSlots = false;
        SlotsNote.Text = points % Categories == 0
            ? Texts.Get("Divisions.Generate.Slots.Note", ("categories", Categories))
            : Texts.Get("Divisions.Generate.Points.Rounded", ("points", Texts.Number(slots * Categories)));
    }

    /// <summary>Leaving the activation points: they show the slots' points again, and the note what a slot is.</summary>
    private void PointsLeft(object sender, KeyboardFocusChangedEventArgs e)
    {
        SlotsNote.Text = Texts.Get("Divisions.Generate.Slots.Note", ("categories", Categories));
        ShowPoints();
    }

    /// <summary>The activation points of the slots typed, unless they are being typed themselves.</summary>
    private void ShowPoints()
    {
        if (fillingSlots || PointsBox.IsKeyboardFocusWithin) return;
        PointsBox.Text = SlotsTyped() is int slots ? (slots * Categories).ToString(Texts.Catalog.Culture) : "";
    }

    // ---- what is shown ----

    /// <summary>
    /// Everything that follows the choices: each step's "N selected", Limited's and Leave out's settings dimmed while
    /// their kind is not selected, the sample card, the activation points, the preview and the line at the foot, where
    /// Generate is on only when something can be made.
    /// </summary>
    private void ShowSummary()
    {
        if (rosters is null) return;
        UnitsCount.Text = Texts.Get("Divisions.Generate.Selected", ("count", Texts.Number(ScopesSelected().Count)));
        KindsCount.Text = Texts.Get("Divisions.Generate.Selected", ("count", Texts.Number(KindsSelected().Count)));
        SameSettings.Opacity = SameTick.IsChecked == true ? 1 : 0.4;
        SameSettings.IsEnabled = SameTick.IsChecked == true;
        LeaveOutButton.IsEnabled = AsTheGameTick.IsChecked == true;
        int leftOutSettings = (IgnoredDivisionsTyped().Count > 0 ? 1 : 0) + (IgnoredRulesBox.Text.Trim() != "" ? 1 : 0);
        LeaveOutCount.Text = "  " + (leftOutSettings == 0 ? Texts.Get("Divisions.Generate.LeaveOut.None") : Texts.Get("Divisions.Generate.LeaveOut.Set", ("count", leftOutSettings)));
        ShowPoints();
        ShowSample();
        ShowPreview();
        ShowSummaryText();
        // while Generate is off, the line saying what is missing is also its tooltip
        OffReason.SetText(GoButton, GoButton.IsEnabled ? null : Summary.Text);
    }

    /// <summary>The summary's words, and whether Generate is on (see <see cref="ShowSummary"/>).</summary>
    private void ShowSummaryText()
    {
        if (rosters is null) return;
        List<DivisionGeneration> generations = Generations();
        List<DivisionGeneration> kept = GenerationsKept();
        NameExample.Text = generations.Count == 0 ? "" : Texts.Get("Divisions.Generate.Names.Example", ("name", NameOf(generations[0])));
        GoButton.IsEnabled = kept.Count > 0 && (IsChanging || NamesBox.Text.Trim() != "");
        if (!LimitedTypedRight() || SlotsTyped() is null)
        {
            Summary.Text = Texts.Get(ByPhase ? "Divisions.Generate.Summary.WrongByPhase" : "Divisions.Generate.Summary.Wrong");
            return;
        }
        // read as empty, it would learn from every rule, the opposite of what was typed: said, and Generate waits
        if (IgnoredRulesBox.Text.Trim() != "" && IgnoredRulesTyped() is null)
        {
            GoButton.IsEnabled = false;
            Summary.Text = Texts.Get("Divisions.Generate.Summary.WrongRules");
            return;
        }
        if (generations.Count == 0)
        {
            Summary.Text = Texts.Get("Divisions.Generate.Summary.None");
            return;
        }
        if (kept.Count == 0)
        {
            Summary.Text = Texts.Get("Divisions.Generate.Summary.AllLeftOut");
            return;
        }
        // with no name pattern nothing is made, so the line says what is missing rather than what would be made
        if (!IsChanging && NamesBox.Text.Trim() == "")
        {
            Summary.Text = Texts.Get("Divisions.Generate.Summary.NoName");
            return;
        }
        long units = kept.Sum(generation => (long)UnitsIn(generation));
        // Steel Division 2's divisions are written in Packs.ndf too, a pack for each unit
        Summary.Text = IsChanging
            ? Texts.Get(ByPhase ? "Divisions.Generate.Summary.One.Packs" : "Divisions.Generate.Summary.One", ("units", Texts.Number(units)), ("size", SizeOf(units)))
            : Texts.Count(ByPhase ? "Divisions.Generate.Summary.Packs" : "Divisions.Generate.Summary", kept.Count, ("count", Texts.Number(kept.Count)), ("size", SizeOf(units)));
    }

    /// <summary>About how many megabytes so many units' lines add to the exported DivisionRules.ndf.</summary>
    private string SizeOf(long units) => (units * bytesPerUnit / (1024 * 1024)).ToString("0.#", Texts.Catalog.Culture);

    /// <summary>The units each nation's or side's division takes, counted once each as the window is open.</summary>
    private readonly Dictionary<string, int> unitCounts = [];

    /// <summary>
    /// The units a division takes: its nation's or side's, and the mods' own nations' when they go into every division.
    /// </summary>
    /// <remarks>
    /// Tuned for speed: each nation or side is counted once while the window is open, not again on each key typed.
    /// Keep it that way when tidying.
    /// </remarks>
    private int UnitsIn(DivisionGeneration generation)
    {
        if (rosters is null) return 0;
        string key = $"{generation.Units}|{generation.Nation}|{generation.Side}|{string.Join(",", generation.EveryDivisionNations)}";
        if (!unitCounts.TryGetValue(key, out int count)) unitCounts[key] = count = rosters.UnitsTaken(generation).Count;
        return count;
    }

    // ---- the sample card ----

    /// <summary>The unit on the sample card; null until one is chosen.</summary>
    private Unit? sampleUnit;

    /// <summary>The tile of <see cref="sampleUnit"/>.</summary>
    private UnitTile? sampleTile;

    /// <summary>The kind the sample card shows; null when none is selected.</summary>
    private GeneratedNumbers? sampleKind;

    /// <summary>
    /// "A card holds": the most expensive unit of the first nation or side selected (of the biggest nation while none
    /// is), as its Armory card, with the units at each level written beside its veterancy marks for the kind chosen with
    /// the buttons under it, one for each kind selected.
    /// </summary>
    private void ShowSample()
    {
        if (rosters is null) return;
        List<GeneratedNumbers> kinds = KindsSelected();
        if (sampleKind is not GeneratedNumbers kind || !kinds.Contains(kind)) sampleKind = kinds.Count > 0 ? kinds[0] : null;
        ShowSampleKinds(kinds);
        if (SampleUnitFor(ScopesSelected().FirstOrDefault()) is not Unit unit)
        {
            SampleSlot.Content = null;
            return;
        }
        if (!ReferenceEquals(unit, sampleUnit) || sampleTile is null)
        {
            sampleUnit = unit;
            IReadOnlyList<string> sides = UnitNames.SidesOf(game);
            sampleTile = new UnitTile(unit, sides.Count > 0 && unit.Side == sides[0], new UnitSelection()) { Cursor = Cursors.Arrow, IsHitTestVisible = true };
            SampleSlot.Content = sampleTile;
        }
        if (ByPhase)
        {
            (List<string> counts, List<string>? tips) = SampleCountsByPhase(unit);
            sampleTile.ShowVeterancyCounts(counts, tips);
        }
        else sampleTile.ShowVeterancyCounts(SampleCounts(unit));
        SampleNote.Text = sampleKind switch
        {
            null => Texts.Get("Divisions.Generate.Sample.None"),
            GeneratedNumbers.AsTheGame => Texts.Get("Divisions.Generate.Sample.AsTheGame"),
            _ => "",
        };
    }

    /// <summary>The buttons under the sample card: one for each kind selected, the one shown pressed in.</summary>
    private void ShowSampleKinds(List<GeneratedNumbers> kinds)
    {
        SampleKinds.Children.Clear();
        foreach (GeneratedNumbers kind in kinds)
        {
            GeneratedNumbers shown = kind;
            var button = new ToggleButton
            {
                Style = (Style)FindResource("SmallChoice"),
                Content = kind == GeneratedNumbers.AsTheGame ? Texts.Get("Divisions.Generate.AsTheGame.Short") : KindName(kind),
                IsChecked = kind == sampleKind,
                Margin = new Thickness(0, 0, 4, 4),
                ToolTip = Texts.Get("Divisions.Generate.Sample.Kind.Tip", ("kind", KindName(kind))),
            };
            button.Click += (_, _) =>
            {
                sampleKind = shown;
                ShowSample();
            };
            SampleKinds.Children.Add(button);
        }
    }

    /// <summary>What the sample card writes beside each level, lowest first, for <see cref="sampleKind"/>.</summary>
    private List<string> SampleCounts(Unit unit)
    {
        switch (sampleKind)
        {
            case GeneratedNumbers.Unlimited:
                return [.. Enumerable.Repeat(DivisionGeneration.UnlimitedCount.ToString(Texts.Catalog.Culture), 4)];
            case GeneratedNumbers.Same:
                return [.. CountBoxes().Select(box => box.Text.Trim() == "" ? "0" : box.Text.Trim())];
            case GeneratedNumbers.AsTheGame when rosters is not null:
                var generation = new DivisionGeneration { Numbers = GeneratedNumbers.AsTheGame, IgnoredDivisions = IgnoredDivisionsTyped(), IgnoredRulesFrom = IgnoredRulesTyped() };
                IReadOnlyList<int> counts = DivisionNames.UnitsAtEachVeterancy(rosters.NumbersFor(unit, generation));
                return counts.Count == 4 ? [.. counts.Select(count => count.ToString(Texts.Catalog.Culture))] : ["?", "?", "?", "?"];
            default:
                return ["", "", "", ""];
        }
    }

    /// <summary>
    /// Steel Division 2's sample card (see <see cref="SampleCounts"/>): beside each of its three levels, the units a card
    /// holds there in the first phase it can be taken in, as the Armory's tiles count them; each level's tooltip gives
    /// them in every phase, "6 / 12 / 0".
    /// </summary>
    private (List<string> Counts, List<string>? Tips) SampleCountsByPhase(Unit unit)
    {
        DivisionUnit? rule = sampleKind switch
        {
            GeneratedNumbers.Unlimited or GeneratedNumbers.Same when rosters is not null => rosters.NumbersFor(unit, SampleGeneration((GeneratedNumbers)sampleKind)),
            GeneratedNumbers.AsTheGame when rosters is not null => rosters.NumbersFor(unit, SampleGeneration(GeneratedNumbers.AsTheGame)),
            _ => null,
        };
        if (rule is null) return (["", "", ""], null);
        if (sampleKind == GeneratedNumbers.Same && (PhasesTyped() is null || SharesTyped() is null)) return (["?", "?", "?"], null);
        IReadOnlyList<int> counts = DivisionNames.UnitsAtEachVeterancy(rule);
        return counts.Count == 3 ? ([.. counts.Select(count => count.ToString(Texts.Catalog.Culture))], [.. DivisionNames.VeterancyTipsByPhase(rule) ?? []]) : (["?", "?", "?"], null);
    }

    /// <summary>A generation of a kind, with the numbers typed, for the sample card's unit alone.</summary>
    private DivisionGeneration SampleGeneration(GeneratedNumbers kind) => new()
    {
        ByPhase = true,
        Numbers = kind,
        PhaseCounts = PhasesTyped() ?? [0, 0, 0],
        VeterancyShares = SharesTyped() ?? [0, 0, 0],
        IgnoredDivisions = IgnoredDivisionsTyped(),
        IgnoredRulesFrom = IgnoredRulesTyped(),
    };

    /// <summary>The sample card's unit: the most expensive a division of a nation or side takes; of the biggest nation for none.</summary>
    private Unit? SampleUnitFor(DivisionGeneration? scope)
    {
        if (rosters is null) return null;
        scope ??= BiggestNation(rosters, ByPhase);
        if (scope is null) return null;
        if (sampleScope == RowKey(scope) && sampleUnit is not null) return sampleUnit;
        sampleScope = RowKey(scope);
        return rosters.UnitsTaken(scope).OrderByDescending(unit => unit.Cost ?? 0).FirstOrDefault();
    }

    /// <summary>The nation or side the sample card's unit was chosen from.</summary>
    private string sampleScope = "";

    /// <summary>A division of the nation with the most units, for the sample card while nothing is selected.</summary>
    private static DivisionGeneration? BiggestNation(DivisionRosters units, bool byPhase)
    {
        IGrouping<string, Unit>? biggest = units.AllUnits.Where(DivisionRosters.IsDeckable).GroupBy(unit => unit.Nation).MaxBy(group => group.Count());
        return biggest is null ? null : new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = biggest.Key, Side = biggest.First().Side, ByPhase = byPhase };
    }

    // ---- the Generation Preview ----

    /// <summary>The column the preview is sorted by (see <see cref="PreviewColumns"/>); null for the order of the choices.</summary>
    private int? sortColumn;

    /// <summary>Whether the preview's sort runs from A to Z (or the least first).</summary>
    private bool sortAscending = true;

    /// <summary>The preview's columns after the box: their headings' texts, and whether they are numbers, right-aligned.</summary>
    private static readonly (string Heading, bool IsNumber)[] PreviewColumns =
    [
        ("Divisions.Generate.Col.Division", false),
        ("Divisions.Generate.Col.Units", false),
        ("Divisions.Generate.Col.Kind", false),
        ("Divisions.Generate.Col.Count", true),
        ("Divisions.Generate.Col.Points", true),
    ];

    /// <summary>One row of the preview: a division, and what its columns show.</summary>
    private sealed record PreviewRow(DivisionGeneration Generation, string Name, string Scope, string Kind, string Detail, int Units, int Points)
    {
        /// <summary>What a column is sorted by.</summary>
        public IComparable SortValue(int column) => column switch
        {
            0 => Name,
            1 => Scope,
            2 => Kind,
            3 => Units,
            _ => Points,
        };
    }

    /// <summary>
    /// The Generation Preview: a row for each division, with a box to leave it out (none when one division's making is
    /// changed), ⚠ on a name the open mod has already, and the totals under it. A heading sorts by its column; again,
    /// the other way.
    /// </summary>
    private void ShowPreview()
    {
        List<DivisionGeneration> generations = Generations();
        ShowPreviewHeadings();
        PreviewRows.Children.Clear();
        PreviewTotals.Text = "";
        if (ScopesSelected().Count == 0 || KindsSelected().Count == 0 || generations.Count == 0)
        {
            string empty = ScopesSelected().Count == 0 ? "Divisions.Generate.Preview.NoUnits"
                : KindsSelected().Count == 0 ? "Divisions.Generate.Preview.NoKind" : ByPhase ? "Divisions.Generate.Summary.WrongByPhase" : "Divisions.Generate.Summary.Wrong";
            var note = new TextBlock { Text = Texts.Get(empty), Margin = new Thickness(8, 6, 8, 6), TextWrapping = TextWrapping.Wrap };
            note.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            PreviewRows.Children.Add(note);
            return;
        }
        List<PreviewRow> rows = [.. generations.Select(RowOf)];
        if (sortColumn is int column)
        {
            rows.Sort((left, right) => left.SortValue(column).CompareTo(right.SortValue(column)));
            if (!sortAscending) rows.Reverse();
        }
        foreach (PreviewRow row in rows) PreviewRows.Children.Add(PreviewLine(row));
        List<PreviewRow> kept = [.. rows.Where(row => !leftOut.Contains(RowKey(row.Generation)))];
        long units = kept.Sum(row => (long)row.Units);
        PreviewTotals.Inlines.Clear();
        PreviewTotals.Inlines.Add(new System.Windows.Documents.Run(Texts.Get(ByPhase ? "Divisions.Generate.Totals.Packs" : "Divisions.Generate.Totals",
            ("kept", Texts.Number(kept.Count)), ("count", Texts.Number(rows.Count)), ("units", Texts.Number(units)), ("size", SizeOf(units)))));
    }

    /// <summary>A division's row: its name, its nation or side, its kind and numbers, its units and its activation points.</summary>
    private PreviewRow RowOf(DivisionGeneration generation)
    {
        string scope = generation.Units switch
        {
            GeneratedUnits.Nation => UnitNames.Nation(generation.Nation),
            GeneratedUnits.Side => Texts.Get("Divisions.Generate.Side", ("side", UnitNames.Side(generation.Side))),
            _ => Texts.Get("Divisions.Generate.Everything", ("side", UnitNames.Side(generation.Side))),
        };
        string detail = (generation.Numbers, generation.ByPhase) switch
        {
            (GeneratedNumbers.Unlimited, false) => Texts.Get("Divisions.Generate.Unlimited.Note"),
            (GeneratedNumbers.Unlimited, true) => Texts.Get("Divisions.Generate.UnlimitedByPhase.Note"),
            (GeneratedNumbers.Same, false) => Texts.Get("Divisions.Generate.Same.Detail", ("counts", string.Join(" / ", generation.Counts))),
            (GeneratedNumbers.Same, true) => Texts.Get("Divisions.Generate.SameByPhase.Detail",
                ("counts", string.Join(" / ", generation.PhaseCounts)), ("shares", string.Join(" / ", generation.VeterancyShares.Select(share => share.ToString("0.0##", Texts.Catalog.Culture))))),
            (_, false) => Texts.Get("Divisions.Generate.AsTheGame.Note"),
            _ => Texts.Get("Divisions.Generate.AsTheGameByPhase.Note"),
        };
        return new PreviewRow(generation, NameOf(generation), scope, KindName(generation.Numbers), detail, UnitsIn(generation), generation.ActivationPoints);
    }

    /// <summary>
    /// The preview's columns, the same in the headings and in each row: the box, then <see cref="PreviewColumns"/>. The
    /// division's name takes what the others leave, cut short with "…" when long, so the numbers always show.
    /// </summary>
    private static Grid PreviewGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        for (int column = 0; column < PreviewColumns.Length; column++)
        {
            grid.ColumnDefinitions.Add(column == 0
                ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                : new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "PreviewColumn" + column });
        }
        return grid;
    }

    /// <summary>The headings over the preview's rows; a click sorts by that column, a second click the other way.</summary>
    private void ShowPreviewHeadings()
    {
        Grid headings = PreviewGrid();
        headings.SetResourceReference(Panel.BackgroundProperty, "panel");
        for (int column = 0; column < PreviewColumns.Length; column++)
        {
            int sortedBy = column;
            (string key, bool isNumber) = PreviewColumns[column];
            var text = new TextBlock { FontSize = 11.5, Text = Texts.Get(key) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            if (sortColumn == column)
            {
                var arrow = new System.Windows.Documents.Run(sortAscending ? " ▴" : " ▾");
                arrow.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "accent");
                text.Inlines.Add(arrow);
            }
            var cell = new Border
            {
                Child = text, Padding = new Thickness(6, 3, 6, 3), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = Texts.Get("Divisions.Generate.Col.Sort.Tip"),
            };
            text.HorizontalAlignment = isNumber ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            cell.MouseLeftButtonUp += (_, _) =>
            {
                sortAscending = sortColumn != sortedBy || !sortAscending;
                sortColumn = sortedBy;
                ShowPreview();
            };
            Grid.SetColumn(cell, column + 1);
            headings.Children.Add(cell);
        }
        var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        line.SetResourceReference(Border.BorderBrushProperty, "line");
        Grid.SetColumnSpan(line, PreviewColumns.Length + 1);
        headings.Children.Add(line);
        PreviewHeadings.Children.Clear();
        PreviewHeadings.Children.Add(headings);
    }

    /// <summary>One division's row in the preview.</summary>
    private Grid PreviewLine(PreviewRow row)
    {
        Grid line = PreviewGrid();
        string key = RowKey(row.Generation);
        bool isLeftOut = leftOut.Contains(key);
        line.Opacity = isLeftOut ? 0.45 : 1;
        if (!IsChanging)
        {
            var make = new CheckBox
            {
                Style = (Style)FindResource("Tick"), IsChecked = !isLeftOut, Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = Texts.Get("Divisions.Generate.Row.Make.Tip"),
            };
            make.Click += (_, _) =>
            {
                if (make.IsChecked == true) leftOut.Remove(key);
                else leftOut.Add(key);
                ShowSummary();
            };
            line.Children.Add(make);
        }
        // the ⚠ after the name stays in view when a long name is cut short
        var name = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left };
        if (takenNames.Contains(row.Name))
        {
            var warning = new TextBlock { Text = " ⚠", ToolTip = Texts.Get("Divisions.Generate.Taken.Tip") };
            warning.SetResourceReference(TextBlock.ForegroundProperty, "caution");
            DockPanel.SetDock(warning, Dock.Right);
            name.Children.Add(warning);
        }
        var nameText = new TextBlock { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = row.Name };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        name.Children.Add(nameText);
        var kind = new TextBlock();
        kind.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        kind.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(row.Kind)));
        var detail = new System.Windows.Documents.Run(" " + row.Detail);
        detail.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "muted");
        kind.Inlines.Add(detail);
        var scope = new TextBlock { Text = row.Scope };
        scope.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        UIElement[] cells = [name, scope, kind, NumberCell(row.Units), NumberCell(row.Points)];
        for (int column = 0; column < cells.Length; column++)
        {
            var cell = new Border { Child = cells[column], Padding = new Thickness(6, 3, 6, 3) };
            Grid.SetColumn(cell, column + 1);
            line.Children.Add(cell);
        }
        var under = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        under.SetResourceReference(Border.BorderBrushProperty, "line");
        Grid.SetColumnSpan(under, PreviewColumns.Length + 1);
        line.Children.Add(under);
        return line;
    }

    /// <summary>A number in a preview row: dim, at the right.</summary>
    private static TextBlock NumberCell(int number)
    {
        var text = new TextBlock { Text = Texts.Number(number), HorizontalAlignment = HorizontalAlignment.Right };
        text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return text;
    }

    /// <summary>
    /// The rows scrolled or changed: the headings leave room for the rows' scroll bar when it shows, so the numbers stay
    /// under their headings.
    /// </summary>
    private void PreviewScrolled(object sender, ScrollChangedEventArgs e) =>
        PreviewHeadings.Margin = new Thickness(0, 0, PreviewScroller.ComputedVerticalScrollBarVisibility == Visibility.Visible ? SystemParameters.VerticalScrollBarWidth : 0, 0);

    /// <summary>
    /// The bytes a unit's line takes in the exported file: measured on the 999-a-card list of the nation with the most
    /// units, which takes a moment, once as the window opens.
    /// </summary>
    /// <param name="byPhase">Whether the divisions are Steel Division 2's: a unit there is its line, and its pack in Packs.ndf.</param>
    private static double MeasureBytesPerUnit(DivisionRosters units, bool byPhase)
    {
        if (BiggestNation(units, byPhase) is not DivisionGeneration biggest) return DefaultBytesPerUnit;
        IReadOnlyList<DivisionUnit> list = units.UnitsFor(biggest);
        if (list.Count == 0) return DefaultBytesPerUnit;
        if (!byPhase) return (double)GeneratedUnitLists.ListText(list).Length / list.Count;
        const string Division = "Descriptor_Deck_Division_X_multi";
        return (double)list.Sum(unit => DivisionChangeRules.SteelDivisionUnitLine(unit).Length
            + DivisionChangeRules.PackText(DivisionChangeRules.OwnPackOf(Division, unit.Unit), unit.Unit).Length) / list.Count;
    }

    private void GoClicked(object sender, RoutedEventArgs e)
    {
        List<DivisionGeneration> generations = GenerationsKept();
        if (generations.Count == 0) return;
        Action<List<GeneratedDivisionChoice>>? whenGenerated = generate;
        Action<DivisionGeneration>? whenChanged = change;
        List<GeneratedDivisionChoice> choices = [.. generations.Select(generation => new GeneratedDivisionChoice(generation, NameOf(generation)))];
        Close();
        whenGenerated?.Invoke(choices);
        whenChanged?.Invoke(generations[0]);
    }

    private void Close()
    {
        LeaveOutPopup.IsOpen = false;
        Visibility = Visibility.Collapsed;
        generate = null;
        change = null;
        rosters = null;
        sampleTile = null;
        SampleSlot.Content = null;
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>Escape closes the window, unless it is closing the names' suggestions or the Leave out pop-up.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Suggestions.IsOpenFor(NamesBox)) return;
        e.Handled = true;
        if (LeaveOutPopup.IsOpen)
        {
            LeaveOutPopup.IsOpen = false;
            LeaveOutButton.Focus();
            return;
        }
        Close();
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>
    /// 800 pixels wide, or 96% of a narrower window; never higher than 96% of the window, the steps scrolling between
    /// the title and the buttons, which always show.
    /// </summary>
    private void BackdropResized(object sender, SizeChangedEventArgs e)
    {
        Box.Width = Math.Min(800, (e.NewSize.Width + 20) * 0.96);
        Box.MaxHeight = e.NewSize.Height * 0.96;
    }
}
