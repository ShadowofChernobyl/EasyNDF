using System.Text.RegularExpressions;
using EasyNdf.Data.Game;
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

    /// <summary>Reads every unit. What cannot be read is listed in the catalogue's problems; the rest is still read.</summary>
    public static UnitCatalog Read(GameDataFiles files, GameTitle game, GameTexts texts)
    {
        var problems = new List<string>();
        Dictionary<string, List<(string Ammunition, int Count)>> weaponSets = ReadWeaponSets(files, problems);
        Dictionary<string, AmmunitionStats> ammunition = ReadAmmunition(files, problems);
        Dictionary<string, List<string>> divisions = ReadDivisions(files, texts, problems);
        Dictionary<string, string> pictures = ReadPictureSources(files);
        Dictionary<string, string> flags = ReadFlagSources(files);

        var units = new List<Unit>();
        foreach (Declaration declaration in Declarations(files, "Generated/Gameplay/Gfx/UniteDescriptor.ndf", name => name.StartsWith(UnitPrefix, StringComparison.Ordinal), problems))
        {
            if (declaration.Value is not ObjectValue unit) continue;
            units.Add(ReadUnit(declaration.Name, unit, texts, weaponSets, ammunition, divisions, pictures));
        }
        return new UnitCatalog(game, units, flags, problems);
    }

    // ---- one unit ----

    private static Unit ReadUnit(
        string descriptor, ObjectValue unit, GameTexts texts,
        Dictionary<string, List<(string Ammunition, int Count)>> weaponSets, Dictionary<string, AmmunitionStats> ammunition,
        Dictionary<string, List<string>> divisions, Dictionary<string, string> pictures)
    {
        string nation = Text(Find(unit, "MotherCountry")) ?? "";
        string? weaponSet = FirstReference(unit, "WeaponDescriptor_");
        string? pictureKey = Text(Find(unit, "ButtonTexture"));
        return new Unit
        {
            Descriptor = descriptor,
            Name = texts.Get(Text(Find(unit, "NameToken"))) ?? NameFromDescriptor(descriptor, nation),
            // WARNO: Coalition = TWargameCoalition/NATO; Steel Division 2: Nationalite = ENationalite/Allied
            Side = Text(Find(unit, "Coalition")) ?? Text(Find(unit, "Nationalite")) ?? "",
            Nation = nation,
            Factory = Text(Find(unit, "FactoryType")) ?? Text(Find(unit, "Factory")) ?? "",
            Role = Text(Find(unit, "UnitRole")) ?? Text(Find(unit, "TypeSpecificToken")) ?? "",
            RoleName = texts.Get(Text(Find(unit, "TypeSpecificToken"))),
            Cost = CommandPoints(unit),
            Strength = Number(Find(unit, "MaxPhysicalDamages")) ?? Number(Find(unit, "MaxDamages")),
            Armor = ArmorOf(unit),
            Speed = Number(Find(unit, "MaxSpeedInKmph")) ?? Number(Find(unit, "Maxspeed")),
            RoadSpeed = Number(Find(unit, "DisplayRoadSpeedInKmph")) ?? Number(Find(unit, "RealRoadSpeed")),
            Optics = NumberFor(Find(unit, "OpticalStrengths"), "Standard") ?? Number(Find(unit, "OpticalStrength")),
            Vision = NumberFor(Find(unit, "VisionRangesGRU"), "Standard") ?? Number(Find(unit, "PorteeVision")),
            Stealth = Number(Find(unit, "UnitConcealmentBonus")),
            Fuel = Number(Find(unit, "FuelCapacity")),
            Specialties = ItemsOf(Find(unit, "SpecialtiesList")).Select(Text).OfType<string>().Where(s => s != "").ToList(),
            WeaponSet = weaponSet,
            Weapons = weaponSet is not null && weaponSets.TryGetValue(weaponSet, out var mounts)
                ? mounts.Select(mount => WeaponOf(mount.Ammunition, mount.Count, ammunition)).ToList()
                : [],
            Divisions = divisions.TryGetValue(descriptor, out List<string>? inDivisions) ? inDivisions : [],
            PictureSource = pictureKey is not null && pictures.TryGetValue(pictureKey, out string? source) ? source : null,
        };
    }

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
    private static double? CommandPoints(ObjectValue unit)
    {
        foreach (var (key, value) in PairsOf(Find(unit, "ProductionRessourcesNeeded")))
            if (Text(key) == "Resource_CommandPoints") return Number(value);
        return null;
    }

    /// <summary>
    /// The armour on each side. WARNO writes ResistanceFront = TResistanceTypeRTTI(Family=... Index=18); Steel
    /// Division 2 writes ArmorDescriptorFront = ArmorDescriptor_Blindage_22, where the number is the level and a
    /// name without one (such as ArmorDescriptor_Toit_Lourd) is 0.
    /// </summary>
    private static UnitArmor ArmorOf(ObjectValue unit)
    {
        int? Side(string side)
        {
            if (Find(unit, "Resistance" + side) is ObjectValue resistance) return (int?)Number(Find(resistance, "Index"));
            if (Text(Find(unit, "ArmorDescriptor" + side)) is string armor)
            {
                Match level = Regex.Match(armor, "_([0-9]+)\\z");
                return level.Success ? int.Parse(level.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            }
            return null;
        }
        return new UnitArmor(Side("Front"), Side("Sides"), Side("Rear"), Side("Top"));
    }

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

    /// <summary>What the Units tab shows of an ammunition.</summary>
    private sealed record AmmunitionStats(
        string File, string Family, int Level, double? Range, double? Damage, double? Suppression, double? Salvo, string? Missile);

    /// <summary>Each weapon set in WeaponDescriptor.ndf: its weapons' ammunition, and how many of each.</summary>
    private static Dictionary<string, List<(string Ammunition, int Count)>> ReadWeaponSets(GameDataFiles files, List<string> problems)
    {
        var sets = new Dictionary<string, List<(string, int)>>(StringComparer.Ordinal);
        foreach (Declaration declaration in Declarations(files, "Generated/Gameplay/Gfx/WeaponDescriptor.ndf", name => name.StartsWith("WeaponDescriptor_", StringComparison.Ordinal), problems))
        {
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

    /// <summary>Every ammunition, from Ammunition.ndf and (WARNO's missiles) AmmunitionMissiles.ndf.</summary>
    private static Dictionary<string, AmmunitionStats> ReadAmmunition(GameDataFiles files, List<string> problems)
    {
        var all = new Dictionary<string, AmmunitionStats>(StringComparer.Ordinal);
        foreach (string file in new[] { "Ammunition.ndf", "AmmunitionMissiles.ndf" })
        {
            foreach (Declaration declaration in Declarations(files, "Generated/Gameplay/Gfx/" + file, name => name.StartsWith("Ammo_", StringComparison.Ordinal), problems, mustExist: file == "Ammunition.ndf"))
                if (declaration.Value is ObjectValue ammunition)
                    all[declaration.Name] = AmmunitionOf(ammunition, file);
        }
        return all;
    }

    private static AmmunitionStats AmmunitionOf(ObjectValue ammunition, string file)
    {
        (string family, int level) = DamageOf(Own(ammunition, "Arme"));
        // the longest of its ranges: against the ground, helicopters and planes (WARNO, in GRU), or as Steel Division 2
        // writes them (in metres)
        double? range = new[] { "MaximumRangeGRU", "MaximumRangeHelicopterGRU", "MaximumRangeAirplaneGRU", "PorteeMaximale", "PorteeMaximaleTBA", "PorteeMaximaleHA" }
            .Select(name => Number(Own(ammunition, name))).Where(value => value > 0).DefaultIfEmpty().Max();
        return new AmmunitionStats(
            file, family, level, range,
            Number(Own(ammunition, "PhysicalDamages")),
            Number(Own(ammunition, "SuppressDamages")),
            Number(Own(ammunition, "TimeBetweenTwoSalvos")) ?? Number(Own(ammunition, "TempsEntreDeuxSalves")),
            Text(Own(ammunition, "MissileDescriptor")));
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

    private static UnitWeapon WeaponOf(string ammunition, int count, Dictionary<string, AmmunitionStats> all)
    {
        string name = (ammunition.StartsWith("Ammo_", StringComparison.Ordinal) ? ammunition[5..] : ammunition).Replace('_', ' ');
        if (!all.TryGetValue(ammunition, out AmmunitionStats? stats))
            return new UnitWeapon(name, count, "", 0, null, null, null, null, ammunition, null, null);
        return new UnitWeapon(name, count, stats.Family, stats.Level, stats.Range, stats.Damage, stats.Suppression, stats.Salvo, ammunition, stats.File, stats.Missile);
    }

    // ---- divisions ----

    /// <summary>
    /// The multiplayer divisions each unit is in (by the unit's descriptor): the divisions in Divisions.ndf tagged
    /// DEFAULT, named as the game names them, and their units from DivisionRules.ndf. WARNO gives each division a
    /// rule of its own (DivisionRule = ..._Rule); Steel Division 2 keeps them all in one MAP, by division.
    /// </summary>
    private static Dictionary<string, List<string>> ReadDivisions(GameDataFiles files, GameTexts texts, List<string> problems)
    {
        var namesByDivision = new Dictionary<string, string>(StringComparer.Ordinal);
        var namesByRule = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Declaration declaration in Declarations(files, "Generated/Gameplay/Decks/Divisions.ndf", _ => true, problems))
        {
            if (declaration.Value is not ObjectValue { TypeName: "TDeckDivisionDescriptor" } division) continue;
            bool forMultiplayer = ItemsOf(Own(division, "DivisionTags")).Select(Text).Contains("DEFAULT");
            if (!forMultiplayer) continue;
            string name = texts.Get(Text(Own(division, "DivisionName"))) ?? texts.Get(Text(Own(division, "DivisionNickname")))
                ?? DivisionNameFromDescriptor(Text(Own(division, "CfgName")) ?? declaration.Name);
            namesByDivision[declaration.Name] = name;
            if (Text(Own(division, "DivisionRule")) is string rule) namesByRule[rule] = name;
        }

        var unitsIn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void AddUnits(NdfValue rule, string divisionName)
        {
            foreach (ObjectValue unitRule in AllObjects(rule))
                if (Text(Own(unitRule, "UnitDescriptor")) is string unit)
                {
                    if (!unitsIn.TryGetValue(unit, out List<string>? list)) unitsIn[unit] = list = [];
                    if (!list.Contains(divisionName)) list.Add(divisionName);
                }
        }
        foreach (Declaration declaration in Declarations(files, "Generated/Gameplay/Decks/DivisionRules.ndf", _ => true, problems))
        {
            if (namesByRule.TryGetValue(declaration.Name, out string? divisionName)) AddUnits(declaration.Value, divisionName);
            foreach (var (key, rule) in PairsOf(Find(declaration.Value, "DivisionRules")))
                if (Text(key) is string division && namesByDivision.TryGetValue(division, out string? name)) AddUnits(rule, name);
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
    private static IEnumerable<Declaration> Declarations(GameDataFiles files, string endOfPath, Func<string, bool> wanted, List<string> problems, bool mustExist = true)
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

    /// <summary>An object's own property of this name (not one inside it); null when it has none.</summary>
    private static NdfValue? Own(ObjectValue o, string name) =>
        o.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))?.Value;
}
