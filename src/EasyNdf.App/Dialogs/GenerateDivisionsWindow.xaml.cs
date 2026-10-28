using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Dialogs;

/// <summary>A division the Generate Divisions window makes: how it is made, and its name.</summary>
/// <param name="Generation">How it is made.</param>
/// <param name="Name">Its name, from the window's name pattern.</param>
public sealed record GeneratedDivisionChoice(DivisionGeneration Generation, string Name);

/// <summary>
/// The Generate Divisions window, opened from ⚙ beside the Divisions heading: one division for each nation and side
/// ticked, times each kind ticked. Opened from a generated division's "Change…", it changes how that division is made,
/// with one choice in each group. Generate (or Done) hands back the choice; Cancel, ×, Escape or a click outside change
/// nothing.
/// </summary>
public partial class GenerateDivisionsWindow : UserControl
{
    /// <summary>The name pattern the window starts with: "YSM SOV – Unlimited".</summary>
    private const string DefaultNames = "{mod} {nation} – {kind}";

    /// <summary>The bytes a unit's line takes in the exported DivisionRules.ndf, about, when no list could be measured.</summary>
    private const double DefaultBytesPerUnit = 3000;

    /// <summary>What Generate does with the divisions ticked; null in change mode.</summary>
    private Action<List<GeneratedDivisionChoice>>? generate;

    /// <summary>What Done does with the one division's new making; null in generate mode.</summary>
    private Action<DivisionGeneration>? change;

    /// <summary>The units the divisions are made from, for the counts and sizes shown.</summary>
    private DivisionRosters? rosters;

    /// <summary>The open mod's name, for the name pattern.</summary>
    private string modName = "";

    /// <summary>The bytes a unit's line takes in the exported file, measured on one list as the window opens.</summary>
    private double bytesPerUnit = DefaultBytesPerUnit;

    /// <summary>Builds the window, closed.</summary>
    public GenerateDivisionsWindow()
    {
        InitializeComponent();
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Whether the window changes one division's making, rather than making new divisions.</summary>
    private bool IsChanging => change is not null;

    /// <summary>Opens the window to make new divisions, with nothing ticked but the first kind.</summary>
    /// <param name="units">The units the divisions are made from (as the Armory shows them).</param>
    /// <param name="game">The game, for its sides.</param>
    /// <param name="mod">The open mod's name, for the name pattern.</param>
    /// <param name="modNations">The nations the mods add of their own (no unit of the game is of them), offered for every division.</param>
    /// <param name="whenDone">What Generate does with the divisions ticked.</param>
    public void Open(DivisionRosters units, GameTitle game, string mod, IReadOnlyList<string> modNations, Action<List<GeneratedDivisionChoice>> whenDone)
    {
        generate = whenDone;
        change = null;
        Title.Text = Texts.Get("Divisions.Generate.Title");
        Lead.Text = Texts.Get("Divisions.Generate.Lead");
        GoButton.Content = Texts.Get("Divisions.Generate.Go");
        GoButton.ToolTip = Texts.Get("Divisions.Generate.Go.Tip");
        NamesRow.Visibility = Visibility.Visible;
        NamesBox.Text = DefaultNames;
        Prepare(units, game, mod, new DivisionGeneration(), modNations);
        UnlimitedTick.IsChecked = true;
        Show();
    }

    /// <summary>Opens the window to change how one generated division is made, its choices ticked.</summary>
    /// <param name="division">The division's name, for the title.</param>
    /// <param name="current">How it is made now.</param>
    /// <param name="units">The units it is made from (as the Armory shows them).</param>
    /// <param name="game">The game, for its sides.</param>
    /// <param name="modNations">The nations the mods add of their own, offered for every division.</param>
    /// <param name="whenDone">What Done does with its new making.</param>
    public void OpenToChange(string division, DivisionGeneration current, DivisionRosters units, GameTitle game, IReadOnlyList<string> modNations,
        Action<DivisionGeneration> whenDone)
    {
        generate = null;
        change = whenDone;
        Title.Text = Texts.Get("Divisions.Generate.ChangeTitle", ("division", division));
        Lead.Text = Texts.Get("Divisions.Generate.ChangeLead");
        GoButton.Content = Texts.Get("Divisions.Generate.Done");
        GoButton.ToolTip = Texts.Get("Divisions.Generate.Done.Tip");
        NamesRow.Visibility = Visibility.Collapsed;
        Prepare(units, game, "", current, modNations);
        TickWhatMakes(current);
        Show();
    }

    /// <summary>
    /// The nations offered for every division: the mods' own, and those the division being changed has already. Empty
    /// when there are none, and the tick is then off and greyed.
    /// </summary>
    private List<string> everyDivisionNations = [];

    /// <summary>Fills the lists and boxes for the units given, with a generation's counts, slots and other settings.</summary>
    private void Prepare(DivisionRosters units, GameTitle game, string mod, DivisionGeneration start, IReadOnlyList<string> modNations)
    {
        rosters = units;
        modName = mod;
        scopeTicks.Clear();
        everyDivisionNations = [.. start.EveryDivisionNations.Union(modNations, StringComparer.Ordinal)];
        FillNations(units);
        FillSides(game);
        UnlimitedTick.IsChecked = SameTick.IsChecked = AsTheGameTick.IsChecked = false;
        // wrapped: the nations named can be many, and the column is a third of the window
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
        bytesPerUnit = MeasureBytesPerUnit(units);
    }

    private void Show()
    {
        ShowSummary();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => GoButton.Focus());
    }

    private TextBox[] CountBoxes() => [CountLowest, CountSecond, CountThird, CountHighest];

    // ---- the lists ----

    /// <summary>
    /// A tick for each nation whose units a deck can hold, those with the most units first, with their count; a mod's
    /// own nations among them.
    /// </summary>
    private void FillNations(DivisionRosters units)
    {
        NationTicks.Children.Clear();
        // the nations that go into every division have no division of their own
        var byNation = units.AllUnits.Where(DivisionRosters.IsDeckable).Where(unit => !everyDivisionNations.Contains(unit.Nation))
            .GroupBy(unit => unit.Nation)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => UnitNames.Nation(group.Key), StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
        foreach (IGrouping<string, Unit> nation in byNation)
        {
            var generation = new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = nation.Key, Side = nation.First().Side };
            NationTicks.Children.Add(ScopeTick(UnitNames.Nation(nation.Key), Texts.Number(nation.Count()), generation));
        }
    }

    /// <summary>A tick for each side's nations, then one for every unit under each side's flag.</summary>
    private void FillSides(GameTitle game)
    {
        SideTicks.Children.Clear();
        IReadOnlyList<string> sides = UnitNames.SidesOf(game);
        foreach (string side in sides)
        {
            var generation = new DivisionGeneration { Units = GeneratedUnits.Side, Side = side };
            SideTicks.Children.Add(ScopeTick(UnitNames.Side(side), "", generation, Texts.Get("Divisions.Generate.Side.Note")));
        }
        foreach (string side in sides)
        {
            var generation = new DivisionGeneration { Units = GeneratedUnits.Everything, Side = side };
            SideTicks.Children.Add(ScopeTick(Texts.Get("Divisions.Generate.Everything", ("side", UnitNames.Side(side))), "",
                generation, Texts.Get("Divisions.Generate.Everything.Note", ("side", UnitNames.Side(side)))));
        }
    }

    /// <summary>
    /// A nation's or side's tick: its name, with a count at the right or a note under it. Its tag is the generation it
    /// stands for (which units), so the choices can be read back.
    /// </summary>
    private UIElement ScopeTick(string name, string count, DivisionGeneration generation, string note = "")
    {
        var label = new DockPanel();
        if (count != "")
        {
            var countText = new TextBlock { Text = count, Margin = new Thickness(8, 0, 0, 0) };
            countText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            DockPanel.SetDock(countText, Dock.Right);
            label.Children.Add(countText);
        }
        label.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
        var tick = new CheckBox { Style = (Style)FindResource("Tick"), Content = label, Tag = generation, Margin = new Thickness(0, 2, 0, note == "" ? 2 : 0) };
        tick.Click += TickClicked;
        scopeTicks.Add(tick);
        if (note == "") return tick;
        var withNote = new StackPanel();
        withNote.Children.Add(tick);
        withNote.Children.Add(new TextBlock { Text = note, Style = (Style)FindResource("Note"), Margin = new Thickness(22, 0, 0, 6) });
        return withNote;
    }

    /// <summary>Every nation's and side's tick, in the order shown.</summary>
    private readonly List<CheckBox> scopeTicks = [];

    private IEnumerable<CheckBox> ScopeTicks() => scopeTicks;

    private IEnumerable<CheckBox> KindTicks() => [UnlimitedTick, SameTick, AsTheGameTick];

    /// <summary>Ticks the nation or side and the kind a generation is made with, for Change.</summary>
    private void TickWhatMakes(DivisionGeneration current)
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
    /// the others of its group, and stays ticked.
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

    /// <summary>The parts of division names typed in "Not from divisions named", one between each comma.</summary>
    private List<string> IgnoredDivisionsTyped() =>
        [.. IgnoredDivisionsBox.Text.Split(',').Select(part => part.Trim()).Where(part => part != "")];

    /// <summary>The number typed in "nor from rules of … or more"; null when empty or not a whole number above nothing.</summary>
    private int? IgnoredRulesTyped() =>
        int.TryParse(IgnoredRulesBox.Text.Trim(), System.Globalization.NumberStyles.Integer | System.Globalization.NumberStyles.AllowThousands,
            Texts.Catalog.Culture, out int from) && from > 0 ? from : null;

    /// <summary>The card slots typed; null when not a whole number from 1 to 999.</summary>
    private int? SlotsTyped() =>
        int.TryParse(SlotsBox.Text.Trim(), System.Globalization.NumberStyles.Integer, Texts.Catalog.Culture, out int slots) && slots is > 0 and < 1000 ? slots : null;

    /// <summary>The kinds ticked.</summary>
    private List<GeneratedNumbers> KindsTicked()
    {
        List<GeneratedNumbers> kinds = [];
        if (UnlimitedTick.IsChecked == true) kinds.Add(GeneratedNumbers.Unlimited);
        if (SameTick.IsChecked == true) kinds.Add(GeneratedNumbers.Same);
        if (AsTheGameTick.IsChecked == true) kinds.Add(GeneratedNumbers.AsTheGame);
        return kinds;
    }

    /// <summary>The divisions the ticks make, with the counts, slots and choice of mod nations typed; empty when one of them is wrong.</summary>
    private List<DivisionGeneration> Generations()
    {
        if (CountsTyped() is not List<int> counts || SlotsTyped() is not int slots) return [];
        List<DivisionGeneration> scopes = [.. ScopeTicks().Where(tick => tick.IsChecked == true).Select(tick => (DivisionGeneration)tick.Tag)];
        return [.. scopes.SelectMany(scope => KindsTicked().Select(kind => scope with
        {
            Numbers = kind,
            Counts = counts,
            Slots = slots,
            EveryDivisionNations = ModNationsTick.IsChecked == true ? everyDivisionNations : [],
            IgnoredDivisions = IgnoredDivisionsTyped(),
            IgnoredRulesFrom = IgnoredRulesTyped(),
        }))];
    }

    /// <summary>A division's name from the pattern: {mod}, {nation} (its code, or its side's) and {kind}.</summary>
    private string NameOf(DivisionGeneration generation)
    {
        string nation = generation.Units switch
        {
            GeneratedUnits.Nation => generation.Nation,
            GeneratedUnits.Side => generation.Side,
            _ => Texts.Get("Divisions.Generate.Everything", ("side", generation.Side)),
        };
        string kind = generation.Numbers switch
        {
            GeneratedNumbers.Unlimited => Texts.Get("Divisions.Generate.Unlimited"),
            GeneratedNumbers.Same => Texts.Get("Divisions.Generate.Same"),
            _ => Texts.Get("Divisions.Generate.AsTheGame"),
        };
        string name = NamesBox.Text.Replace("{mod}", modName, StringComparison.Ordinal)
            .Replace("{nation}", nation, StringComparison.Ordinal)
            .Replace("{kind}", kind, StringComparison.Ordinal);
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// The line under the choices: how many divisions the ticks make and about how much they add to the exported
    /// DivisionRules.ndf, or what is missing; Generate is on only when something can be made. Also the name pattern's
    /// example.
    /// </summary>
    private void ShowSummary()
    {
        if (rosters is null) return;
        List<DivisionGeneration> generations = Generations();
        NameExample.Text = generations.Count == 0 ? "" : Texts.Get("Divisions.Generate.Names.Example", ("name", NameOf(generations[0])));
        GoButton.IsEnabled = generations.Count > 0 && (IsChanging || NamesBox.Text.Trim() != "");
        if (CountsTyped() is null || SlotsTyped() is null)
        {
            Summary.Text = Texts.Get("Divisions.Generate.Summary.Wrong");
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
        long units = generations.Sum(generation => (long)rosters.UnitsTaken(generation).Count);
        string size = (units * bytesPerUnit / (1024 * 1024)).ToString("0.#", Texts.Catalog.Culture);
        Summary.Text = IsChanging
            ? Texts.Get("Divisions.Generate.Summary.One", ("units", Texts.Number(units)), ("size", size))
            : Texts.Count("Divisions.Generate.Summary", generations.Count, ("count", Texts.Number(generations.Count)), ("size", size));
    }

    /// <summary>
    /// The bytes a unit's line takes in the exported file: measured on the 999-a-card list of the nation with the most
    /// units, which takes a moment, once as the window opens.
    /// </summary>
    private static double MeasureBytesPerUnit(DivisionRosters units)
    {
        string? biggest = units.AllUnits.Where(DivisionRosters.IsDeckable).GroupBy(unit => unit.Nation)
            .OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();
        if (biggest is null) return DefaultBytesPerUnit;
        Unit first = units.AllUnits.First(unit => unit.Nation == biggest);
        IReadOnlyList<DivisionUnit> list = units.UnitsFor(new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = biggest, Side = first.Side });
        return list.Count == 0 ? DefaultBytesPerUnit : (double)GeneratedUnitLists.ListText(list).Length / list.Count;
    }

    private void GoClicked(object sender, RoutedEventArgs e)
    {
        List<DivisionGeneration> generations = Generations();
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
        Visibility = Visibility.Collapsed;
        generate = null;
        change = null;
        rosters = null;
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
