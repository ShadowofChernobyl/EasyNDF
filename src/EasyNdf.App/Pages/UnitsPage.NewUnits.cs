using System.IO;
using System.Windows;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// The units the open mod adds: "+ New Unit…" opens the New Unit window, which makes one from scratch with another
/// unit's 3D model; Duplicate Unit… copies a unit whole under a new name; Delete Unit takes one the mod added out again.
/// Each goes through <see cref="ChangeDivisions"/>, as the division changes do: refused when a file it is written into
/// is not on the Files tab, otherwise saved in the mod with its rules in the Units Armory preset.
/// </summary>
public partial class UnitsPage
{
    /// <summary>"+ New Unit…": the New Unit window, on every type.</summary>
    private void NewUnitClicked(object sender, RoutedEventArgs e) => OpenNewUnitWindow();

    /// <summary>The New Unit window, with the source's units as the units to choose from; refused while the Armory cannot make rules.</summary>
    private void OpenNewUnitWindow()
    {
        if (loaded is null) return;
        if (WhyRulesCannotBeMade() is string reason)
        {
            _ = MainView.Of(this).Messages.Tell(reason);
            return;
        }
        MainView.Of(this).NewUnit.Open(GameUnits, Game, TakenUnitDescriptors(), ChooseUnitPicture, MakeUnit, gameUnits?.Catalog.ArmorLevels, this);
    }

    /// <summary>The unit descriptors in use: the source's, and those the open mod adds.</summary>
    private HashSet<string> TakenUnitDescriptors() =>
        [.. GameUnits.Select(unit => unit.Descriptor), .. OpenMod.Units.Select(unit => unit.Descriptor), .. (loaded?.Catalog.Units ?? []).Select(unit => unit.Descriptor)];

    /// <summary>
    /// "My own…" in the New Unit window: a card picture of the user's own, kept as a .png the game's tools read (as a
    /// division's emblem is); null when none was chosen, or it could not be kept, which a message says.
    /// </summary>
    private string? ChooseUnitPicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Texts.Get("Units.New.Picture.Title"), Filter = Texts.Get("Divisions.Pane.Emblem.Picture.Filter") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return null;
        string? kept = KeepPicture(dialog.FileName);
        if (kept is null) _ = MainView.Of(this).Messages.Tell(Texts.Get("Divisions.Pane.Emblem.Picture.NotKept", ("file", dialog.FileName)));
        return kept;
    }

    /// <summary>
    /// Adds a unit to the open mod with the next deck number free, unless a file it is written into is not on the
    /// Files tab; once made, it is shown selected on the Units sub-tab.
    /// </summary>
    /// <param name="unit">The unit, with its changes to its model's weapons.</param>
    /// <param name="copies">The copies of weapons its changes mount, made with it.</param>
    /// <returns>Whether it was made.</returns>
    private bool MakeUnit(ModUnit unit, IReadOnlyList<ModWeapon> copies)
    {
        bool made = ChangeDivisions(mod =>
        {
            foreach (ModWeapon copy in copies) mod.Weapons.Add(copy.Copy());
            ModUnit added = unit.Copy();
            added.DeckNumber = ModUnit.NextDeckNumber(GameUnits, mod);
            mod.Units.Add(added);
            // the copies given its model's loadout reach its own loadout too, as they would its model's
            if (added.LoadoutFrom is string from) ModWeapon.FollowOwnLoadout(mod, from, LoadoutRules.OwnLoadoutName(added.Descriptor));
        });
        if (made) _ = ShowMadeUnit(unit.Descriptor);
        return made;
    }

    /// <summary>
    /// A unit just made, shown selected on the Units sub-tab once the units are shown with it. It is in no division
    /// yet, so "Only units in a division" is switched off, or it would be hidden.
    /// </summary>
    private async Task ShowMadeUnit(string descriptor)
    {
        await ShowModValues();
        if (loaded?.Catalog.Units.FirstOrDefault(unit => unit.Descriptor == descriptor) is not Unit made) return;
        if (filter.OnlyInDivision && made.Divisions.Count == 0)
        {
            OnlyInDivision.IsChecked = false;
            filter.OnlyInDivision = false;
        }
        ShowInUnits(made);
    }

    /// <summary>
    /// Duplicate Unit…: a copy of a unit, everything kept but its name, which is asked for. A unit the mod adds is
    /// copied as the mod has it, made from the same unit.
    /// </summary>
    private async Task DuplicateUnit(Unit unit)
    {
        if (WhyRulesCannotBeMade() is string reason)
        {
            await MainView.Of(this).Messages.Tell(reason);
            return;
        }
        ModUnit? added = OpenMod.Units.FirstOrDefault(each => each.Descriptor == unit.Descriptor);
        // only the source's own units and those the mod adds can be copied: another's rules make the rest
        if (added is null && !GameUnits.Any(each => each.Descriptor == unit.Descriptor)) return;
        string name = await MainView.Of(this).Messages.AskName(Texts.Get("Units.Duplicate.Ask", ("unit", unit.Name)), unit.Name);
        if (name == "") return;
        ModUnit copy = added?.Copy() ?? new ModUnit { MadeFrom = unit.Descriptor, IsDuplicate = true };
        copy.Name = name;
        copy.Descriptor = ModUnit.NewDescriptor(name, unit.Nation, TakenUnitDescriptors());
        MakeUnit(copy, []);
    }

    /// <summary>Whether a unit is one the open mod adds in the Armory, which Delete Unit can take out again.</summary>
    private bool IsAddedUnit(Unit unit) => OpenMod.Units.Any(each => each.Descriptor == unit.Descriptor);

    /// <summary>Whether a unit can be duplicated: one of the source's own, or one the open mod adds.</summary>
    private bool CanDuplicateUnit(Unit unit) => IsAddedUnit(unit) || GameUnits.Any(each => each.Descriptor == unit.Descriptor);

    /// <summary>
    /// Delete Unit, for a unit the open mod adds, once the user says so: it is taken out of the mod, and out of the
    /// mod's divisions that use it. Undo brings it back.
    /// </summary>
    private async Task DeleteUnit(Unit unit)
    {
        if (!await MainView.Of(this).Messages.Confirm(Texts.Get("Units.Delete.Ask", ("unit", unit.Name)), Texts.Get("Units.Menu.Delete"))) return;
        ChangeDivisions(mod => ModUnit.RemoveFrom(mod, unit.Descriptor));
    }

    /// <summary>
    /// The Delete key on the Units sub-tab, once the user says so. The units the open mod adds are deleted; its changes
    /// to the game's units are taken back, those units as the game has them again. The game's own units are never
    /// deleted: a unit taken out of the game's files breaks every division and battlegroup that names it. With nothing
    /// to delete or take back, a message says so. Undo brings it all back.
    /// </summary>
    private async Task DeleteUnits(IReadOnlyList<Unit> units)
    {
        List<Unit> added = [.. units.Where(IsAddedUnit)];
        List<Unit> changed = [.. units.Where(unit => !IsAddedUnit(unit) && UnitEdit.Of(OpenMod, unit.Descriptor) is not null)];
        if (added.Count == 0 && changed.Count == 0)
        {
            await MainView.Of(this).Messages.Tell(Texts.Count("Units.Delete.GameUnit", units.Count, ("unit", units[0].Name)));
            return;
        }
        var question = new List<string>();
        if (added.Count > 0) question.Add(Texts.Count("Units.Delete.AskAdded", added.Count, ("unit", added[0].Name)));
        if (changed.Count > 0) question.Add(Texts.Count("Units.Delete.AskChanged", changed.Count, ("unit", changed[0].Name)));
        int left = units.Count - added.Count - changed.Count;
        if (left > 0) question.Add(Texts.Count("Units.Delete.Kept", left));
        if (!await MainView.Of(this).Messages.Confirm(string.Join("\n\n", question), Texts.Get("Units.Delete.Ok"))) return;
        ChangeDivisions(mod =>
        {
            foreach (Unit unit in added) ModUnit.RemoveFrom(mod, unit.Descriptor);
            foreach (Unit unit in changed) mod.UnitEdits.RemoveAll(edit => edit.Descriptor == unit.Descriptor);
        });
    }

    /// <inheritdoc/>
    public bool CanDuplicate(Unit unit) => CanDuplicateUnit(unit);

    /// <inheritdoc/>
    public void Duplicate(Unit unit) => _ = DuplicateUnit(unit);
}
