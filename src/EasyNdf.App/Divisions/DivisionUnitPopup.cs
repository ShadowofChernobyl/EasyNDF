using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The pop-up for one unit in one division, opened from its tile's veterancy strip: its name and the division's, its
/// cards and units per card, and the multiplier of each veterancy level. Its transports have a pop-up of their own
/// (<see cref="DivisionTransportPopup"/>), from the button under the strip. Everything here is the unit's in this division only; the Division pane
/// shows what belongs to the whole division. Done or Enter keeps the changes, Esc or a click outside leaves them.
/// </summary>
public sealed class DivisionUnitPopup
{
    /// <summary>How much − and + change a multiplier.</summary>
    private const double MultiplierStep = 0.05;

    /// <summary>The pop-up's width.</summary>
    private const double Width = 300;

    /// <summary>How wide the names of the Cards and Units per card rows are, so their boxes line up.</summary>
    private const double LabelWidth = 96;

    /// <summary>The layer of the app's window the pop-up is drawn on, beside the strip (see <see cref="FloatingPanel"/>).</summary>
    private readonly FloatingPanel floating;

    /// <summary>The pop-up's box, which takes the keyboard.</summary>
    private readonly Bevel box;
    private readonly DivisionUnit rule;
    private readonly IDivisionEditing editing;

    // the values as they are being changed; kept only by Done or Enter
    private int cards;
    private int unitsPerCard;
    private readonly double[] multipliers;

    private readonly List<LevelRow> levels = [];

    /// <summary>Opens the pop-up at the right of a tile's veterancy strip.</summary>
    /// <param name="strip">The strip clicked.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="divisionName">The division's name, for the pop-up's second line.</param>
    /// <param name="rule">How the division has the unit now.</param>
    /// <param name="editing">What keeps the changes, in the open mod.</param>
    public static void Open(FrameworkElement strip, Unit unit, string divisionName, DivisionUnit rule, IDivisionEditing editing) =>
        _ = new DivisionUnitPopup(strip, unit, divisionName, rule, editing);

    private DivisionUnitPopup(FrameworkElement strip, Unit unit, string divisionName, DivisionUnit rule, IDivisionEditing editing)
    {
        this.rule = rule;
        this.editing = editing;
        cards = rule.Cards;
        unitsPerCard = rule.UnitsPerCard ?? 0;
        multipliers = [.. rule.VeterancyMultipliers];
        floating = MainView.Of(strip).Floating;

        var parts = new StackPanel();
        parts.Children.Add(Title(unit.Name, divisionName));
        string unitName = rule.Unit;
        parts.Children.Add(NumberRow(Texts.Get("Divisions.Column.Cards"), cards, least: 0, changed => cards = changed,
            DivisionMarks.UnitChanges(unitName, "Cards"), division => DivisionMarks.UnitIn(division, unitName)?.Cards.ToString(Texts.Catalog.Culture) ?? ""));
        parts.Children.Add(NumberRow(Texts.Get("Divisions.Column.PerCard"), unitsPerCard, least: 1, changed =>
        {
            unitsPerCard = changed;
            // the units each level makes follow the units per card
            foreach (LevelRow level in levels) level.Show();
        }, DivisionMarks.UnitChanges(unitName, "UnitsPerCard"), division => DivisionMarks.UnitIn(division, unitName)?.UnitsPerCard?.ToString(Texts.Catalog.Culture) ?? ""));
        parts.Children.Add(SectionHeading(Texts.Get("Divisions.Column.Veterancy")));
        // the highest level at the top, as the tiles' strips and the game's cards stack them
        for (int level = multipliers.Length - 1; level >= 0; level--)
        {
            var row = new LevelRow(level, this);
            levels.Add(row);
            parts.Children.Add(row.Row);
        }
        parts.Children.Add(Muted(Texts.Get("Divisions.Veterancy.Popup.Note"), new Thickness(0, 4, 0, 0)));
        var done = new Button { Content = Texts.Get("Divisions.Veterancy.Popup.Done"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        done.Click += (_, _) => Keep();
        parts.Children.Add(done);

        box = new Bevel { Child = parts, Padding = new Thickness(9, 7, 9, 8), Width = Width, Focusable = true, FocusVisualStyle = null };
        box.SetResourceReference(Bevel.TopLeftProperty, "hi");
        box.SetResourceReference(Bevel.BottomRightProperty, "lo");
        box.SetResourceReference(Bevel.BackgroundProperty, "panel");
        box.PreviewKeyDown += KeyPressed;
        floating.Show(box, strip);
        // the keyboard goes to the pop-up itself, so Esc closes it at once, with no box picked out until one is clicked
        box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => box.Focus());
    }

    /// <summary>Esc leaves the changes; Enter keeps them, the value being typed with them.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            floating.Close();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            // the box being typed in takes its value as it loses the keyboard
            box.Focus();
            Keep();
        }
    }

    /// <summary>The rule with the changes made in the pop-up.</summary>
    private DivisionUnit Changed() => rule with
    {
        Cards = cards,
        UnitsPerCard = rule.UnitsPerCard is null ? null : unitsPerCard,
        VeterancyMultipliers = [.. multipliers],
    };

    /// <summary>Whether anything was changed in the pop-up.</summary>
    private bool HasChanges =>
        cards != rule.Cards || (rule.UnitsPerCard is not null && unitsPerCard != rule.UnitsPerCard)
        || !multipliers.SequenceEqual(rule.VeterancyMultipliers);

    /// <summary>Done: the changes are kept, in the open mod, and the pop-up closes.</summary>
    private void Keep()
    {
        floating.Close();
        if (HasChanges) editing.ChangeRule(Changed());
    }

    // ---- the parts ----

    /// <summary>The unit's name in the accent colour, then the division and that the values are for this division only.</summary>
    private static StackPanel Title(string unitName, string divisionName)
    {
        var name = new TextBlock { Text = unitName, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        return new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 6),
            Children = { name, Muted(Texts.Get("Divisions.UnitPopup.InDivision", ("division", divisionName)), new Thickness(0, 1, 0, 0)) },
        };
    }

    /// <summary>A small heading over a part of the pop-up, dim, with a faint line under it.</summary>
    private static Bevel SectionHeading(string text)
    {
        var heading = new Bevel { Child = Muted(text, new Thickness(0)), Edges = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 8, 0, 3), Padding = new Thickness(0, 0, 0, 1) };
        heading.SetResourceReference(Bevel.BottomRightProperty, "line");
        return heading;
    }

    private static TextBlock Muted(string text, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = margin };
        block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return block;
    }

    /// <summary>
    /// A whole number's row: its name, −, the number in a box, +. Steel Division 2 counts units by phase, which this
    /// pop-up does not change, so its units per card are not offered (see the caller).
    /// </summary>
    /// <param name="changes">The changes that set the value, for its mark when the open mod changed it.</param>
    /// <param name="write">The value in a division as the box shows it, to compare with the game's.</param>
    private static DockPanel NumberRow(string name, int value, int least, Action<int> changed, string[] changes, Func<Division, string> write)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var label = new TextBlock { Text = name, Width = LabelWidth, VerticalAlignment = VerticalAlignment.Center };
        var box = new TextBox { Text = Texts.Number(value), Width = 46, TextAlignment = TextAlignment.Center, Margin = new Thickness(3, 0, 3, 0) };
        int now = value;
        void Set(int number)
        {
            now = Math.Max(least, number);
            box.Text = Texts.Number(now);
            changed(now);
        }
        // a number typed is taken when the box is left; anything else puts the number back
        box.LostKeyboardFocus += (_, _) =>
        {
            if (DivisionTableColumns.ReadWholeNumber(box.Text.Trim(), least) is int typed) Set(typed);
            else box.Text = Texts.Number(now);
        };
        // the value marked when the open mod changed it: * after +, in the headings' colour
        FrameworkElement more = StarIfChanged(StepButton("+", () => Set(now + 1)), box, changes, write);
        foreach (FrameworkElement part in new FrameworkElement[] { label, StepButton("−", () => Set(now - 1)), box, more })
        {
            DockPanel.SetDock(part, Dock.Left);
            row.Children.Add(part);
        }
        row.Children.Add(new Border());
        return row;
    }

    /// <summary>
    /// When the open mod changed a value: its box with the tooltip saying what changed it, and "*" after the + button
    /// in the headings' colour. Gives back what to put in the + button's place.
    /// </summary>
    private static FrameworkElement StarIfChanged(Button more, TextBox box, string[] changes, Func<Division, string> write)
    {
        if (DivisionMarks.TipIfChanged(changes, write) is not string tip) return more;
        box.ToolTip = tip;
        if (!ModMarks.AreShown) return more;
        var star = new TextBlock { Text = "*", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), ToolTip = tip };
        star.SetResourceReference(TextBlock.ForegroundProperty, ModMarks.ChangedColour);
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { more, star } };
    }

    private static Button StepButton(string text, Action clicked)
    {
        var button = new Button { Content = text, Width = 22, Padding = new Thickness(0) };
        button.Click += (_, _) => clicked();
        return button;
    }

    /// <summary>
    /// One veterancy level's row: its mark, −, its multiplier in a box, +, and the units per card it makes ("not
    /// offered" at 0). A level not offered is dimmed. The row changes in place, so a box being typed in keeps the
    /// keyboard.
    /// </summary>
    private sealed class LevelRow
    {
        private readonly int level;
        private readonly DivisionUnitPopup owner;
        private readonly TextBox box = new() { Width = 46, TextAlignment = TextAlignment.Center, Margin = new Thickness(3, 0, 3, 0), ToolTip = Texts.Get("Divisions.Veterancy.Multiplier.Tip") };
        private readonly TextBlock units = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

        /// <summary>The row, to put in the pop-up.</summary>
        public DockPanel Row { get; } = new() { Margin = new Thickness(0, 2, 0, 2) };

        /// <summary>Builds a level's row.</summary>
        /// <param name="level">The level, from 0 for the lowest.</param>
        /// <param name="owner">The pop-up, which holds every level's multiplier and the units per card.</param>
        public LevelRow(int level, DivisionUnitPopup owner)
        {
            this.level = level;
            this.owner = owner;
            var mark = VeterancyMarks.Mark(level);
            mark.Margin = new Thickness(0, 0, 6, 0);
            units.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            // a value typed is taken when the box is left
            box.LostKeyboardFocus += (_, _) => TakeTyped();
            Button less = StepButton("−", () => Set(owner.multipliers[level] - MultiplierStep));
            Button more = StepButton("+", () => Set(owner.multipliers[level] + MultiplierStep));
            // the level's multiplier marked when the open mod changed it, its * after +
            string unit = owner.rule.Unit;
            string WriteLevel(Division division) =>
                DivisionMarks.UnitIn(division, unit) is DivisionUnit each && level < each.VeterancyMultipliers.Count
                    ? each.VeterancyMultipliers[level].ToString("0.##", Texts.Catalog.Culture) : "";
            FrameworkElement markedMore = StarIfChanged(more, box, DivisionMarks.UnitChanges(unit, "Veterancy"), WriteLevel);
            foreach (FrameworkElement part in new FrameworkElement[] { mark, less, box, markedMore })
            {
                DockPanel.SetDock(part, Dock.Left);
                Row.Children.Add(part);
            }
            Row.Children.Add(units);
            Show();
        }

        /// <summary>The multiplier set by − or +: kept from 0 to 1, to two decimals, as the game writes them.</summary>
        private void Set(double value)
        {
            owner.multipliers[level] = Math.Round(Math.Clamp(value, 0, 1), 2);
            Show();
        }

        /// <summary>The value typed: taken from 0 to 1, with the language's decimal mark or a dot; anything else is put back.</summary>
        private void TakeTyped()
        {
            string typed = box.Text.Trim().Replace(',', '.');
            if (double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value >= 0 && value <= 1) Set(value);
            else Show();
        }

        /// <summary>The row as its multiplier is now: the box, the units it makes, dimmed when the level is not offered.</summary>
        public void Show()
        {
            double multiplier = owner.multipliers[level];
            box.Text = multiplier.ToString("0.##", Texts.Catalog.Culture);
            units.Text = multiplier == 0
                ? Texts.Get("Divisions.Veterancy.NotOffered")
                : Texts.Count("Divisions.Veterancy.Units", DivisionNames.UnitsAt(owner.unitsPerCard, multiplier));
            Row.Opacity = multiplier == 0 ? 0.55 : 1;
        }
    }
}
