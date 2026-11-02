using System.Globalization;
using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Engine.Rules;
using static EasyNdf.Data.Divisions.DivisionChangeRules;

namespace EasyNdf.Data.Units;

/// <summary>
/// The mod's units as ordinary rules, for its "Units" Armory preset, under a group for each unit. First its changes to
/// the game's own units: an Edit rule giving the unit the values the user changed. Then the units it adds: a Clone rule
/// that copies the unit it is made from in UniteDescriptor.ndf and gives the copy its own values, and a rule giving it a
/// deck number. A card picture of the user's own gets a rule adding it to the game's list of unit pictures. Like the
/// divisions' rules (<see cref="DivisionChangeRules"/>), they are made again whenever the mod's units change, are
/// locked, and are run by the export like any rule.
/// </summary>
public sealed class UnitChangeRules
{
    /// <summary>The key, under <see cref="DivisionChangeRules.ArmoryKey"/>, of the unit a rule is for, by its descriptor.</summary>
    public const string UnitKey = "Unit";

    /// <summary>The files a unit the mod adds is written into, by what they hold.</summary>
    public const string UnitsFile = "UniteDescriptor.ndf", PicturesFile = "ButtonTexturesUnites.ndf", LoadoutsFile = "WeaponDescriptor.ndf";

    /// <summary>Where the game keeps its units' card pictures, under GameData; a picture of the user's own goes in its side's folder there.</summary>
    public const string PictureFolder = "Assets/2D/Interface/Common/UnitsIcons/";

    /// <summary>
    /// What the Armory keeps of a rule (its <see cref="DivisionChangeRules.ChangeKey"/>): "New" for the rules of a unit
    /// the mod adds, "Edit" for those of its changes to one of the game's units, "DeckNumber" for the number for deck
    /// codes it gives a unit its rules add (<see cref="Mod.UnitDeckNumbers"/>).
    /// </summary>
    public const string NewChange = "New", EditChange = "Edit", DeckNumberChange = "DeckNumber";

    /// <summary>The operator that sets a single value, such as a number or a text.</summary>
    private const string SetValue = "Set To [int, float, string, boolean]";

    /// <summary>
    /// The games whose every Armory change to units is written as rules: new units, and a unit's name, picture,
    /// weapons and values. The other games get only the values of their own units (<see cref="Writes"/>).
    /// </summary>
    private static readonly IReadOnlyList<GameTitle> GamesWrittenInFull = [GameTitle.Warno];

    /// <summary>
    /// What only <see cref="GamesWrittenInFull"/> get written: nothing now. Steel Division 2's names are written into
    /// its MODDING.csv (<see cref="DivisionExtras.NamesFileOf"/>), its loadouts in its own forms
    /// (<see cref="Weapons.LoadoutRules"/>), all but a number of a weapon at one place, which it does not have, and a
    /// picture of the user's own as a texture of the Modded kind with its file under the mod's AssetsModding folder
    /// (<see cref="SteelDivisionPictureRules"/>). Its new units need more files than WARNO's (see the Steel Division 2
    /// notes).
    /// </summary>
    private static readonly IReadOnlyList<string> FieldsWrittenInFullOnly = [];

    /// <summary>Whether the Armory can write a change to one of a unit's values (<see cref="UnitField"/>) for a game.</summary>
    public static bool Writes(GameTitle game, string field) => GamesWrittenInFull.Contains(game) || !FieldsWrittenInFullOnly.Contains(field);

    /// <summary>
    /// Whether the Armory can add units (new ones and duplicates) to a mod of a game: WARNO, and Steel Division 2, whose
    /// new units are written in the other files it keeps each unit in too (<see cref="SteelDivisionNewUnitRules"/>).
    /// </summary>
    public static bool AddsUnits(GameTitle game) => GamesWrittenInFull.Contains(game) || game == GameTitle.SteelDivision2;

    /// <summary>
    /// Whether the Armory can write every one of a mod's changes as rules for its game: for WARNO, always; for a game
    /// written in part, only when the mod changes nothing but its own units', weapons' and divisions' values, those the
    /// Armory writes for it (no new divisions, units or weapons, numbers for deck codes, names or pictures of units,
    /// nor units in divisions: see <see cref="DivisionChangeRules.WritesEverythingOf"/>).
    /// </summary>
    public static bool WritesEverythingOf(Mod mod)
    {
        if (GamesWrittenInFull.Contains(mod.Game)) return true;
        if (!AddsUnits(mod.Game) && (mod.Units.Count > 0 || mod.UnitDeckNumbers.Count > 0)) return false;
        if (mod.Weapons.Count > 0 && !Weapons.WeaponChangeRules.AddsWeapons(mod.Game)) return false;
        if (!mod.Weapons.All(copy => copy.FieldsGiven().All(field => Weapons.WeaponChangeRules.Writes(mod.Game, field)))) return false;
        if (!mod.Divisions.All(entry => DivisionChangeRules.WritesEverythingOf(entry, mod.Game))) return false;
        if (!mod.WeaponEdits.All(edit => edit.FieldsGiven().All(field => Weapons.WeaponChangeRules.Writes(mod.Game, field)))) return false;
        return mod.UnitEdits.All(edit => edit.FieldsChanged().All(field => Writes(mod.Game, field)) && (edit.PictureFile is null || Writes(mod.Game, UnitField.Picture))
            && edit.LoadoutChanges.All(change => Weapons.LoadoutRules.Writes(mod.Game, change.Kind)));
    }

    private readonly TextCatalog texts;
    private readonly IReadOnlyDictionary<string, Weapons.Loadout> loadouts;
    private readonly GameTitle game;
    private readonly JsonArray rules = [];

    private UnitChangeRules(TextCatalog texts, IReadOnlyDictionary<string, Weapons.Loadout>? loadouts, GameTitle? game = null)
    {
        this.texts = texts;
        this.loadouts = loadouts ?? new Dictionary<string, Weapons.Loadout>();
        this.game = game ?? GameTitle.Warno;
    }

    /// <summary>
    /// The rules for the mod's changes to the game's units, then for the units it adds, in the order it keeps them. The
    /// changes come first, so a unit added as a copy of a changed one is copied with the changes, as the Armory shows
    /// it. A change to a unit the game does not have, and a unit made from one (another source chosen), get none; a
    /// unit without a deck number is given one, so the caller saves the mod after. Last come the numbers for deck codes
    /// the mod gives units its rules add (<see cref="Mod.UnitDeckNumbers"/>).
    /// </summary>
    /// <param name="mod">The mod.</param>
    /// <param name="game">The game's units, before the mod's changes.</param>
    /// <param name="texts">The language the rules are named in.</param>
    /// <param name="loadouts">The game's loadouts by descriptor, for the units whose weapons the mod changes.</param>
    /// <param name="unitName">A unit's name as the Armory shows it, by its descriptor, for the units the mod's rules add; their descriptor when not given.</param>
    public static JsonArray For(Mod mod, IReadOnlyList<Unit> game, TextCatalog texts, IReadOnlyDictionary<string, Weapons.Loadout>? loadouts = null,
        Func<string, string>? unitName = null)
    {
        var byDescriptor = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in game) byDescriptor.TryAdd(unit.Descriptor, unit);
        var writer = new UnitChangeRules(texts, loadouts, mod.Game);
        foreach (UnitEdit given in mod.UnitEdits)
        {
            // what the Armory cannot write for the mod's game is left out, never written WARNO's way
            UnitEdit edit = WithoutUnwritten(given, mod.Game);
            if (edit.ChangesNothing || !byDescriptor.TryGetValue(edit.Descriptor, out Unit? gameUnit)) continue;
            writer.Group(edit.Name ?? gameUnit.Name);
            // a change to the unit's weapons alone has no values to write in the unit
            if (edit.Name is not null || edit.PictureFile is not null || writer.ValueActions(edit, gameUnit).Count > 0) writer.EditRule(edit, gameUnit);
            if (edit.PictureFile is string pictureFile)
                writer.PictureRule(edit.Descriptor, edit.Name ?? gameUnit.Name, pictureFile, EditChange, EditedPictureTexture(edit.Descriptor), EditedPicturePath(edit, gameUnit),
                    edit.Side ?? gameUnit.Side);
            writer.LoadoutRules(edit, edit.Descriptor, edit.Name ?? gameUnit.Name, gameUnit.WeaponSet, gameUnit.File, EditChange);
        }
        // new units, and their numbers for deck codes, are written for the games the Armory adds units to only
        if (!AddsUnits(mod.Game)) return writer.rules;
        foreach (ModUnit unit in mod.Units)
        {
            if (!byDescriptor.TryGetValue(unit.MadeFrom, out Unit? madeFrom)) continue;
            // Steel Division 2's defences are no model for a unit: they are not units of UniteDescriptor.ndf
            if (mod.Game == GameTitle.SteelDivision2 && madeFrom.File != UnitsFile) continue;
            unit.DeckNumber ??= ModUnit.NextDeckNumber(game, mod);
            writer.Group(unit.Name);
            writer.CloneRule(unit, madeFrom);
            writer.DeckNumberRule(unit.Descriptor, unit.Name, unit.DeckNumber ?? 0, NewChange);
            if (mod.Game == GameTitle.SteelDivision2) writer.SteelDivisionNewUnitRules(unit, madeFrom);
            if (unit.PictureFile is string pictureFile)
                writer.PictureRule(unit.Descriptor, unit.Name, pictureFile, NewChange, PictureTexture(unit.Descriptor), PicturePath(unit, madeFrom), unit.Side ?? madeFrom.Side);
            writer.LoadoutRules(unit, unit.Descriptor, unit.Name, madeFrom.WeaponSet, UnitsFile, NewChange);
        }
        foreach ((string descriptor, int number) in mod.UnitDeckNumbers)
        {
            string name = unitName?.Invoke(descriptor) ?? descriptor;
            writer.Group(name);
            writer.DeckNumberRule(descriptor, name, number, DeckNumberChange);
        }
        return writer.rules;
    }

    /// <summary>
    /// A change without the name, picture and weapon changes the Armory cannot write for a game: a copy, the change
    /// itself left as it is; the change itself for a game written in full.
    /// </summary>
    private static UnitEdit WithoutUnwritten(UnitEdit edit, GameTitle game)
    {
        if (GamesWrittenInFull.Contains(game)) return edit;
        UnitEdit kept = edit.Copy();
        if (!Writes(game, UnitField.Name)) kept.Name = null;
        if (!Writes(game, UnitField.Picture)) kept.PictureFile = null;
        if (!Writes(game, UnitField.Weapons)) kept.LoadoutChanges = [];
        return kept;
    }

    /// <summary>
    /// The unit's descriptor: a copy of the unit it is made from, with new GUIDs, given its own name, and the nation,
    /// role and values the user changed. Nothing else is touched: the copy keeps its weapons and everything its model
    /// expects.
    /// </summary>
    private void CloneRule(ModUnit unit, Unit madeFrom)
    {
        string token = NameToken(unit.Descriptor);
        var actions = new JsonArray(
            Action("ClassNameForDebug", SetValue, "'" + unit.Descriptor["Descriptor_".Length..] + "'"),
            Action("NameToken", SetValue, "'" + token + "'"));
        // Steel Division 2's label over the unit on the map names it by a token of its own, the same as the card's
        if (game == GameTitle.SteelDivision2) actions.Add(Action("UnitName", SetValue, "'" + token + "'"));
        foreach (JsonObject action in ValueActions(unit, madeFrom)) actions.Add(action);
        if (unit.PictureFile is not null) actions.Add(Action("ButtonTexture", SetValue, "'" + PictureTexture(unit.Descriptor) + "'"));
        string what = unit.IsDuplicate ? "Units.Rule.Duplicate" : "Units.Rule.New";
        JsonObject clone = Rule(new JsonObject
        {
            ["Name"] = Line(unit.Name, what, ("unit", madeFrom.Name)),
            ["Kind"] = "Clone",
            ["Params"] = new JsonObject { ["Source"] = unit.MadeFrom, ["NewName"] = unit.Descriptor },
            ["Actions"] = actions,
        }, UnitsFile, unit.Descriptor, "Clone", NewChange);
        // its name, for the export's UNITS.csv
        ((JsonObject)clone[ArmoryKey]!)[TextsKey] = new JsonObject { [token] = unit.Name };
    }

    /// <summary>The files besides UniteDescriptor.ndf and DeckSerializer.ndf where Steel Division 2 keeps each unit (see <see cref="SteelDivisionNewUnitRules"/>).</summary>
    public const string SteelDivisionUnitListFile = "AllUnitsTactic.ndf", SteelDivisionShowRoomFile = "ShowRoomEquivalence.ndf",
        SteelDivisionProductionButtonsFile = "Generated_CubeActionsUnites.ndf", SteelDivisionUnitTagsFile = "UnitToUnitTag.ndf";

    /// <summary>
    /// A new Steel Division 2 unit in the other files the game keeps every one of its units in (see the format notes,
    /// section 2.4; each of the game's 3,810 units is in all of them): the list of the units a battle loads
    /// (AllUnitsTactic.ndf), the unit the showroom shows for it (ShowRoomEquivalence.ndf: its model's, as it has the
    /// same 3D model), its production button (a copy of its model's in Generated_CubeActionsUnites.ndf, pointed at it,
    /// with its name and picture), and its tag for the AI (UnitToUnitTag.ndf: its model's UNITE_ tag, which the copy
    /// keeps in its own TagSet, so the tag's TagWrapper in TagUnit.ndf is the game's). Its descriptor, number for deck
    /// codes, picture and loadout are written as for any unit.
    /// </summary>
    private void SteelDivisionNewUnitRules(ModUnit unit, Unit madeFrom)
    {
        string core = unit.Descriptor["Descriptor_Unit_".Length..], modelCore = madeFrom.Descriptor["Descriptor_Unit_".Length..];
        string title = Line(unit.Name, unit.IsDuplicate ? "Units.Rule.Duplicate" : "Units.Rule.New", ("unit", madeFrom.Name));
        Rule(new JsonObject
        {
            ["Name"] = title,
            ["Kind"] = "Edit",
            // the file's one declaration is a list itself ("AllUnitsTactic is [ ... ]"): the rules reach it as a constant
            ["Params"] = new JsonObject { ["Scope"] = "constants" },
            ["Actions"] = new JsonArray(Action("AllUnitsTactic", "Insert [array]", "LastIndex: ~/" + unit.Descriptor)),
        }, SteelDivisionUnitListFile, unit.Descriptor, "Clone", NewChange);
        Rule(new JsonObject
        {
            ["Name"] = title,
            ["Kind"] = "Edit",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, "ShowRoomEquivalenceManager")),
            ["Actions"] = new JsonArray(Action("UnitToShowRoomEquivalent", "Insert [array]",
                "LastIndex: ( ~/" + unit.Descriptor + ", ~/Descriptor_ShowRoomUnit_" + modelCore + " )")),
        }, SteelDivisionShowRoomFile, unit.Descriptor, "Clone", NewChange);
        string token = NameToken(unit.Descriptor);
        var button = new JsonArray(
            Action("CubeActionName", SetValue, "\"CubeAction_ProductionUnit_" + core + "\""),
            Action("ButtonTextToken", SetValue, "\"" + token + "\""),
            Action("HintTitleToken", SetValue, "\"" + token + "\""),
            Action("Descriptor", SetValue, "~/" + unit.Descriptor));
        if (unit.PictureFile is not null) button.Add(Action("BackgroundTexture", SetValue, "\"" + PictureTexture(unit.Descriptor) + "\""));
        Rule(new JsonObject
        {
            ["Name"] = title,
            ["Kind"] = "Clone",
            ["Params"] = new JsonObject { ["Source"] = "CubeAction_ProductionUnit_" + modelCore, ["NewName"] = "CubeAction_ProductionUnit_" + core },
            ["Actions"] = button,
        }, SteelDivisionProductionButtonsFile, unit.Descriptor, "Clone", NewChange);
        Rule(new JsonObject
        {
            ["Name"] = title,
            ["Kind"] = "Edit",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, "UnitToUnitTag")),
            ["Actions"] = new JsonArray(Action("UnitToUnitTag", "Insert [array]", "LastIndex: ( ~/" + unit.Descriptor + ", \"UNITE_" + modelCore + "\" )")),
        }, SteelDivisionUnitTagsFile, unit.Descriptor, "Clone", NewChange);
    }

    /// <summary>
    /// A change to one of the game's units: an Edit rule on its descriptor giving it the name, nation, role, picture
    /// and values the user changed. Nothing else of the unit is touched.
    /// </summary>
    private void EditRule(UnitEdit edit, Unit gameUnit)
    {
        var actions = new JsonArray();
        if (edit.Name is not null) actions.Add(Action("NameToken", SetValue, "'" + NameToken(edit.Descriptor) + "'"));
        // Steel Division 2's label over the unit on the map names it by a token of its own, the same as the card's
        if (edit.Name is not null && game == GameTitle.SteelDivision2) actions.Add(Action("UnitName", SetValue, "'" + NameToken(edit.Descriptor) + "'"));
        foreach (JsonObject action in ValueActions(edit, gameUnit)) actions.Add(action);
        if (edit.PictureFile is not null) actions.Add(Action("ButtonTexture", SetValue, "'" + EditedPictureTexture(edit.Descriptor) + "'"));
        JsonObject rule = Rule(new JsonObject
        {
            ["Name"] = EditRuleName(edit, gameUnit, texts),
            ["Kind"] = "Edit",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, edit.Descriptor)),
            ["Actions"] = actions,
        }, gameUnit.File, edit.Descriptor, "Edit", EditChange);
        // its new name, for the export's dictionary (WARNO's UNITS.csv, Steel Division 2's MODDING.csv)
        if (edit.Name is string name) ((JsonObject)rule[ArmoryKey]!)[TextsKey] = new JsonObject { [NameToken(edit.Descriptor)] = name };
    }

    /// <summary>The name of a change's rule, which the Armory's marks name: "M1A1 ABRAMS: changed in the Armory".</summary>
    public static string EditRuleName(UnitEdit edit, Unit gameUnit, TextCatalog texts) =>
        (edit.Name ?? gameUnit.Name) + ": " + texts.Get("Units.Rule.Changed");

    /// <summary>
    /// The nation, role and stats a unit is given, each an action on the property the game keeps it in, as the
    /// game the rules are for writes it (<see cref="WarnoValueActions"/>, <see cref="SteelDivisionValueActions"/>).
    /// </summary>
    /// <param name="unit">The values given.</param>
    /// <param name="gameUnit">The unit they are given to (for a new unit, its model), as the game has it: its scales.</param>
    private List<JsonObject> ValueActions(UnitValues unit, Unit gameUnit) =>
        game == GameTitle.SteelDivision2 ? SteelDivisionValueActions(unit, gameUnit) : WarnoValueActions(unit);

    /// <summary>
    /// WARNO's actions for the values given: whole numbers as the game writes them, and the optics, vision and
    /// stealth with a decimal point.
    /// </summary>
    private static List<JsonObject> WarnoValueActions(UnitValues unit)
    {
        var actions = new List<JsonObject>();
        if (unit.Nation is string nation) actions.Add(Action("MotherCountry", SetValue, "'" + nation + "'"));
        if (unit.Side is string side) actions.Add(Action("Coalition", SetValue, "TWargameCoalition/" + side));
        if (unit.Role is string role) actions.Add(Action("UnitRole", SetValue, "'" + role + "'"));
        void Whole(string target, int? value)
        {
            if (value is int number) actions.Add(Action(target, SetValue, number.ToString(CultureInfo.InvariantCulture)));
        }
        void Decimal(string target, double? value)
        {
            if (value is double number) actions.Add(Action(target, SetValue, number.ToString("0.0##############", CultureInfo.InvariantCulture)));
        }
        Whole("$/GFX/Resources/Resource_CommandPoints", unit.Cost);
        Whole("MaxPhysicalDamages", unit.Strength);
        Whole("ResistanceFront" + ActionApplier.PathSeparator + "Index", unit.FrontArmor);
        Whole("ResistanceSides" + ActionApplier.PathSeparator + "Index", unit.SideArmor);
        Whole("ResistanceRear" + ActionApplier.PathSeparator + "Index", unit.RearArmor);
        Whole("ResistanceTop" + ActionApplier.PathSeparator + "Index", unit.TopArmor);
        Whole("MaxSpeedInKmph", unit.Speed);
        Whole("DisplayRoadSpeedInKmph", unit.RoadSpeed);
        Whole("FuelCapacity", unit.Fuel);
        Decimal("OpticalStrengths" + ActionApplier.PathSeparator + "EOpticalStrength/Standard", unit.Optics);
        Decimal("VisionRangesGRU" + ActionApplier.PathSeparator + "EVisionRange/Standard", unit.Vision);
        Decimal("UnitConcealmentBonus", unit.Stealth);
        return actions;
    }

    /// <summary>
    /// Steel Division 2's actions for the values given, as its UniteDescriptor.ndf writes them (see the format notes,
    /// section 2.2). Each target is a property's name, found wherever it is in the unit: inside the module selectors'
    /// Default, and in the placement ghost's copy too, which shows the nation and role (each such name is in the
    /// unit's own modules only, once or in both copies). Numbers keep the files' forms: MaxDamages and FuelCapacity
    /// with a decimal point, distances and speeds as ((N) * Metre), in the files' own units (the unit's
    /// <see cref="Unit.MetresScale"/> and <see cref="Unit.SpeedScale"/> times what the game shows), the armour as the
    /// name of its plate's descriptor. A value the game shows twice (the strength in the HUD, the front armour on the
    /// label) is given to both.
    /// </summary>
    private static List<JsonObject> SteelDivisionValueActions(UnitValues unit, Unit gameUnit)
    {
        var actions = new List<JsonObject>();
        void Set(string target, string value) => actions.Add(Action(target, SetValue, value));
        if (unit.Nation is string nation)
        {
            Set("MotherCountry", "'" + nation + "'");
            Set("CountryTexture", "'" + CountryFlagTexture + nation + "'");
        }
        if (unit.Side is string side) Set("Nationalite", "ENationalite/" + side);
        if (unit.Role is string role) Set("TypeSpecificToken", "'" + role + "'");
        if (unit.Cost is int cost) Set("~/Resource_CommandPoints", WholeText(cost));
        if (unit.Strength is int strength)
        {
            Set("MaxDamages", DecimalText(strength));
            Set("MaxHPForHUD", WholeText(strength));
        }
        if (unit.FrontArmor is int front)
        {
            Set("ArmorDescriptorFront", ArmorPlate(front));
            // the label shows the front plate's number (PlatingValue); only some units have it
            Set("PlatingValue", WholeText(front));
        }
        if (unit.SideArmor is int sides) Set("ArmorDescriptorSides", ArmorPlate(sides));
        if (unit.RearArmor is int rear) Set("ArmorDescriptorRear", ArmorPlate(rear));
        if (unit.TopArmor is int top) Set("ArmorDescriptorTop", ArmorPlate(top));
        if (unit.Speed is int speed)
        {
            string written = InMetres(speed * (gameUnit.SpeedScale ?? 1));
            if (gameUnit.SpeedIsInFlight) Set("Speed", written);
            else
            {
                // the game's units all move at their top speed in combat too: both are given the new speed
                Set("Maxspeed", written);
                Set("VitesseCombat", written);
            }
        }
        if (unit.RoadSpeed is int roadSpeed) Set("RealRoadSpeed", WholeText(roadSpeed));
        if (unit.Fuel is int fuel) Set("FuelCapacity", DecimalText(fuel));
        if (unit.Optics is double optics) Set("OpticalStrength", WholeText((int)Math.Round(optics)));
        if (unit.Vision is double vision) Set("PorteeVision", InMetres(vision * (gameUnit.MetresScale ?? 1)));
        if (unit.Stealth is double stealth) Set("UnitConcealmentBonus", DecimalText(stealth));
        return actions;
    }

    /// <summary>What Steel Division 2's flags of the nations are called, before the nation's code: its units' CountryTexture.</summary>
    private const string CountryFlagTexture = "CommonTexture_MotherCountryFlag_";

    /// <summary>A whole number as the files write it: "85".</summary>
    private static string WholeText(int number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>A number with a decimal point, as the files write a float: "10.0", "2.5".</summary>
    private static string DecimalText(double number) => number.ToString("0.0##############", CultureInfo.InvariantCulture);

    /// <summary>A distance or speed as Steel Division 2 writes one, in its own units: "((12500) * Metre)".</summary>
    private static string InMetres(double number) => "((" + number.ToString("0.###############", CultureInfo.InvariantCulture) + ") * Metre)";

    /// <summary>Steel Division 2's armour plate of a level: its descriptor, such as "ArmorDescriptor_Blindage_18".</summary>
    private static string ArmorPlate(int level) =>
        UnitReader.ArmorDescriptorPrefix + UnitReader.ArmorPlateFamily + "_" + level.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A unit's changes to its weapons: a loadout of its own, a copy of the one it had (so no other unit's weapons
    /// change), the unit pointed at it, and the changes made in it. None when the unit's weapons are not changed, or its
    /// loadout is not known.
    /// </summary>
    /// <param name="values">The unit's values, with its weapon changes.</param>
    /// <param name="descriptor">The unit's descriptor.</param>
    /// <param name="unitName">The unit's name, for the rules' names.</param>
    /// <param name="loadout">The loadout it has, by descriptor: its model's, for a unit the mod adds.</param>
    /// <param name="unitFile">The file the unit is in, where it is pointed at its own loadout.</param>
    /// <param name="change">Whether it is a unit the mod adds or one it changes.</param>
    private void LoadoutRules(UnitValues values, string descriptor, string unitName, string? loadout, string unitFile, string change)
    {
        loadout = values.LoadoutFrom ?? loadout;
        if (values.LoadoutChanges.Count == 0 || loadout is null || !loadouts.TryGetValue(loadout, out Weapons.Loadout? original)) return;
        string own = Weapons.LoadoutRules.OwnLoadoutName(descriptor);
        Rule(new JsonObject
        {
            ["Name"] = Line(unitName, "Units.Rule.OwnLoadout", ("loadout", loadout)),
            ["Kind"] = "Clone",
            ["Params"] = new JsonObject { ["Source"] = loadout, ["NewName"] = own },
        }, LoadoutsFile, descriptor, "Loadout", change);
        Rule(new JsonObject
        {
            ["Name"] = Line(unitName, "Units.Rule.UsesOwnLoadout", ("loadout", own)),
            ["Kind"] = "Replace",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, descriptor)),
            ["Params"] = new JsonObject { ["Find"] = Weapons.LoadoutRules.LoadoutReference(loadout, game), ["Replace"] = Weapons.LoadoutRules.LoadoutReference(own, game) },
        }, unitFile, descriptor, "Loadout", change);
        var actions = new JsonArray();
        foreach (Weapons.LoadoutRules.LoadoutAction action in Weapons.LoadoutRules.ActionsFor(original, values.LoadoutChanges, game))
            actions.Add(Action(action.Target, action.Operator, action.Value));
        Rule(new JsonObject
        {
            ["Name"] = Line(unitName, "Units.Rule.WeaponsChanged", ("count", values.LoadoutChanges.Count)),
            ["Kind"] = "Edit",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, own)),
            ["Actions"] = actions,
        }, LoadoutsFile, descriptor, "Weapons", change);
    }

    /// <summary>
    /// The rules that give a unit its rules add its number for deck codes, under a heading with its name: those
    /// <see cref="For"/> makes from <see cref="Mod.UnitDeckNumbers"/>.
    /// </summary>
    /// <param name="descriptor">The unit's descriptor.</param>
    /// <param name="unitName">Its name, for the rules' heading and name.</param>
    /// <param name="number">Its number for deck codes.</param>
    /// <param name="texts">The language the rules are named in.</param>
    /// <param name="game">The game whose DeckSerializer.ndf it goes into; WARNO when not given.</param>
    public static JsonArray DeckNumberRules(string descriptor, string unitName, int number, TextCatalog texts, GameTitle? game = null)
    {
        var writer = new UnitChangeRules(texts, null, game);
        writer.Group(unitName);
        writer.DeckNumberRule(descriptor, unitName, number, DeckNumberChange);
        return writer.rules;
    }

    /// <summary>
    /// The unit's number for deck codes, added to DeckSerializer.ndf: without one, a deck with the unit cannot be shared
    /// as a code. WARNO's file is one unnamed TDeckSerializerEntries naming units as $/GFX/Unit/X; Steel Division 2's is
    /// DeckSerializer is TDeckSerializer, naming them bare: (Descriptor_Unit_X, 446).
    /// </summary>
    /// <param name="change">Whether it is for a unit the mod adds (<see cref="NewChange"/>) or one its rules add (<see cref="DeckNumberChange"/>).</param>
    private void DeckNumberRule(string descriptor, string unitName, int number, string change)
    {
        if (game == GameTitle.SteelDivision2) SteelDivisionDeckNumberRule(descriptor, unitName, number, change);
        else WarnoDeckNumberRule(descriptor, unitName, number, change);
    }

    /// <summary>A unit's number for deck codes in WARNO's DeckSerializer.ndf (see <see cref="DeckNumberRule"/>).</summary>
    private void WarnoDeckNumberRule(string descriptor, string unitName, int number, string change) => Rule(new JsonObject
    {
        ["Name"] = Line(unitName, "Units.Rule.DeckNumber", ("number", number)),
        ["Kind"] = "Edit",
        ["Params"] = new JsonObject { ["Scope"] = "type", ["ScopeType"] = "TDeckSerializerEntries" },
        ["Actions"] = new JsonArray(Action("UnitIds", "Insert [array]",
            "LastIndex: ($/GFX/Unit/" + descriptor + ", " + number.ToString(CultureInfo.InvariantCulture) + ")")),
    }, DeckNumbersFile, descriptor, "Edit", change);

    /// <summary>A unit's number for deck codes in Steel Division 2's DeckSerializer.ndf (see <see cref="DeckNumberRule"/>).</summary>
    private void SteelDivisionDeckNumberRule(string descriptor, string unitName, int number, string change) => Rule(new JsonObject
    {
        ["Name"] = Line(unitName, "Units.Rule.DeckNumber", ("number", number)),
        ["Kind"] = "Edit",
        ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, "DeckSerializer")),
        ["Actions"] = new JsonArray(Action("UnitIds", "Insert [array]",
            "LastIndex: (" + descriptor + ", " + number.ToString(CultureInfo.InvariantCulture) + ")")),
    }, DeckNumbersFile, descriptor, "Edit", change);

    /// <summary>
    /// A card picture of the user's own: added to the game's list of unit pictures, under the texture name the unit's
    /// ButtonTexture is set to. The export copies the picture where the list says.
    /// </summary>
    /// <param name="descriptor">The unit's descriptor.</param>
    /// <param name="unitName">The unit's name, for the rule's.</param>
    /// <param name="pictureFile">The picture, as the app keeps it.</param>
    /// <param name="change">Whether it is for a unit the mod adds or one it changes (<see cref="NewChange"/>, <see cref="EditChange"/>).</param>
    /// <param name="texture">The texture name the unit's ButtonTexture is set to.</param>
    /// <param name="path">Where the export puts the picture, under GameData (WARNO's way: Steel Division 2's is worked out from the texture).</param>
    /// <param name="side">The unit's side, which Steel Division 2's texture is masked for.</param>
    private void PictureRule(string descriptor, string unitName, string pictureFile, string change, string texture, string path, string side)
    {
        if (game == GameTitle.SteelDivision2)
        {
            SteelDivisionPictureRules(descriptor, unitName, pictureFile, change, texture, side);
            return;
        }
        JsonObject rule = Rule(new JsonObject
        {
            ["Name"] = Line(unitName, "Units.Rule.Picture", ("picture", Path.GetFileName(pictureFile))),
            ["Kind"] = "Edit",
            ["Params"] = new JsonObject { ["Scope"] = "type", ["ScopeType"] = "TBUCKToolAdditionalTextureBank" },
            ["Actions"] = new JsonArray(Action("Textures", "Insert [array]",
                "LastIndex: (\"" + texture + "\", MAP [( ~/ComponentState/Normal, TUIResourceTexture( FileName = \"GameData:/" + path + "\"))])")),
        }, PicturesFile, descriptor, "Picture", change);
        ((JsonObject)rule[ArmoryKey]!)[PictureFileKey] = pictureFile;
        ((JsonObject)rule[ArmoryKey]!)[PicturePathKey] = path;
    }

    /// <summary>
    /// A card picture of the user's own in Steel Division 2, as its modding manual says to add one: a texture of its
    /// own, of the Modded kind, whose FileName is under the mod's AssetsModding folder, and its entry in the game's bank
    /// of unit pictures (UnitButtonTextureAdditionalBank), which the unit's ButtonTexture names. The export copies the
    /// picture into AssetsModding, beside the mod's GameData folder.
    /// </summary>
    /// <param name="texture">The texture name the unit's ButtonTexture is set to.</param>
    /// <param name="side">The unit's side: Allied pictures are masked for the game's first side, Axis ones for its second, as the game's own.</param>
    private void SteelDivisionPictureRules(string descriptor, string unitName, string pictureFile, string change, string texture, string side)
    {
        string assetsPath = SteelDivisionPictureFolder + "/" + texture["Texture_Button_".Length..] + ".png";
        // relative to AssetsModding, with the backslashes the game's own FileNames have
        string fileName = assetsPath[(AssetsModdingFolder.Length + 1)..].Replace('/', '\\');
        string mask = side == "Axis" ? "CivMask/Nationalite2" : "CivMask/Nationalite1";
        string title = Line(unitName, "Units.Rule.Picture", ("picture", Path.GetFileName(pictureFile)));
        JsonObject declaration = Rule(new JsonObject
        {
            ["Name"] = title,
            ["Kind"] = "Add",
            ["Params"] = new JsonObject
            {
                ["Text"] = "export " + texture + " is TUIResourceTextureWithUsefulnessMaskModded\n(\n    TypeMask = TypeMask/BuildMenu\n    CivMask  = " + mask
                    + "\n    FileName = '" + fileName + "'\n)",
                ["Name"] = "",
            },
        }, PicturesFile, descriptor, "Picture", change);
        ((JsonObject)declaration[ArmoryKey]!)[PictureFileKey] = pictureFile;
        ((JsonObject)declaration[ArmoryKey]!)[PicturePathKey] = assetsPath;
        Rule(new JsonObject
        {
            ["Name"] = title,
            ["Kind"] = "Edit",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, SteelDivisionPictureBank)),
            ["Actions"] = new JsonArray(Action("Textures", "Insert [array]",
                "LastIndex: (\"" + texture + "\", MAP [( ~/ComponentState/Normal, ~/" + texture + "), ])")),
        }, PicturesFile, descriptor, "Picture", change);
    }

    /// <summary>Steel Division 2's bank of unit pictures, at the end of ButtonTexturesUnites.ndf, which names each texture a unit's ButtonTexture may give.</summary>
    private const string SteelDivisionPictureBank = "UnitButtonTextureAdditionalBank";

    /// <summary>
    /// A mod's folder for its own pictures in Steel Division 2, beside its GameData folder (the modding manual's
    /// "AssetsModding/"), and where the Armory puts the units' card pictures in it. A picture path that starts with it
    /// is copied there rather than under GameData (<see cref="DivisionExtras.CopyPictures"/>).
    /// </summary>
    public const string AssetsModdingFolder = "AssetsModding", SteelDivisionPictureFolder = AssetsModdingFolder + "/UnitsIcons";

    /// <summary>The texture name of a unit's own card picture, as the game names its own: "Texture_Button_Unit_EZ_X".</summary>
    public static string PictureTexture(string descriptor) => "Texture_Button_" + descriptor["Descriptor_".Length..];

    /// <summary>
    /// The texture name of a picture of the user's own for one of the game's units: "Texture_Button_EZ_Unit_X", so it
    /// is never the name of the game's own picture for it.
    /// </summary>
    public static string EditedPictureTexture(string descriptor) => "Texture_Button_EZ_" + descriptor["Descriptor_".Length..];

    /// <summary>Where the export puts a unit's own card picture, under GameData: in its side's folder, as the game's are.</summary>
    private static string PicturePath(ModUnit unit, Unit madeFrom) =>
        PictureFolder + (unit.Side ?? madeFrom.Side) + "/" + ModUnit.ShortName(unit.Descriptor) + ".png";

    /// <summary>Where the export puts a picture of the user's own for one of the game's units: "EZ_Unit_X.png" in its side's folder.</summary>
    private static string EditedPicturePath(UnitEdit edit, Unit gameUnit) =>
        PictureFolder + (edit.Side ?? gameUnit.Side) + "/EZ_" + edit.Descriptor["Descriptor_".Length..] + ".png";

    /// <summary>
    /// A unit's name token, made from its descriptor as a division's is (<see cref="DivisionChangeRules.TextToken"/>):
    /// ten characters, the most Steel Division 2 takes.
    /// </summary>
    public static string NameToken(string descriptor) => TextToken(descriptor, "name");

    // ---- making rules ----

    /// <summary>A group heading for a unit's rules.</summary>
    private void Group(string unitName) => rules.Add(new JsonObject
    {
        ["Name"] = unitName,
        ["Kind"] = "Group",
        ["Enabled"] = true,
        ["LogicGate"] = "All",
        ["Conditions"] = new JsonArray(),
        ["Actions"] = new JsonArray(),
        ["Params"] = new JsonObject(),
        [ArmoryKey] = new JsonObject { [ShowsKey] = "Group" },
    });

    /// <summary>Adds a rule with what every rule has, and what the Armory knows of it; it is returned to add to.</summary>
    private JsonObject Rule(JsonObject rule, string file, string descriptor, string shows, string change)
    {
        rule["Enabled"] = true;
        rule["LogicGate"] = "All";
        rule["Conditions"] ??= new JsonArray();
        rule["Actions"] ??= new JsonArray();
        rule["Params"] ??= new JsonObject();
        // a unit's rules go together: one alone would leave the unit half made, or its picture missing
        rule[ArmoryKey] = new JsonObject { [FileKey] = file, [UnitKey] = descriptor, [ShowsKey] = shows, [ChangeKey] = change, [LockedKey] = true };
        rules.Add(rule);
        return rule;
    }

    /// <summary>A rule's name for a unit: "ABRAMS HUNTER: new unit, model of M1A1 ABRAMS".</summary>
    private string Line(string unitName, string what, params (string Name, object? Value)[] blanks) => unitName + ": " + texts.Get(what, blanks);
}
