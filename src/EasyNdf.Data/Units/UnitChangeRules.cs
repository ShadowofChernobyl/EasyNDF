using System.Globalization;
using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
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
    /// the mod adds, "Edit" for those of its changes to one of the game's units.
    /// </summary>
    public const string NewChange = "New", EditChange = "Edit";

    /// <summary>The operator that sets a single value, such as a number or a text.</summary>
    private const string SetValue = "Set To [int, float, string, boolean]";

    private readonly TextCatalog texts;
    private readonly IReadOnlyDictionary<string, Weapons.Loadout> loadouts;
    private readonly JsonArray rules = [];

    private UnitChangeRules(TextCatalog texts, IReadOnlyDictionary<string, Weapons.Loadout>? loadouts)
    {
        this.texts = texts;
        this.loadouts = loadouts ?? new Dictionary<string, Weapons.Loadout>();
    }

    /// <summary>
    /// The rules for the mod's changes to the game's units, then for the units it adds, in the order it keeps them. The
    /// changes come first, so a unit added as a copy of a changed one is copied with the changes, as the Armory shows
    /// it. A change to a unit the game does not have, and a unit made from one (another source chosen), get none; a
    /// unit without a deck number is given one, so the caller saves the mod after.
    /// </summary>
    /// <param name="mod">The mod.</param>
    /// <param name="game">The game's units, before the mod's changes.</param>
    /// <param name="texts">The language the rules are named in.</param>
    /// <param name="loadouts">The game's loadouts by descriptor, for the units whose weapons the mod changes.</param>
    public static JsonArray For(Mod mod, IReadOnlyList<Unit> game, TextCatalog texts, IReadOnlyDictionary<string, Weapons.Loadout>? loadouts = null)
    {
        var byDescriptor = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in game) byDescriptor.TryAdd(unit.Descriptor, unit);
        var writer = new UnitChangeRules(texts, loadouts);
        foreach (UnitEdit edit in mod.UnitEdits)
        {
            if (edit.ChangesNothing || !byDescriptor.TryGetValue(edit.Descriptor, out Unit? gameUnit)) continue;
            writer.Group(edit.Name ?? gameUnit.Name);
            // a change to the unit's weapons alone has no values to write in the unit
            if (edit.Name is not null || edit.PictureFile is not null || ValueActions(edit).Count > 0) writer.EditRule(edit, gameUnit);
            if (edit.PictureFile is string pictureFile)
                writer.PictureRule(edit.Descriptor, edit.Name ?? gameUnit.Name, pictureFile, EditChange, EditedPictureTexture(edit.Descriptor), EditedPicturePath(edit, gameUnit));
            writer.LoadoutRules(edit, edit.Descriptor, edit.Name ?? gameUnit.Name, gameUnit.WeaponSet, gameUnit.File, EditChange);
        }
        foreach (ModUnit unit in mod.Units)
        {
            if (!byDescriptor.TryGetValue(unit.MadeFrom, out Unit? madeFrom)) continue;
            unit.DeckNumber ??= ModUnit.NextDeckNumber(game, mod);
            writer.Group(unit.Name);
            writer.CloneRule(unit, madeFrom);
            writer.DeckNumberRule(unit);
            if (unit.PictureFile is string pictureFile)
                writer.PictureRule(unit.Descriptor, unit.Name, pictureFile, NewChange, PictureTexture(unit.Descriptor), PicturePath(unit, madeFrom));
            writer.LoadoutRules(unit, unit.Descriptor, unit.Name, madeFrom.WeaponSet, UnitsFile, NewChange);
        }
        return writer.rules;
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
        foreach (JsonObject action in ValueActions(unit)) actions.Add(action);
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

    /// <summary>
    /// A change to one of the game's units: an Edit rule on its descriptor giving it the name, nation, role, picture
    /// and values the user changed. Nothing else of the unit is touched.
    /// </summary>
    private void EditRule(UnitEdit edit, Unit gameUnit)
    {
        var actions = new JsonArray();
        if (edit.Name is not null) actions.Add(Action("NameToken", SetValue, "'" + NameToken(edit.Descriptor) + "'"));
        foreach (JsonObject action in ValueActions(edit)) actions.Add(action);
        if (edit.PictureFile is not null) actions.Add(Action("ButtonTexture", SetValue, "'" + EditedPictureTexture(edit.Descriptor) + "'"));
        JsonObject rule = Rule(new JsonObject
        {
            ["Name"] = EditRuleName(edit, gameUnit, texts),
            ["Kind"] = "Edit",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, edit.Descriptor)),
            ["Actions"] = actions,
        }, UnitsFile, edit.Descriptor, "Edit", EditChange);
        // its new name, for the export's UNITS.csv
        if (edit.Name is string name) ((JsonObject)rule[ArmoryKey]!)[TextsKey] = new JsonObject { [NameToken(edit.Descriptor)] = name };
    }

    /// <summary>The name of a change's rule, which the Armory's marks name: "M1A1 ABRAMS: changed in the Armory".</summary>
    public static string EditRuleName(UnitEdit edit, Unit gameUnit, TextCatalog texts) =>
        (edit.Name ?? gameUnit.Name) + ": " + texts.Get("Units.Rule.Changed");

    /// <summary>
    /// The nation, role and stats a unit is given, each an action on the property the game keeps it in: whole numbers
    /// as the game writes them, and the optics, vision and stealth with a decimal point.
    /// </summary>
    private static List<JsonObject> ValueActions(UnitValues unit)
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
            ["Params"] = new JsonObject { ["Find"] = Weapons.LoadoutRules.ReferencePrefix + loadout, ["Replace"] = Weapons.LoadoutRules.ReferencePrefix + own },
        }, unitFile, descriptor, "Loadout", change);
        var actions = new JsonArray();
        foreach (Weapons.LoadoutRules.LoadoutAction action in Weapons.LoadoutRules.ActionsFor(original, values.LoadoutChanges))
            actions.Add(Action(action.Target, action.Operator, action.Value));
        Rule(new JsonObject
        {
            ["Name"] = Line(unitName, "Units.Rule.WeaponsChanged", ("count", values.LoadoutChanges.Count)),
            ["Kind"] = "Edit",
            ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, own)),
            ["Actions"] = actions,
        }, LoadoutsFile, descriptor, "Weapons", change);
    }

    /// <summary>The unit's number for deck codes, added to DeckSerializer.ndf: without one, a deck with the unit cannot be shared as a code.</summary>
    private void DeckNumberRule(ModUnit unit) => Rule(new JsonObject
    {
        ["Name"] = Line(unit.Name, "Units.Rule.DeckNumber", ("number", unit.DeckNumber)),
        ["Kind"] = "Edit",
        ["Params"] = new JsonObject { ["Scope"] = "type", ["ScopeType"] = "TDeckSerializerEntries" },
        ["Actions"] = new JsonArray(Action("UnitIds", "Insert [array]",
            "LastIndex: ($/GFX/Unit/" + unit.Descriptor + ", " + (unit.DeckNumber ?? 0).ToString(CultureInfo.InvariantCulture) + ")")),
    }, DeckNumbersFile, unit.Descriptor, "Edit", NewChange);

    /// <summary>
    /// A card picture of the user's own: added to the game's list of unit pictures, under the texture name the unit's
    /// ButtonTexture is set to. The export copies the picture where the list says.
    /// </summary>
    /// <param name="descriptor">The unit's descriptor.</param>
    /// <param name="unitName">The unit's name, for the rule's.</param>
    /// <param name="pictureFile">The picture, as the app keeps it.</param>
    /// <param name="change">Whether it is for a unit the mod adds or one it changes (<see cref="NewChange"/>, <see cref="EditChange"/>).</param>
    /// <param name="texture">The texture name the unit's ButtonTexture is set to.</param>
    /// <param name="path">Where the export puts the picture, under GameData.</param>
    private void PictureRule(string descriptor, string unitName, string pictureFile, string change, string texture, string path)
    {
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

    /// <summary>The texture name of a unit's own card picture, as the game names its own: "Texture_Button_Unit_EZ_X".</summary>
    public static string PictureTexture(string descriptor) => "Texture_Button_" + descriptor["Descriptor_".Length..];

    /// <summary>
    /// The texture name of a picture of the user's own for one of the game's units: "Texture_Button_EZ_Unit_X", so it
    /// is never the name of the game's own picture for it.
    /// </summary>
    public static string EditedPictureTexture(string descriptor) => "Texture_Button_EZ_" + descriptor["Descriptor_".Length..];

    /// <summary>Where the export puts a unit's own card picture, under GameData: in its side's folder, as the game's are.</summary>
    private static string PicturePath(ModUnit unit, Unit madeFrom) =>
        PictureFolder + (unit.Side ?? madeFrom.Side) + "/" + unit.Descriptor[ModUnit.DescriptorPrefix.Length..] + ".png";

    /// <summary>Where the export puts a picture of the user's own for one of the game's units: "EZ_Unit_X.png" in its side's folder.</summary>
    private static string EditedPicturePath(UnitEdit edit, Unit gameUnit) =>
        PictureFolder + (edit.Side ?? gameUnit.Side) + "/EZ_" + edit.Descriptor["Descriptor_".Length..] + ".png";

    /// <summary>A unit's name token, made from its descriptor as a division's is (<see cref="DivisionChangeRules.TextToken"/>).</summary>
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
