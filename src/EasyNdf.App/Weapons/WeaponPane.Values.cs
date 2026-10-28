using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// The Weapon pane's values, as the mock (A) chosen: every value of the weapon's ammunition, in folding sections of two
/// columns. A value's name has a tooltip saying what it does in the game, its name in the file, and how sure that is;
/// a value from its name and the game's data only is marked ≈, one whose effect is not known ? (shown only, until tried
/// in the game). Numbers change in a box, switches with a switch, choices from a list of the values the game's weapons
/// have; references to other descriptors are shown only. A value the ammunition does not have shows "–": a rule
/// cannot set it.
/// </summary>
public static partial class WeaponPane
{
    /// <summary>How sure the pane is of what a value does.</summary>
    private enum Certainty
    {
        /// <summary>Documented, or certain from the game's data.</summary>
        Known,

        /// <summary>From its name and how the game uses it; not documented. Marked ≈.</summary>
        Likely,

        /// <summary>Not known: shown only, until tried in the game. Marked ?.</summary>
        Unknown,
    }

    /// <summary>What kind of value it is, which says how it is changed.</summary>
    private enum Shape
    {
        /// <summary>A number, changed in a box (<see cref="WeaponStat"/>).</summary>
        Number,

        /// <summary>True or False, changed with a switch (<see cref="WeaponChoice"/>).</summary>
        Switch,

        /// <summary>One of the values the game's weapons have, chosen from a list (<see cref="WeaponChoice"/>).</summary>
        Choice,

        /// <summary>Shown only: a reference to another descriptor, or a text.</summary>
        ShownOnly,
    }

    /// <summary>
    /// A value of the pane: its name's text key, its tooltip's text key, its field (<see cref="WeaponField"/>, or the
    /// property's name), its name in the file, how sure the pane is of it, its kind, and how it is written.
    /// </summary>
    private sealed record PaneValue(string NameKey, string AboutKey, string Field, string Property, Certainty Certainty, Shape Shape, Func<Weapon, string> Write);

    /// <summary>A folding section of values: its key, its title's text key, whether it is folded at first, and its values.</summary>
    private sealed record ValueSection(string Key, string TitleKey, bool FoldedAtFirst, PaneValue[] Values);

    /// <summary>The sections of values, in the pane's order; Used By and Files follow them.</summary>
    private static readonly ValueSection[] ValueSections =
    [
        new("damage", "Weapons.Pane.Damage", false,
        [
            Choice("Weapons.Pane.DamageType", "Weapons.About.DamageType", WeaponFileValues.DamageFamily, Certainty.Known),
            Typed("Weapons.Pane.Level", "Weapons.About.Level", WeaponField.DamageLevel, Certainty.Known, each => each.DamageLevel > 0 ? Texts.Number(each.DamageLevel) : UnitNumbers.None),
            Typed("Weapons.Pane.Damage", "Weapons.About.Damage", WeaponField.Damage, Certainty.Known, each => WeaponNames.Number(each.Damage)),
            Typed("Weapons.Pane.Suppression", "Weapons.About.Suppression", WeaponField.Suppression, Certainty.Known, each => WeaponNames.Number(each.Suppression)),
            Typed("Weapons.Pane.DamageRadius", "Weapons.About.DamageRadius", WeaponField.DamageRadius, Certainty.Known, each => WeaponNames.Number(each.DamageRadius)),
            Typed("Weapons.Pane.SuppressionRadius", "Weapons.About.SuppressionRadius", WeaponField.SuppressionRadius, Certainty.Known, each => WeaponNames.Number(each.SuppressionRadius)),
            Switch("Weapons.Pane.SuppressesOnMiss", "Weapons.About.SuppressesOnMiss", "AllowSuppressDamageWhenNoImpact", Certainty.Known),
            Switch("Weapons.Pane.Piercing", "Weapons.About.Piercing", "PiercingWeapon", Certainty.Known),
            Reference("Weapons.Pane.WeakerWithRange", "Weapons.About.WeakerWithRange", "DamageTypeEvolutionOverRangeDescriptor", Certainty.Known),
            Switch("Weapons.Pane.Tandem", "Weapons.About.Tandem", "TandemCharge", Certainty.Likely),
            Switch("Weapons.Pane.HarmlessToAllies", "Weapons.About.HarmlessToAllies", "IsHarmlessForAllies", Certainty.Likely),
            FileNumber("Weapons.Pane.FireChance", "Weapons.About.FireChance", "FireTriggeringProbability", Certainty.Likely, value => WeaponNames.Number(value, 3)),
            Reference("Weapons.Pane.Fire", "Weapons.About.Fire", "FireDescriptor", Certainty.Known),
            Reference("Weapons.Pane.Smoke", "Weapons.About.Smoke", "SmokeDescriptor", Certainty.Known),
            Switch("Weapons.Pane.TopArmor", "Weapons.About.TopArmor", "ForceHitTopArmor", Certainty.Likely),
            Switch("Weapons.Pane.TopArmorOnHit", "Weapons.About.TopArmorOnHit", "ForceHitTopArmorOnSuccess", Certainty.Likely),
            Switch("Weapons.Pane.ArmorFromImpact", "Weapons.About.ArmorFromImpact", "ComputeArmorFromImpactLocation", Certainty.Likely),
            Switch("Weapons.Pane.IgnoresFlammability", "Weapons.About.IgnoresFlammability", "IgnoreInflammabilityConditions", Certainty.Unknown),
        ]),
        new("range", "Weapons.Pane.Range", false,
        [
            Typed("Weapons.Pane.Ground", "Weapons.About.Ground", WeaponField.Range, Certainty.Known, each => WeaponNames.Range(each.Range)),
            Typed("Weapons.Pane.Minimum", "Weapons.About.Minimum", WeaponField.MinimumRange, Certainty.Known, each => WeaponNames.Range(each.MinimumRange)),
            Typed("Weapons.Pane.Helicopters", "Weapons.About.Helicopters", WeaponField.HelicopterRange, Certainty.Known, each => WeaponNames.Range(each.HelicopterRange)),
            FileNumber("Weapons.Pane.HelicoptersMinimum", "Weapons.About.HelicoptersMinimum", "MinimumRangeHelicopterGRU", Certainty.Known, WeaponNames.Range),
            Typed("Weapons.Pane.Planes", "Weapons.About.Planes", WeaponField.PlaneRange, Certainty.Known, each => WeaponNames.Range(each.PlaneRange)),
            FileNumber("Weapons.Pane.PlanesMinimum", "Weapons.About.PlanesMinimum", "MinimumRangeAirplaneGRU", Certainty.Known, WeaponNames.Range),
            FileNumber("Weapons.Pane.Missiles", "Weapons.About.Missiles", "MaximumRangeProjectileGRU", Certainty.Likely, WeaponNames.Range),
            FileNumber("Weapons.Pane.MissilesMinimum", "Weapons.About.MissilesMinimum", "MinimumRangeProjectileGRU", Certainty.Likely, WeaponNames.Range),
        ]),
        new("accuracy", "Weapons.Pane.Accuracy", false,
        [
            Typed("Weapons.Pane.Stopped", "Weapons.About.Stopped", WeaponField.AccuracyStopped, Certainty.Known, each => WeaponNames.Percent(each.AccuracyStopped)),
            Typed("Weapons.Pane.Moving", "Weapons.About.Moving", WeaponField.AccuracyMoving, Certainty.Known, each => WeaponNames.Percent(each.AccuracyMoving)),
            Switch("Weapons.Pane.FallsWithDistance", "Weapons.About.FallsWithDistance", "DistanceToTarget", Certainty.Known),
            FileNumber("Weapons.Pane.Critical", "Weapons.About.Critical", "BaseCriticModifier", Certainty.Known, value => WeaponNames.Number(value)),
            Typed("Weapons.Pane.DispersionFar", "Weapons.About.DispersionFar", WeaponField.DispersionFar, Certainty.Known, each => WeaponNames.Number(each.DispersionFar)),
            Typed("Weapons.Pane.DispersionNear", "Weapons.About.DispersionNear", WeaponField.DispersionNear, Certainty.Known, each => WeaponNames.Number(each.DispersionNear)),
            FileNumber("Weapons.Pane.SpreadAngle", "Weapons.About.SpreadAngle", "AngleDispersion", Certainty.Likely, value => WeaponNames.Number(value, 3)),
            FileNumber("Weapons.Pane.CorrectedSpread", "Weapons.About.CorrectedSpread", "CorrectedShotDispersionMultiplier", Certainty.Likely, value => WeaponNames.Number(value)),
            FileNumber("Weapons.Pane.SuccessiveHits", "Weapons.About.SuccessiveHits", "MaxSuccessiveHitCount", Certainty.Unknown, value => WeaponNames.Number(value)),
            FileNumber("Weapons.Pane.AccuracyBase", "Weapons.About.AccuracyBase", WeaponFileValues.AccuracyBase, Certainty.Unknown, value => WeaponNames.Number(value)),
            FileNumber("Weapons.Pane.AccuracyTargeted", "Weapons.About.AccuracyTargeted", WeaponFileValues.AccuracyTargeted, Certainty.Unknown, value => WeaponNames.Number(value)),
        ]),
        new("firing", "Weapons.Pane.Firing", false,
        [
            Typed("Weapons.Pane.Aiming", "Weapons.About.Aiming", WeaponField.AimingTime, Certainty.Known, each => WeaponNames.Seconds(each.AimingTime)),
            Typed("Weapons.Pane.PerSalvo", "Weapons.About.PerSalvo", WeaponField.ShotsPerSalvo, Certainty.Known, each => WeaponNames.Number(each.ShotsPerSalvo)),
            FileNumber("Weapons.Pane.AtOnce", "Weapons.About.AtOnce", WeaponFileValues.SimultaneousShots, Certainty.Known, value => WeaponNames.Number(value)),
            FileNumber("Weapons.Pane.BetweenShots", "Weapons.About.BetweenShots", "TimeBetweenTwoShots", Certainty.Known, WeaponNames.Seconds),
            Typed("Weapons.Pane.Reload", "Weapons.About.Reload", WeaponField.Reload, Certainty.Known, each => WeaponNames.Seconds(each.Reload)),
            FileNumber("Weapons.Pane.ReloadAtLeast", "Weapons.About.ReloadAtLeast", "TimeBetweenTwoSalvos_Min", Certainty.Likely, SecondsOrOff),
            FileNumber("Weapons.Pane.ReloadAtMost", "Weapons.About.ReloadAtMost", "TimeBetweenTwoSalvos_Max", Certainty.Likely, SecondsOrOff),
            Typed("Weapons.Pane.Supply", "Weapons.About.Supply", WeaponField.SupplyCost, Certainty.Likely, each => WeaponNames.Number(each.SupplyCost)),
            FileNumber("Weapons.Pane.ShownPerSalvo", "Weapons.About.ShownPerSalvo", "AffichageMunitionParSalve", Certainty.Likely, value => WeaponNames.Number(value)),
            FileNumber("Weapons.Pane.EffectsBetween", "Weapons.About.EffectsBetween", "TimeBetweenTwoFx", Certainty.Likely, WeaponNames.Seconds),
        ]),
        new("use", "Weapons.Pane.Use", false,
        [
            Switch("Weapons.Pane.FiresMoving", "Weapons.About.FiresMoving", "CanShootWhileMoving", Certainty.Likely),
            Switch("Weapons.Pane.MustDeploy", "Weapons.About.MustDeploy", WeaponFileValues.HasDeploymentTime, Certainty.Likely),
            Switch("Weapons.Pane.FirePosition", "Weapons.About.FirePosition", "CanShootOnPosition", Certainty.Known),
            FileNumber("Weapons.Pane.SalvosAtPosition", "Weapons.About.SalvosAtPosition", "NbSalvosShootOnPosition", Certainty.Likely, value => WeaponNames.Number(value)),
            Switch("Weapons.Pane.Indirect", "Weapons.About.Indirect", "TirIndirect", Certainty.Known),
            Switch("Weapons.Pane.Reflex", "Weapons.About.Reflex", "TirReflexe", Certainty.Unknown),
            Switch("Weapons.Pane.NoReflexOnPriority", "Weapons.About.NoReflexOnPriority", "ReflexShotDisabledIfPriorityTarget", Certainty.Unknown),
            Switch("Weapons.Pane.OnePerDistrict", "Weapons.About.OnePerDistrict", "TargetOnlyOneUnitInDistrict", Certainty.Unknown),
        ]),
        new("projectile", "Weapons.Pane.Projectile", false,
        [
            Choice("Weapons.Pane.ProjectileKind", "Weapons.About.ProjectileKind", "ProjectileType", Certainty.Likely),
            FileNumber("Weapons.Pane.Speed", "Weapons.About.Speed", "ProjectileSpeedGRU", Certainty.Likely, value => WeaponNames.Number(value, 0)),
            FileNumber("Weapons.Pane.Acceleration", "Weapons.About.Acceleration", "MaxAccelerationGRU", Certainty.Likely, value => WeaponNames.Number(value, 0)),
            FileNumber("Weapons.Pane.Arc", "Weapons.About.Arc", WeaponFileValues.Arc, Certainty.Likely, Degrees),
            FileNumber("Weapons.Pane.Corrections", "Weapons.About.Corrections", "MissileTimeBetweenCorrections", Certainty.Likely, WeaponNames.Seconds),
            Switch("Weapons.Pane.FireAndForget", "Weapons.About.FireAndForget", "IsFireAndForget", Certainty.Likely),
            Choice("Weapons.Pane.Guidance", "Weapons.About.Guidance", "Guidance", Certainty.Likely),
            Switch("Weapons.Pane.Submunitions", "Weapons.About.Submunitions", "IsSubAmmunition", Certainty.Likely),
            Reference("Weapons.Pane.Missile", "Weapons.About.Missile", "MissileDescriptor", Certainty.Known),
            Switch("Weapons.Pane.ShotsInOrder", "Weapons.About.ShotsInOrder", "SalvoShotsSorted", Certainty.Unknown),
            Switch("Weapons.Pane.AffectedByNumber", "Weapons.About.AffectedByNumber", "AffecteParNombre", Certainty.Unknown),
        ]),
        new("detection", "Weapons.Pane.Detection", true,
        [
            FileNumber("Weapons.Pane.Noise", "Weapons.About.Noise", "NoiseDissimulationMalus", Certainty.Likely, value => WeaponNames.Number(value)),
            FileNumber("Weapons.Pane.ShotsToNoise", "Weapons.About.ShotsToNoise", "ShotsBeforeMaxNoise", Certainty.Likely, value => WeaponNames.Number(value)),
        ]),
        new("display", "Weapons.Pane.Display", true,
        [
            new("Weapons.Pane.Picture", "Weapons.About.Picture", WeaponField.Picture, "InterfaceWeaponTexture", Certainty.Known, Shape.ShownOnly, PictureName),
            new("Weapons.Pane.Traits", "Weapons.About.Traits", "", "TraitsToken", Certainty.Known, Shape.ShownOnly, TraitNames),
            Choice("Weapons.Pane.ComparedWith", "Weapons.About.ComparedWith", "MinMaxCategory", Certainty.Likely),
            Switch("Weapons.Pane.ShowsDamage", "Weapons.About.ShowsDamage", "ShowDamageInUI", Certainty.Likely),
            Switch("Weapons.Pane.ShowsSalvoAccuracy", "Weapons.About.ShowsSalvoAccuracy", "DisplaySalveAccuracy", Certainty.Likely),
            Choice("Weapons.Pane.EffectSize", "Weapons.About.EffectSize", "FxPower", Certainty.Likely),
            Choice("Weapons.Pane.ImpactEffect", "Weapons.About.ImpactEffect", "ImpactHappening", Certainty.Likely),
            Choice("Weapons.Pane.Cursor", "Weapons.About.Cursor", "WeaponCursorType", Certainty.Likely),
        ]),
    ];

    // ---- the kinds of values ----

    /// <summary>A number the weapon has a property of its own for, changed in a box.</summary>
    private static PaneValue Typed(string nameKey, string aboutKey, string field, Certainty certainty, Func<Weapon, string> write) =>
        new(nameKey, aboutKey, field, PropertyOf(field), certainty, Shape.Number, write);

    /// <summary>A number kept as the file writes it (<see cref="Weapon.FileValues"/>), changed in a box when known or likely.</summary>
    private static PaneValue FileNumber(string nameKey, string aboutKey, string name, Certainty certainty, Func<double?, string> write) =>
        new(nameKey, aboutKey, name, name, certainty, Shape.Number, weapon => write(weapon.FileNumber(name)));

    /// <summary>A switch, True or False, changed with a switch when known or likely.</summary>
    private static PaneValue Switch(string nameKey, string aboutKey, string name, Certainty certainty) =>
        new(nameKey, aboutKey, name, name, certainty, Shape.Switch, weapon => SwitchName(weapon.FileValue(name)));

    /// <summary>A choice among the values the game's weapons have, chosen from a list.</summary>
    private static PaneValue Choice(string nameKey, string aboutKey, string name, Certainty certainty) =>
        new(nameKey, aboutKey, name, name, certainty, Shape.Choice, weapon => weapon.FileValue(name) is string written ? ChoiceName(name, written) : UnitNumbers.None);

    /// <summary>A reference to another descriptor, shown only: its name, without the path; "none" for nil.</summary>
    private static PaneValue Reference(string nameKey, string aboutKey, string name, Certainty certainty) =>
        new(nameKey, aboutKey, name, name, certainty, Shape.ShownOnly, weapon => ReferenceName(weapon.FileValue(name)));

    /// <summary>The property of the ammunition a value with a property of its own is in, as a rule names it.</summary>
    private static string PropertyOf(string field) => field == WeaponField.ShotsPerSalvo ? "ShotsCountPerSalvo" : WeaponStat.Of(field)?.Target ?? field;

    // ---- how values are written ----

    /// <summary>Yes or No; "–" when the ammunition does not have it.</summary>
    private static string SwitchName(string? written) => written switch
    {
        WeaponChoice.On => Texts.Get("Weapons.Value.Yes"),
        WeaponChoice.Off => Texts.Get("Weapons.Value.No"),
        null => UnitNumbers.None,
        _ => written,
    };

    /// <summary>Seconds, or "off" for the game's -1.</summary>
    private static string SecondsOrOff(double? value) => value < 0 ? Texts.Get("Weapons.Value.Off") : WeaponNames.Seconds(value);

    /// <summary>An angle kept in radians, in degrees: "35°".</summary>
    private static string Degrees(double? radians) =>
        radians is double angle ? Texts.Get("Weapons.Value.Degrees", ("value", UnitNumbers.Of(angle * 180 / Math.PI, 1))) : UnitNumbers.None;

    /// <summary>The picture's texture, without the part every weapon's has: "Texture_Interface_Weapon_agm114" is "agm114".</summary>
    private static string PictureName(Weapon weapon)
    {
        const string Prefix = "Texture_Interface_Weapon_";
        if (weapon.PictureTexture is not string texture) return UnitNumbers.None;
        return texture.StartsWith(Prefix, StringComparison.Ordinal) ? texture[Prefix.Length..] : texture;
    }

    /// <summary>The traits, by name: "Motion, Semi-auto, HEAT".</summary>
    private static string TraitNames(Weapon weapon) => weapon.Traits.Count == 0 ? UnitNumbers.None : TraitIcon.NamesOf(weapon);

    /// <summary>
    /// The names the pane gives the values of the choices the game writes in French or in code: "EProjectileType/Obus" is
    /// "Shell". Others are made readable (<see cref="Readable"/>).
    /// </summary>
    private static readonly Dictionary<string, string> ChoiceNameKeys = new(StringComparer.Ordinal)
    {
        ["EProjectileType/Obus"] = "Weapons.Value.Shell",
        ["EProjectileType/Artillerie"] = "Weapons.Value.ArtilleryShell",
        ["EProjectileType/GuidedMissile"] = "Weapons.Value.GuidedMissile",
        ["EProjectileType/Bombe"] = "Weapons.Value.Bomb",
        ["EProjectileType/Grenade"] = "Weapons.Value.Grenade",
        ["EProjectileType/Fake"] = "Weapons.Value.Fake",
        ["EFXPower/Moyen"] = "Weapons.Value.Medium",
        ["EFXPower/UltraLourd"] = "Weapons.Value.UltraHeavy",
        ["Guidance_Radar"] = "Weapons.Value.Radar",
        ["Guidance_AntiRadar"] = "Weapons.Value.AntiRadar",
    };

    /// <summary>A choice's value as the pane names it: the kind of damage in capitals ("AP MISSILE"), the others by name.</summary>
    private static string ChoiceName(string field, string written)
    {
        if (field == WeaponFileValues.DamageFamily) return WeaponChoice.FamilyName(written).Replace('_', ' ').ToUpperInvariant();
        return ChoiceNameKeys.TryGetValue(written, out string? key) ? Texts.Get(key) : Readable(written);
    }

    /// <summary>A reference's name: "$/GFX/Weapon/Descriptor_Fire_Incendie" is "Fire Incendie"; nil is "none".</summary>
    private static string ReferenceName(string? written)
    {
        if (written is null) return UnitNumbers.None;
        if (written == "nil") return Texts.Get("Weapons.Value.NoneSet");
        string name = written[(written.LastIndexOf('/') + 1)..];
        foreach (string prefix in new[] { "DamageTypeEvolutionOverRangeDescriptor_", "Descriptor_" })
            if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
        return name.Replace('_', ' ');
    }

    /// <summary>
    /// A value written in code, readable: without its quotes, its enumeration ("EFXPower/"), and the part every value
    /// of it has ("MinMax_", "Weapon_Cursor_"), and with spaces between its words: "Weapon_Cursor_GroundMissile" is
    /// "Ground Missile".
    /// </summary>
    private static string Readable(string written)
    {
        string name = written.Trim('\'', '"');
        name = name[(name.LastIndexOf('/') + 1)..];
        foreach (string prefix in new[] { "MinMax_", "Weapon_Cursor_", "Guidance_" })
            if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
        return Regex.Replace(name.Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
    }

    // ---- the grid ----

    /// <summary>A section of values, folding as the Unit pane's do.</summary>
    private static FoldSection ValuesSection(ValueSection section, Weapon weapon, IWeaponActions actions) =>
        Section(section.Key, section.TitleKey, Values(weapon, actions, section.Values), foldedAtFirst: section.FoldedAtFirst);

    /// <summary>
    /// Two pairs of name and value a row, the names dim on the panel colour, as the Unit pane's stats; each value
    /// marked when the open mod changes it, and where the tab lets it, changed with a click. Under them, how to change
    /// them, and when some are marked ?, that those are shown only.
    /// </summary>
    private static StackPanel Values(Weapon weapon, IWeaponActions actions, PaneValue[] values)
    {
        var grid = new Grid();
        // the values at their own width, the names the rest, as the Unit pane's stats: a name gives way, never a value
        foreach (GridLength width in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        int rows = (values.Length + 1) / 2;
        bool canChange = actions.CanChangeWeapon(weapon);
        bool anyChangeable = false;
        for (int at = 0; at < values.Length; at++)
        {
            int row = at / 2, column = at % 2 * 2;
            if (column == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool lastRow = row == rows - 1;
            PaneValue value = values[at];
            grid.Children.Add(NameCell(value, row, column, lastRow));
            Bevel cell = UnitPane.Cell("", row, column + 1, isName: false, lastRow);
            ShowValue((TextBlock)cell.Child, weapon, value);
            if (canChange && value.Certainty != Certainty.Unknown && MakeChangeable(cell, weapon, value, actions)) anyChangeable = true;
            else if (canChange && value.Field == WeaponField.ShotsPerSalvo && cell.Child is TextBlock { ToolTip: null } perSalvo) perSalvo.ToolTip = Texts.Get("Weapons.Pane.PerSalvo.Tip");
            grid.Children.Add(cell);
        }
        var contents = new StackPanel { Children = { UnitPane.Boxed(grid) } };
        string note = anyChangeable ? Texts.Get("Units.Pane.ClickToChange") : "";
        if (values.Any(value => value.Certainty == Certainty.Unknown)) note = (note + " " + Texts.Get("Weapons.Pane.UnknownShownOnly")).Trim();
        if (note != "") contents.Children.Add(UnitPane.Muted(note, new Thickness(0, 6, 0, 0)));
        return contents;
    }

    /// <summary>A value's text: marked when the open mod changes it; dim when not known, or when it is a reference shown only.</summary>
    private static void ShowValue(TextBlock text, Weapon weapon, PaneValue value)
    {
        if (value.Field == "") text.Text = value.Write(weapon);
        else WeaponMarks.Show(text, weapon, value.Field, value.Write(weapon), value.Write);
        if (value.Certainty == Certainty.Unknown || value.Shape == Shape.ShownOnly) text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        // a reference's full name, for one the pane shortens
        if (value.Shape == Shape.ShownOnly && text.ToolTip is null && weapon.FileValue(value.Property) is string written && written != "nil") text.ToolTip = written;
    }

    /// <summary>
    /// A value's name, dim on the panel colour, with ≈ after it (dim) when it is likely, or ? (in the caution colour)
    /// when not known; its tooltip says what it does, its name in the file, and how sure that is.
    /// </summary>
    private static Bevel NameCell(PaneValue value, int row, int column, bool lastRow)
    {
        Bevel cell = UnitPane.Cell(Texts.Get(value.NameKey), row, column, isName: true, lastRow);
        var name = (TextBlock)cell.Child;
        // cut short with … when the pane is narrow: the tooltip says it whole, with its name in the file
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        if (value.Certainty != Certainty.Known)
        {
            var mark = new Run(value.Certainty == Certainty.Likely ? "≈" : "?") { FontSize = 9.5, BaselineAlignment = BaselineAlignment.Superscript };
            if (value.Certainty == Certainty.Unknown)
            {
                mark.FontWeight = FontWeights.Bold;
                mark.SetResourceReference(TextElement.ForegroundProperty, "caution");
            }
            name.Inlines.Add(new Run(" ") { FontSize = 6 });
            name.Inlines.Add(mark);
        }
        UnitPane.AddFileName(name, value.Property);
        cell.Background ??= Brushes.Transparent;
        cell.ToolTip = AboutTip(value);
        ToolTipService.SetInitialShowDelay(cell, 300);
        return cell;
    }

    /// <summary>A value's name's tooltip: what it does in the game, its name in the file in the code font, and how sure that is.</summary>
    private static StackPanel AboutTip(PaneValue value)
    {
        var about = new TextBlock { Text = Texts.Get(value.AboutKey), TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        var property = new TextBlock { Text = value.Property, FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        property.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        property.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        string sureKey = value.Certainty switch
        {
            Certainty.Likely => "Weapons.Certainty.Likely",
            Certainty.Unknown => "Weapons.Certainty.Unknown",
            _ => "Weapons.Certainty.Known",
        };
        var sure = new TextBlock { Text = Texts.Get(sureKey), FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        sure.SetResourceReference(TextBlock.ForegroundProperty, value.Certainty == Certainty.Unknown ? "caution" : "muted");
        return new StackPanel { Children = { about, property, sure } };
    }

    // ---- changing values ----

    /// <summary>
    /// A value made changeable, by its kind: a number in a box, a switch with a switch, a choice from a list. A value
    /// the weapon's ammunition does not have is not: a rule cannot set it.
    /// </summary>
    /// <returns>Whether it was made changeable.</returns>
    private static bool MakeChangeable(Bevel cell, Weapon weapon, PaneValue value, IWeaponActions actions) => value.Shape switch
    {
        Shape.Number => MakeNumberChangeable(cell, weapon, value.Field, actions),
        Shape.Switch => MakeSwitchChangeable(cell, weapon, value.Field, actions),
        Shape.Choice => MakeChoiceChangeable(cell, weapon, value.Field, actions),
        _ => false,
    };

    /// <summary>A number changed in a box, outlined in the caution colour while what is typed is not a value the game takes.</summary>
    private static bool MakeNumberChangeable(Bevel cell, Weapon weapon, string field, IWeaponActions actions)
    {
        if (WeaponStat.Of(field) is not WeaponStat stat || stat.Read(weapon) is not double value) return false;
        string typedNow = stat.Shown(value).ToString("0.##########", Texts.Catalog.Culture);
        ClickToChange.WithBox(cell, typedNow, typed => stat.ValueOf(typed, weapon, actions.DamageLevels, Texts.Catalog.Culture) is not null,
            typed => actions.ChangeWeapon(weapon, field, typed), ValueTip(weapon, stat, actions));
        return true;
    }

    /// <summary>
    /// A switch before the value's Yes or No: a click turns it over. When the change is refused, it is turned back.
    /// The switch is drawn smaller than the Settings tab's, so the row stays a text row.
    /// </summary>
    private static bool MakeSwitchChangeable(Bevel cell, Weapon weapon, string field, IWeaponActions actions)
    {
        if (WeaponChoice.Of(field) is not WeaponChoice choice || choice.Read(weapon) is not string written || cell.Child is not TextBlock text) return false;
        var toggle = new CheckBox
        {
            Style = (Style)Application.Current.FindResource("Toggle"),
            IsChecked = written == WeaponChoice.On,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            LayoutTransform = new ScaleTransform(0.75, 0.75),
            ToolTip = Texts.Get("Weapons.Pane.Switch.Tip"),
        };
        toggle.Click += (_, _) =>
        {
            string wanted = toggle.IsChecked == true ? WeaponChoice.On : WeaponChoice.Off;
            if (!actions.ChangeWeapon(weapon, field, wanted)) toggle.IsChecked = written == WeaponChoice.On;
        };
        text.VerticalAlignment = VerticalAlignment.Center;
        cell.Child = null;
        cell.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { toggle, text } };
        return true;
    }

    /// <summary>A choice from a list of the values the game's weapons have, opened by a click on the value.</summary>
    private static bool MakeChoiceChangeable(Bevel cell, Weapon weapon, string field, IWeaponActions actions)
    {
        if (WeaponChoice.Of(field) is not WeaponChoice choice || choice.Read(weapon) is not string written) return false;
        IReadOnlyList<string> offered = actions.ChoicesFor(weapon, field);
        if (offered.Count < 2) return false;
        List<(string Code, string Shown)> choices = [.. offered.Select(each => (each, ChoiceName(field, each)))];
        ClickToChange.WithChoice(cell, choices, written, chosen => actions.ChangeWeapon(weapon, field, chosen));
        if (field == WeaponFileValues.DamageFamily && cell.Child is TextBlock text)
            text.ToolTip ??= Texts.Get("Weapons.Pane.DamageType.Tip", ("level", weapon.DamageLevel));
        return true;
    }

    /// <summary>
    /// What a number's box takes: the levels its kind of damage has, a chance in percent or from 0 to 1, an angle in
    /// degrees, shots at once no more than a salvo's, or a number (whole or not, from 0, 1 or -1).
    /// </summary>
    private static string ValueTip(Weapon weapon, WeaponStat stat, IWeaponActions actions)
    {
        if (stat.Field == WeaponField.DamageLevel && ArmorLevels.RangeOf(actions.DamageLevels, WeaponStat.DamageFamilyKey(weapon)) is (int lowest, int highest))
            return Texts.Get("Weapons.Pane.Level.Tip", ("family", weapon.DamageFamily.Replace('_', ' ').ToUpperInvariant()), ("lowest", lowest), ("highest", highest));
        if (stat.Field == WeaponFileValues.SimultaneousShots)
            return Texts.Get("Weapons.Pane.AtOnce.Tip", ("highest", WeaponNames.Number(weapon.ShotsPerSalvo)));
        if (stat.ShownTimes != 1 && stat.Highest is double angle) return Texts.Get("Weapons.Pane.Degrees.Tip", ("highest", Math.Round(stat.Shown(angle))));
        if (stat.Highest is 1) return Texts.Get("Weapons.Pane.Chance.Tip");
        if (stat.Highest is double most) return Texts.Get("Weapons.Pane.Percent.Tip", ("highest", most));
        if (stat.Lowest < 0) return Texts.Get("Weapons.Pane.OrOff.Tip");
        if (stat.Lowest >= 1) return Texts.Get("Weapons.Pane.FromOne.Tip");
        return Texts.Get(stat.IsWhole ? "Units.Pane.Stat.Whole.Tip" : "Units.Pane.Stat.Decimal.Tip");
    }
}
