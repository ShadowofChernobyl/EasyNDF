using System.Windows;
using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Divisions;

/// <summary>
/// What the Division pane's boxes and buttons change in the division shown; the Divisions tab does it, saving each
/// change in the open mod.
/// </summary>
public interface IDivisionEditing
{
    /// <summary>The nations a division can be of, each with its side: those of the game's divisions.</summary>
    IReadOnlyList<(string Country, string Side)> Nations { get; }

    /// <summary>The division types a division can be of: the game's, by key ("ARMORED").</summary>
    IReadOnlyList<string> Types { get; }

    /// <summary>Renames the division.</summary>
    void Rename(string name);

    /// <summary>Changes the division's details, as <paramref name="change"/> sets them.</summary>
    void ChangeDetails(Action<ModDivision> change);

    /// <summary>"Game's…": the game's emblems to choose one from, under <paramref name="button"/>.</summary>
    void ChooseGameEmblem(FrameworkElement button);

    /// <summary>"Picture…": a picture of the user's own for the emblem.</summary>
    void ChooseEmblemPicture();

    /// <summary>"+ Add…": units of the division to show on its card in the game.</summary>
    void AddStandoutUnits();
}
