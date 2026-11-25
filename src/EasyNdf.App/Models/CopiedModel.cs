using System.IO;
using EasyNdf.Data.Game;
using EasyNdf.Data.Models;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Models;

/// <summary>
/// The copied unit's 3D model, read for the 3D Model panel: the model as the game has it (its parts are the ones a
/// model of the user's own must have, and its size the one to compare with), its pictures, where it is in the game, and
/// its look (its depiction and detail levels, which the unit's own look copies, see <see cref="OwnModel"/>).
/// </summary>
/// <param name="Mesh">The model; null when it could not be found.</param>
/// <param name="Pictures">Each of its parts' pictures; empty when the model was not found.</param>
/// <param name="Path">Its path in the game, such as "Assets/3D/Units/US/Char/M1A1_Abrams/M1A1_Abrams.fbx"; null when it was not found.</param>
/// <param name="Key">Its look's key (its BlackHoleKey), such as "M1A1_Abrams_US"; null when the unit names none.</param>
/// <param name="Look">Its depiction and detail levels; null when it is not a ground vehicle's look.</param>
/// <param name="Problem">What stopped the model being read; null when nothing did.</param>
internal sealed record CopiedModel(ModelMesh? Mesh, List<MaterialPictures> Pictures, string? Path, string? Key,
    (string Depiction, string Alternatives)? Look, string? Problem = null)
{
    private const string VehiclesFile = "Generated/Gameplay/Gfx/Depictions/DepictionVehicles.ndf";
    private const string AlternativesFile = "Generated/Gameplay/Gfx/Depictions/DepictionAlternatives.ndf";

    /// <summary>Reads a unit's model and look from its source (the game, or a game mod with its own Gen folder); slow, run it in the background.</summary>
    public static CopiedModel Read(Unit unit, UnitSource source)
    {
        string? key = unit.Look?.Depiction;
        try
        {
            static bool Wanted(ArchiveFile file) => MeshPack.IsPackFile(file.Name) || ModelTextures.IsModelTextureFile(file.Name);
            GameArchives archives = GameArchives.Open(source.GameFolder, Wanted);
            if (source.Mod?.Folder is string modFolder) archives.AddLooseFiles(System.IO.Path.Combine(modFolder, "Gen"), Wanted);
            using var files = new GameDataFiles(GameInstall.BaseZipOf(source.GameFolder), source.Mod?.Folder);
            (string, string)? look = null;
            if (key is not null && files.Read(VehiclesFile) is string vehicles && files.Read(AlternativesFile) is string alternatives)
                look = OwnModel.ReadLook(vehicles, alternatives, key);
            if (unit.Look is not UnitLook unitLook) return new CopiedModel(null, [], null, key, look);
            ModelFile? file = new ModelLinks(files).ModelsOf(unitLook).FirstOrDefault();
            if (file is null) return new CopiedModel(null, [], null, key, look);
            MeshPack? pack = MeshPack.Open(archives);
            List<MeshPack> packs = [.. MeshPack.OpenModPacks(archives), .. pack is null ? Array.Empty<MeshPack>() : [pack]];
            try
            {
                ModelMesh? mesh = packs.FirstOrDefault(each => each.Has(file.Path))?.Read(file.Path) is ModelMesh read ? read with { Textures = file.Textures } : null;
                List<MaterialPictures> pictures = [];
                if (mesh is not null)
                {
                    var textures = ModelTextures.For(archives, mesh);
                    var byMaterial = new Dictionary<ModelMaterial, MaterialPictures>();
                    foreach (ModelPart part in mesh.Parts)
                    {
                        if (!byMaterial.TryGetValue(part.Material, out MaterialPictures? maps)) byMaterial[part.Material] = maps = textures.PicturesOf(part.Material);
                        pictures.Add(maps);
                    }
                }
                return new CopiedModel(mesh, pictures, file.Path, key, look);
            }
            finally
            {
                foreach (MeshPack each in packs) each.Dispose();
            }
        }
        catch (Exception problem) when (problem is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new CopiedModel(null, [], null, key, null, problem.Message);
        }
    }
}
