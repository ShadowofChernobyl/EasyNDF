using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Battlegroups sub-tab: the chosen division's Official Battlegroup in the centre (<see cref="BattlegroupView"/>),
/// its name in the heading with a link back to its division, and the Unit pane at the right for the card clicked. Its
/// changes go through <see cref="ChangeDivisions"/>, as every division change does: saved in the open mod's Divisions
/// preset, refused when Decks.ndf or DeckPacks.ndf is not on the Files tab.
/// </summary>
public partial class UnitsPage : IBattlegroupEditing
{
    /// <summary>How many tiles a battlegroup's rows had across when they were last made.</summary>
    private int battlegroupColumns;

    /// <summary>Draws the chosen division's battlegroup, as many tiles across as fit.</summary>
    private void DrawBattlegroup()
    {
        if (chosenDivision is null) return;
        battlegroupColumns = CardRowView.ColumnsFor(RoomIn(BattlegroupScroller));
        bool hasPlace = chosenDivision.Battlegroup is not null || IsNewInMod(chosenDivision);
        BattlegroupContent.Content = BattlegroupView.Build(chosenDivision, unitsByDescriptor, Game, selection, battlegroupColumns, this,
            (button, type) => BattlegroupCardPopup.Open(button, type, () => chosenDivision, unitsByDescriptor, ((IUnitActions)this).IsFirstSide, this),
            foldedBattlegroupCategories, BattlegroupFoldClicked, hasPlace, cardsTried, WhyChosen);
    }

    /// <summary>The categories folded away in the Battlegroups view, by code; kept apart from the Divisions view's.</summary>
    private readonly HashSet<string> foldedBattlegroupCategories = [];

    /// <summary>A category's fold button clicked: its cards fold away or come back.</summary>
    private void BattlegroupFoldClicked(string factory)
    {
        if (!foldedBattlegroupCategories.Remove(factory)) foldedBattlegroupCategories.Add(factory);
        DrawBattlegroup();
    }

    /// <summary>The view was resized: the rows are made again when a different number of tiles fits across.</summary>
    private void BattlegroupResized(object sender, SizeChangedEventArgs e)
    {
        if (!ShowsBattlegroup || drawIsWaiting || CardRowView.ColumnsFor(RoomIn(BattlegroupScroller)) == battlegroupColumns) return;
        DrawBattlegroup();
    }

    /// <summary>The units of the battlegroup's cards, row by row, as they are on screen, for Shift+Click and Ctrl+A.</summary>
    private List<Unit> BattlegroupTilesInOrder()
    {
        var units = new List<Unit>();
        if (BattlegroupContent.Content is not StackPanel rows) return units;
        foreach (UniformGrid tiles in rows.Children.OfType<UniformGrid>())
            units.AddRange(tiles.Children.OfType<Border>().Select(place => place.Child).OfType<UnitTile>().Select(tile => tile.Unit));
        return units;
    }

    /// <summary>How many cards the battlegroup has, for the foot of the tab.</summary>
    private string BattlegroupCountText() => Texts.Count("Divisions.Battlegroup.CardCount", chosenDivision?.Battlegroup?.Cards.Count ?? 0);

    /// <summary>
    /// The heading on the Battlegroups sub-tab: the battlegroup's name (made from the division's when it has none
    /// yet), then "of 3RD ARMORED", a link that opens the division on the Divisions sub-tab.
    /// </summary>
    private void ShowBattlegroupTitle(Division division)
    {
        TitleText.Inlines.Clear();
        TitleText.Inlines.Add(new Run(BattlegroupName(division)));
        ShowBattlegroupChanges(division);
        SummaryText.Inlines.Clear();
        var link = new Hyperlink(new Run(division.Name)) { Style = (Style)FindResource("CrumbLink"), ToolTip = Texts.Get("Divisions.Battlegroup.Of.Tip") };
        link.Click += (_, _) => ShowDivisionOfBattlegroup();
        string words = Texts.Get("Divisions.Battlegroup.Of", ("division", "\u0001"));
        // the words around the division's name, which is the link
        string[] parts = words.Split('\u0001');
        SummaryText.Inlines.Add(new Run(parts[0]));
        SummaryText.Inlines.Add(link);
        if (parts.Length > 1) SummaryText.Inlines.Add(new Run(parts[1]));
    }

    /// <summary>
    /// Back to the Game's shows for a game division's battlegroup the mod changed, and ⚙ Generation Settings… for a
    /// generated division's; the title's tooltip says what the game's has.
    /// </summary>
    private void ShowBattlegroupChanges(Division division)
    {
        ModDivision? entry = DivisionChanges.EntryOf(OpenMod, division.Descriptor);
        bool changed = entry is { IsNew: false } && (entry.BattlegroupCards is not null || entry.BattlegroupName != "");
        RevertBattlegroupButton.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
        GenerationSettingsButton.Visibility = EntryShown(division.Descriptor) is { IsGenerated: true } ? Visibility.Visible : Visibility.Collapsed;
        Division? game = GameDivisions.FirstOrDefault(each => each.Descriptor == division.Descriptor);
        TitleText.ToolTip = changed && game?.Battlegroup is Battlegroup before
            ? Texts.Get("Divisions.Battlegroup.Changed.Tip", ("mod", OpenMod.Name), ("cards", Texts.Count("Divisions.Battlegroup.CardCount", before.Cards.Count)))
            : null;
    }

    /// <summary>Back to the Game's: asked first, the mod's changes to the battlegroup are taken back.</summary>
    private async void RevertBattlegroupClicked(object sender, RoutedEventArgs e)
    {
        if (chosenDivision is not Division division) return;
        string ask = Texts.Get("Divisions.Battlegroup.Revert.Ask", ("battlegroup", BattlegroupName(division)), ("mod", OpenMod.Name));
        if (!await MainView.Of(this).Messages.Confirm(ask, Texts.Get("Divisions.Battlegroup.Revert.Do"))) return;
        string descriptor = division.Descriptor;
        ChangeDivisions(mod => DivisionChanges.ChangeDetails(mod, GameDivisions, descriptor, entry =>
        {
            entry.BattlegroupCards = null;
            entry.BattlegroupName = "";
        }));
    }

    // ---- the battlegroup's changes ----

    /// <inheritdoc/>
    public bool AddCard(BattlegroupCard card) => ChangeCards(cards =>
    {
        // after the last card of its type, so its row keeps together; at the end when it is the first
        string? type = TypeOfUnit(card.Unit);
        int last = cards.FindLastIndex(each => TypeOfUnit(each.Unit) == type);
        cards.Insert(last < 0 ? cards.Count : last + 1, card);
    });

    /// <inheritdoc/>
    public bool ChangeCard(int place, BattlegroupCard card) => ChangeCards(cards =>
    {
        if (place >= 0 && place < cards.Count) cards[place] = card;
    });

    /// <inheritdoc/>
    public bool TakeOutCard(int place) => ChangeCards(cards =>
    {
        if (place >= 0 && place < cards.Count) cards.RemoveAt(place);
    });

    /// <inheritdoc/>
    public bool MoveCard(int from, int to) => ChangeCards(cards =>
    {
        if (from < 0 || from >= cards.Count || to < 0 || to >= cards.Count || from == to) return;
        BattlegroupCard moved = cards[from];
        cards.RemoveAt(from);
        cards.Insert(to, moved);
    });

    /// <summary>
    /// Changes the chosen division's battlegroup: its cards as they are now, changed by <paramref name="change"/>, kept
    /// in the mod (a game division's own cards given back let the change go). Returns whether it was saved.
    /// </summary>
    private bool ChangeCards(Action<List<BattlegroupCard>> change)
    {
        if (chosenDivision is not Division division) return false;
        // its cards are chosen again at each export: a change by hand would be lost
        if (division.Battlegroup?.Generated is not null)
        {
            _ = MainView.Of(this).Messages.Tell(Texts.Get("Divisions.Battlegroup.Generation.CardsLocked"));
            return false;
        }
        List<BattlegroupCard> cards = [.. division.Battlegroup?.Cards ?? []];
        change(cards);
        string descriptor = division.Descriptor;
        return ChangeDivisions(mod => DivisionChanges.ChangeDetails(mod, GameDivisions, descriptor, entry => entry.BattlegroupCards = [.. cards]));
    }

    /// <summary>A unit's type (its factory, "Tanks"); null for a unit the source does not have.</summary>
    private string? TypeOfUnit(string unit) => unitsByDescriptor.GetValueOrDefault(unit)?.Factory;

    /// <summary>A division's battlegroup's name: its own, or, for one with none yet, one made from the division's.</summary>
    private static string BattlegroupName(Division division) =>
        division.Battlegroup is { Name: { Length: > 0 } name } ? name : Texts.Get("Divisions.Battlegroup.DefaultName", ("division", division.Name));
}
