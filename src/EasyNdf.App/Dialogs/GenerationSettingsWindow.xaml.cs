using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EasyNdf.App.Text;
using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Dialogs;

/// <summary>A card a change of Generation Settings adds or drops, with why it is chosen: "M163 VADS", "anti-air · good value".</summary>
/// <param name="Unit">The unit's name.</param>
/// <param name="Why">Its role and pick.</param>
public sealed record GenerationChange(string Unit, string Why);

/// <summary>What Generation Settings make of a battlegroup, against the battlegroup as it is saved.</summary>
/// <param name="Cards">How many cards it would have.</param>
/// <param name="Points">The activation points they would cost.</param>
/// <param name="WasCards">How many cards it has now.</param>
/// <param name="WasPoints">The activation points those cost.</param>
/// <param name="Added">The cards it would have that it has not now.</param>
/// <param name="Dropped">The cards it has now that it would not have.</param>
public sealed record GenerationPreview(int Cards, int Points, int WasCards, int WasPoints, IReadOnlyList<GenerationChange> Added, IReadOnlyList<GenerationChange> Dropped);

/// <summary>
/// The Generation Settings window of a generated battlegroup (<see cref="BattlegroupGeneration"/>): a ready setting to
/// start from, the weight of each of the eight measures, the picks each role gets and the size. Each change redraws
/// the battlegroup behind and lists what changes. Done keeps the settings; Cancel, ×, Escape or a click outside put the
/// battlegroup back as it was; Make Fixed keeps its cards as they are with these settings, as an ordinary battlegroup.
/// </summary>
public partial class GenerationSettingsWindow : UserControl
{
    /// <summary>How long after the last change the battlegroup is chosen again, so dragging a slider stays smooth.</summary>
    private static readonly TimeSpan RedrawDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>The settings as the window opened with them: changed weights are marked against these.</summary>
    private BattlegroupGeneration opened = new();

    /// <summary>The ready setting the weights come from (the last one clicked), for "Back to …".</summary>
    private string lastReady = "Balanced";

    private Func<BattlegroupGeneration, GenerationPreview>? preview;
    private Action<BattlegroupGeneration>? done;
    private Action<BattlegroupGeneration>? makeFixed;
    private Action? cancelled;

    private readonly Dictionary<BattlegroupMeasure, Slider> sliders = [];
    private readonly Dictionary<BattlegroupMeasure, TextBlock> values = [];
    private readonly Dictionary<string, ToggleButton> readyButtons = new(StringComparer.Ordinal);
    private readonly DispatcherTimer redraw;

    /// <summary>Whether the window is filling its controls, when their changes are not the user's.</summary>
    private bool filling;

    /// <summary>Builds the window, closed, with its sliders and ready settings.</summary>
    public GenerationSettingsWindow()
    {
        InitializeComponent();
        redraw = new DispatcherTimer { Interval = RedrawDelay };
        redraw.Tick += (_, _) =>
        {
            redraw.Stop();
            Refresh();
        };
        MakeReadyButtons();
        MakeWeightRows();
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Opens the window for a battlegroup.</summary>
    /// <param name="battlegroup">The battlegroup's name, beside the title.</param>
    /// <param name="current">Its settings now (the ready "Balanced" for a battlegroup not generated yet).</param>
    /// <param name="previewOf">Redraws the battlegroup behind with settings, and says what changes.</param>
    /// <param name="whenDone">What Done does with the settings: keeps them.</param>
    /// <param name="whenMadeFixed">What Make Fixed does with the settings: keeps the cards they make, as fixed cards.</param>
    /// <param name="whenCancelled">What Cancel does: draws the battlegroup as it is saved.</param>
    public void Open(string battlegroup, BattlegroupGeneration current, Func<BattlegroupGeneration, GenerationPreview> previewOf,
        Action<BattlegroupGeneration> whenDone, Action<BattlegroupGeneration> whenMadeFixed, Action whenCancelled)
    {
        preview = previewOf;
        done = whenDone;
        makeFixed = whenMadeFixed;
        cancelled = whenCancelled;
        opened = current;
        lastReady = BattlegroupGeneration.ReadySettings.ContainsKey(current.ReadySetting) ? current.ReadySetting : "Balanced";
        BattlegroupName.Text = battlegroup;
        Fill(current);
        Visibility = Visibility.Visible;
        Refresh();
        Dispatcher.BeginInvoke(() => DoneButton.Focus());
    }

    /// <summary>For the UI checks: clicks a ready setting, and chooses the battlegroup again at once.</summary>
    internal void UseReadyNow(string name)
    {
        UseReady(name);
        redraw.Stop();
        Refresh();
    }

    /// <summary>For the UI checks: how many cards the list of changes shows as added and dropped.</summary>
    internal int ChangesShownNow => Changes.Children.OfType<DockPanel>().Count();

    /// <summary>For the UI checks: Done, as its button does.</summary>
    internal void DoneNow() => DoneClicked(this, new RoutedEventArgs());

    // ---- the controls ----

    /// <summary>A button for each ready setting, then Custom, which is chosen by changing a weight, not by a click.</summary>
    private void MakeReadyButtons()
    {
        foreach (string name in BattlegroupGeneration.ReadySettings.Keys.Append(BattlegroupGeneration.Custom))
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("SmallChoice"),
                Content = Texts.Get(BattlegroupGeneration.TextNamesOf(name).Name),
                ToolTip = Texts.Get(BattlegroupGeneration.TextNamesOf(name).Tip),
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(8, 1, 8, 1),
                IsHitTestVisible = name != BattlegroupGeneration.Custom,
                Focusable = name != BattlegroupGeneration.Custom,
            };
            string ready = name;
            button.Click += (_, _) => UseReady(ready);
            readyButtons[name] = button;
            ReadySettings.Children.Add(button);
        }
    }

    /// <summary>A row for each weight: its name, its slider (0 to 100) and its number, marked when changed.</summary>
    private void MakeWeightRows()
    {
        foreach (BattlegroupMeasure measure in Enum.GetValues<BattlegroupMeasure>())
        {
            int row = Weights.RowDefinitions.Count;
            Weights.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            (string text, string tip) = TextNamesOf(measure);
            var name = new TextBlock { Text = Texts.Get(text), ToolTip = Texts.Get(tip), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) };
            var slider = new Slider { Style = (Style)FindResource("WeightSlider"), ToolTip = name.ToolTip };
            System.Windows.Automation.AutomationProperties.SetName(slider, name.Text);
            slider.ValueChanged += (_, _) => WeightChanged();
            var value = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            value.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            Grid.SetRow(name, row);
            Grid.SetRow(slider, row);
            Grid.SetColumn(slider, 1);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 2);
            Weights.Children.Add(name);
            Weights.Children.Add(slider);
            Weights.Children.Add(value);
            sliders[measure] = slider;
            values[measure] = value;
        }
    }

    /// <summary>The names of a measure's text and tooltip.</summary>
    private static (string Name, string Tip) TextNamesOf(BattlegroupMeasure measure) => measure switch
    {
        BattlegroupMeasure.AgainstArmour => ("Divisions.Battlegroup.Generation.Measure.AgainstArmour", "Divisions.Battlegroup.Generation.Measure.AgainstArmour.Tip"),
        BattlegroupMeasure.AgainstInfantry => ("Divisions.Battlegroup.Generation.Measure.AgainstInfantry", "Divisions.Battlegroup.Generation.Measure.AgainstInfantry.Tip"),
        BattlegroupMeasure.AgainstAircraft => ("Divisions.Battlegroup.Generation.Measure.AgainstAircraft", "Divisions.Battlegroup.Generation.Measure.AgainstAircraft.Tip"),
        BattlegroupMeasure.Survivability => ("Divisions.Battlegroup.Generation.Measure.Survivability", "Divisions.Battlegroup.Generation.Measure.Survivability.Tip"),
        BattlegroupMeasure.Mobility => ("Divisions.Battlegroup.Generation.Measure.Mobility", "Divisions.Battlegroup.Generation.Measure.Mobility.Tip"),
        BattlegroupMeasure.ReconAndOptics => ("Divisions.Battlegroup.Generation.Measure.ReconAndOptics", "Divisions.Battlegroup.Generation.Measure.ReconAndOptics.Tip"),
        BattlegroupMeasure.ValueForPoints => ("Divisions.Battlegroup.Generation.Measure.ValueForPoints", "Divisions.Battlegroup.Generation.Measure.ValueForPoints.Tip"),
        _ => ("Divisions.Battlegroup.Generation.Measure.Variety", "Divisions.Battlegroup.Generation.Measure.Variety.Tip"),
    };

    /// <summary>Sets the controls to a set of settings, without each change redrawing the battlegroup.</summary>
    private void Fill(BattlegroupGeneration settings)
    {
        filling = true;
        foreach (BattlegroupMeasure measure in Enum.GetValues<BattlegroupMeasure>()) sliders[measure].Value = settings.WeightOf(measure);
        BestTick.IsChecked = settings.TakesBest;
        GoodValueTick.IsChecked = settings.TakesGoodValue;
        CheapTick.IsChecked = settings.TakesCheap;
        CardsBox.Text = settings.CardsPerCategory.ToString(Texts.Catalog.Culture);
        PointsBox.Text = settings.PointsAtMost?.ToString(Texts.Catalog.Culture) ?? "";
        filling = false;
        ShowWeights();
    }

    /// <summary>
    /// Each weight's number, with an asterisk when it is not what the window opened with; the ready setting the
    /// weights match pressed (Custom when none); "Back to …" when they no longer match the last one chosen.
    /// </summary>
    private void ShowWeights()
    {
        IReadOnlyList<int> weights = WeightsSet();
        foreach (BattlegroupMeasure measure in Enum.GetValues<BattlegroupMeasure>())
        {
            int weight = weights[(int)measure];
            values[measure].Text = weight.ToString(Texts.Catalog.Culture) + (weight != opened.WeightOf(measure) ? "*" : "");
            values[measure].ToolTip = weight != opened.WeightOf(measure)
                ? Texts.Get("Divisions.Battlegroup.Generation.Changed.Tip", ("was", opened.WeightOf(measure)))
                : null;
        }
        string ready = BattlegroupGeneration.ReadySettingOf(weights);
        foreach ((string name, ToggleButton button) in readyButtons) button.IsChecked = name == ready;
        BackToReadyText.Text = Texts.Get("Divisions.Battlegroup.Generation.BackTo", ("ready", Texts.Get(BattlegroupGeneration.TextNamesOf(lastReady).Name)));
        BackToReadyLink.IsEnabled = ready != lastReady;
    }

    /// <summary>The weights the sliders are set to, in <see cref="BattlegroupMeasure"/>'s order.</summary>
    private List<int> WeightsSet() => [.. Enum.GetValues<BattlegroupMeasure>().Select(measure => (int)Math.Round(sliders[measure].Value))];

    /// <summary>The settings the controls are set to; null when a size typed is not a whole number (the empty points box is the division's own).</summary>
    private BattlegroupGeneration? SettingsSet()
    {
        if (!int.TryParse(CardsBox.Text.Trim(), NumberStyles.Integer, Texts.Catalog.Culture, out int cards) || cards < 1) return null;
        int? points = null;
        if (PointsBox.Text.Trim() != "")
        {
            if (!int.TryParse(PointsBox.Text.Trim(), NumberStyles.Integer, Texts.Catalog.Culture, out int most) || most < 1) return null;
            points = most;
        }
        List<int> weights = WeightsSet();
        return new BattlegroupGeneration
        {
            ReadySetting = BattlegroupGeneration.ReadySettingOf(weights),
            Weights = weights,
            TakesBest = BestTick.IsChecked == true,
            TakesGoodValue = GoodValueTick.IsChecked == true,
            TakesCheap = CheapTick.IsChecked == true,
            CardsPerCategory = cards,
            PointsAtMost = points,
        };
    }

    // ---- changes ----

    /// <summary>A ready setting clicked: the sliders take its weights.</summary>
    private void UseReady(string name)
    {
        if (!BattlegroupGeneration.ReadySettings.TryGetValue(name, out IReadOnlyList<int>? weights)) return;
        lastReady = name;
        filling = true;
        foreach (BattlegroupMeasure measure in Enum.GetValues<BattlegroupMeasure>()) sliders[measure].Value = weights[(int)measure];
        filling = false;
        ShowWeights();
        RedrawSoon();
    }

    private void BackToReadyClicked(object sender, RoutedEventArgs e) => UseReady(lastReady);

    private void WeightChanged()
    {
        if (filling) return;
        ShowWeights();
        RedrawSoon();
    }

    private void ChoiceChanged(object sender, RoutedEventArgs e) => RedrawSoon();

    private void NumberChanged(object sender, TextChangedEventArgs e)
    {
        if (!filling) RedrawSoon();
    }

    /// <summary>Chooses the battlegroup again a moment after the last change.</summary>
    private void RedrawSoon()
    {
        redraw.Stop();
        redraw.Start();
    }

    /// <summary>Chooses the battlegroup again with the settings set (the page redraws it behind), and lists what changes.</summary>
    private void Refresh()
    {
        if (preview is null) return;
        BattlegroupGeneration? settings = SettingsSet();
        DoneButton.IsEnabled = settings is not null;
        Changes.Children.Clear();
        if (settings is null)
        {
            CardsSummary.Text = Texts.Get("Divisions.Battlegroup.Generation.Wrong");
            PointsSummary.Text = "";
            return;
        }
        GenerationPreview made = preview(settings);
        CardsSummary.Text = Texts.Get("Divisions.Battlegroup.Generation.CardsWas", ("cards", made.Cards), ("was", made.WasCards));
        PointsSummary.Text = Texts.Get("Divisions.Battlegroup.Generation.PointsWas", ("points", made.Points), ("was", made.WasPoints));
        foreach (GenerationChange added in made.Added) Changes.Children.Add(ChangeRow("+", "accent", added, struck: false));
        foreach (GenerationChange dropped in made.Dropped) Changes.Children.Add(ChangeRow("−", "warn", dropped, struck: true));
        if (made.Added.Count == 0 && made.Dropped.Count == 0)
        {
            var same = new TextBlock { Text = Texts.Get("Divisions.Battlegroup.Generation.NoChange") };
            same.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            Changes.Children.Add(same);
        }
    }

    /// <summary>A card added (+) or dropped (−, struck through), with why it is chosen at the right.</summary>
    private static DockPanel ChangeRow(string sign, string colour, GenerationChange change, bool struck)
    {
        var mark = new TextBlock { Text = sign, FontWeight = FontWeights.Bold, Width = 14 };
        mark.SetResourceReference(TextBlock.ForegroundProperty, colour);
        var why = new TextBlock { Text = change.Why, FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        why.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var name = new TextBlock { Text = change.Unit, TextTrimming = TextTrimming.CharacterEllipsis };
        if (struck)
        {
            name.TextDecorations = TextDecorations.Strikethrough;
            name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        }
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(why, Dock.Right);
        return new DockPanel { Margin = new Thickness(0, 1, 0, 1), Children = { mark, why, name } };
    }

    // ---- closing ----

    private void DoneClicked(object sender, RoutedEventArgs e)
    {
        if (SettingsSet() is not BattlegroupGeneration settings) return;
        Action<BattlegroupGeneration>? whenDone = done;
        Close();
        whenDone?.Invoke(settings);
    }

    private void MakeFixedClicked(object sender, RoutedEventArgs e)
    {
        if (SettingsSet() is not BattlegroupGeneration settings) return;
        Action<BattlegroupGeneration>? whenMadeFixed = makeFixed;
        Close();
        whenMadeFixed?.Invoke(settings);
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => Cancel();

    /// <summary>Closes the window without keeping anything: the battlegroup is drawn again as it is saved.</summary>
    private void Cancel()
    {
        Action? whenCancelled = cancelled;
        Close();
        whenCancelled?.Invoke();
    }

    private void Close()
    {
        redraw.Stop();
        Visibility = Visibility.Collapsed;
        preview = null;
        done = null;
        makeFixed = null;
        cancelled = null;
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Cancel();
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Cancel();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
