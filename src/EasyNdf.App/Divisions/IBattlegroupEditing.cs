using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Divisions;

/// <summary>
/// What changes the chosen division's Official Battlegroup, saved in the open mod's Battlegroups preset, as its other
/// division changes are in its Divisions preset. Each returns whether the change was made: one that needs a file not
/// on the Files tab is refused, with a message saying which.
/// </summary>
public interface IBattlegroupEditing
{
    /// <summary>Adds a card after the battlegroup's last card of its type.</summary>
    bool AddCard(BattlegroupCard card);

    /// <summary>Gives the card at a place another veterancy or transport.</summary>
    bool ChangeCard(int place, BattlegroupCard card);

    /// <summary>Takes the card at a place in the battlegroup out.</summary>
    bool TakeOutCard(int place);

    /// <summary>Moves the card at <paramref name="from"/> to <paramref name="to"/>, the places between moving up or down one.</summary>
    bool MoveCard(int from, int to);
}
