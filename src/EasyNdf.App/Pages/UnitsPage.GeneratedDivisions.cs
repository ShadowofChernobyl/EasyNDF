using System.Windows;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Armory's generated divisions: ⚙ beside the Divisions heading opens the Generate Divisions window, and a
/// generated division shows a line above its units saying how it is made, with Change… and Make Fixed. What they are
/// made from and how is in <see cref="DivisionRosters"/>.
/// </summary>
public partial class UnitsPage
{
    /// <summary>"⚙": the Generate Divisions window; the divisions ticked are made in the mod as one change.</summary>
    private void GenerateDivisionsClicked(object sender, RoutedEventArgs e)
    {
        if (loaded is null) return;
        MainView.Of(this).GenerateDivisions.Open(Rosters, Game, OpenMod.Name, ModNations(), Generate);
    }

    /// <summary>Makes the divisions chosen in the Generate Divisions window, one undo step for all, and shows the first.</summary>
    private void Generate(List<GeneratedDivisionChoice> choices)
    {
        List<string> made = [];
        bool saved = ChangeDivisions(mod =>
        {
            made.Clear();
            foreach (GeneratedDivisionChoice choice in choices)
            {
                if (FrameFor(choice.Generation) is not Division frame) continue;
                made.Add(DivisionChanges.AddGenerated(mod, GameDivisions, choice.Name, frame, choice.Generation, NationFor(choice.Generation), Game));
            }
        });
        if (saved && made.Count > 0) ShowDivisionNamed(made[0]);
    }

    /// <summary>
    /// The nations the mods that are on add of their own: those of units the Armory shows that no unit of the source
    /// itself is of, A to Z. Offered in the Generate Divisions window as nations whose units go into every division.
    /// </summary>
    private List<string> ModNations()
    {
        HashSet<string> ofTheSource = [.. (gameUnits?.Catalog.Units ?? []).Select(unit => unit.Nation)];
        return [.. Rosters.AllUnits.Where(DivisionRosters.IsDeckable).Select(unit => unit.Nation)
            .Where(nation => !ofTheSource.Contains(nation)).Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The game division a generated division's files are made from (none of its values are kept): a multiplayer
    /// division of its nation, else any multiplayer division.
    /// </summary>
    private Division? FrameFor(DivisionGeneration generation)
    {
        string nation = Rosters.NationOf(generation);
        return GameDivisions.Where(division => division.IsForMultiplayer).FirstOrDefault(division => division.Country == nation)
            ?? GameDivisions.FirstOrDefault(division => division.IsForMultiplayer);
    }

    /// <summary>The nation a generated division is listed under, and its side.</summary>
    private (string Country, string Side) NationFor(DivisionGeneration generation) => (Rosters.NationOf(generation), generation.Side);

    /// <summary>
    /// Above a generated division's units, how it is made, with Change… and Make Fixed; hidden for any other division,
    /// and on Battlegroups.
    /// </summary>
    private void ShowGeneratedLine()
    {
        ModDivision? entry = chosenDivision is null || part != ArmoryPart.Divisions ? null : EntryShown(chosenDivision.Descriptor);
        if (entry?.Generation is not DivisionGeneration generation)
        {
            GeneratedLine.Visibility = Visibility.Collapsed;
            return;
        }
        GeneratedLine.Visibility = Visibility.Visible;
        GeneratedText.Text = Texts.Get("Divisions.Generated.Line", ("how", generation.Describe(Texts.Catalog)));
        GeneratedText.ToolTip = Texts.Get("Divisions.Generated.Line.Tip");
    }

    /// <summary>"Change…": the Generate Divisions window for the chosen division alone; its new making is saved in the mod.</summary>
    private void ChangeGenerationClicked(object sender, RoutedEventArgs e)
    {
        if (chosenDivision is not Division division || EntryShown(division.Descriptor)?.Generation is not DivisionGeneration current) return;
        string descriptor = division.Descriptor;
        MainView.Of(this).GenerateDivisions.OpenToChange(division.Name, current, Rosters, Game, ModNations(), generation =>
        {
            if (ChangeDivisions(mod => DivisionChanges.ChangeGeneration(mod, descriptor, generation, NationFor(generation)))) ShowDivisionNamed(descriptor);
        });
    }

    /// <summary>
    /// "Make Fixed": the chosen division keeps the units it has now, as an ordinary new division; it no longer follows
    /// the files it was made from.
    /// </summary>
    private void MakeFixedClicked(object sender, RoutedEventArgs e)
    {
        if (chosenDivision is not Division division) return;
        string descriptor = division.Descriptor;
        if (ChangeDivisions(mod => DivisionChanges.MakeFixed(mod, GameDivisions, descriptor, Rosters))) ShowDivisionNamed(descriptor);
    }
}
