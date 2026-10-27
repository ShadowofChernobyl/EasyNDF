using System.Windows;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The battlegroups of generated divisions: "⚙ Generation Settings…" in the Battlegroups heading opens the Generation
/// Settings window, which redraws the battlegroup behind it with each change (<see cref="BattlegroupGenerator"/>).
/// Done keeps the settings in the mod; Make Fixed keeps the cards they make, as an ordinary battlegroup.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The chosen division as the mod has it, while the Generation Settings window draws another battlegroup behind; null otherwise.</summary>
    private Division? divisionBeforeTrial;

    /// <summary>The places of the cards the settings being tried add, outlined in the accent colour; empty otherwise.</summary>
    private HashSet<int> cardsTried = [];

    /// <summary>"⚙ Generation Settings…": the window for the chosen generated division's battlegroup.</summary>
    private void GenerationSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (chosenDivision is not Division division || EntryShown(division.Descriptor) is not { IsGenerated: true } entry) return;
        string descriptor = division.Descriptor;
        divisionBeforeTrial = division;
        MainView.Of(this).GenerationSettings.Open(BattlegroupName(division), entry.BattlegroupGeneration ?? new BattlegroupGeneration(),
            settings => TryGeneration(division, entry, settings),
            settings => KeepGeneration(descriptor, settings, makeFixed: false),
            settings => KeepGeneration(descriptor, settings, makeFixed: true),
            EndTrial);
    }

    /// <summary>
    /// Draws the battlegroup the settings make in place of the one the mod has, and says what changes: the cards
    /// added and dropped, and the cards and points before and after.
    /// </summary>
    private GenerationPreview TryGeneration(Division saved, ModDivision entry, BattlegroupGeneration settings)
    {
        ModDivision trial = entry.Copy();
        trial.BattlegroupGeneration = settings;
        trial.BattlegroupCards = null;
        Division tried = DivisionChanges.WithGeneratedBattlegroup(saved, trial, Rosters);
        IReadOnlyList<BattlegroupCard> before = saved.Battlegroup?.Cards ?? [];
        IReadOnlyList<BattlegroupCard> after = tried.Battlegroup?.Cards ?? [];
        cardsTried = [.. PlacesNotIn(after, before)];
        chosenDivision = tried;
        DrawBattlegroup();
        return new GenerationPreview(after.Count, PointsOf(after, tried), before.Count, PointsOf(before, saved),
            [.. PlacesNotIn(after, before).Select(place => ChangeOf(tried.Battlegroup!, place))],
            [.. PlacesNotIn(before, after).Select(place => ChangeOf(saved.Battlegroup!, place))]);
    }

    /// <summary>Done or Make Fixed: the battlegroup is drawn as the mod has it, then the settings are saved in the mod.</summary>
    private void KeepGeneration(string descriptor, BattlegroupGeneration settings, bool makeFixed)
    {
        EndTrial();
        ChangeDivisions(mod =>
        {
            DivisionChanges.SetBattlegroupGeneration(mod, descriptor, settings);
            if (makeFixed) DivisionChanges.MakeBattlegroupFixed(mod, GameDivisions, descriptor, Rosters);
        });
    }

    /// <summary>The window closed: the battlegroup is drawn again as the mod has it.</summary>
    private void EndTrial()
    {
        cardsTried = [];
        if (divisionBeforeTrial is null) return;
        chosenDivision = divisionBeforeTrial;
        divisionBeforeTrial = null;
        DrawBattlegroup();
    }

    /// <summary>
    /// The places of the cards in <paramref name="cards"/> that <paramref name="others"/> has not: a unit with two
    /// cards in one and one in the other counts once.
    /// </summary>
    private static IEnumerable<int> PlacesNotIn(IReadOnlyList<BattlegroupCard> cards, IReadOnlyList<BattlegroupCard> others)
    {
        Dictionary<string, int> left = others.GroupBy(card => card.Unit, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        for (int place = 0; place < cards.Count; place++)
        {
            if (left.TryGetValue(cards[place].Unit, out int count) && count > 0) left[cards[place].Unit] = count - 1;
            else yield return place;
        }
    }

    /// <summary>The activation points of a division's cards.</summary>
    private int PointsOf(IReadOnlyList<BattlegroupCard> cards, Division division) =>
        BattlegroupCheck.Points(cards, division, TypeOfUnit);

    /// <summary>A card for the window's list of changes: its unit's name, and why it was chosen when it was generated.</summary>
    private GenerationChange ChangeOf(Battlegroup battlegroup, int place)
    {
        BattlegroupCard card = battlegroup.Cards[place];
        string name = unitsByDescriptor.TryGetValue(card.Unit, out Unit? unit) ? unit.Name : card.Unit;
        string why = battlegroup.Generated is { } generated && place < generated.Count ? WhyChosen(generated[place]) : "";
        return new GenerationChange(name, why);
    }

    /// <summary>Why a generated card was chosen: its role and its pick, "anti-air · good value".</summary>
    internal string WhyChosen(GeneratedCard card)
    {
        string role = unitsByDescriptor.TryGetValue(card.Card.Unit, out Unit? unit) && unit.Role == card.Role
            ? UnitNames.RoleOf(unit)
            : UnitNames.Factory(card.Role);
        return Texts.Get("Divisions.Battlegroup.Generation.Why", ("role", role), ("pick", Texts.Get(PickTextName(card.Pick))));
    }

    /// <summary>The name of a pick's text: "the best", "good value", "cheap".</summary>
    private static string PickTextName(BattlegroupPick pick) => pick switch
    {
        BattlegroupPick.Best => "Divisions.Battlegroup.Generation.Pick.Best",
        BattlegroupPick.GoodValue => "Divisions.Battlegroup.Generation.Pick.GoodValue",
        _ => "Divisions.Battlegroup.Generation.Pick.Cheap",
    };
}
