using System.Text.RegularExpressions;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Models;

/// <summary>
/// Finds the 3D models of a unit or a missile in the game's files: a model's descriptor (Modele_...) is a TResourceMesh
/// in GameData/Gameplay/Gfx/DepictionResources, whose Mesh is the model's path, written out or as a constant of those
/// files ("ModelFile_M1A1_Abrams_US", "US_AB_1", each "GameData:/....fbx"). A vehicle names its descriptor
/// (<see cref="UnitLook.Mesh"/>); an infantry squad's soldiers are listed in DepictionInfantry.ndf, up to ten models to
/// a squad; a missile names its descriptor in MissileDescriptors.ndf.
/// </summary>
/// <param name="files">The game's files, with a game mod's own copies first.</param>
public sealed partial class ModelLinks(GameDataFiles files)
{
    private const string ResourcesFolder = "/Gameplay/Gfx/DepictionResources/";

    /// <summary>Each model descriptor's Mesh as written: a path, or the name of a constant holding one.</summary>
    private readonly Dictionary<string, string> meshes = new(StringComparer.Ordinal);

    /// <summary>The constants holding a path, such as US_AB_1, "Assets/3D/Units/US/Infanterie/Airborne/Airborne_1.fbx".</summary>
    private readonly Dictionary<string, string> pathConstants = new(StringComparer.Ordinal);

    private readonly HashSet<string> filesRead = new(StringComparer.OrdinalIgnoreCase);
    private List<string>? resourceFiles;

    /// <summary>
    /// A unit's models, as their paths in the game ("Assets/3D/....fbx"): one for a vehicle or an aircraft, one for each
    /// soldier of an infantry squad; none when they cannot be found.
    /// </summary>
    public IReadOnlyList<string> ModelsOf(UnitLook look)
    {
        if (look.Mesh is not null && MeshPathOf(look.Mesh) is string path) return [path];
        // a squad's soldiers are its squad module's BlackHoleKey's (a mod's zombies keep another squad's MimeticName),
        // else its MimeticName's
        string? mimeticSquad = look.Mimetic is null ? null : look.Mimetic.StartsWith("squad_", StringComparison.Ordinal) ? look.Mimetic["squad_".Length..] : look.Mimetic;
        foreach (string? squad in new[] { look.Depiction, mimeticSquad })
        {
            if (squad is null) continue;
            List<string> soldiers = [.. SoldierDescriptors(squad).Select(MeshPathOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
            if (soldiers.Count > 0) return soldiers;
        }
        // else the model its depiction draws up close: Alternatives_<BlackHoleKey> in DepictionAlternatives.ndf
        foreach (string? key in new[] { look.Depiction, look.Mimetic })
            if (key is not null && AlternativesMesh(key) is string descriptor && MeshPathOf(descriptor) is string drawn) return [drawn];
        // Steel Division 2: the model its depiction draws up close
        if (look.SteelDivisionDepiction is string gfx && SteelDivisionModel(gfx) is string model) return [model];
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
    public string? MissileModel(string missile)
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

    /// <summary>A model descriptor's path ("Assets/3D/....fbx"), such as a pylon's or a launcher's; null when no DepictionResources file has it.</summary>
    public string? MeshPath(string descriptor) => MeshPathOf(descriptor);

    /// <summary>A model descriptor's path ("Assets/3D/....fbx"); null when no DepictionResources file has it.</summary>
    private string? MeshPathOf(string descriptor)
    {
        if (Resolved(descriptor) is string known) return known;
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
            foreach (Match mesh in MeshLine().Matches(text)) meshes.TryAdd(mesh.Groups[1].Value, mesh.Groups[2].Value);
            foreach (Match constant in PathConstant().Matches(text)) pathConstants.TryAdd(constant.Groups[1].Value, constant.Groups[2].Value);
            if (Resolved(descriptor) is string found) return found;
        }
        return null;
    }

    /// <summary>A descriptor's path once its Mesh, and the constant it names if it names one, have been read; null before.</summary>
    private string? Resolved(string descriptor)
    {
        if (!meshes.TryGetValue(descriptor, out string? mesh)) return null;
        if (mesh.StartsWith("GameData:/", StringComparison.OrdinalIgnoreCase)) return mesh["GameData:/".Length..];
        return pathConstants.TryGetValue(mesh, out string? path) ? path : null;
    }

    /// <summary>
    /// A TResourceMesh: its name (Modele_... in the game, anything in a mod), then its Mesh wherever it comes among its
    /// values, a path in quotes or a constant's name (group 2 either way).
    /// </summary>
    [GeneratedRegex("""(?:export\s+)?([A-Za-z0-9_]+)\s+is\s+TResourceMesh\s*\((?:[^()]|\([^()]*\))*?\bMesh\s*=\s*["']?([^"'\s)]+)""")]
    private static partial Regex MeshLine();

    /// <summary>A depiction's visual: its selector (DepictionVisual_LOD_High...), then the model descriptor it draws.</summary>
    [GeneratedRegex("""(\w*)\s*\(\s*(?:SelectorId\s*=\s*\[[^\]]*\]\s*)?MeshDescriptor\s*=\s*\$/GFX/DepictionResources/([A-Za-z0-9_]+)""")]
    private static partial Regex AlternativeMesh();

    /// <summary>A constant holding a path: its name, then the path after "GameData:/" (which stays in group 2's text).</summary>
    [GeneratedRegex("""(?m)^\s*(?:export\s+)?([A-Za-z0-9_]+)\s+is\s+["']GameData:/([^"']+)["']""")]
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
