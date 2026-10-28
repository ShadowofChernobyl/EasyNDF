using System.Text.RegularExpressions;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Weapons;
using EasyNdf.Engine.Ndf;
using static EasyNdf.Data.Units.NdfValues;

namespace EasyNdf.Data.Units;

/// <summary>
/// Reads a game's (or mod's) units from its GameData files: each unit from UniteDescriptor.ndf, its weapons from
/// WeaponDescriptor.ndf and the ammunition files, the multiplayer divisions it is in, and where its picture is. The two
/// games keep the same fields under different names and nestings; each field is looked for under both.
/// </summary>
public static class UnitReader
{
    private const string UnitPrefix = "Descriptor_Unit_";

    /// <summary>
    /// The files units are read from, in order: UniteDescriptor.ndf, then BuildingDescriptors.ndf, where WARNO keeps
    /// the units that are buildings, such as each nation's FOB (in the Logistics category). Only the first must exist.
    /// </summary>
    private static readonly (string File, bool MustExist)[] UnitFilesRead = [("UniteDescriptor.ndf", true), ("BuildingDescriptors.ndf", false)];

    /// <summary>
    /// The names of the files whose values the units and divisions show (pictures and flags aside): an EasyNDF mod's
    /// rules on these change what the Armory shows. Kept up to date with the files <see cref="Read"/> and
    /// <see cref="DivisionReader"/> read.
    /// </summary>
    public static IReadOnlyList<string> FilesWithValues { get; } =
    [
        "UniteDescriptor.ndf", "BuildingDescriptors.ndf", "WeaponDescriptor.ndf", "Ammunition.ndf", "AmmunitionMissiles.ndf", "DeckSerializer.ndf",
        "Divisions.ndf", "DivisionList.ndf", "DivisionRules.ndf", "DivisionCostMatrix.ndf", "Decks.ndf", "DeckPacks.ndf",
    ];

    /// <summary>Reads every unit. What cannot be read is listed in the catalogue's problems; the rest is still read.</summary>
    /// <param name="files">The game's files.</param>
    /// <param name="game">The game they are of.</param>
    /// <param name="texts">The game's texts.</param>
    /// <param name="knownFlags">
    /// The nations' flags when they were read before, for a source read again: no rule changes them, and finding them
    /// means reading every file of the interface.
    /// </param>
    public static UnitCatalog Read(GameDataFiles files, GameTitle game, GameTexts texts, IReadOnlyDictionary<string, string>? knownFlags = null)
    {
        var problems = new List<string>();
        Dictionary<string, Loadout> loadouts = [];
        Dictionary<string, List<(string Ammunition, int Count)>> weaponSets = ReadWeaponSets(files, problems, loadouts);
        Dictionary<string, string> weaponPictures = ReadWeaponPictures(files);
        Dictionary<string, Weapon> ammunition = ReadAmmunition(files, problems, weaponPictures);
        var battlegroupCards = new HashSet<string>(StringComparer.Ordinal);
        List<Division> allDivisions = DivisionReader.Read(files, texts, problems, battlegroupCards);
        Dictionary<string, List<string>> divisions = DivisionsOfEachUnit(allDivisions);
        Dictionary<string, string> pictures = ReadPictureSources(files);
        IReadOnlyDictionary<string, string> flags = knownFlags ?? ReadFlagSources(files);
        Dictionary<string, int> deckNumbers = ReadDeckNumbers(files);
        Dictionary<string, int> armorLevels = ReadArmorLevels(files);

        var units = new List<Unit>();
        foreach ((string file, bool mustExist) in UnitFilesRead)
        {
            foreach (Declaration declaration in Declarations(files, "Generated/Gameplay/Gfx/" + file, name => name.StartsWith(UnitPrefix, StringComparison.Ordinal), problems, mustExist))
            {
                if (declaration.Value is not ObjectValue unit) continue;
                units.Add(ReadUnit(declaration.Name, file, unit, texts, weaponSets, ammunition, divisions, pictures, deckNumbers));
            }
        }
        return new UnitCatalog(game, units, flags, problems, allDivisions, battlegroupCards, armorLevels, Weapon.WithUsersAmong(ammunition.Values, units), loadouts,
            WeaponTraitReader.Read(files, texts), weaponPictures);
    }

    // ---- one unit ----

    private static Unit ReadUnit(
        string descriptor, string file, ObjectValue unit, GameTexts texts,
        Dictionary<string, List<(string Ammunition, int Count)>> weaponSets, Dictionary<string, Weapon> ammunition,
        Dictionary<string, List<string>> divisions, Dictionary<string, string> pictures, Dictionary<string, int> deckNumbers)
    {
        // what transports go by: the unit's tags; as a transport, the tags it carries and its seats; the seats it takes in one
        var fields = new UnitFields(unit);
        ObjectValue? tags = fields.ModuleOf("TTagsModuleDescriptor");
        ObjectValue? transporter = fields.ModuleOf("TTransporterModuleDescriptor");
        ObjectValue? transportable = fields.ModuleOf("TTransportableModuleDescriptor");
        string nation = Text(fields.Find("MotherCountry")) ?? "";
        string? weaponSet = FirstReference(unit, "WeaponDescriptor_");
        string? pictureKey = Text(fields.Find("ButtonTexture"));
        return new Unit
        {
            Descriptor = descriptor,
            File = file,
            Name = texts.Get(Text(fields.Find("NameToken"))) ?? NameFromDescriptor(descriptor, nation),
            // WARNO: Coalition = TWargameCoalition/NATO; Steel Division 2: Nationalite = ENationalite/Allied
            Side = Text(fields.Find("Coalition")) ?? Text(fields.Find("Nationalite")) ?? "",
            Nation = nation,
            Factory = Text(fields.Find("FactoryType")) ?? Text(fields.Find("Factory")) ?? "",
            Role = Text(fields.Find("UnitRole")) ?? Text(fields.Find("TypeSpecificToken")) ?? "",
            RoleName = texts.Get(Text(fields.Find("TypeSpecificToken"))),
            StrategicType = Text(fields.Find("TypeStrategicCount")) ?? "",
            Flies = Text(fields.Find("PathfindType"))?.EndsWith("Flying", StringComparison.Ordinal) == true,
            Cost = CommandPoints(fields),
            UpgradesFrom = Text(fields.Find("UpgradeFromUnit")),
            Strength = Number(fields.Find("MaxPhysicalDamages")) ?? Number(fields.Find("MaxDamages")),
            Armor = ArmorOf(fields),
            ArmorFamilies = ArmorFamiliesOf(fields),
            Speed = Number(fields.Find("MaxSpeedInKmph")) ?? Number(fields.Find("Maxspeed")),
            RoadSpeed = Number(fields.Find("DisplayRoadSpeedInKmph")) ?? Number(fields.Find("RealRoadSpeed")),
            Optics = NumberFor(fields.Find("OpticalStrengths"), "Standard") ?? Number(fields.Find("OpticalStrength")),
            Vision = NumberFor(fields.Find("VisionRangesGRU"), "Standard") ?? Number(fields.Find("PorteeVision")),
            Stealth = Number(fields.Find("UnitConcealmentBonus")),
            Fuel = Number(fields.Find("FuelCapacity")),
            Specialties = ItemsOf(fields.Find("SpecialtiesList")).Select(Text).OfType<string>().Where(s => s != "").ToList(),
            WeaponSet = weaponSet,
            Weapons = weaponSet is not null && weaponSets.TryGetValue(weaponSet, out var mounts)
                ? mounts.Select(mount => WeaponOf(mount.Ammunition, mount.Count, ammunition)).ToList()
                : [],
            Divisions = divisions.TryGetValue(descriptor, out List<string>? inDivisions) ? inDivisions : [],
            PictureSource = pictureKey is not null && pictures.TryGetValue(pictureKey, out string? source) ? source : null,
            Tags = tags is null ? [] : TextsOf(Own(tags, "TagSet")),
            CarriesTags = transporter is null ? null : TextsOf(Own(transporter, "TransportableTagSet")),
            Seats = transporter is null ? null : Number(Own(transporter, "NbSeatsAvailable")),
            // a unit that can ride in a transport takes one seat when the game does not say
            SeatsTaken = transportable is null ? null : Number(Own(transportable, "NbSeatsOccupied")) ?? 1,
            HasDeckNumber = deckNumbers.ContainsKey(descriptor),
            DeckNumber = deckNumbers.TryGetValue(descriptor, out int number) ? number : null,
        };
    }

    /// <summary>
    /// A unit's fields by name and its modules by type, found in one pass over the unit: each name's first property
    /// and each type's first object, in the order <see cref="NdfValues.Find"/> and <see cref="NdfValues.AllObjects"/>
    /// look through a unit, so each field is the one they would find. A unit is asked for some forty fields, many of
    /// which it does not have, and looking through the whole unit again for each took a quarter of reading the units.
    /// </summary>
    private sealed class UnitFields
    {
        private readonly Dictionary<string, NdfValue> firstByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ObjectValue> firstByType = new(StringComparer.Ordinal);

        public UnitFields(ObjectValue unit)
        {
            foreach (ObjectValue each in AllObjects(unit))
            {
                firstByType.TryAdd(each.TypeName, each);
                foreach (Property property in each.Properties) firstByName.TryAdd(property.Name, property.Value);
            }
        }

        /// <summary>The first property of this name anywhere in the unit (<see cref="NdfValues.Find"/>); null when it has none.</summary>
        public NdfValue? Find(string name) => firstByName.GetValueOrDefault(name);

        /// <summary>The unit's module of a type, such as TTagsModuleDescriptor; null when it has none.</summary>
        public ObjectValue? ModuleOf(string type) => firstByType.GetValueOrDefault(type);
    }

    /// <summary>The texts of a list, such as a TagSet's.</summary>
    private static List<string> TextsOf(NdfValue? list) => [.. ItemsOf(list).Select(Text).OfType<string>()];

    /// <summary>
    /// The units' deck-code numbers: each ($/GFX/Unit/Descriptor_Unit_X, 123) of DeckSerializer.ndf. The file
    /// has no declaration name ("unnamed TDeckSerializerEntries"), so its lines are looked through rather than parsed.
    /// The divisions' numbers in the same file are named Descriptor_Deck_..., so they are not taken.
    /// </summary>
    private static Dictionary<string, int> ReadDeckNumbers(GameDataFiles files)
    {
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        using TextReader? reader = files.Open("Generated/Gameplay/Decks/DeckSerializer.ndf");
        if (reader is null) return numbers;
        while (reader.ReadLine() is string line)
            if (DeckNumberLine.Match(line) is { Success: true } match && int.TryParse(match.Groups[2].Value, out int number))
                numbers.TryAdd(match.Groups[1].Value, number);
        return numbers;
    }

    /// <summary>
    /// The descriptors of the units in a game's (or mod's) files, found from their declaration lines alone
    /// ("export Descriptor_Unit_X is TEntityDescriptor"), without reading the units: quick, for telling vanilla units
    /// from a mod's.
    /// </summary>
    public static HashSet<string> UnitDescriptorsIn(GameDataFiles files)
    {
        var descriptors = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string file, _) in UnitFilesRead)
        {
            using TextReader? reader = files.Open("Generated/Gameplay/Gfx/" + file);
            if (reader is null) continue;
            while (reader.ReadLine() is string line)
                if (UnitDeclarationLine.Match(line) is { Success: true } match) descriptors.Add(match.Groups[1].Value);
        }
        return descriptors;
    }

    /// <summary>
    /// The descriptors of every weapon in the files (their ammunition in Ammunition.ndf and AmmunitionMissiles.ndf),
    /// found from their declaration lines alone: quick, for telling the game's own weapons from a mod's.
    /// </summary>
    public static HashSet<string> AmmunitionDescriptorsIn(GameDataFiles files)
    {
        var descriptors = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in new[] { "Ammunition.ndf", "AmmunitionMissiles.ndf" })
        {
            using TextReader? reader = files.Open("Generated/Gameplay/Gfx/" + file);
            if (reader is null) continue;
            while (reader.ReadLine() is string line)
                if (AmmunitionDeclarationLine.Match(line) is { Success: true } match) descriptors.Add(match.Groups[1].Value);
        }
        return descriptors;
    }

    // "export Ammo_Canon_AP_120mm is TAmmunitionDescriptor", at the start of a line
    private static readonly Regex AmmunitionDeclarationLine = new(@"^(?:export[ 	]+|private[ 	]+|public[ 	]+)?(Ammo_[A-Za-z0-9_]+)[ 	]+is[ 	]", RegexOptions.Compiled);

    // "export Descriptor_Unit_M1A1_Abrams_US is TEntityDescriptor", at the start of a line
    private static readonly Regex UnitDeclarationLine = new(@"^(?:export[ 	]+|private[ 	]+|public[ 	]+)?(Descriptor_Unit_[A-Za-z0-9_]+)[ 	]+is[ 	]", RegexOptions.Compiled);

    // "($/GFX/Unit/Descriptor_Unit_M1A1_Abrams_US, 123)", or with "~/" in front, as Steel Division 2 writes it
    private static readonly Regex DeckNumberLine = new(@"\([ 	]*[$~/A-Za-z]*?(Descriptor_Unit_[A-Za-z0-9_]+)[ 	]*,[ 	]*([0-9]+)[ 	]*\)", RegexOptions.Compiled);

    /// <summary>
    /// A name for a unit the game has no name for: its descriptor without "Descriptor_Unit_" and its nation, with
    /// spaces: "Descriptor_Unit_M1A1_Abrams_US" is "M1A1 Abrams".
    /// </summary>
    public static string NameFromDescriptor(string descriptor, string nation)
    {
        string name = descriptor.StartsWith(UnitPrefix, StringComparison.Ordinal) ? descriptor[UnitPrefix.Length..] : descriptor;
        if (nation != "" && name.EndsWith("_" + nation, StringComparison.Ordinal) && name.Length > nation.Length + 1)
            name = name[..^(nation.Length + 1)];
        return name.Replace('_', ' ');
    }

    /// <summary>The cost: the command points in ProductionRessourcesNeeded.</summary>
    private static double? CommandPoints(UnitFields unit)
    {
        foreach (var (key, value) in PairsOf(unit.Find("ProductionRessourcesNeeded")))
            if (Text(key) == "Resource_CommandPoints") return Number(value);
        return null;
    }

    /// <summary>
    /// The armour on each side. WARNO writes ResistanceFront = TResistanceTypeRTTI(Family=... Index=18); Steel
    /// Division 2 writes ArmorDescriptorFront = ArmorDescriptor_Blindage_22, where the number is the level and a
    /// name without one (such as ArmorDescriptor_Toit_Lourd) is 0.
    /// </summary>
    private static UnitArmor ArmorOf(UnitFields unit)
    {
        int? Side(string side)
        {
            if (unit.Find("Resistance" + side) is ObjectValue resistance) return (int?)Number(Find(resistance, "Index"));
            if (Text(unit.Find("ArmorDescriptor" + side)) is string armor)
            {
                Match level = Regex.Match(armor, "_([0-9]+)\\z");
                return level.Success ? int.Parse(level.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            }
            return null;
        }
        return new UnitArmor(Side("Front"), Side("Sides"), Side("Rear"), Side("Top"));
    }

    /// <summary>The armour family of each side: TResistanceTypeRTTI's Family; none in Steel Division 2.</summary>
    private static UnitArmorFamilies ArmorFamiliesOf(UnitFields unit)
    {
        string? Side(string side) => unit.Find("Resistance" + side) is ObjectValue resistance ? Text(Find(resistance, "Family")) : null;
        return new UnitArmorFamilies(Side("Front"), Side("Sides"), Side("Rear"), Side("Top"));
    }

    /// <summary>
    /// How many levels each armour family has, and each kind of weapon damage: DamageResistance.ndf's
    /// ResistanceFamilyCounts and DamageFamilyCounts, such as "(ResistanceFamily_helico, 3)" and "(DamageFamily_ap, 40)".
    /// Empty when the game has no such file (Steel Division 2).
    /// </summary>
    private static Dictionary<string, int> ReadArmorLevels(GameDataFiles files)
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        string text = files.Read("Generated/Gameplay/Gfx/DamageResistance.ndf") ?? "";
        foreach (Match count in ArmorLevelCount.Matches(text))
            if (int.TryParse(count.Groups[2].Value, out int number)) levels.TryAdd(count.Groups[1].Value, number);
        return levels;
    }

    // "(ResistanceFamily_helico, 3)", "(DamageFamily_ap, 40)"
    private static readonly Regex ArmorLevelCount = new(@"\([ 	]*((?:Resistance|Damage)Family_[A-Za-z0-9_]+)[ 	]*,[ 	]*([0-9]+)[ 	]*\)", RegexOptions.Compiled);

    /// <summary>The first reference inside a value whose last part starts with <paramref name="prefix"/>.</summary>
    private static string? FirstReference(NdfValue value, string prefix)
    {
        foreach (ObjectValue o in AllObjects(value))
            foreach (Property property in o.Properties)
                foreach (NdfValue candidate in ItemsOf(property.Value).Prepend(property.Value))
                    if (candidate is ScalarValue { Kind: ScalarKind.Reference } reference
                        && LastPart(reference.Text) is string name && name.StartsWith(prefix, StringComparison.Ordinal))
                        return name;
        return null;
    }

    // ---- weapons and ammunition ----

    /// <summary>
    /// Each weapon set in WeaponDescriptor.ndf: its weapons' ammunition, and how many of each. Each is also read as a
    /// loadout, turret by turret, into <paramref name="loadouts"/>, for the Armory's weapons editor.
    /// </summary>
    private static Dictionary<string, List<(string Ammunition, int Count)>> ReadWeaponSets(GameDataFiles files, List<string> problems, Dictionary<string, Loadout> loadouts)
    {
        var sets = new Dictionary<string, List<(string, int)>>(StringComparer.Ordinal);
        foreach (Declaration declaration in Declarations(files, "Generated/Gameplay/Gfx/WeaponDescriptor.ndf", name => name.StartsWith("WeaponDescriptor_", StringComparison.Ordinal), problems))
        {
            if (declaration.Value is ObjectValue manager && LoadoutOf(declaration.Name, manager) is Loadout loadout) loadouts[declaration.Name] = loadout;
            var mounts = new List<(string, int)>();
            // each mounted weapon (TMountedWeaponDescriptor) names its Ammunition, and how many (NbWeapons, 1 when not given)
            foreach (ObjectValue weapon in AllObjects(declaration.Value))
            {
                if (Own(weapon, "Ammunition") is not NdfValue ammunition || Text(ammunition) is not string name) continue;
                int count = (int)(Number(Own(weapon, "NbWeapons")) ?? 1);
                mounts.Add((name, Math.Max(1, count)));
            }
            sets[declaration.Name] = mounts;
        }
        return sets;
    }

    /// <summary>
    /// A weapon set as a loadout: its ammo boxes (Salves), and each turret of TurretDescriptorList with the weapons of its
    /// MountedWeaponDescriptorList. Null when it has a mounted weapon outside them, which the editor could not place.
    /// </summary>
    private static Loadout? LoadoutOf(string descriptor, ObjectValue manager)
    {
        var turrets = new List<LoadoutTurret>();
        IReadOnlyList<NdfValue> turretList = ItemsOf(Own(manager, "TurretDescriptorList"));
        for (int turretIndex = 0; turretIndex < turretList.Count; turretIndex++)
        {
            if (turretList[turretIndex] is not ObjectValue turret) return null;
            var mounts = new List<LoadoutMount>();
            IReadOnlyList<NdfValue> mountList = ItemsOf(Own(turret, "MountedWeaponDescriptorList"));
            for (int mountIndex = 0; mountIndex < mountList.Count; mountIndex++)
            {
                if (mountList[mountIndex] is not ObjectValue mount || Text(Own(mount, "Ammunition")) is not string ammunition) return null;
                mounts.Add(new LoadoutMount(turretIndex, mountIndex, ammunition,
                    AmmoBox: (int)(Number(Own(mount, "AmmoBoxIndex")) ?? 0),
                    Count: Math.Max(1, (int)(Number(Own(mount, "NbWeapons")) ?? 1)),
                    EffectTag: Text(Own(mount, "EffectTag")) ?? "",
                    WeaponNumber: WeaponNumberOf(Text(Own(mount, "HandheldEquipmentKey")))));
            }
            turrets.Add(new LoadoutTurret(turretIndex, Text(Own(turret, "Tag")) ?? "", turret.TypeName ?? "", mounts));
        }
        int mountsInTurrets = turrets.Sum(turret => turret.Mounts.Count);
        if (mountsInTurrets != AllObjects(manager).Count(each => Own(each, "Ammunition") is not null)) return null;
        List<int> boxes = [.. ItemsOf(Own(manager, "Salves")).Select(box => (int)(Number(box) ?? 0))];
        return new Loadout(descriptor, boxes, turrets);
    }

    /// <summary>The N of "WeaponAlternative_N"; 0 when there is none.</summary>
    private static int WeaponNumberOf(string? key)
    {
        const string Prefix = "WeaponAlternative_";
        return key is not null && key.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(key[Prefix.Length..], out int number) ? number : 0;
    }

    /// <summary>
    /// Every ammunition, from Ammunition.ndf and (WARNO's missiles) AmmunitionMissiles.ndf, as the weapons the Weapons
    /// sub-tab shows, by descriptor, in the files' order.
    /// </summary>
    private static Dictionary<string, Weapon> ReadAmmunition(GameDataFiles files, List<string> problems, Dictionary<string, string> pictures)
    {
        var all = new Dictionary<string, Weapon>(StringComparer.Ordinal);
        foreach (string file in new[] { "Ammunition.ndf", "AmmunitionMissiles.ndf" })
        {
            foreach (Declaration declaration in Declarations(files, "Generated/Gameplay/Gfx/" + file, name => name.StartsWith("Ammo_", StringComparison.Ordinal), problems, mustExist: file == "Ammunition.ndf"))
                if (declaration.Value is ObjectValue ammunition)
                    all[declaration.Name] = WeaponOf(declaration.Name, ammunition, file, pictures);
        }
        return all;
    }

    /// <summary>
    /// An ammunition as a weapon. WARNO's names first, then Steel Division 2's for the values it keeps under others
    /// (its ranges in metres, its time between salvos).
    /// </summary>
    private static Weapon WeaponOf(string descriptor, ObjectValue ammunition, string file, Dictionary<string, string> pictures)
    {
        (string family, int level) = DamageOf(Own(ammunition, "Arme"));
        string name = Weapon.NameOf(descriptor);
        string? missile = Text(Own(ammunition, "MissileDescriptor"));
        NdfValue? hitChances = Find(ammunition, "BaseHitValueModifiers");
        string? texture = Text(Own(ammunition, "InterfaceWeaponTexture"));
        WeaponKind kind = WeaponKinds.Of(name, family, file, missile);
        return new Weapon
        {
            Descriptor = descriptor,
            File = file,
            Name = name,
            Kind = kind,
            Category = WeaponCategories.Of(descriptor, kind),
            DamageFamily = family,
            DamageLevel = level,
            Damage = Number(Own(ammunition, "PhysicalDamages")),
            DamageRadius = Number(Own(ammunition, "RadiusSplashPhysicalDamagesGRU")),
            Suppression = Number(Own(ammunition, "SuppressDamages")),
            SuppressionRadius = Number(Own(ammunition, "RadiusSplashSuppressDamagesGRU")),
            Range = Number(Own(ammunition, "MaximumRangeGRU")) ?? Number(Own(ammunition, "PorteeMaximale")),
            MinimumRange = Number(Own(ammunition, "MinimumRangeGRU")) ?? Number(Own(ammunition, "PorteeMinimale")),
            HelicopterRange = Number(Own(ammunition, "MaximumRangeHelicopterGRU")) ?? Number(Own(ammunition, "PorteeMaximaleHA")),
            PlaneRange = Number(Own(ammunition, "MaximumRangeAirplaneGRU")) ?? Number(Own(ammunition, "PorteeMaximaleTBA")),
            AccuracyStopped = hitChances is null ? null : NumberFor(hitChances, "Idling"),
            AccuracyMoving = hitChances is null ? null : NumberFor(hitChances, "Moving"),
            DispersionFar = Number(Own(ammunition, "DispersionAtMaxRangeGRU")),
            DispersionNear = Number(Own(ammunition, "DispersionAtMinRangeGRU")),
            AimingTime = Number(Own(ammunition, "AimingTime")),
            Reload = Number(Own(ammunition, "TimeBetweenTwoSalvos")) ?? Number(Own(ammunition, "TempsEntreDeuxSalves")),
            ShotsPerSalvo = Number(Own(ammunition, "ShotsCountPerSalvo")),
            SupplyCost = Number(Own(ammunition, "SupplyCost")),
            Traits = [.. ItemsOf(Own(ammunition, "TraitsToken")).Select(Text).OfType<string>()],
            PictureTexture = texture,
            PictureSource = texture is null ? null : pictures.GetValueOrDefault(texture),
            Missile = missile,
            MustDeploy = Text(Own(ammunition, "HasDeploymentTime")) == "True",
            FileValues = WeaponFileValues.Read(ammunition),
        };
    }

    /// <summary>
    /// The weapons' pictures, by texture name, from the game's list (WeaponTextures.ndf):
    /// "Texture_Interface_Weapon_M68_120 is TUIResourceTexture_Common ( FileName = "GameData:/.../M68_120.png" )".
    /// </summary>
    private static Dictionary<string, string> ReadWeaponPictures(GameDataFiles files)
    {
        string text = (files.Read("WeaponTextures.ndf") ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match texture in Regex.Matches(text, @"(?m)^(?:export |private |public )?(Texture_Interface_Weapon_[A-Za-z0-9_]+) is [A-Za-z0-9_]+\n\((.*?)\n\)", RegexOptions.Singleline))
        {
            Match file = Regex.Match(texture.Groups[2].Value, @"FileName\s*=\s*['""]([^'""]+)['""]");
            if (file.Success) sources[texture.Groups[1].Value] = GamePictures.Normalised(file.Groups[1].Value);
        }
        return sources;
    }

    /// <summary>
    /// The damage family and level: WARNO's Arme = TDamageTypeRTTI(Family=DamageFamily_ap Index=32) is ("ap", 32);
    /// Steel Division 2's Arme = EArmeType/AP_14 is ("AP", 14), and EArmeType/Roquette is ("Roquette", 0).
    /// </summary>
    private static (string Family, int Level) DamageOf(NdfValue? arme)
    {
        if (arme is ObjectValue damage)
        {
            string family = Text(Find(damage, "Family")) ?? "";
            if (family.StartsWith("DamageFamily_", StringComparison.Ordinal)) family = family["DamageFamily_".Length..];
            return (family, (int)(Number(Find(damage, "Index")) ?? 0));
        }
        if (Text(arme) is string written)
        {
            Match parts = Regex.Match(written, "\\A([A-Za-z]+?)_?([0-9]*)\\z");
            if (parts.Success)
                return (parts.Groups[1].Value, parts.Groups[2].Value == "" ? 0 : int.Parse(parts.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            return (written, 0);
        }
        return ("", 0);
    }

    /// <summary>
    /// One of a unit's weapons, with the numbers of its ammunition: its longest range of the ground's, helicopters' and
    /// planes'.
    /// </summary>
    private static UnitWeapon WeaponOf(string ammunition, int count, Dictionary<string, Weapon> all)
    {
        string name = Weapon.NameOf(ammunition);
        if (!all.TryGetValue(ammunition, out Weapon? weapon))
            return new UnitWeapon(name, count, "", 0, null, null, null, null, ammunition, null, null);
        return weapon.ForUnit(count);
    }

    // ---- divisions ----

    /// <summary>
    /// The multiplayer divisions each unit is in (by the unit's descriptor), by their names as the game shows them,
    /// sorted: the divisions tagged DEFAULT, and the units their rules list.
    /// </summary>
    private static Dictionary<string, List<string>> DivisionsOfEachUnit(List<Division> divisions)
    {
        var unitsIn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Division division in divisions.Where(d => d.IsForMultiplayer))
            foreach (DivisionUnit unit in division.Units)
            {
                if (!unitsIn.TryGetValue(unit.Unit, out List<string>? list)) unitsIn[unit.Unit] = list = [];
                if (!list.Contains(division.Name)) list.Add(division.Name);
            }
        foreach (List<string> list in unitsIn.Values) list.Sort(StringComparer.CurrentCultureIgnoreCase);
        return unitsIn;
    }

    /// <summary>A division's name when the game gives none: "Descriptor_Deck_Division_US_3rd_Arm_multi" is "US 3rd Arm".</summary>
    public static string DivisionNameFromDescriptor(string descriptor)
    {
        string name = descriptor.StartsWith("Descriptor_Deck_Division_", StringComparison.Ordinal) ? descriptor["Descriptor_Deck_Division_".Length..] : descriptor;
        if (name.EndsWith("_multi", StringComparison.Ordinal)) name = name[..^"_multi".Length];
        return name.Replace('_', ' ');
    }

    // ---- pictures and flags ----

    /// <summary>
    /// Each unit picture's path, by the key units give it (their ButtonTexture), from ButtonTexturesUnites.ndf, as a
    /// "GameData:/....png" path. WARNO writes ("Texture_Button_Unit_X", MAP [(~/ComponentState/Normal,
    /// TUIResourceTexture(FileName = "GameData:/....png"))]); Steel Division 2 writes
    /// export Texture_Button_Unit_X is ...( FileName = 'Assets\...\X.png' ).
    /// </summary>
    private static Dictionary<string, string> ReadPictureSources(GameDataFiles files)
    {
        string text = (files.Read("ButtonTexturesUnites.ndf") ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match entry in Regex.Matches(text, @"\(\s*""([A-Za-z0-9_]+)""\s*,\s*MAP\s*\[\s*\(\s*~/ComponentState/Normal\s*,\s*[A-Za-z0-9_]+\s*\(\s*FileName\s*=\s*""([^""]+)"""))
            sources[entry.Groups[1].Value] = GamePictures.Normalised(entry.Groups[2].Value);
        foreach (Match texture in Regex.Matches(text, @"(?m)^(?:export |private |public )?(Texture_Button_Unit_[A-Za-z0-9_]+) is [A-Za-z0-9_]+\n\((.*?)\n\)", RegexOptions.Singleline))
        {
            Match file = Regex.Match(texture.Groups[2].Value, @"FileName\s*=\s*['""]([^'""]+)['""]");
            if (file.Success) sources[texture.Groups[1].Value] = GamePictures.Normalised(file.Groups[1].Value);
        }
        return sources;
    }

    /// <summary>
    /// Each nation's flag, by the nation's code as units use it ("US", "SOV", "CZ"): the "..._FLAG.png" pictures of the
    /// interface's Flags folder. A nation with no such flag (Poland in Steel Division 2) gets the first picture of its
    /// animated division flag instead.
    /// </summary>
    private static Dictionary<string, string> ReadFlagSources(GameDataFiles files)
    {
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var divisionFlags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // the files that list the interface's pictures: under UserInterface, or the UI's Resources/Textures
        bool IsTextureList(string path) =>
            path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)
            && (path.Contains("/UserInterface/", StringComparison.OrdinalIgnoreCase) || path.Contains("/UI/Resources/Textures/", StringComparison.OrdinalIgnoreCase));
        foreach (string path in files.PathsWhere(IsTextureList))
        {
            string text = files.ReadPath(path) ?? "";
            foreach (Match flag in Regex.Matches(text, @"FileName\s*=\s*[""']([^""']*[\\/]Flags[\\/](?:Normal[\\/])?([A-Za-z]+)_FLAG\.png)[""']", RegexOptions.IgnoreCase))
                flags.TryAdd(flag.Groups[2].Value.ToUpperInvariant(), GamePictures.Normalised(flag.Groups[1].Value));
            foreach (Match flag in Regex.Matches(text, @"FileName\s*=\s*[""']([^""']*[\\/]Division[\\/]Flags[\\/]([A-Za-z]+)[\\/][A-Za-z]+_flag_00000\.png)[""']", RegexOptions.IgnoreCase))
                divisionFlags.TryAdd(flag.Groups[2].Value.ToUpperInvariant(), GamePictures.Normalised(flag.Groups[1].Value));
        }
        foreach (var (code, path) in divisionFlags) flags.TryAdd(code, path);
        return flags;
    }

    // ---- reading files ----

    /// <summary>
    /// The declarations of a file whose names <paramref name="wanted"/> picks, each parsed on its own as the file is read
    /// line by line. Steel Division 2's UniteDescriptor.ndf is over 100 MB: read and parsed whole, it takes well over a
    /// gigabyte of memory; this way only one declaration is held at a time, and the ones not wanted are not parsed.
    /// A declaration that is not valid NDF is noted in <paramref name="problems"/> and left out. Nothing, with a
    /// problem noted, when the file is missing and <paramref name="mustExist"/>.
    /// </summary>
    internal static IEnumerable<Declaration> Declarations(GameDataFiles files, string endOfPath, Func<string, bool> wanted, List<string> problems, bool mustExist = true)
    {
        using TextReader? reader = files.Open(endOfPath);
        if (reader is null)
        {
            if (mustExist) problems.Add(Path.GetFileName(endOfPath) + ": not found");
            yield break;
        }
        foreach (var (name, text) in DeclarationTexts(reader))
        {
            if (!wanted(name)) continue;
            List<Declaration> parsed;
            try
            {
                parsed = Parser.Parse(text).Declarations;
            }
            catch (NdfParseException problem)
            {
                problems.Add(Path.GetFileName(endOfPath) + ", " + name + ": " + problem.Message);
                continue;
            }
            foreach (Declaration declaration in parsed) yield return declaration;
        }
    }

    // a line that starts a top-level declaration: "export Name is", "Name is", "private Name is"...
    private static readonly Regex DeclarationStart = new(@"\A(?:(?:export|private|public|template)[ \t]+)*([A-Za-z_][A-Za-z0-9_]*)[ \t]+is\b", RegexOptions.Compiled);

    /// <summary>
    /// Each top-level declaration of a game file, with its name and text: from a line that starts one to the next such
    /// line. The game's generated files start every top-level declaration at the start of a line and indent
    /// everything inside one. What comes before the first declaration (the file's opening comments) is left out.
    /// </summary>
    public static IEnumerable<(string Name, string Text)> DeclarationTexts(TextReader reader)
    {
        var text = new System.Text.StringBuilder();
        string? name = null;
        while (reader.ReadLine() is string line)
        {
            Match start = line.Length > 0 && !char.IsWhiteSpace(line[0]) ? DeclarationStart.Match(line) : Match.Empty;
            if (start.Success)
            {
                if (name is not null) yield return (name, text.ToString());
                name = start.Groups[1].Value;
                text.Clear();
            }
            if (name is not null) text.Append(line).Append('\n');
        }
        if (name is not null) yield return (name, text.ToString());
    }
}
