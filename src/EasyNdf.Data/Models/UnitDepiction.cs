using EasyNdf.Data.Effects;

namespace EasyNdf.Data.Models;

/// <summary>
/// How the game shows a unit beyond its model, as its depiction (DepictionVehicles.ndf, DepictionAerialUnits.ndf)
/// says: what each weapon fires from and with, how its guns recoil, its rotors and gatling barrels, the sounds of its
/// engine and turret, the effects it plays as it moves, and what hangs on it.
/// </summary>
/// <param name="Rotors">A helicopter's rotors.</param>
/// <param name="Engine">Its engine's sounds; null for none.</param>
/// <param name="Turret">Its main turret's turning sounds; null for none.</param>
public sealed record UnitDepiction(IReadOnlyList<UnitRotor> Rotors, EngineSounds? Engine, TurretSounds? Turret)
{
    /// <summary>Its weapons, by the number of their effect tag ("weapon_effet_tag1" is 1).</summary>
    public IReadOnlyDictionary<int, DepictedWeapon> Weapons { get; init; } = new Dictionary<int, DepictedWeapon>();

    /// <summary>How each turret's gun recoils, by turret number; none for a turret whose gun does not.</summary>
    public IReadOnlyDictionary<int, GunRecoil> Recoils { get; init; } = new Dictionary<int, GunRecoil>();

    /// <summary>The barrels a gatling gun spins; null for none.</summary>
    public GatlingBarrels? Gatling { get; init; }

    /// <summary>The effects it plays as it moves, such as a vehicle's dust (Actions' DepictionAction_MovementFX_...).</summary>
    public IReadOnlyList<string> MovementEffects { get; init; } = [];

    /// <summary>The models of their own drawn on the unit's: a launcher's turret (its TSubDepictions with a model).</summary>
    public IReadOnlyList<SubModel> SubModels { get; init; } = [];

    /// <summary>The munitions hung on its pylons and in its pods.</summary>
    public IReadOnlyList<HungWeapon> Hung { get; init; } = [];
}

/// <summary>One weapon as the depiction shows it firing.</summary>
/// <param name="Number">Its effect tag's number.</param>
/// <param name="Anchors">The model's nodes it fires from, in turn (fx_tourelle1_tir_01...); none for a weapon fired from a missile carriage.</param>
/// <param name="FiresContinuously">
/// Whether it fires as a stream (DepictionOperator_WeaponContinuousFire, machine guns and gatlings), whose sound plays once
/// for a burst, rather than shot by shot.
/// </param>
/// <param name="Look">Its effects and sound (the weapon block its tag names); null when the depiction names none.</param>
public sealed record DepictedWeapon(int Number, IReadOnlyList<string> Anchors, bool FiresContinuously, WeaponLook? Look);

/// <summary>What a weapon fires with, from DepictionWeaponBlock.ndf and DepictionFXWeapons.ndf.</summary>
/// <param name="Block">The weapon block, such as "Weapon_Canon_AP_120_mm_L52_M68".</param>
/// <param name="FiringEffects">The effects one of which plays at the muzzle with each shot (fx_tir_...), as GameFx names them.</param>
/// <param name="Sound">Its sound; null when it has none.</param>
public sealed record WeaponLook(string Block, IReadOnlyList<string> FiringEffects, SoundChoice? Sound);

/// <summary>How a gun recoils as it fires (TCosmeticCannonHydraulicRecoilOperatorDesc).</summary>
/// <param name="Node">The node that moves back, such as "canon_01".</param>
/// <param name="Distance">How far it moves back, in the model's units (MaxRecoil).</param>
/// <param name="Speed">How many back-and-forths a second (RecoilSpeed).</param>
/// <param name="Peak">How far into the back-and-forth it is fully back, 0 to 1 (RecoilPeak).</param>
public sealed record GunRecoil(string Node, double Distance, double Speed, double Peak);

/// <summary>A gatling gun's barrels, which spin as it fires (TCosmeticCannonGatlingOperatorDesc).</summary>
/// <param name="Node">The node that spins about its x axis (XAxisRotationNode).</param>
/// <param name="Speed">How fast it spins at full speed (RotationSpeed).</param>
/// <param name="Acceleration">How fast it gets there (RotationAcceleration).</param>
public sealed record GatlingBarrels(string Node, double Speed, double Acceleration);

/// <summary>
/// A sound the game picks one file of at random each time (a TSoundSet), with how loud and how much its pitch varies.
/// </summary>
/// <param name="Files">The sound files, as their paths in the archives ("AllPlatforms/Sound/Assets/Sounds/....ess").</param>
/// <param name="Volume">How loud: a TSoundHappening's Volume (1 or 2), 1 when it gives none.</param>
/// <param name="Pitch">Its pitch, 1 for as recorded.</param>
/// <param name="RandomPitch">How much its pitch varies from one play to the next (the sound settings' RandomPitch).</param>
public sealed record SoundChoice(IReadOnlyList<string> Files, double Volume, double Pitch, double RandomPitch);

/// <summary>A helicopter's rotor (THelix).</summary>
/// <param name="Node">The node that turns, such as "bloc_moteur_1".</param>
/// <param name="Axis">The node's axis it turns about: 0 for x, 1 for y, 2 for z.</param>
/// <param name="Speed">How fast it turns (RotationSpeed; 242 for a Hind's rotors, read as turns a minute).</param>
/// <param name="Clockwise">Which way it turns.</param>
/// <param name="Blades">The node its blades hang from, hidden while their blur shows; null for none.</param>
/// <param name="BlurAction">The tag of the action that draws its blur (BlurActionId); null for none.</param>
/// <param name="Blur">That action: the blur drawn in place of its blades when they turn fast (FX_Binder_Rotor at its hub, told the blades' length and count); null for none.</param>
public sealed record UnitRotor(string Node, int Axis, double Speed, bool Clockwise, string? Blades = null, string? BlurAction = null, FxAttachment? Blur = null);

/// <summary>
/// A model of its own drawn on the unit's at one of its nodes (a TSubDepiction with a model), such as the TOW Humvee's
/// launcher: a turret with its own nodes, which turns as the unit's would.
/// </summary>
/// <param name="Anchor">The unit's node it hangs at, such as "base_tourelle_01".</param>
/// <param name="Model">Its model descriptor, such as "Modele_M1025_Humvee_TOW_US_Tourelle_01".</param>
public sealed record SubModel(string Anchor, string Model);

/// <summary>
/// A weapon's munitions hung on the unit: its missiles or bombs on pylons, or its rockets in pods, at the model's mounts
/// (a TMissileCarriageWeaponInfo of the unit's missile carriage, with what its depiction hangs: MissileCarriageDepiction.ndf).
/// </summary>
/// <param name="WeaponIndex">The weapon, by its number in the unit (its mounts' WeaponNumber), as the carriage's firing operator names it for the carriage's own WeaponIndex.</param>
/// <param name="Mounts">
/// The mounts' names before their number: "as_1_" for the carriage's first ground-attack weapon (eAGM), "aa_1_" for its
/// first air-to-air one (eAAM), counted apart.
/// </param>
/// <param name="Count">How many munitions it hangs.</param>
/// <param name="Mounting">How they hang (MountingType): "eMountingPod" for rockets in pods, "eMountingBomb", else "".</param>
/// <param name="Munition">The model hung at each place of a pylon (ProjectileModelResource); null for pods, whose rockets are inside.</param>
/// <param name="Pylons">The pylon or pod models the pylon set gives this mounting, in its order; each has places named missile_1, missile_2...</param>
/// <param name="InModel">The sub-model whose mounts these are (the weapon info's MeshDescriptor); null for the unit's own model.</param>
public sealed record HungWeapon(int WeaponIndex, string Mounts, int Count, string Mounting, string? Munition, IReadOnlyList<string> Pylons, string? InModel);

/// <summary>An engine's sounds, as its movement-sound operator gives them (SM_Drive.ndf, SM_Fly.ndf, SM_Constant.ndf).</summary>
/// <param name="Start">Played as it starts; null for none.</param>
/// <param name="Running">Looped while it moves (RunSounds); for an engine that only ever runs, its one sound.</param>
/// <param name="Idling">Looped while it stands with the engine on (IdleSounds); null for none.</param>
/// <param name="Stop">Played as it stops; null for none.</param>
/// <param name="FadeIn">How long the running sound takes to rise, in seconds.</param>
/// <param name="FadeOut">How long it takes to fall away.</param>
public sealed record EngineSounds(SoundChoice? Start, SoundChoice? Running, SoundChoice? Idling, SoundChoice? Stop, double FadeIn, double FadeOut);

/// <summary>A turret's turning sounds (TCosmeticTurretSoundOperatorDesc).</summary>
/// <param name="TurretTag">The turret they belong to, such as "tourelle1".</param>
/// <param name="Start">Played as it starts turning; null for none.</param>
/// <param name="Turning">Looped while it turns.</param>
/// <param name="Stop">Played as it stops; null for none.</param>
public sealed record TurretSounds(string TurretTag, SoundChoice? Start, SoundChoice? Turning, SoundChoice? Stop);
