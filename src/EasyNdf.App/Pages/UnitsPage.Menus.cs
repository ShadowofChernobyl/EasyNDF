using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// What the Units tab's buttons and right-click menus do: show a unit, its weapon set or a weapon's ammunition in the
/// File Editor, make a rule for it, compare it, pin it, copy its name.
/// </summary>
public partial class UnitsPage : IUnitActions
{
    /// <summary>
    /// A unit's menu: show it or its weapon set in the File Editor; on Compare, pin it; compare it; new rules for it or
    /// for the units shown; copy its name; add it to a division.
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
            Menus.Line,
            .. MainView.Of(this).DivisionsTab.AddToDivisionChoices(unit),
        ];
        return at is Button ? Menus.ShowBelow(at, items) : Menus.Show(at, items);
    }

    /// <summary>"Show Unit in File Editor" and, when the unit has one, "Show Weapon Set in File Editor".</summary>
    internal MenuChoice?[] ShowInEditorChoices(Unit unit) =>
    [
        ShowInEditorChoice(Texts.Get("Units.Menu.ShowUnit"), unit.Descriptor, UnitFiles.Units),
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
            ShowInEditorChoice(Texts.Get("Units.Menu.ShowAmmunition"), weapon.Ammunition, weapon.AmmunitionFile ?? UnitFiles.Ammunition),
            weapon.Missile is null ? null : ShowInEditorChoice(Texts.Get("Units.Menu.ShowMissile"), weapon.Missile, UnitFiles.Missiles),
            unit.WeaponSet is null ? null : ShowInEditorChoice(Texts.Get("Units.Menu.ShowWeaponSet"), unit.WeaponSet, UnitFiles.WeaponSets),
            ShowInEditorChoice(Texts.Get("Units.Menu.ShowUnit"), unit.Descriptor, UnitFiles.Units),
            Menus.Line,
            new MenuChoice(Texts.Get("Units.Menu.NewRuleForAmmunition"), () => NewRule(weapon.Name, [weapon.Ammunition])),
            new MenuChoice(Texts.Get("Units.Menu.CopyName"), () => Copy(weapon.Ammunition), Key: weapon.Ammunition),
        ];
        return at is Button ? Menus.ShowBelow(at, items) : Menus.Show(at, items);
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
    public bool CanShowInEditor(Unit unit) => LoadedFile(UnitFiles.Units) is not null;

    /// <inheritdoc/>
    public void ShowInEditor(Unit unit) => ShowInEditor(LoadedFile(UnitFiles.Units), unit.Descriptor);

    /// <inheritdoc/>
    public void NewRuleFor(Unit unit) => NewRule(unit.Name, [unit.Descriptor]);

    /// <inheritdoc/>
    public void Compare(Unit unit) => AddToCompare([unit]);

    /// <summary>
    /// "New Rule for the Units Shown…": one rule matching every unit the filters leave. A rule of many conditions is
    /// slow to open and to read, so past a hundred units the user is asked first.
    /// </summary>
    private async Task NewRuleForShown()
    {
        if (shown.Count > 100 && !await MainView.Of(this).Messages.Confirm(Texts.Get("Units.Rule.ManyUnits", ("count", Texts.Number(shown.Count))), Texts.Get("Units.Rule.MakeIt")))
            return;
        NewRule(Texts.Get("Units.Rule.UnitsShown"), [.. shown.Select(unit => unit.Descriptor)]);
    }

    /// <summary>A new Edit rule on the Rules tab for some descriptors, opened to choose what it changes.</summary>
    internal void NewRule(string name, IReadOnlyList<string> descriptors) => MainView.Of(this).RulesTab.AddRuleFor(name, descriptors);

    internal static void Copy(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { } // the clipboard is busy with another program
    }
}
