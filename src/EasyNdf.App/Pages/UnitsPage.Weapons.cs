using EasyNdf.App.Dialogs;
using EasyNdf.App.Text;
using EasyNdf.App.Weapons;
using EasyNdf.Data;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Armory's Weapons sub-tab (<see cref="WeaponsArmory"/>): it shows the weapons of the units this tab has read, as
/// the open mod makes them, and asks this tab to show a weapon in the File Editor, to make a rule for it, to show a
/// unit that carries it, or to change it. A change to one of the source's weapons is a change of the mod's
/// (<see cref="WeaponEdit"/>), which only keeps what differs from the weapon as the game has it; a copy
/// (<see cref="ModWeapon"/>) takes its values itself. Each goes through <see cref="ChangeDivisions"/>, as every Armory
/// change does: refused when a file it is written into is not on the Files tab, otherwise saved in the mod as locked
/// rules in its Weapons preset, with Undo.
/// </summary>
public partial class UnitsPage : IWeaponActions
{
    /// <summary>The weapons of the units shown, in the Weapons sub-tab; nothing before a source is read.</summary>
    private void ShowWeapons()
    {
        if (loaded is null) return;
        WeaponsView.Show(loaded.Catalog, loaded.Source.Name, this, gameUnits?.VanillaWeapons ?? new HashSet<string>());
    }

    /// <inheritdoc/>
    public bool CanShowInEditor(Weapon weapon) => LoadedFile(weapon.File) is not null;

    /// <inheritdoc/>
    public void ShowInEditor(Weapon weapon) => ShowInEditor(LoadedFile(weapon.File), weapon.Descriptor);

    /// <inheritdoc/>
    public void NewRuleFor(Weapon weapon) => NewRule(weapon.Name, [weapon.Descriptor]);

    /// <inheritdoc/>
    public void ShowUnit(string descriptor)
    {
        if (loaded?.Catalog.Units.FirstOrDefault(unit => unit.Descriptor == descriptor) is Unit unit) ShowInUnits(unit);
    }

    // ---- changing weapons ----

    /// <summary>The source's weapons before the open mod's rules, which its weapon changes are made on.</summary>
    private IReadOnlyList<Weapon> GameWeapons => gameUnits?.Catalog.Weapons ?? [];

    /// <inheritdoc/>
    public bool CanChangeWeapon(Weapon weapon) =>
        CanChangeWeapons() && (IsCopy(weapon) || GameWeapons.Any(each => each.Descriptor == weapon.Descriptor));

    /// <inheritdoc/>
    public bool IsCopy(Weapon weapon) => OpenMod.Weapons.Any(copy => copy.Descriptor == weapon.Descriptor);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, int>? DamageLevels => gameUnits?.Catalog.ArmorLevels;

    /// <inheritdoc/>
    public bool ChangeWeapon(Weapon weapon, string field, string typed)
    {
        string descriptor = weapon.Descriptor;
        if (field == WeaponField.Name)
        {
            string name = typed.Trim();
            if (name == "" || !IsCopy(weapon)) return false;
            return ChangeDivisions(mod =>
            {
                if (CopyIn(mod, descriptor) is ModWeapon copy) copy.Name = name;
            });
        }
        if (WeaponChoice.Of(field) is WeaponChoice choice) return Choose(weapon, choice, typed);
        if (WeaponStat.Of(field) is not WeaponStat stat || stat.ValueOf(typed, weapon, DamageLevels, Texts.Catalog.Culture) is not double value) return false;
        if (IsCopy(weapon))
        {
            // a copy's value back to its original's is let go of, so only what differs is written
            double? original = OriginalOf(weapon) is Weapon from ? stat.Read(from) : null;
            return ChangeDivisions(mod => CopyIn(mod, descriptor)?.Give(field, value == original ? null : value));
        }
        double? before = WeaponBeforeChanges(descriptor) is Weapon beforeChanges ? stat.Read(beforeChanges) : null;
        return ChangeDivisions(mod =>
        {
            WeaponEdit.For(mod, descriptor).Give(field, value == before ? null : value);
            WeaponEdit.LetGoOfIfEmpty(mod, descriptor);
        });
    }

    /// <summary>
    /// Gives a weapon a switch or choice, as the file writes it: one of those offered only, and only where its
    /// ammunition has the value, which a rule can change. One set back to the weapon's own is let go of.
    /// </summary>
    private bool Choose(Weapon weapon, WeaponChoice choice, string written)
    {
        if (choice.Read(weapon) is null || !ChoicesFor(weapon, choice.Field).Contains(written)) return false;
        List<string> cannotDeploy = CarriersThatCannotDeploy(weapon, choice, written);
        if (cannotDeploy.Count > 0 && !riskyChoicesAllowed)
        {
            // the pane's switch goes back; it is set again if the user goes on
            _ = ChooseAfterWarning(weapon, choice, written, cannotDeploy);
            return false;
        }
        return ChooseNow(weapon, choice, written);
    }

    /// <summary>"Don't ask again during this session" was ticked in the warning before a risky choice: it is not shown again until the app restarts.</summary>
    private bool riskyChoicesAllowed;

    /// <summary>
    /// The units that carry a weapon and would have to deploy to fire it if Must deploy were switched on, but carry no
    /// other weapon that must deploy: the Swap window refuses such a weapon, and the game may not take it (tanks that
    /// stop to deploy their main gun). Names, in the order the units are listed; empty for any other choice.
    /// </summary>
    private List<string> CarriersThatCannotDeploy(Weapon weapon, WeaponChoice choice, string written)
    {
        if (choice.Field != WeaponFileValues.HasDeploymentTime || written != WeaponChoice.On || weapon.MustDeploy || loaded is null) return [];
        HashSet<string> deployingWeapons = [.. (loaded.Catalog.Weapons ?? []).Where(each => each.MustDeploy).Select(each => each.Descriptor)];
        var carriers = new HashSet<string>(weapon.Users, StringComparer.Ordinal);
        return [.. loaded.Catalog.Units
            .Where(unit => carriers.Contains(unit.Descriptor))
            .Where(unit => !unit.Weapons.Any(each => each.Ammunition != weapon.Descriptor && deployingWeapons.Contains(each.Ammunition)))
            .Select(unit => unit.Name)];
    }

    /// <summary>
    /// Warns that a choice may not work in the game, naming the units it may not work on, and makes it only when the
    /// user says they know what they are doing. Nothing is ever refused here: the user decides.
    /// </summary>
    private async Task ChooseAfterWarning(Weapon weapon, WeaponChoice choice, string written, List<string> cannotDeploy)
    {
        const int Named = 8;
        string units = string.Join(", ", cannotDeploy.Take(Named));
        if (cannotDeploy.Count > Named) units += " " + Texts.Get("Weapons.Risky.More", ("count", cannotDeploy.Count - Named));
        DialogAnswer answer = await MainView.Of(this).Messages.Ask(
            Texts.Get("Weapons.Risky.MustDeploy", ("weapon", weapon.Name), ("count", cannotDeploy.Count), ("units", units)),
            new DialogOptions { Ok = Texts.Get("Weapons.Risky.GoOn"), CheckText = Texts.Get("Weapons.Risky.HideForSession") });
        if (answer.Choice != DialogChoice.Ok) return;
        if (answer.IsChecked) riskyChoicesAllowed = true;
        ChooseNow(weapon, choice, written);
    }

    /// <summary>Makes a choice of the Weapon pane, after any warning: in the mod's copy, or as a change of the source's weapon.</summary>
    private bool ChooseNow(Weapon weapon, WeaponChoice choice, string written)
    {
        string descriptor = weapon.Descriptor;
        if (IsCopy(weapon))
        {
            string? original = OriginalOf(weapon) is Weapon from ? choice.Read(from) : null;
            return ChangeDivisions(mod => CopyIn(mod, descriptor)?.Choose(choice.Field, written == original ? null : written));
        }
        string? before = WeaponBeforeChanges(descriptor) is Weapon beforeChanges ? choice.Read(beforeChanges) : null;
        return ChangeDivisions(mod =>
        {
            WeaponEdit.For(mod, descriptor).Choose(choice.Field, written == before ? null : written);
            WeaponEdit.LetGoOfIfEmpty(mod, descriptor);
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ChoicesFor(Weapon weapon, string field) =>
        WeaponChoice.Of(field)?.Offered(weapon, GameWeapons, DamageLevels) ?? [];

    /// <inheritdoc/>
    public string OpenModName => OpenMod.Name;

    /// <inheritdoc/>
    public void NewWeapon()
    {
        if (!CanChangeWeapons())
        {
            _ = MainView.Of(this).Messages.Tell(Texts.Get("Weapons.New.NotHere"));
            return;
        }
        Dictionary<string, Weapon> weapons = ShownWeapons();
        List<SwapChoice> choices = [.. weapons.Values.Select(weapon => new SwapChoice(weapon, LoadoutRules.Fit.Fits, ""))];
        MainView.Of(this).SwapWeapon.Open(Texts.Get("Weapons.New.Title"), Texts.Get("Weapons.New.Where"), null, choices, IsCopy, offerCopy: false,
            (weapon, _) => CopyWeapon(weapon, Texts.Get("Weapons.Copy.DefaultName", ("weapon", weapon.Name)), []), SwapKind.New);
    }

    /// <summary>Whether the source's weapons can be changed and copied here: WARNO's only, for now.</summary>
    private bool CanChangeWeapons() => Game == Data.Game.GameTitle.Warno;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> WeaponPictures => gameUnits?.Catalog.WeaponPictures ?? new Dictionary<string, string>();

    /// <inheritdoc/>
    public bool ChangePicture(Weapon weapon, string? texture)
    {
        string descriptor = weapon.Descriptor;
        if (IsCopy(weapon))
        {
            // a copy given its original's picture again keeps its original's
            string? original = OriginalOf(weapon)?.PictureTexture;
            return ChangeDivisions(mod =>
            {
                if (CopyIn(mod, descriptor) is ModWeapon copy) copy.Picture = texture == original ? null : texture;
            });
        }
        string? before = WeaponBeforeChanges(descriptor)?.PictureTexture;
        return ChangeDivisions(mod =>
        {
            WeaponEdit.For(mod, descriptor).Picture = texture == before ? null : texture;
            WeaponEdit.LetGoOfIfEmpty(mod, descriptor);
        });
    }

    /// <inheritdoc/>
    public bool ChangeTraits(Weapon weapon, IReadOnlyList<string> traits)
    {
        string descriptor = weapon.Descriptor;
        if (IsCopy(weapon))
        {
            // a copy given its original's traits again keeps its original's
            IReadOnlyList<string>? original = OriginalOf(weapon)?.Traits;
            return ChangeDivisions(mod =>
            {
                if (CopyIn(mod, descriptor) is ModWeapon copy) copy.Traits = SameTraits(traits, original) ? null : traits;
            });
        }
        IReadOnlyList<string>? before = WeaponBeforeChanges(descriptor)?.Traits;
        return ChangeDivisions(mod =>
        {
            WeaponEdit.For(mod, descriptor).Traits = SameTraits(traits, before) ? null : traits;
            WeaponEdit.LetGoOfIfEmpty(mod, descriptor);
        });
    }

    /// <summary>Whether two lists of traits are the same, in the same order.</summary>
    private static bool SameTraits(IReadOnlyList<string> traits, IReadOnlyList<string>? others) =>
        others is not null && traits.SequenceEqual(others, StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool HasOtherPicture(Weapon weapon) =>
        (CopyIn(OpenMod, weapon.Descriptor)?.Picture ?? WeaponEdit.Of(OpenMod, weapon.Descriptor)?.Picture) is not null;

    /// <summary>A weapon as the mod's rules make it, without its Armory changes: what a value typed back to is let go of against.</summary>
    private Weapon? WeaponBeforeChanges(string descriptor) =>
        unitsByRules?.Catalog.Weapons?.FirstOrDefault(each => each.Descriptor == descriptor) ?? GameWeapons.FirstOrDefault(each => each.Descriptor == descriptor);

    /// <summary>A copy of the mod given, by its descriptor: changes are made on the mod they are given, a trial copy first.</summary>
    private static ModWeapon? CopyIn(Mod mod, string descriptor) => mod.Weapons.FirstOrDefault(copy => copy.Descriptor == descriptor);

    /// <inheritdoc/>
    public void CopyWeapon(Weapon weapon, string name, IReadOnlyList<string> loadouts)
    {
        IEnumerable<string> taken = (loaded?.Catalog.Weapons ?? []).Select(each => each.Descriptor).Concat(OpenMod.Weapons.Select(copy => copy.Descriptor));
        string descriptor = ModWeapon.NewDescriptor(name, [.. taken]);
        bool made = ChangeDivisions(mod =>
        {
            var copy = new ModWeapon { Descriptor = descriptor, MadeFrom = weapon.Descriptor, File = weapon.File, Name = name };
            mod.Weapons.Add(copy);
            ModWeapon.GiveTo(mod, copy, loadouts);
        });
        if (made) WeaponsView.SelectWhenShown(descriptor);
    }

    /// <inheritdoc/>
    public IReadOnlyList<Weapon> CopiesOf(Weapon weapon)
    {
        var descriptors = OpenMod.Weapons.Where(copy => copy.MadeFrom == weapon.Descriptor).Select(copy => copy.Descriptor).ToHashSet(StringComparer.Ordinal);
        return [.. (loaded?.Catalog.Weapons ?? []).Where(each => descriptors.Contains(each.Descriptor))];
    }

    /// <inheritdoc/>
    public Weapon? OriginalOf(Weapon weapon) =>
        CopyIn(OpenMod, weapon.Descriptor) is ModWeapon copy ? loaded?.Catalog.Weapons?.FirstOrDefault(each => each.Descriptor == copy.MadeFrom) : null;

    /// <inheritdoc/>
    public void GiveCopy(Weapon to, Weapon from, IReadOnlyList<string> loadouts)
    {
        if (loadouts.Count == 0) return;
        string toDescriptor = to.Descriptor, fromDescriptor = from.Descriptor;
        ChangeDivisions(mod =>
        {
            // back to the weapon a copy was made from: the copy lets go of the loadouts
            if (CopyIn(mod, fromDescriptor) is ModWeapon fromCopy && fromCopy.MadeFrom == toDescriptor) ModWeapon.TakeBack(mod, fromCopy, loadouts);
            else if (CopyIn(mod, toDescriptor) is ModWeapon toCopy) ModWeapon.GiveTo(mod, toCopy, loadouts);
        });
    }

    /// <inheritdoc/>
    public async Task DeleteWeapon(Weapon weapon)
    {
        string descriptor = weapon.Descriptor;
        if (IsCopy(weapon))
        {
            if (!await MainView.Of(this).Messages.Confirm(Texts.Get("Weapons.Delete.AskCopy", ("weapon", weapon.Name)), Texts.Get("Units.Delete.Ok"))) return;
            ChangeDivisions(mod => ModWeapon.RemoveFrom(mod, descriptor));
            return;
        }
        if (WeaponEdit.Of(OpenMod, descriptor) is not null)
        {
            if (!await MainView.Of(this).Messages.Confirm(Texts.Get("Weapons.Delete.AskChanged", ("weapon", weapon.Name)), Texts.Get("Units.Delete.Ok"))) return;
            ChangeDivisions(mod => mod.WeaponEdits.RemoveAll(edit => edit.Descriptor == descriptor));
            return;
        }
        await MainView.Of(this).Messages.Tell(Texts.Get("Weapons.Delete.GameWeapon", ("weapon", weapon.Name)));
    }
}
