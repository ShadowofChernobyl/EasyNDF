using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// What the Units tab's buttons and right-click menus do: show a unit, its weapon set or a weapon's ammunition in the
/// File Editor, make a rule for it, compare it, pin it, copy its name.
/// </summary>
public partial class UnitsPage : IUnitActions
{
    /// <summary>
    /// A unit's menu: show it or its weapon set in the File Editor; on Compare, pin it; compare it; new rules for it or
    /// for the units shown; copy its name; duplicate it, or delete it when the mod adds it; take it out of the division
    /// chosen, or add it to a division.
    /// </summary>
    public ContextMenu OpenUnitMenu(Unit unit, FrameworkElement at)
    {
        bool onCompare = view == UnitView.Compare;
        MenuChoice?[] items =
        [
            Menus.Heading(unit.Name),
            .. ShowInEditorChoices(unit),
            Menus.Line,
            onCompare && compared.Contains(unit) && !ReferenceEquals(unit, pinned) ? new MenuChoice(Texts.Get("Units.Menu.Pin"), () => Pin(unit)) : null,
            new MenuChoice(Texts.Get("Units.Menu.Compare"), () => Compare(unit), IsEnabled: !compared.Contains(unit)),
            new MenuChoice(Texts.Get("Units.Menu.NewRuleForUnit"), () => NewRuleFor(unit)),
            new MenuChoice(Texts.Get("Units.Menu.NewRuleForShown"), () => _ = NewRuleForShown(), IsEnabled: !onCompare && shown.Count > 0),
            new MenuChoice(Texts.Get("Units.Menu.CopyName"), () => Copy(unit.Descriptor), Key: unit.Descriptor),
            .. LineBefore(DuplicateChoices(unit)),
            Menus.Line,
            InChosenDivision(unit) ? new MenuChoice(Texts.Get("Divisions.Menu.Remove"), () => _ = AskToRemoveFromDivision(chosenDivision!, [unit]), Key: "Delete") : null,
            .. AddToDivisionChoices([unit]),
            .. LineBefore(DivisionClipboardChoices(unit, [unit])),
        ];
        return at is Button ? Menus.ShowBelow(at, items) : Menus.Show(at, items);
    }

    /// <summary>
    /// The menu of several units selected (Ctrl+Click, Shift+Click, Ctrl+A), right-clicked on one of them: compare them,
    /// a new rule for them, copy their names; take them out of the division chosen, or add them to a division.
    /// </summary>
    /// <param name="clicked">The unit right-clicked, one of those selected: what is copied comes from it.</param>
    private void OpenUnitsMenu(Unit clicked, IReadOnlyList<Unit> units, FrameworkElement at)
    {
        List<Unit> inDivision = [.. units.Where(InChosenDivision)];
        Menus.Show(at,
        [
            Menus.Heading(Texts.Count("Units.Menu.Selected", units.Count)),
            new MenuChoice(Texts.Get("Units.Menu.Compare"), () => AddToCompare(units), IsEnabled: units.Any(unit => !compared.Contains(unit))),
            new MenuChoice(Texts.Get("Units.Menu.NewRuleForSelected"), () => _ = NewRuleForUnits(Texts.Get("Units.Rule.UnitsSelected"), units)),
            new MenuChoice(Texts.Get("Units.Menu.CopyNames"), () => Copy(string.Join(Environment.NewLine, units.Select(unit => unit.Descriptor)))),
            Menus.Line,
            inDivision.Count > 0 ? new MenuChoice(Texts.Get("Divisions.Menu.Remove"), () => _ = AskToRemoveFromDivision(chosenDivision!, inDivision), Key: "Delete") : null,
            .. AddToDivisionChoices(units),
            .. LineBefore(DivisionClipboardChoices(clicked, units)),
        ]);
    }

    /// <summary>Duplicate Unit… for a unit that can be copied, and Delete Unit for one the open mod adds; none for others.</summary>
    private MenuChoice?[] DuplicateChoices(Unit unit) =>
    [
        .. new[]
        {
            CanDuplicateUnit(unit) ? new MenuChoice(Texts.Get("Units.Menu.Duplicate"), () => _ = DuplicateUnit(unit)) : null,
            IsAddedUnit(unit) ? new MenuChoice(Texts.Get("Units.Menu.Delete"), () => _ = DeleteUnit(unit)) : null,
        }.OfType<MenuChoice>(),
    ];

    /// <summary>Some choices with a line before them; none when there are none.</summary>
    private static MenuChoice?[] LineBefore(MenuChoice?[] choices) => choices.Length == 0 ? [] : [Menus.Line, .. choices];

    /// <summary>Whether a unit is in the division chosen in the list.</summary>
    private bool InChosenDivision(Unit unit) => chosenDivision?.Units.Any(rule => rule.Unit == unit.Descriptor) == true;

    /// <summary>"Show Unit in File Editor" and, when the unit has one, "Show Weapon Set in File Editor".</summary>
    internal MenuChoice?[] ShowInEditorChoices(Unit unit) =>
    [
        ShowInEditorChoice(Texts.Get("Units.Menu.ShowUnit"), unit.Descriptor, unit.File),
        unit.WeaponSet is null ? null : ShowInEditorChoice(Texts.Get("Units.Menu.ShowWeaponSet"), unit.WeaponSet, UnitFiles.WeaponSets),
    ];

    /// <summary>The menu of the list's empty room: a new rule for the units shown.</summary>
    private void OpenListMenu(FrameworkElement at) =>
        Menus.Show(at, [new MenuChoice(Texts.Get("Units.Menu.NewRuleForShown"), () => _ = NewRuleForShown(), IsEnabled: shown.Count > 0)]);

    /// <summary>
    /// A weapon's menu: show its ammunition, its missile, the unit's weapon set or the unit in the File Editor; a new
    /// rule for its ammunition; copy the ammunition's name.
    /// </summary>
    public ContextMenu OpenWeaponMenu(Unit unit, UnitWeapon weapon, FrameworkElement at)
    {
        MenuChoice?[] items =
        [
            Menus.Heading(weapon.Name),
            new MenuChoice(Texts.Get("Units.Menu.ShowInWeapons"), () => ShowWeaponInArmory(weapon.Ammunition), IsEnabled: CanShowWeaponInArmory(weapon.Ammunition),
                Tip: Texts.Get("Units.Menu.ShowInWeapons.Tip")),
            Menus.Line,
            ShowInEditorChoice(Texts.Get("Units.Menu.ShowAmmunition"), weapon.Ammunition, weapon.AmmunitionFile ?? UnitFiles.Ammunition),
            weapon.Missile is null ? null : ShowInEditorChoice(Texts.Get("Units.Menu.ShowMissile"), weapon.Missile, UnitFiles.Missiles),
            unit.WeaponSet is null ? null : ShowInEditorChoice(Texts.Get("Units.Menu.ShowWeaponSet"), unit.WeaponSet, UnitFiles.WeaponSets),
            ShowInEditorChoice(Texts.Get("Units.Menu.ShowUnit"), unit.Descriptor, unit.File),
            CopyForUnitChoice(unit, weapon),
            Menus.Line,
            new MenuChoice(Texts.Get("Units.Menu.NewRuleForAmmunition"), () => NewRule(weapon.Name, [weapon.Ammunition])),
            new MenuChoice(Texts.Get("Units.Menu.CopyName"), () => Copy(weapon.Ammunition), Key: weapon.Ammunition),
        ];
        return at is Button ? Menus.ShowBelow(at, items) : Menus.Show(at, items);
    }

    /// <summary>
    /// "Copy for This Unit": in one step, a copy of the weapon that only this unit carries (with the units sharing its
    /// loadout, which carry the same weapons), as Copy... on the Weapons sub-tab makes for the units ticked. The other
    /// units keep the original. None for a unit with no loadout, or a weapon that cannot be copied here.
    /// </summary>
    private MenuChoice? CopyForUnitChoice(Unit unit, UnitWeapon carried)
    {
        if (unit.WeaponSet is not string loadout || !CanChangeWeapons()) return null;
        if (loaded?.Catalog.Weapons?.FirstOrDefault(each => each.Descriptor == carried.Ammunition) is not Weapon weapon) return null;
        IReadOnlyList<Unit> sharing = UnitsSharingLoadout(unit);
        string tip = sharing.Count == 0
            ? Texts.Get("Units.Menu.CopyForUnit.Tip")
            : Texts.Get("Units.Menu.CopyForUnit.Shared.Tip", ("units", string.Join(", ", sharing.Select(other => other.Name))));
        return new MenuChoice(Texts.Get("Units.Menu.CopyForUnit"),
            () => CopyWeapon(weapon, Texts.Get("Weapons.Copy.DefaultName", ("weapon", weapon.Name)), [loadout]), Tip: tip);
    }

    /// <summary>
    /// "Show … in File Editor", with the file's name at the right. Dim while the file is not on the Files tab, with a
    /// tooltip saying to add it.
    /// </summary>
    private MenuChoice ShowInEditorChoice(string label, string descriptor, string file)
    {
        FileEntry? entry = LoadedFile(file);
        return new MenuChoice(label, () => ShowInEditor(entry, descriptor), IsEnabled: entry is not null, Key: file,
            Tip: entry is null ? Texts.Get("Units.Menu.NotLoaded.Tip", ("file", file)) : descriptor);
    }

    /// <summary>A file on the Files tab with this name that has been read; null when there is none.</summary>
    private FileEntry? LoadedFile(string file) =>
        MainView.Find(this)?.FilesTab.Model.Entries.FirstOrDefault(entry => entry.Source is not null && string.Equals(entry.Name, file, StringComparison.OrdinalIgnoreCase));

    private void ShowInEditor(FileEntry? entry, string descriptor)
    {
        if (entry is not null) _ = MainView.Of(this).EditorTab.ShowDescriptor(entry, descriptor);
    }

    /// <inheritdoc/>
    public bool CanShowInEditor(Unit unit) => LoadedFile(unit.File) is not null;

    /// <inheritdoc/>
    public void ShowInEditor(Unit unit) => ShowInEditor(LoadedFile(unit.File), unit.Descriptor);

    /// <inheritdoc/>
    public void NewRuleFor(Unit unit) => NewRule(unit.Name, [unit.Descriptor]);

    /// <inheritdoc/>
    public void Compare(Unit unit) => AddToCompare([unit]);

    /// <summary>"New Rule for the Units Shown…": one rule matching every unit the filters leave.</summary>
    private Task NewRuleForShown() => NewRuleForUnits(Texts.Get("Units.Rule.UnitsShown"), shown);

    /// <summary>
    /// One new rule matching some units. A rule of many conditions is slow to open and to read, so past a hundred units
    /// the user is asked first.
    /// </summary>
    private async Task NewRuleForUnits(string name, IReadOnlyList<Unit> units)
    {
        if (units.Count > 100 && !await MainView.Of(this).Messages.Confirm(Texts.Get("Units.Rule.ManyUnits", ("count", Texts.Number(units.Count))), Texts.Get("Units.Rule.MakeIt")))
            return;
        NewRule(name, [.. units.Select(unit => unit.Descriptor)]);
    }

    /// <summary>A new Edit rule on the Rules tab for some descriptors, opened to choose what it changes.</summary>
    internal void NewRule(string name, IReadOnlyList<string> descriptors) => MainView.Of(this).RulesTab.AddRuleFor(name, descriptors);

    internal static void Copy(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { } // the clipboard is busy with another program
    }
}
