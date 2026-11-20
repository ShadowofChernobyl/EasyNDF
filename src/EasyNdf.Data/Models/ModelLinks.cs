using System.Text.RegularExpressions;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Models;

/// <summary>
/// Finds the 3D models of a unit or a missile in the game's files: a model's descriptor (Modele_...) is a TResourceMesh
/// in GameData/Gameplay/Gfx/DepictionResources, whose Mesh is the model's path, written out or as a constant of those
/// files ("ModelFile_M1A1_Abrams_US", "US_AB_1", each "GameData:/....fbx"), and whose Textures, when it has one, is the
/// folder its pictures come from (one army's model painted for another's). A vehicle names its descriptor
/// (<see cref="UnitLook.Mesh"/>); an infantry squad's soldiers are listed in DepictionInfantry.ndf, up to ten models to
/// a squad; a missile names its descriptor in MissileDescriptors.ndf.
/// </summary>
/// <param name="files">The game's files, with a game mod's own copies first.</param>
public sealed partial class ModelLinks(GameDataFiles files)
{
    private const string ResourcesFolder = "/Gameplay/Gfx/DepictionResources/";

    /// <summary>
    /// Each model descriptor's Mesh as written: a path, or the name of a constant holding one, then what is added to it
    /// ("" for nothing; Modele_AEC_Militant_UK_Path+"/AEC_Militant.fbx" adds a file to a folder).
    /// </summary>
    private readonly Dictionary<string, (string Value, string Added)> meshes = new(StringComparer.Ordinal);

    /// <summary>Each model descriptor's Textures, for those that have one: a folder, such as "Assets/3D/Units/COL/Avion/SilverStar_Mk3AT".</summary>
    private readonly Dictionary<string, string> textureFolders = new(StringComparer.Ordinal);

    /// <summary>The constants holding a path, such as US_AB_1, "Assets/3D/Units/US/Infanterie/Airborne/Airborne_1.fbx".</summary>
    private readonly Dictionary<string, string> pathConstants = new(StringComparer.Ordinal);

    private readonly HashSet<string> filesRead = new(StringComparer.OrdinalIgnoreCase);
    private List<string>? resourceFiles;

    /// <summary>
    /// A unit's models, as their paths in the game ("Assets/3D/....fbx") with their pictures' folders: one for a vehicle
    /// or an aircraft, one for each soldier of an infantry squad; none when they cannot be found.
    /// </summary>
    public IReadOnlyList<ModelFile> ModelsOf(UnitLook look)
    {
        if (look.Mesh is not null && MeshPathOf(look.Mesh) is ModelFile file) return [file];
        // a squad's soldiers are its squad module's BlackHoleKey's (a mod's zombies keep another squad's MimeticName),
        // else its MimeticName's
        string? mimeticSquad = look.Mimetic is null ? null : look.Mimetic.StartsWith("squad_", StringComparison.Ordinal) ? look.Mimetic["squad_".Length..] : look.Mimetic;
        foreach (string? squad in new[] { look.Depiction, mimeticSquad })
        {
            if (squad is null) continue;
            List<ModelFile> soldiers = [.. SoldierDescriptors(squad).Select(MeshPathOf).OfType<ModelFile>().DistinctBy(soldier => soldier.Path + "|" + soldier.Textures, StringComparer.OrdinalIgnoreCase)];
            if (soldiers.Count > 0) return soldiers;
        }
        // else the model its depiction draws up close: Alternatives_<BlackHoleKey> in DepictionAlternatives.ndf
        foreach (string? key in new[] { look.Depiction, look.Mimetic })
            if (key is not null && AlternativesMesh(key) is string descriptor && MeshPathOf(descriptor) is ModelFile drawn) return [drawn];
        // Steel Division 2: the model its depiction draws up close
        if (look.SteelDivisionDepiction is string gfx && SteelDivisionModel(gfx) is string model) return [new ModelFile(model)];
        return [];
    }

    // ---- Steel Division 2 ----

    /// <summary>Steel Division 2's model descriptors' files (FileName), as written: a path, or the name of a constant holding one.</summary>
    private readonly Dictionary<string, string> steelDivisionFiles = new(StringComparer.Ordinal);

    /// <summary>Steel Division 2's constants holding a model's path, such as ModelFile_Tiger_E, "Assets\3D\Units\GER\Char\Tiger_E_GER\Tiger_E_GER.Ase2NdfBin".</summary>
    private readonly Dictionary<string, string> steelDivisionPaths = new(StringComparer.Ordinal);

    private List<string>? steelDivisionUnitFiles;

    /// <summary>
    /// The model a Steel Division 2 depiction (Gfx_Tiger_E, or Gfx_Tiger_E_Autogen in the generated depictions) draws
    /// up close: its first DepictionDescriptor_LOD_High's MeshDescriptor (Modele_Tiger_E), whose FileName is the
    /// model's path ("GameData:Assets\3D\Units\GER\Char\Tiger_E_GER\Tiger_E_GER.Ase2NdfBin") or a constant holding one.
    /// The path is given from "Assets/" on, with forward slashes; null when it cannot be found.
    /// </summary>
    public string? SteelDivisionModel(string gfx)
    {
        steelDivisionUnitFiles ??= [.. files.PathsWithModWhere(path => path.Contains("/Gameplay/Gfx/Units/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase))];
        string stem = gfx["Gfx_".Length..];
        if (stem.EndsWith("_Autogen", StringComparison.Ordinal)) stem = stem[..^"_Autogen".Length];
        // the depiction: in a file of its own name, else among the generated depictions
        IEnumerable<string?> texts = steelDivisionUnitFiles.Where(path => path.EndsWith("/" + stem + "_Depiction.ndf", StringComparison.OrdinalIgnoreCase)).Select(files.ReadPath)
            .Append(files.Read("Generated/Gameplay/Gfx/Depictions/GeneratedDepictionVehicles.ndf"));
        foreach (string? text in texts)
        {
            if (text is null) continue;
            Match declaration = Regex.Match(text, @"(?m)^(?:export\s+)?" + Regex.Escape(gfx) + @"\s+is\b");
            if (!declaration.Success) continue;
            Match next = NextDeclaration().Match(text, declaration.Index + declaration.Length);
            string block = text[declaration.Index..(next.Success ? next.Index : text.Length)];
            Match close = SteelDivisionHigh().Match(block);
            if (!close.Success) close = SteelDivisionAnyMesh().Match(block);
            if (close.Success) return SteelDivisionPathOf(close.Groups[1].Value);
        }
        return null;
    }

    /// <summary>A Steel Division 2 model descriptor's path ("Assets/3D/...Ase2NdfBin"); null when no unit file has it.</summary>
    private string? SteelDivisionPathOf(string descriptor)
    {
        if (SteelDivisionResolved(descriptor) is string known) return known;
        // a vehicle's descriptor usually sits in a file of its own name (Modele_Tiger_E in Tiger_E.ndf)
        string ownFile = "/" + (descriptor.StartsWith("Modele_", StringComparison.Ordinal) ? descriptor["Modele_".Length..] : descriptor) + ".ndf";
        foreach (string path in steelDivisionUnitFiles!.Where(path => path.EndsWith(ownFile, StringComparison.OrdinalIgnoreCase)).Concat(steelDivisionUnitFiles!))
        {
            if (!filesRead.Add(path)) continue;
            if (files.ReadPath(path) is not string text) continue;
            foreach (Match file in SteelDivisionFileLine().Matches(text)) steelDivisionFiles.TryAdd(file.Groups[1].Value, file.Groups[2].Value);
            foreach (Match constant in SteelDivisionPathConstant().Matches(text)) steelDivisionPaths.TryAdd(constant.Groups[1].Value, constant.Groups[2].Value);
            if (SteelDivisionResolved(descriptor) is string found) return found;
        }
        return null;
    }

    /// <summary>A Steel Division 2 descriptor's path once its FileName, and the constant it names if it names one, have been read; null before.</summary>
    private string? SteelDivisionResolved(string descriptor)
    {
        if (!steelDivisionFiles.TryGetValue(descriptor, out string? file)) return null;
        if (file.StartsWith("GameData:", StringComparison.OrdinalIgnoreCase)) return ModdingAssets.Normalised(file);
        return steelDivisionPaths.TryGetValue(file, out string? path) ? ModdingAssets.Normalised(path) : null;
    }

    /// <summary>A Steel Division 2 depiction's close model: DepictionDescriptor_LOD_High( MeshDescriptor = Modele_... ).</summary>
    [GeneratedRegex("""DepictionDescriptor_LOD_High\s*\(\s*MeshDescriptor\s*=\s*~?/?([A-Za-z0-9_]+)""")]
    private static partial Regex SteelDivisionHigh();

    /// <summary>Any model a Steel Division 2 depiction names, for one with no LOD_High.</summary>
    [GeneratedRegex("""MeshDescriptor\s*=\s*~?/?([A-Za-z0-9_]+)""")]
    private static partial Regex SteelDivisionAnyMesh();

    /// <summary>A Steel Division 2 model descriptor (a TResource… such as TResourceMultiMaterialMeshWithLod): its name, then its FileName, a path in quotes or a constant's name.</summary>
    [GeneratedRegex("""(?:export\s+)?([A-Za-z0-9_]+)\s+is\s+TResource\w*\s*\((?:[^()]|\([^()]*\))*?\bFileName\s*=\s*["']?([^"'\s)]+)""")]
    private static partial Regex SteelDivisionFileLine();

    /// <summary>A Steel Division 2 constant holding a model's path: its name, then the path from "GameData:" on.</summary>
    [GeneratedRegex("""(?m)^\s*(?:export\s+)?([A-Za-z0-9_]+)\s+is\s+["'](GameData:[^"']+\.Ase2NdfBin)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex SteelDivisionPathConstant();

    /// <summary>The model a vehicle's or aircraft's depiction draws up close (its LOD_High, else its first); null when it has none.</summary>
    private string? AlternativesMesh(string key)
    {
        string? text = files.Read("Generated/Gameplay/Gfx/Depictions/DepictionAlternatives.ndf");
        if (text is null) return null;
        Match declaration = Regex.Match(text, @"(?m)^(?:export\s+)?Alternatives_" + Regex.Escape(key) + @"\s+is\b");
        if (!declaration.Success) return null;
        Match next = NextDeclaration().Match(text, declaration.Index + declaration.Length);
        string block = text[declaration.Index..(next.Success ? next.Index : text.Length)];
        Match close = AlternativeMesh().Matches(block).FirstOrDefault(match => match.Groups[1].Value.Contains("High", StringComparison.Ordinal))
            ?? AlternativeMesh().Match(block);
        return close.Success ? close.Groups[2].Value : null;
    }

    /// <summary>A missile's model, by its descriptor in MissileDescriptors.ndf (such as "Descriptor_Missile_ATGM_BGM71_TOW"); null when none is found.</summary>
    public ModelFile? MissileModel(string missile)
    {
        string? text = files.Read("Generated/Gameplay/Gfx/MissileDescriptors.ndf");
        if (text is null) return null;
        Match declaration = Regex.Match(text, @"(?m)^(?:export\s+)?" + Regex.Escape(missile) + @"\s+is\b");
        if (!declaration.Success) return null;
        // the first model it names after its own name, before the next declaration
        Match next = NextDeclaration().Match(text, declaration.Index + declaration.Length);
        int end = next.Success ? next.Index : text.Length;
        Match mesh = ReferenceMesh().Match(text[declaration.Index..end]);
        return mesh.Success ? MeshPathOf(mesh.Groups[1].Value) : null;
    }

    /// <summary>
    /// The model of a weapon that is not a missile but has one of its own, by its ammunition's descriptor: a weapon held
    /// by soldiers, or a bomb or rocket pod an aircraft carries. The first unit whose weapons use the ammunition gives it:
    /// for soldiers, the weapon's HandheldEquipmentKey ("WeaponAlternative_N") in the squad's AllWeaponAlternatives_ list in
    /// DepictionInfantry.ndf; for an aircraft, its turret's number (N of Tag "tourelleN") as WeaponIndex in the aircraft's
    /// SubGenerators_Showroom_ in MissileCarriageDepiction.ndf, or for rockets in pods the pod its pylon set gives (see
    /// <see cref="ReadPods"/>). Null when no unit carries a model of it (guns, shells).
    /// </summary>
    public ModelFile? CarriedModel(string ammunition)
    {
        carried ??= ReadCarried();
        foreach ((string unit, string? key, int? number) in carried.Mounts.GetValueOrDefault(ammunition) ?? [])
        {
            if (key is not null && carried.Held.TryGetValue((unit, key), out string? held) && MeshPathOf(held) is ModelFile weapon) return weapon;
            if (number is int n && carried.OnPylons.TryGetValue((unit, n), out string? hung) && MeshPathOf(hung) is ModelFile ordnance) return ordnance;
            if (number is int p && carried.Pods.TryGetValue((unit, p), out string? pod) && MeshPathOf(pod) is ModelFile rockets) return rockets;
        }
        return null;
    }

    /// <summary>What <see cref="CarriedModel"/> reads, once: every unit's weapons, the squads' held weapons and the aircraft's carried ones.</summary>
    private sealed record CarriedModels(
        Dictionary<string, List<(string Unit, string? Key, int? Number)>> Mounts,
        Dictionary<(string Unit, string Key), string> Held,
        Dictionary<(string Unit, int Number), string> OnPylons,
        Dictionary<(string Unit, int Number), string> Pods);

    private CarriedModels? carried;

    private CarriedModels ReadCarried()
    {
        var mounts = new Dictionary<string, List<(string Unit, string? Key, int? Number)>>(StringComparer.Ordinal);
        if (files.Read("Generated/Gameplay/Gfx/WeaponDescriptor.ndf") is string weapons)
        {
            MatchCollection managers = WeaponManager().Matches(weapons);
            for (int i = 0; i < managers.Count; i++)
            {
                string unit = managers[i].Groups[1].Value;
                int end = i + 1 < managers.Count ? managers[i + 1].Index : weapons.Length;
                // each turret's values run to the next turret's start; its Tag ("tourelle2") is the number the carriage
                // lists give its weapons (WeaponIndex), as two guns on one turret share it
                string[] turrets = weapons[managers[i].Index..end].Split("TTurret");
                foreach (string turret in turrets.Skip(1))
                {
                    Match tag = TurretTag().Match(turret);
                    int? number = tag.Success ? int.Parse(tag.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
                    // each mounted weapon's values run to the next one's start
                    foreach (string part in turret.Split("TMountedWeaponDescriptor").Skip(1))
                    {
                        Match ammo = MountAmmunition().Match(part);
                        if (!ammo.Success) continue;
                        Match key = HandheldKey().Match(part);
                        if (!mounts.TryGetValue(ammo.Groups[1].Value, out var list)) mounts[ammo.Groups[1].Value] = list = [];
                        list.Add((unit, key.Success ? key.Groups[1].Value : null, number));
                    }
                }
            }
        }
        var held = new Dictionary<(string Unit, string Key), string>();
        if (files.Read("Generated/Gameplay/Gfx/Infanterie/DepictionInfantry.ndf") is string infantry)
            foreach (Match list in WeaponAlternatives().Matches(infantry))
            {
                // the list ends at the first "]" alone on its line, after the selectors' own brackets
                Match close = ListEnd().Match(infantry, list.Index);
                string block = infantry[list.Index..(close.Success ? close.Index : infantry.Length)];
                foreach (Match weapon in HeldWeapon().Matches(block)) held.TryAdd((list.Groups[1].Value, weapon.Groups[1].Value), weapon.Groups[2].Value);
            }
        var onPylons = new Dictionary<(string Unit, int Number), string>();
        if (files.Read("Generated/Gameplay/Gfx/MissileCarriageDepiction.ndf") is string carriages)
        {
            MatchCollection showrooms = ShowroomCarriage().Matches(carriages);
            for (int i = 0; i < showrooms.Count; i++)
            {
                // up to the next declaration: the next aircraft's in-game list comes before its showroom list
                Match next = NextDeclaration().Match(carriages, showrooms[i].Index + showrooms[i].Length);
                int end = next.Success ? next.Index : carriages.Length;
                foreach (Match hung in CarriedOrdnance().Matches(carriages[showrooms[i].Index..end]))
                    onPylons.TryAdd((showrooms[i].Groups[1].Value, int.Parse(hung.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)), hung.Groups[1].Value);
            }
        }
        return new CarriedModels(mounts, held, onPylons, ReadPods());
    }

    /// <summary>
    /// The rocket pods units carry, by unit and weapon number: MissileCarriage.ndf gives a weapon carried in pods
    /// (MountingType=eMountingPod) with its rocket count and the unit's pylon set; Pylons.ndf gives the set's pod models,
    /// each named by how many rockets it holds (PodModelPylon4, 7, 19). A weapon's pod is the largest of them whose size
    /// divides its rocket count (14 rockets are two pods of 7; 28, four of 7; 38, two of 19).
    /// </summary>
    private Dictionary<(string Unit, int Number), string> ReadPods()
    {
        var pods = new Dictionary<(string Unit, int Number), string>();
        if (files.Read("Gameplay/Gfx/Units/Pylons.ndf") is not string pylons || files.Read("Generated/Gameplay/Gfx/MissileCarriage.ndf") is not string carriages) return pods;
        // each pylon set's models, and each multi-set's pod set
        var models = new Dictionary<string, List<(int Size, string Model)>>(StringComparer.Ordinal);
        var podSets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match set in PylonSetDeclaration().Matches(pylons))
        {
            Match next = NextDeclaration().Match(pylons, set.Index + set.Length);
            string block = pylons[set.Index..(next.Success ? next.Index : pylons.Length)];
            if (set.Groups[2].Value == "TMultiPylonSet")
            {
                if (PodPylonSet().Match(block) is { Success: true } pod) podSets[set.Groups[1].Value] = pod.Groups[1].Value;
                continue;
            }
            models[set.Groups[1].Value] = [.. PylonModel().Matches(block)
                .Select(model => (Size: int.TryParse(TrailingDigits().Match(model.Groups[1].Value).Value, out int size) ? size : 0, Model: model.Groups[1].Value))
                .Where(model => model.Size > 0)];
        }
        foreach (Match carriage in CarriageDeclaration().Matches(carriages))
        {
            Match next = NextDeclaration().Match(carriages, carriage.Index + carriage.Length);
            string block = carriages[carriage.Index..(next.Success ? next.Index : carriages.Length)];
            if (CarriagePylonSet().Match(block) is not { Success: true } setName || !podSets.TryGetValue(setName.Groups[1].Value, out string? podSet)
                || !models.TryGetValue(podSet, out var sizes) || sizes.Count == 0) continue;
            foreach (Match info in PodWeapon().Matches(block))
            {
                int count = int.Parse(info.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int number = int.Parse(info.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (sizes.Where(size => count % size.Size == 0).OrderByDescending(size => size.Size).FirstOrDefault() is { Model: string model })
                    pods.TryAdd((carriage.Groups[1].Value, number), model);
            }
        }
        return pods;
    }

    /// <summary>A pylon set in Pylons.ndf: its name (group 1) and its kind (group 2: TPylonSet or TMultiPylonSet).</summary>
    [GeneratedRegex("""(?m)^(?:export\s+)?(\w+)\s+is\s+(TPylonSet|TMultiPylonSet)\b""")]
    private static partial Regex PylonSetDeclaration();

    /// <summary>A multi-set's pod set: PodPylonSet = &lt;set&gt; (group 1).</summary>
    [GeneratedRegex("""\bPodPylonSet\s*=\s*~?/?(\w+)""")]
    private static partial Regex PodPylonSet();

    /// <summary>A pylon model: ModelResource = $/GFX/DepictionResources/&lt;model&gt; (group 1).</summary>
    [GeneratedRegex("""\bModelResource\s*=\s*\$/GFX/DepictionResources/(\w+)""")]
    private static partial Regex PylonModel();

    /// <summary>The digits a pod model's name ends with: how many rockets it holds.</summary>
    [GeneratedRegex("""\d+$""")]
    private static partial Regex TrailingDigits();

    /// <summary>A unit's carriage in MissileCarriage.ndf: MissileCarriage_&lt;unit&gt; is TMissileCarriageConnoisseur (group 1, the unit).</summary>
    [GeneratedRegex("""(?m)^(?:export\s+)?MissileCarriage_(\w+?)\s+is\s+TMissileCarriageConnoisseur\b""")]
    private static partial Regex CarriageDeclaration();

    /// <summary>A carriage's pylon set: PylonSet = ~/&lt;set&gt; (group 1).</summary>
    [GeneratedRegex("""\bPylonSet\s*=\s*~?/?(\w+)""")]
    private static partial Regex CarriagePylonSet();

    /// <summary>A weapon carried in pods: its MissileCount (group 1) and WeaponIndex (group 2), with MountingType=eMountingPod.</summary>
    [GeneratedRegex("""TMissileCarriageWeaponInfo\(\s*MissileCount\s*=\s*(\d+)[^)]*?MountingType\s*=\s*eMountingPod[^)]*?WeaponIndex\s*=\s*(\d+)""")]
    private static partial Regex PodWeapon();

    /// <summary>A unit's weapons: WeaponDescriptor_&lt;unit&gt; is TWeaponManagerModuleDescriptor (group 1, the unit).</summary>
    [GeneratedRegex("""(?m)^(?:export\s+)?WeaponDescriptor_(\w+)\s+is\s+TWeaponManagerModuleDescriptor\b""")]
    private static partial Regex WeaponManager();

    /// <summary>A mounted weapon's ammunition: $/GFX/Weapon/&lt;descriptor&gt; (group 1).</summary>
    [GeneratedRegex("""\bAmmunition\s*=\s*\$/GFX/Weapon/(\w+)""")]
    private static partial Regex MountAmmunition();

    /// <summary>A soldier's weapon slot: HandheldEquipmentKey = "WeaponAlternative_N" (group 1).</summary>
    [GeneratedRegex("""\bHandheldEquipmentKey\s*=\s*["']([^"']+)["']""")]
    private static partial Regex HandheldKey();

    /// <summary>A turret's number: the N of its Tag = "tourelleN" (group 1).</summary>
    [GeneratedRegex("""\bTag\s*=\s*["']tourelle(\d+)["']""")]
    private static partial Regex TurretTag();

    /// <summary>A squad's held weapons: AllWeaponAlternatives_&lt;unit&gt; is [ (group 1, the unit).</summary>
    [GeneratedRegex("""(?m)^(?:export\s+)?AllWeaponAlternatives_(\w+)\s+is\s*\[""")]
    private static partial Regex WeaponAlternatives();

    /// <summary>A held weapon: SelectorId = ['WeaponAlternative_N'] (group 1) MeshDescriptor = $/GFX/DepictionResources/&lt;model&gt; (group 2).</summary>
    [GeneratedRegex("""SelectorId\s*=\s*\[\s*['"]([^'"]+)['"]\s*\]\s*MeshDescriptor\s*=\s*\$/GFX/DepictionResources/(\w+)""")]
    private static partial Regex HeldWeapon();

    /// <summary>An aircraft's or vehicle's carried ordnance as its showroom shows it: SubGenerators_Showroom_&lt;unit&gt; is (group 1).</summary>
    [GeneratedRegex("""(?m)^(?:export\s+)?SubGenerators_Showroom_(\w+)\s+is\b""")]
    private static partial Regex ShowroomCarriage();

    /// <summary>One carried kind: its ProjectileModelResource (group 1), then its WeaponIndex (group 2).</summary>
    [GeneratedRegex("""ProjectileModelResource\s*=\s*\$/GFX/DepictionResources/(\w+)(?:(?!ProjectileModelResource)[\s\S])*?WeaponIndex\s*=\s*(\d+)""")]
    private static partial Regex CarriedOrdnance();

    /// <summary>
    /// The models of a squad's soldiers: TacticDepiction_&lt;squad&gt;_Alternatives in DepictionInfantry.ndf lists one per
    /// soldier with the selector LOD_High ('01', '02'…), then a lighter one for far away.
    /// </summary>
    private List<string> SoldierDescriptors(string squad)
    {
        string? text = files.Read("Generated/Gameplay/Gfx/Infanterie/DepictionInfantry.ndf");
        if (text is null) return [];
        int start = text.IndexOf("TacticDepiction_" + squad + "_Alternatives is", StringComparison.Ordinal);
        if (start < 0) return [];
        // the selectors' own brackets close before the list's, so the list ends at the first "]" alone on its line
        Match close = ListEnd().Match(text, start);
        string list = text[start..(close.Success ? close.Index : text.Length)];
        return [.. SoldierLine().Matches(list).Select(match => match.Groups[1].Value)];
    }

    /// <summary>A model descriptor's model ("Assets/3D/....fbx"), such as a pylon's or a launcher's; null when no DepictionResources file has it.</summary>
    public ModelFile? MeshPath(string descriptor) => MeshPathOf(descriptor);

    /// <summary>A model descriptor's model ("Assets/3D/....fbx"); null when no DepictionResources file has it.</summary>
    private ModelFile? MeshPathOf(string descriptor)
    {
        if (Resolved(descriptor) is ModelFile known) return known;
        // a mod's new units often name their models in files of their own, which the game does not have
        resourceFiles ??= [.. files.PathsWithModWhere(path => path.Contains(ResourcesFolder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase))];
        // a vehicle's descriptor usually sits in a file of its own name; the shared files hold soldiers, weapons and missiles
        string ownFile = "/" + (descriptor.StartsWith("Modele_", StringComparison.Ordinal) ? descriptor["Modele_".Length..] : descriptor) + ".ndf";
        IEnumerable<string> likely = resourceFiles.Where(path => path.EndsWith(ownFile, StringComparison.OrdinalIgnoreCase))
            .Concat(resourceFiles.Where(path => path.EndsWith("/Gfx_infanterie.ndf", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/AllMissileResources.ndf", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/SharedResources.ndf", StringComparison.OrdinalIgnoreCase)));
        foreach (string path in likely.Concat(resourceFiles))
        {
            if (!filesRead.Add(path)) continue;
            if (files.ReadPath(path) is not string text) continue;
            foreach (Match mesh in MeshLine().Matches(text))
            {
                string value = mesh.Groups[2].Success ? mesh.Groups[2].Value : mesh.Groups[3].Value;
                if (!meshes.TryAdd(mesh.Groups[1].Value, (value, mesh.Groups[4].Value))) continue;
                // its Textures, before or after its Mesh among its values, up to the next declaration
                Match next = NextDeclaration().Match(text, mesh.Index + mesh.Length);
                Match textures = TexturesLine().Match(text, mesh.Index, (next.Success ? next.Index : text.Length) - mesh.Index);
                if (textures.Success) textureFolders[mesh.Groups[1].Value] = textures.Groups[1].Value.TrimEnd('/');
            }
            foreach (Match constant in PathConstant().Matches(text)) pathConstants.TryAdd(constant.Groups[1].Value, constant.Groups[2].Value);
            if (Resolved(descriptor) is ModelFile found) return found;
        }
        return null;
    }

    /// <summary>A descriptor's model once its Mesh, and the constant it names if it names one, have been read; null before.</summary>
    private ModelFile? Resolved(string descriptor)
    {
        if (!meshes.TryGetValue(descriptor, out var mesh)) return null;
        string? path = mesh.Value.StartsWith("GameData:/", StringComparison.OrdinalIgnoreCase) ? mesh.Value["GameData:/".Length..] : pathConstants.GetValueOrDefault(mesh.Value);
        return path is null ? null : new ModelFile(path + mesh.Added, textureFolders.GetValueOrDefault(descriptor));
    }

    /// <summary>
    /// A TResourceMesh: its name (Modele_... in the game, anything in a mod), then its Mesh wherever it comes among its
    /// values: a path in quotes (group 2) or a constant's name, with or without "~/" before it (group 3), then any text
    /// in quotes added to it with "+" (group 4).
    /// </summary>
    [GeneratedRegex("""(?:export\s+)?([A-Za-z0-9_]+)\s+is\s+TResourceMesh\s*\((?:[^()]|\([^()]*\))*?\bMesh\s*=\s*(?:["']([^"']+)["']|(?:~/)?([^"'\s)+]+))(?:\s*\+\s*["']([^"']*)["'])?""")]
    private static partial Regex MeshLine();

    /// <summary>A TResourceMesh's Textures: the folder after "GameData:/" (group 1).</summary>
    [GeneratedRegex("""\bTextures\s*=\s*["']GameData:/([^"']+)["']""")]
    private static partial Regex TexturesLine();

    /// <summary>A depiction's visual: its selector (DepictionVisual_LOD_High...), then the model descriptor it draws.</summary>
    [GeneratedRegex("""(\w*)\s*\(\s*(?:SelectorId\s*=\s*\[[^\]]*\]\s*)?MeshDescriptor\s*=\s*\$/GFX/DepictionResources/([A-Za-z0-9_]+)""")]
    private static partial Regex AlternativeMesh();

    /// <summary>
    /// A constant holding a path: its name, then the path after "GameData:/" (which stays in group 2's text). It need
    /// not start a line: some files put one straight after the declaration before it, as in
    /// "...TResourceMesh (Mesh=DCA_ZUR_23_2S_JOD_POL_LOW)DCA_ZUR_23_2S_JOD_POL is 'GameData:/...'".
    /// </summary>
    [GeneratedRegex("""(?<![A-Za-z0-9_])(?:export\s+)?([A-Za-z0-9_]+)\s+is\s+["']GameData:/([^"']+)["']""")]
    private static partial Regex PathConstant();

    [GeneratedRegex("""SelectorId\s*=\s*\[\s*LOD_High\s*,[^\]]*\]\s*MeshDescriptor\s*=\s*\$/GFX/DepictionResources/([A-Za-z0-9_]+)""")]
    private static partial Regex SoldierLine();

    [GeneratedRegex("""(?m)^\s*\]\s*$""")]
    private static partial Regex ListEnd();

    [GeneratedRegex("""ReferenceMesh\s*=\s*\$/GFX/DepictionResources/([A-Za-z0-9_]+)""")]
    private static partial Regex ReferenceMesh();

    [GeneratedRegex("""(?m)^(?:export\s+)?[A-Za-z0-9_]+\s+is\b""")]
    private static partial Regex NextDeclaration();
}
