using System.Numerics;

namespace EasyNdf.Data.Effects;

/// <summary>The values a particle carries, by their names in the effect scripts ($/Particle/Size, CustomFloat3_2 ...).</summary>
public static class FxFields
{
    private static readonly string[] Names =
    [
        "Position", "Speed", "Size", "ColorRGB", "ColorModifier", "Alpha", "Opacity", "ColorMultiplier", "EmissivePower",
        "ZBias", "Count", "TexCoordU", "Direction", "Stretching", "UpVector", "Rotation", "RotationSpeed",
        "OffsetAlongDirection", "RotationAxisForMesh", "ScaleForMesh", "TextureSmallIndex", "Animation",
        "CustomFloat_1", "CustomFloat_2", "CustomFloat_3", "CustomFloat_4", "CustomFloat_5", "CustomFloat_6", "CustomFloat_7", "CustomFloat_8", "CustomFloat_9",
        "CustomFloat3_1", "CustomFloat3_2", "CustomFloat3_3", "CustomFloat3_4", "CustomFloat3_5", "CustomFloat3_6", "CustomFloat3_7", "CustomFloat3_8", "CustomFloat3_9",
        "ActionCentroid", "OffsetFromCentroidPositionAtSpawn", "DistanceFromGround", "PreviousEvolverSpawnPosition",
        "TimeScale", "CurvilinearAbscissa", "ForwardVector", "LeftVector", "DynamicMesh",
    ];

    private static readonly Dictionary<string, int> NumberOf = Names.Select((name, number) => (name, number)).ToDictionary(pair => pair.name, pair => pair.number, StringComparer.Ordinal);

    /// <summary>How many values a particle carries.</summary>
    public static int Count => Names.Length;

    /// <summary>The number of a value, by its name; -1 for a name that is not one.</summary>
    public static int Of(string name) => NumberOf.TryGetValue(name, out int number) ? number : -1;

    /// <summary>Where a particle is, in the model's space.</summary>
    public const int Position = 0;
    /// <summary>How fast and which way it moves.</summary>
    public const int Speed = 1;
    /// <summary>How wide it is drawn, in the model's units.</summary>
    public const int Size = 2;
    /// <summary>Its colour.</summary>
    public const int ColorRGB = 3;
    /// <summary>A colour its colour is multiplied by.</summary>
    public const int ColorModifier = 4;
    /// <summary>How much of it shows, 0 to 1.</summary>
    public const int Alpha = 5;
    /// <summary>How much it hides what is behind it rather than adding light to it, 0 to 1.</summary>
    public const int Opacity = 6;
    /// <summary>A number its colour is multiplied by.</summary>
    public const int ColorMultiplier = 7;
    /// <summary>How much light it gives off.</summary>
    public const int EmissivePower = 8;
    /// <summary>Which way it is stretched.</summary>
    public const int Direction = 12;
    /// <summary>How far it is stretched along its direction.</summary>
    public const int Stretching = 13;
    /// <summary>Its turn on screen, in radians.</summary>
    public const int Rotation = 15;
    /// <summary>How fast it turns, in radians a second.</summary>
    public const int RotationSpeed = 16;
    /// <summary>How far it is moved along its direction, drawn.</summary>
    public const int OffsetAlongDirection = 17;
    /// <summary>Which picture of a set it shows.</summary>
    public const int TextureSmallIndex = 20;
    /// <summary>How far through its texture's animation it is, 0 to 1.</summary>
    public const int Animation = 21;

    /// <summary>The Opacity of a particle whose scripts give none: the viewer draws it as adding light only when it gives off light, else as covering.</summary>
    public const float NotGiven = -1;

    /// <summary>A new particle's values, before its initialisers run.</summary>
    public static FxValue[] Fresh()
    {
        var fields = new FxValue[Names.Length];
        for (int i = 0; i < fields.Length; i++) fields[i] = FxValue.Of(0);
        fields[Position] = fields[Speed] = FxValue.Of(Vector3.Zero);
        fields[Size] = FxValue.Of(1);
        fields[ColorRGB] = fields[ColorModifier] = FxValue.Of(Vector3.One);
        fields[Alpha] = fields[ColorMultiplier] = FxValue.Of(1);
        // not given (see NotGiven): a particle that gives off light then only adds it, others cover fully
        fields[Opacity] = FxValue.Of(NotGiven);
        fields[Direction] = FxValue.Of(Vector3.UnitZ);
        fields[Of("UpVector")] = FxValue.Of(Vector3.UnitZ);
        fields[Of("TimeScale")] = FxValue.Of(1);
        return fields;
    }
}

/// <summary>What a set of particles was spawned by: where it is, how it moves, and how far through its life it is.</summary>
public sealed class FxSpawner
{
    /// <summary>Where it is, in the model's space.</summary>
    public Vector3 Position { get; set; }

    /// <summary>How fast and which way it moves.</summary>
    public Vector3 Speed { get; set; }

    /// <summary>How far through its spawning it is, 0 to 1.</summary>
    public float LifeRatio { get; set; }

    /// <summary>The frame it spawns in.</summary>
    public FxFrame? Frame { get; init; }
}

/// <summary>How particles of a kind are drawn.</summary>
public enum FxDrawing
{
    /// <summary>A picture facing the camera (LitParticleRenderer, StandardRenderer).</summary>
    Picture,

    /// <summary>A light that lights the model (PointLightRendererLBU).</summary>
    Light,

    /// <summary>
    /// A ribbon (ParticleRendererMeshTrail): the particles a trail drops, joined in the order they were dropped into a
    /// strip facing the camera, each as wide as its Size, the texture running along it by its TexCoordU.
    /// </summary>
    Ribbon,

    /// <summary>
    /// A model (DynamicMeshRenderer): a rocket, a shell, a grenade, a spent casing or a piece of debris, its model named by
    /// its DynamicMesh, along its Direction, as large as its ScaleForMesh, turned about its RotationAxisForMesh by its
    /// Rotation.
    /// </summary>
    Mesh,

    /// <summary>
    /// A rotor's blur (RotorRenderer): a picture of a blade's blur for each of its Count blades, spread evenly round its
    /// UpVector from its Direction, each as long and as wide as its Size.
    /// </summary>
    Rotor,

    /// <summary>Nothing: it only carries other particles (NullRenderer).</summary>
    Nothing,

    /// <summary>Something the viewer does not draw: a mesh, a trail ribbon, a mark on the ground, a heat haze.</summary>
    NotShown,
}

/// <summary>A particle spawner a particle carries with it (EvolverSpraySpawner, TrailSpawner): it spawns particles as long as the particle lives.</summary>
/// <param name="Frequency">How many particles a second; null for a trail.</param>
/// <param name="Distance">For a trail (TrailSpawner): how far the particle moves between two it spawns; null otherwise.</param>
/// <param name="Recipe">What it spawns.</param>
/// <param name="Condition">Whether it spawns, worked out each time; null for always.</param>
public sealed record FxCarriedSpawner(FxBinding? Frequency, FxBinding? Distance, FxSpawnRecipe Recipe, FxBinding? Condition);

/// <summary>What a ParticleSpawner makes: its kind of particle, how long each lives, and how each starts.</summary>
/// <param name="Kind">The kind of particle.</param>
/// <param name="Lifetime">How long each lives, in seconds (ParticleLifetime).</param>
/// <param name="Initialisers">How each starts, before it is placed at its spawner (Initialisers).</param>
/// <param name="AfterPlacing">How each starts, after (PostTransformInitialisers).</param>
/// <param name="Independent">
/// Whether its initialisers place it in the model's space themselves (PositionIndependentFromEmitter), as a gun's smoke
/// at SpawnerPosition plus its own offset: it is then not turned and placed again.
/// </param>
public sealed record FxSpawnRecipe(FxParticleKind Kind, FxBinding? Lifetime, IReadOnlyList<FxBinding> Initialisers, IReadOnlyList<FxBinding> AfterPlacing, bool Independent);

/// <summary>A kind of particle, from a ParticleDescriptor: how it is drawn and how its values change as it lives.</summary>
public sealed class FxParticleKind
{
    /// <summary>The picture it is drawn with, as the script names it ('GameData:/Assets/2D/Fx/spot.png'); null for none.</summary>
    public string? Texture { get; init; }

    /// <summary>How it is drawn.</summary>
    public FxDrawing Drawing { get; init; }

    /// <summary>What it is when the viewer cannot draw it (its renderer's or render info's type), for the list of what is not shown.</summary>
    public string? NotShownBecause { get; init; }

    /// <summary>Whether it gives off light (EmissiveParticle, or a lit particle renderer).</summary>
    public bool Emissive { get; init; }

    /// <summary>Whether it hides what is behind it fully where its picture is solid (OpaqueParticle).</summary>
    public bool Opaque { get; init; }

    /// <summary>Its values that change as it lives, in the order they are worked out: each value's number and its calculation.</summary>
    public required IReadOnlyList<(int Field, FxBinding Function)> Evolvers { get; init; }

    /// <summary>Whether one of its evolvers sets its turn, so its turning speed is not added on.</summary>
    public bool SetsRotation { get; init; }

    /// <summary>Whether its position follows its speed (ParticleStandardEvolverListWithPhysic), under this acceleration.</summary>
    public bool Moves { get; init; }

    /// <summary>The acceleration it moves under (gravity, a lift); null for none.</summary>
    public FxBinding? Acceleration { get; init; }

    /// <summary>How much the air slows it each second; null for not at all.</summary>
    public FxBinding? FluidFriction { get; init; }

    /// <summary>The wind it is carried by; null for none.</summary>
    public FxBinding? Wind { get; init; }

    /// <summary>The spawners each particle carries.</summary>
    public IReadOnlyList<FxCarriedSpawner> CarriedSpawners { get; init; } = [];

    /// <summary>Whether its particles live until their effect stops (InfiniteParticles), as a rotor's blur does.</summary>
    public bool Infinite { get; init; }

    /// <summary>
    /// Whether its speed is worked out from how far it moved each step (StandardComputeSpeed), as a wheel's dust
    /// emitter's is: it is placed at the wheel, and its speed tells which way the vehicle drives.
    /// </summary>
    public bool ComputesSpeed { get; init; }

    /// <summary>Whether it plays its texture's animation.</summary>
    public bool Animates => Evolvers.Any(evolver => evolver.Field == FxFields.Animation);

    /// <summary>What the viewer keeps to draw it with (its texture on the graphics card), once found.</summary>
    public object? Drawn { get; set; }
}

/// <summary>One particle as it lives.</summary>
public sealed class FxParticle
{
    /// <summary>Its values, by <see cref="FxFields"/> number.</summary>
    public FxValue[] Fields { get; } = FxFields.Fresh();

    /// <summary>What kind of particle it is.</summary>
    public required FxParticleKind Kind { get; init; }

    /// <summary>What spawned it.</summary>
    public required FxSpawner Spawner { get; init; }

    /// <summary>The particle that spawned it, when it was spawned by one; null otherwise.</summary>
    public FxParticle? Parent { get; init; }

    /// <summary>The effect it belongs to: once that is stopped, the spawners it carries spawn no more.</summary>
    public FxEffect? Effect { get; init; }

    /// <summary>The frame it was spawned in (its MobileWrapper).</summary>
    public FxFrame? Frame { get; set; }

    /// <summary>When it was born, in the effects' seconds.</summary>
    public double Born { get; init; }

    /// <summary>When it dies.</summary>
    public double Dies { get; set; }

    /// <summary>The spawners it carries: when each spawns its next particle.</summary>
    public double[]? NextSpawns { get; set; }

    /// <summary>For each trail it carries, how far along it the trail has come, for its particles' CurvilinearAbscissa.</summary>
    public float[]? TrailLengths { get; set; }

    /// <summary>Its place, from its values.</summary>
    public Vector3 Position => Fields[FxFields.Position].Vector;

    /// <summary>How far through its life it is at this time, 0 to 1.</summary>
    public float LifeRatio(double now) => Dies <= Born ? 1 : (float)Math.Clamp((now - Born) / (Dies - Born), 0, 1);
}
