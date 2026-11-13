namespace EasyNdf.Data.Models;

/// <summary>Where the viewer reads models from: WARNO's mesh pack (<see cref="MeshPack"/>), or Steel Division 2's Modding Assets (<see cref="ModdingAssets"/>).</summary>
public interface IModelPack : IDisposable
{
    /// <summary>Whether it has the model at a path in the game, such as "Assets/3D/Units/US/Char/M1A1_Abrams/M1A1_Abrams.fbx".</summary>
    bool Has(string path);

    /// <summary>The model at a path in the game; null when it has none or it cannot be read.</summary>
    ModelMesh? Read(string path);

    /// <summary>How many models it lists, for the viewer's estimate of the memory it takes.</summary>
    int ModelCount { get; }
}
