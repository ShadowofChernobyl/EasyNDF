using System.Windows;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Unit pane's changes: a click on a value of a unit changes it (its name, nation, role, stats or card picture).
/// A unit the open mod adds takes the value itself; one of the source's own units gets a change of the mod's
/// (<see cref="UnitEdit"/>), which only keeps what differs from the unit as the game has it: a value set back to the
/// game's is let go of. Each goes through <see cref="ChangeDivisions"/>, as every Armory change does: refused when a
/// file it is written into is not on the Files tab, otherwise saved in the mod as locked rules in its Units preset,
/// with Undo.
/// </summary>
public partial class UnitsPage
{
    /// <inheritdoc/>
    public bool CanChangeUnit(Unit unit) => Game == Data.Game.GameTitle.Warno && (IsAddedUnit(unit) || IsSourceUnit(unit));

    /// <summary>Whether a unit is one of the source's own, before the open mod's rules.</summary>
    private bool IsSourceUnit(Unit unit) => GameUnits.Any(each => each.Descriptor == unit.Descriptor);

    /// <inheritdoc/>
    public bool ChangeUnit(Unit unit, string field, string value)
    {
        if (UnitBeforeChanges(unit) is not Unit before) return false;
        return field switch
        {
            UnitField.Name => ChangeUnitName(unit, before, value.Trim()),
            UnitField.Nation => ChangeUnitValues(unit, values =>
            {
                bool isTheSame = value == before.Nation;
                values.Nation = isTheSame ? null : value;
                values.Side = isTheSame ? null : SideOfNation(value);
            }),
            UnitField.Role => ChangeUnitValues(unit, values => values.Role = value == before.Role ? null : value),
            _ => ChangeUnitStat(unit, before, field, value),
        };
    }

    /// <summary>
    /// The unit a change is measured against, its values as they are without it: for a unit the mod adds, the unit it
    /// is made from (as shown, with the mod's changes); for one of the source's, the unit as the mod's other rules make it.
    /// </summary>
    private Unit? UnitBeforeChanges(Unit unit)
    {
        if (OpenMod.Units.FirstOrDefault(each => each.Descriptor == unit.Descriptor) is ModUnit added)
            return loaded?.Catalog.Units.FirstOrDefault(each => each.Descriptor == added.MadeFrom);
        return unitsByRules?.Catalog.Units.FirstOrDefault(each => each.Descriptor == unit.Descriptor)
            ?? GameUnits.FirstOrDefault(each => each.Descriptor == unit.Descriptor);
    }

    /// <summary>A unit renamed; an empty name is not taken. A game unit given its own name back lets go of the change.</summary>
    private bool ChangeUnitName(Unit unit, Unit before, string name)
    {
        if (name == "") return false;
        string descriptor = unit.Descriptor;
        if (IsAddedUnit(unit))
        {
            return ChangeDivisions(mod =>
            {
                if (mod.Units.FirstOrDefault(each => each.Descriptor == descriptor) is ModUnit added) added.Name = name;
            });
        }
        return ChangeDivisions(mod =>
        {
            UnitEdit.For(mod, descriptor).Name = name == before.Name ? null : name;
            UnitEdit.LetGoOfIfEmpty(mod, descriptor);
        });
    }

    /// <summary>A stat changed, when the game takes the value typed; the unit's own value again lets go of it.</summary>
    private bool ChangeUnitStat(Unit unit, Unit before, string field, string typed)
    {
        if (EditableStat.Of(field) is not EditableStat stat) return false;
        if (stat.ValueOf(typed, unit, gameUnits?.Catalog.ArmorLevels, Texts.Catalog.Culture) is not double value) return false;
        return ChangeUnitValues(unit, values => values.Give(field, value == stat.Read(before) ? null : value));
    }

    /// <summary>
    /// Changes values of a unit in the open mod: those of a unit it adds, or its change to one of the source's, which
    /// is let go of once it changes nothing.
    /// </summary>
    private bool ChangeUnitValues(Unit unit, Action<UnitValues> change)
    {
        string descriptor = unit.Descriptor;
        if (IsAddedUnit(unit))
        {
            return ChangeDivisions(mod =>
            {
                if (mod.Units.FirstOrDefault(each => each.Descriptor == descriptor) is ModUnit added) change(added);
            });
        }
        return ChangeDivisions(mod =>
        {
            change(UnitEdit.For(mod, descriptor));
            UnitEdit.LetGoOfIfEmpty(mod, descriptor);
        });
    }

    /// <summary>The side of a nation, as the source's units have it.</summary>
    private string SideOfNation(string nation) => GameUnits.FirstOrDefault(unit => unit.Nation == nation)?.Side ?? "";

    /// <inheritdoc/>
    public IReadOnlyList<(string Code, string Shown)> NationChoices
    {
        get
        {
            var choices = new List<(string, string)>();
            var byName = StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true);
            foreach (string side in UnitNames.SidesOf(Game))
            {
                IEnumerable<string> nations = GameUnits.Where(unit => unit.Side == side).Select(unit => unit.Nation).Distinct().OrderBy(UnitNames.Nation, byName);
                foreach (string nation in nations) choices.Add((nation, UnitNames.Nation(nation) + " (" + UnitNames.Side(side) + ")"));
            }
            return choices;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<(string Code, string Shown)> RoleChoices =>
        [.. GameUnits.Where(unit => unit.Role != "").GroupBy(unit => unit.Role).Select(group => (group.Key, UnitNames.RoleOf(group.First())))
            .OrderBy(role => role.Item2, StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true))];

    /// <summary>The armour families' levels; named apart, as <see cref="Data.Units.ArmorLevels"/> is a class the page uses.</summary>
    IReadOnlyDictionary<string, int>? IUnitActions.ArmorLevels => gameUnits?.Catalog.ArmorLevels;

    // ---- the card picture ----

    /// <inheritdoc/>
    public bool CanChangePicture(Unit unit) => CanChangeUnit(unit);

    /// <summary>
    /// The ✎ on a unit's picture in the Unit pane: "Picture…", a picture of the user's own; and, while it has one, the
    /// unit's own picture again: for a unit the mod adds, that of the unit it was copied from; for one of the
    /// source's, the game's.
    /// </summary>
    public void OpenPictureMenu(Unit unit, FrameworkElement at)
    {
        MenuChoice ownPicture = new(Texts.Get("Units.Pane.Picture.Own"), () => ChooseOwnPicture(unit), Tip: Texts.Get("Units.New.Picture.Own.Tip"));
        if (OpenMod.Units.FirstOrDefault(each => each.Descriptor == unit.Descriptor) is ModUnit added)
        {
            string madeFromName = GameUnits.FirstOrDefault(each => each.Descriptor == added.MadeFrom)?.Name ?? added.MadeFrom;
            Menus.ShowBelow(at,
            [
                ownPicture,
                added.PictureFile is null ? null : new MenuChoice(Texts.Get("Units.New.Picture.Model", ("unit", madeFromName)), () => ChangeUnitPicture(unit, null),
                    Tip: Texts.Get("Units.New.Picture.Model.Tip")),
            ]);
            return;
        }
        Menus.ShowBelow(at,
        [
            ownPicture,
            UnitEdit.Of(OpenMod, unit.Descriptor)?.PictureFile is null ? null : new MenuChoice(Texts.Get("Units.Pane.Picture.Game"), () => ChangeUnitPicture(unit, null),
                Tip: Texts.Get("Units.Pane.Picture.Game.Tip")),
        ]);
    }

    /// <summary>"Picture…": a picture of the user's own for a unit; nothing changes when it is cancelled.</summary>
    private void ChooseOwnPicture(Unit unit)
    {
        if (ChooseUnitPicture() is string pictureFile) ChangeUnitPicture(unit, pictureFile);
    }

    /// <summary>A unit given another card picture: a file of the user's own, or null for its own picture again.</summary>
    private void ChangeUnitPicture(Unit unit, string? pictureFile)
    {
        string? now = OpenMod.Units.FirstOrDefault(each => each.Descriptor == unit.Descriptor)?.PictureFile ?? UnitEdit.Of(OpenMod, unit.Descriptor)?.PictureFile;
        if (pictureFile is null && now is null) return;
        ChangeUnitValues(unit, values => values.PictureFile = pictureFile);
    }
}
