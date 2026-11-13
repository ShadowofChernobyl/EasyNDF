using System.Globalization;
using System.Text.RegularExpressions;
using EasyNdf.Data.Effects;
using EasyNdf.Data.Units;
using EasyNdf.Engine.Ndf;
using static EasyNdf.Data.Units.NdfValues;

namespace EasyNdf.Data.Models;

/// <summary>
/// Reads a unit's depiction (<see cref="UnitDepiction"/>) from the game's files: its registration (found by its
/// BlackHoleKey), the operators and actions it lists, the weapon blocks those name, what hangs on it, and the sounds
/// behind them. Each file is read and parsed once, the first time it is needed, and kept while the reader is.
/// </summary>
/// <param name="files">The game's files, with a game mod's own copies first.</param>
public sealed partial class DepictionReader(GameDataFiles files)
{
    private const string VehiclesFile = "Generated/Gameplay/Gfx/Depictions/DepictionVehicles.ndf";
    private const string AircraftFile = "Generated/Gameplay/Gfx/Depictions/DepictionAerialUnits.ndf";
    private const string OperatorsFile = "Gameplay/Gfx/Units/DepictionOperators.ndf";
    private const string WeaponBlocksFile = "Generated/Gameplay/Gfx/DepictionWeaponBlock.ndf";
    private const string WeaponEffectsFile = "Generated/Gameplay/Gfx/DepictionFXWeapons.ndf";
    private static readonly string[] SoundFiles =
        ["Generated/Sound/SoundDescriptors.ndf", "Generated/Sound/SM_Drive.ndf", "Generated/Sound/SM_Fly.ndf", "Generated/Sound/SM_Constant.ndf", "Generated/Sound/ST_Generic.ndf"];
    private const string SoundSettingsFile = "Sound/Settings/SoundSettings.ndf";
    private const string CarriageFile = "Generated/Gameplay/Gfx/MissileCarriage.ndf";
    private const string HangingFile = "Generated/Gameplay/Gfx/MissileCarriageDepiction.ndf";
    private const string PylonsFile = "Gameplay/Gfx/Units/Pylons.ndf";

    private readonly Dictionary<string, Dictionary<string, Declaration>> declarationsByFile = new(StringComparer.Ordinal);

    /// <summary>Each file's text as read, for an action written out in it (a rotor's blur), which the effects' reader reads.</summary>
    private readonly Dictionary<string, string> textByFile = new(StringComparer.Ordinal);

    /// <summary>
    /// Lets go of the depiction files read (some 24 MB for the M1A1's): a depiction is read once, when the viewer opens.
    /// </summary>
    public void Release()
    {
        declarationsByFile.Clear();
        textByFile.Clear();
    }

    /// <summary>A unit's depiction by its BlackHoleKey (such as "M1A1_Abrams_US"); null when no registration has that key.</summary>
    public UnitDepiction? Read(string key)
    {
        foreach (string file in new[] { VehiclesFile, AircraftFile })
        {
            Dictionary<string, Declaration> declarations = Declarations(file);
            ObjectValue? registration = declarations.Values.Select(d => d.Value).OfType<ObjectValue>()
                .FirstOrDefault(o => o.TypeName.EndsWith("DepictionRegistration", StringComparison.Ordinal) && Text(Own(o, "BlackHoleKey")) == key);
            if (registration is not null) return ReadRegistration(registration, declarations, file);
        }
        return null;
    }

    private UnitDepiction ReadRegistration(ObjectValue registration, Dictionary<string, Declaration> declarations, string file)
    {
        var weaponOperators = new List<(string Tag, IReadOnlyList<string> Anchors, bool Continuous)>();
        var recoils = new Dictionary<int, GunRecoil>();
        var rotors = new List<UnitRotor>();
        var carriages = new List<string>();
        // a missile carriage's weapons by their WeaponIndex there, as its firing operators name the unit's weapon for each
        var carriedWeapons = new Dictionary<int, int>();
        GatlingBarrels? gatling = null;
        EngineSounds? engine = null;
        TurretSounds? turret = null;
        // the unit's own operators and actions, then those of its sub-models (a launcher's turret aims and fires there)
        var operatorLists = new List<NdfValue?> { Own(registration, "Operators") };
        var actionLists = new List<NdfValue?> { Own(registration, "Actions") };
        var subModels = new List<SubModel>();
        foreach (ObjectValue sub in SubDepictionsOf(Own(registration, "SubDepictions"), declarations))
        {
            if (Own(sub, "Depiction") is not ObjectValue drawn) continue;
            string? anchor = ItemsOf(Own(sub, "Anchors")).Select(Text).OfType<string>().FirstOrDefault();
            string? model = AllObjects(Own(drawn, "DepictionAlternatives") ?? drawn).Select(each => Text(Own(each, "MeshDescriptor"))).OfType<string>().FirstOrDefault();
            if (anchor is null || model is null) continue;
            subModels.Add(new SubModel(anchor, LastPart(model)));
            operatorLists.Add(Own(drawn, "Operators"));
            actionLists.Add(Own(drawn, "Actions"));
        }
        foreach (NdfValue item in operatorLists.SelectMany(ItemsOf))
        {
            (string name, ObjectValue? operation) = Resolve(item, declarations);
            if (operation is null) continue;
            if (Text(Value(operation, "Connoisseur")) is string connoisseur)
            {
                carriages.Add(LastPart(connoisseur));
                if (Number(Value(operation, "WeaponIndex")) is double carried && Text(Own(operation, "FireEffectTag")) is string carriedTag && TagNumber(carriedTag) > 0)
                    carriedWeapons[(int)carried] = TagNumber(carriedTag);
            }
            if (Own(operation, "FireEffectTag") is NdfValue tag && Text(tag) is string fireTag)
                weaponOperators.Add((fireTag, [.. ItemsOf(Own(operation, "Anchors")).Select(Text).OfType<string>()], TypeChain(operation).Any(t => t.Contains("ContinuousFire", StringComparison.Ordinal))));
            else if (RecoilName().Match(name) is { Success: true } recoilName && RecoilOf(operation) is GunRecoil recoil)
                recoils[int.Parse(recoilName.Groups[1].Value, CultureInfo.InvariantCulture)] = recoil;
            else if (operation.TypeName == "DepictionOperator_Rotors")
                rotors.AddRange(ItemsOf(Own(operation, "HelixList")).OfType<ObjectValue>().Select(RotorOf).OfType<UnitRotor>());
            else if (TypeChain(operation).Any(t => t.StartsWith("DepictionOperator_TurretCannonGatling", StringComparison.Ordinal) || t == "TCosmeticCannonGatlingOperatorDesc"))
                gatling = GatlingOf(operation);
            else if (name.StartsWith("DepictionOperator_MovementSound_", StringComparison.Ordinal))
                engine = EngineOf(operation);
            else if (name.StartsWith("DepictionOperator_TurretSound_", StringComparison.Ordinal))
                turret = TurretSoundsOf(operation);
        }
        // a rotor's blur is an action of the registration's own, written out in place: Template_DepictionAction_Rotor( ... )
        Dictionary<string, NdfValue> written = WrittenActions(Own(registration, "Actions"));
        for (int i = 0; i < rotors.Count; i++)
            if (rotors[i].BlurAction is string blurAction && written.TryGetValue(blurAction, out NdfValue? blur) && WrittenText(file, blur) is string blurText)
                rotors[i] = rotors[i] with { Blur = FxAttachments.ReadWritten(files, blurText).FirstOrDefault() };
        // each weapon's effects and sound: the weapon block its effect tag names in the actions
        var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (NdfValue? actions in actionLists)
            foreach ((string tag, string block) in WeaponBlocks(actions)) blocks.TryAdd(tag, block);
        var weapons = new Dictionary<int, DepictedWeapon>();
        foreach ((string tag, IReadOnlyList<string> anchors, bool continuous) in weaponOperators)
        {
            int number = TagNumber(tag);
            if (number <= 0 || weapons.ContainsKey(number)) continue;
            weapons[number] = new DepictedWeapon(number, anchors, continuous, blocks.TryGetValue(tag, out string? block) ? LookOf(block) : null);
        }
        return new UnitDepiction(rotors, engine, turret)
        {
            Weapons = weapons,
            Recoils = recoils,
            Gatling = gatling,
            MovementEffects = [.. AddedActions(Own(registration, "Actions")).Where(name => name.StartsWith("DepictionAction_MovementFX_", StringComparison.Ordinal))],
            SubModels = subModels,
            // the carriage numbers its weapons its own way: the Apache's rockets are its weapon 2, the unit's weapon 4
            Hung = [.. HungWeapons(carriages, ItemsOf(Own(registration, "SubDepictionGenerators")).Select(Text).OfType<string>().Select(LastPart))
                .Select(weapon => carriedWeapons.TryGetValue(weapon.WeaponIndex, out int number) ? weapon with { WeaponIndex = number } : weapon)],
        };
    }

    /// <summary>The lists of actions added to the registration's Actions ("+ DepictionAction_..."), by name.</summary>
    private static IEnumerable<string> AddedActions(NdfValue? actions) => actions switch
    {
        BinaryValue { Operator: "+" } sum => AddedActions(sum.Left).Concat(AddedActions(sum.Right)),
        ListValue => [],
        _ => Text(actions) is string name ? [name] : [],
    };

    /// <summary>The weapon blocks in a registration's Actions, by their effect tags ("weapon_effet_tag1", Weapon_...).</summary>
    private static IEnumerable<(string Tag, string Block)> WeaponBlocks(NdfValue? actions)
    {
        if (actions is BinaryValue { Operator: "+" } sum)
        {
            foreach ((string, string) each in WeaponBlocks(sum.Left).Concat(WeaponBlocks(sum.Right))) yield return each;
        }
        else if (actions is ListValue)
        {
            foreach ((NdfValue key, NdfValue block) in PairsOf(actions))
                if (Text(key) is string tag && Text(block) is string name) yield return (tag, name);
        }
    }

    /// <summary>The actions written out in the registration's Actions, by their tags ("FX_Helice_1", Template_DepictionAction_Rotor( ... )).</summary>
    private static Dictionary<string, NdfValue> WrittenActions(NdfValue? actions)
    {
        var written = new Dictionary<string, NdfValue>(StringComparer.Ordinal);
        void Walk(NdfValue? value)
        {
            if (value is BinaryValue { Operator: "+" } sum)
            {
                Walk(sum.Left);
                Walk(sum.Right);
            }
            else if (value is ListValue)
                foreach ((NdfValue key, NdfValue block) in PairsOf(value))
                    if (Text(key) is string tag && block is ObjectValue) written.TryAdd(tag, block);
        }
        Walk(actions);
        return written;
    }

    /// <summary>A value as it is written in a file read; null when it was not read from that file.</summary>
    private string? WrittenText(string file, NdfValue value) =>
        textByFile.TryGetValue(file, out string? text) && value.Start is int start && value.End is int end && start >= 0 && end <= text.Length && start < end ? text[start..end] : null;

    // ---- what hangs on the model ----

    /// <summary>
    /// The registration's sub-depictions written out in place (TSubDepiction( Anchors = [...] Depiction = ... )); the
    /// lists added to them by name (HumanSubDepictions_..., its crew) are left out.
    /// </summary>
    private static IEnumerable<ObjectValue> SubDepictionsOf(NdfValue? value, Dictionary<string, Declaration> declarations)
    {
        switch (value)
        {
            case BinaryValue { Operator: "+" } sum:
                foreach (ObjectValue each in SubDepictionsOf(sum.Left, declarations).Concat(SubDepictionsOf(sum.Right, declarations))) yield return each;
                break;
            case ListValue list:
                foreach (NdfValue item in list.Items)
                    if (item is ObjectValue { TypeName: "TSubDepiction" } sub) yield return sub;
                break;
        }
    }

    /// <summary>
    /// The munitions the unit hangs: for each of its missile carriages (named by its weapons' operators and its
    /// sub-depiction generators), each weapon the carriage hangs, with the model the generators hang of it (or a pod,
    /// for rockets) and the pylons its pylon set gives. A weapon whose missiles the unit's model already has (an Ataka's
    /// tubes), which the generators leave out, hangs nothing.
    /// </summary>
    private List<HungWeapon> HungWeapons(List<string> carriages, IEnumerable<string> generators)
    {
        var models = new Dictionary<int, (string Model, int Count)>();
        foreach (string generator in generators)
        {
            if (!Declarations(HangingFile).TryGetValue(generator, out Declaration? declared) || declared.Value is not ObjectValue hanging) continue;
            if (Text(Own(hanging, "MissileCarriageConnoisseur")) is string connoisseur) carriages.Add(LastPart(connoisseur));
            foreach (ObjectValue missile in ItemsOf(Own(hanging, "Missiles")).OfType<ObjectValue>())
            {
                if (Number(Own(missile, "WeaponIndex")) is not double index || Own(missile, "Depiction") is not ObjectValue drawn || Text(Own(drawn, "ProjectileModelResource")) is not string model) continue;
                // a static generator gives the count; another lists each missile on its own
                int count = (int)(Number(Own(missile, "MissileCount")) ?? 1);
                models[(int)index] = models.TryGetValue((int)index, out var known) && Number(Own(missile, "MissileCount")) is null ? (known.Model, known.Count + 1) : (LastPart(model), count);
            }
        }
        var hung = new List<HungWeapon>();
        foreach (string carriage in carriages.Distinct(StringComparer.Ordinal))
        {
            if (!Declarations(CarriageFile).TryGetValue(carriage, out Declaration? declared) || declared.Value is not ObjectValue connoisseur) continue;
            string? pylonSet = Text(Own(connoisseur, "PylonSet")) is string set ? LastPart(set) : null;
            var counted = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ObjectValue info in ItemsOf(Own(connoisseur, "WeaponInfos")).OfType<ObjectValue>())
            {
                string prefix = Text(Own(info, "MissileType")) == "eAAM" ? "aa" : "as";
                counted[prefix] = counted.GetValueOrDefault(prefix) + 1;
                if (Number(Own(info, "WeaponIndex")) is not double index) continue;
                string mounting = Text(Own(info, "MountingType")) ?? "";
                (string Model, int Count)? hangs = models.TryGetValue((int)index, out var found) ? found : null;
                if (hangs is null && mounting != "eMountingPod") continue;
                int count = hangs?.Count ?? (int)(Number(Own(info, "MissileCount")) ?? 0);
                hung.Add(new HungWeapon((int)index, $"{prefix}_{counted[prefix]}_", count, mounting, hangs?.Model,
                    pylonSet is null ? [] : PylonModels(pylonSet, mounting), Text(Own(info, "MeshDescriptor")) is string inModel ? LastPart(inModel) : null));
            }
        }
        return hung;
    }

    /// <summary>
    /// The pylon or pod models a pylon set (a TMultiPylonSet in Pylons.ndf) gives a mounting: its PodPylonSet for pods,
    /// its BombPylonSet for bombs, else its DefaultPylonSet; each a list of models (ModelResource = ...), in order.
    /// </summary>
    private List<string> PylonModels(string pylonSet, string mounting)
    {
        var given = new Dictionary<string, NdfValue>(StringComparer.Ordinal);
        if (Filled(Declarations(PylonsFile).TryGetValue(pylonSet, out Declaration? declared) ? declared.Value : null, given) is not ObjectValue sets) return [];
        string key = mounting switch { "eMountingPod" => "PodPylonSet", "eMountingBomb" => "BombPylonSet", _ => "DefaultPylonSet" };
        var models = new List<string>();
        CollectPylons(Own(sets, key) ?? Own(sets, "DefaultPylonSet"), given, models, 0);
        return models;
    }

    /// <summary>
    /// A value of the pylons file as it stands: a name followed to its declaration, a template's use replaced by the
    /// template's body (its parameters, written &lt;Name&gt;, kept in <paramref name="given"/>), a parameter by its value.
    /// With <paramref name="keepPylons"/>, a use that names its model (PylonDepictionCommon( ModelResource = ... )) is kept.
    /// </summary>
    private NdfValue? Filled(NdfValue? value, Dictionary<string, NdfValue> given, bool keepPylons = false)
    {
        Dictionary<string, Declaration> pylons = Declarations(PylonsFile);
        for (int depth = 0; depth < 12 && value is not null; depth++)
        {
            if (Text(value) is string text)
            {
                if (text.StartsWith('<') && given.TryGetValue(text.Trim('<', '>'), out NdfValue? parameter)) value = parameter;
                else if (pylons.TryGetValue(LastPart(text), out Declaration? named)) value = named.Value;
                else return value;
            }
            else if (value is ObjectValue use && !(keepPylons && Own(use, "ModelResource") is not null) && pylons.TryGetValue(use.TypeName, out Declaration? template) && template.Prefix.Contains("template", StringComparison.Ordinal))
            {
                foreach (Property property in use.Properties) given[property.Name] = property.Value;
                value = template.Value;
            }
            else return value;
        }
        return value;
    }

    private void CollectPylons(NdfValue? value, Dictionary<string, NdfValue> given, List<string> models, int depth)
    {
        if (depth > 8) return;
        NdfValue? filled = Filled(value, given, keepPylons: true);
        if (filled is ListValue list)
            foreach (NdfValue item in list.Items) CollectPylons(item, given, models, depth + 1);
        else if (filled is ObjectValue pylonOrSet)
        {
            if (Own(pylonOrSet, "ModelResource") is NdfValue resource && Text(Filled(resource, given)) is string model) models.Add(LastPart(model));
            else CollectPylons(Own(pylonOrSet, "Pylons"), given, models, depth + 1);
        }
    }

    /// <summary>"weapon_effet_tag3" is 3; 0 for a tag of another kind.</summary>
    private static int TagNumber(string tag) =>
        TagNumberPattern().Match(tag) is { Success: true } match ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;

    /// <summary>
    /// An operator as listed: a name (a declaration of the same file, of the game's operator templates, or a sound
    /// operator), or an object written out in place.
    /// </summary>
    private (string Name, ObjectValue? Operation) Resolve(NdfValue item, Dictionary<string, Declaration> declarations)
    {
        if (item is ObjectValue written) return (written.TypeName, written);
        if (Text(item) is not string name) return ("", null);
        if (declarations.TryGetValue(name, out Declaration? own) && own.Value is ObjectValue ownObject) return (name, ownObject);
        foreach (string file in SoundFiles.Prepend(OperatorsFile))
            if (Declarations(file).TryGetValue(name, out Declaration? found) && found.Value is ObjectValue foundObject) return (name, foundObject);
        return (name, null);
    }

    /// <summary>A template of the game's operators (DepictionOperators.ndf) by name; null when it is not one.</summary>
    private ObjectValue? Template(string name) =>
        Declarations(OperatorsFile).TryGetValue(name, out Declaration? template) && template.Value is ObjectValue value ? value : null;

    /// <summary>
    /// A value of an operator: its own, else its template's (and that template's template). A template's default for a
    /// value given as a parameter ("&lt;WeaponIndex&gt;") is the operator's own.
    /// </summary>
    private NdfValue? Value(ObjectValue operation, string name)
    {
        foreach (ObjectValue level in Levels(operation))
            if (Own(level, name) is NdfValue value && !(Text(value) is string text && text.StartsWith('<'))) return value;
        return null;
    }

    private IEnumerable<ObjectValue> Levels(ObjectValue operation)
    {
        ObjectValue? level = operation;
        for (int depth = 0; depth < 8 && level is not null; depth++)
        {
            yield return level;
            level = Template(level.TypeName);
        }
    }

    /// <summary>An object's type, and the types of the templates it is made from, in turn: a template is an object type too.</summary>
    private IEnumerable<string> TypeChain(ObjectValue operation)
    {
        string type = operation.TypeName;
        for (int depth = 0; depth < 8 && type.Length > 0; depth++)
        {
            yield return type;
            type = Template(type)?.TypeName ?? "";
        }
    }

    private GunRecoil? RecoilOf(ObjectValue operation)
    {
        string? node = Text(Value(operation, "RecoilNode"));
        double? distance = Number(Value(operation, "MaxRecoil")), speed = Number(Value(operation, "RecoilSpeed")), peak = Number(Value(operation, "RecoilPeak"));
        return node is null || distance is null || speed is null ? null : new GunRecoil(node, distance.Value, speed.Value, peak ?? 0.5);
    }

    private GatlingBarrels? GatlingOf(ObjectValue operation)
    {
        string? node = Text(Value(operation, "XAxisRotationNode"));
        return node is null ? null : new GatlingBarrels(node, Number(Value(operation, "RotationSpeed")) ?? 25, Number(Value(operation, "RotationAcceleration")) ?? 1);
    }

    private static UnitRotor? RotorOf(ObjectValue helix)
    {
        string? node = Text(Own(helix, "HelixBoneName"));
        return node is null ? null : new UnitRotor(node, (int)(Number(Own(helix, "RotationAxis")) ?? 2), Number(Own(helix, "RotationSpeed")) ?? 0, Text(Own(helix, "Clockwise")) == "True",
            Text(Own(helix, "BladesBoneName")), BlurAction: Text(Own(helix, "BlurActionId")));
    }

    private EngineSounds EngineOf(ObjectValue operation)
    {
        SoundChoice? List(string name) => SoundOfDescriptors(ItemsOf(Own(operation, name)).OfType<ObjectValue>());
        // an engine that only ever runs (an aircraft's constant sound) has one Sound, or SoundDescriptor
        SoundChoice? running = List("RunSounds") ?? List("Sounds") ?? (Own(operation, "Sound") is ObjectValue single ? SoundOfDescriptors([single]) : null);
        double fadeIn = Number(Own(operation, "RunFadeInDuration")) ?? Number(Own(operation, "FadeInDuration")) ?? 0.5;
        double fadeOut = Number(Own(operation, "RunFadeOutDuration")) ?? Number(Own(operation, "FadeOutDuration")) ?? 1;
        return new EngineSounds(List("StartSounds"), running, List("IdleSounds"), List("StopSounds"), fadeIn, fadeOut);
    }

    private TurretSounds TurretSoundsOf(ObjectValue operation)
    {
        SoundChoice? One(string name) => Own(operation, name) is ObjectValue descriptor ? SoundOfDescriptors([descriptor]) : null;
        return new TurretSounds(Text(Own(operation, "TurretAxisName")) ?? "tourelle1", One("StartSound"), One("RotateSound"), One("StopSound"));
    }

    /// <summary>
    /// TSoundDescriptors: each one's file (TheSoundStream's FileName, "GameData:/Assets/Sounds/....wav", kept in the
    /// archives as AllPlatforms/Sound/Assets/Sounds/....ess), and the pitch its sound settings give. Null for none.
    /// </summary>
    private SoundChoice? SoundOfDescriptors(IEnumerable<ObjectValue> descriptors)
    {
        var paths = new List<string>();
        double pitch = 1, randomPitch = 0;
        foreach (ObjectValue descriptor in descriptors)
        {
            if (Own(descriptor, "TheSoundStream") is not ObjectValue stream || Text(Own(stream, "FileName")) is not string file) continue;
            paths.Add(ArchivePathOf(file));
            if (Text(Own(descriptor, "SoundSettings")) is string settingsName && SettingsObject(settingsName) is ObjectValue settings)
            {
                pitch = Number(Own(settings, "Pitch")) ?? pitch;
                randomPitch = Number(Own(settings, "RandomPitch")) ?? randomPitch;
            }
        }
        return paths.Count == 0 ? null : new SoundChoice(paths, 1, pitch, randomPitch);
    }

    // ---- weapons' effects and sounds ----

    /// <summary>
    /// A weapon block (a TCompositeHappening in DepictionWeaponBlock.ndf): its muzzle effects (FXWeapon_...) and its
    /// sound (a SoundHappening_...).
    /// </summary>
    private WeaponLook? LookOf(string block)
    {
        if (!Declarations(WeaponBlocksFile).TryGetValue(block, out Declaration? declaration) || declaration.Value is not ObjectValue composite) return null;
        var firing = new List<string>();
        SoundChoice? sound = null;
        foreach (NdfValue part in ItemsOf(Own(composite, "SubHappenings")))
        {
            if (Text(part) is not string name) continue;
            if (name.StartsWith("SoundHappening_", StringComparison.Ordinal)) sound ??= SoundOfHappening(name);
            else FiringEffectsOf(name, firing);
        }
        return new WeaponLook(block, firing, sound);
    }

    /// <summary>
    /// A weapon's muzzle effects (FXWeapon_... in DepictionFXWeapons.ndf): each FXFiring_N( Action = $/GFX/GameFx/fx_tir_... ),
    /// inside random choices or not. Its projectiles' (FXProjectile_N) are left out: the viewer shows no shots in flight.
    /// </summary>
    private void FiringEffectsOf(string name, List<string> firing)
    {
        Dictionary<string, Declaration> declarations = Declarations(WeaponEffectsFile);
        if (!declarations.TryGetValue(name, out Declaration? declaration)) return;
        NdfValue value = declaration.Value;
        for (int depth = 0; depth < 4 && Text(value) is string other && declarations.TryGetValue(LastPart(other), out Declaration? named); depth++) value = named.Value;
        foreach (ObjectValue each in AllObjects(value))
            if (each.TypeName.StartsWith("FXFiring", StringComparison.Ordinal) && Text(Own(each, "Action")) is string action) firing.Add(action);
    }

    /// <summary>A TSoundHappening: its sound set (SoundDescList) and Volume.</summary>
    private SoundChoice? SoundOfHappening(string name)
    {
        ObjectValue? happening = SoundDeclaration(name);
        if (happening is null || Text(Own(happening, "SoundDescList")) is not string setName || SoundDeclaration(setName) is not ObjectValue set) return null;
        SoundChoice? choice = SoundOfDescriptors(ItemsOf(Own(set, "Items")).OfType<ObjectValue>());
        return choice is null ? null : choice with { Volume = Number(Own(happening, "Volume")) ?? 1 };
    }

    private ObjectValue? SoundDeclaration(string name)
    {
        foreach (string file in SoundFiles)
            if (Declarations(file).TryGetValue(name, out Declaration? found) && found.Value is ObjectValue value) return value;
        return null;
    }

    private ObjectValue? SettingsObject(string name) =>
        Declarations(SoundSettingsFile).TryGetValue(name, out Declaration? found) && found.Value is ObjectValue value ? value : null;

    /// <summary>"GameData:/Assets/Sounds/SFX/A.wav" as the archives keep it: "AllPlatforms/Sound/Assets/Sounds/SFX/A.ess".</summary>
    public static string ArchivePathOf(string gameFile)
    {
        string path = gameFile.Replace('\\', '/');
        if (path.StartsWith("GameData:/", StringComparison.OrdinalIgnoreCase)) path = path["GameData:/".Length..];
        return "AllPlatforms/Sound/" + Path.ChangeExtension(path, ".ess");
    }

    /// <summary>Whether an archive path is a sound the viewer may play.</summary>
    public static bool IsSoundFile(string path) =>
        path.StartsWith("AllPlatforms/Sound/Assets/Sounds/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".ess", StringComparison.OrdinalIgnoreCase);

    // ---- the files ----

    /// <summary>A file's declarations by name, parsed the first time; none when the file is missing or cannot be read.</summary>
    private Dictionary<string, Declaration> Declarations(string file)
    {
        if (declarationsByFile.TryGetValue(file, out Dictionary<string, Declaration>? known)) return known;
        var byName = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        try
        {
            if (files.Read(file) is string text)
            {
                textByFile[file] = text;
                foreach (Declaration declaration in Parser.Parse(text).Declarations)
                    if (declaration.Name.Length > 0) byName.TryAdd(declaration.Name, declaration);
                    else byName.TryAdd("unnamed " + byName.Count.ToString(CultureInfo.InvariantCulture), declaration);
            }
        }
        catch (Exception problem) when (problem is NdfParseException or FormatException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            // a file the parser cannot read gives nothing, as a missing file does
        }
        declarationsByFile[file] = byName;
        return byName;
    }

    [GeneratedRegex(@"^DepictionOperator_Turret_([0-9]+)_HydraulicRecoil")]
    private static partial Regex RecoilName();

    [GeneratedRegex(@"weapon_effet_tag([0-9]+)\z")]
    private static partial Regex TagNumberPattern();

}
