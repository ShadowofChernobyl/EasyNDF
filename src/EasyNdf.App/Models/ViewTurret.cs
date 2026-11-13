using System.Numerics;
using EasyNdf.Data.Models;

namespace EasyNdf.App.Models;

/// <summary>How a weapon fires a salvo, from its ammunition as the open mod has it (Ammunition.ndf).</summary>
/// <param name="Shots">How many shots a salvo has (ShotsCountPerSalvo).</param>
/// <param name="SecondsBetweenShots">The time between two shots (TimeBetweenTwoShots).</param>
/// <param name="SecondsBetweenEffects">The time between two flashes (TimeBetweenTwoFx), which a stream of fire can show less often than it fires.</param>
/// <param name="AtOnce">How many shots go at the same time (SimultaneousShotsCount), as a smoke launcher's.</param>
internal sealed record SalvoPattern(int Shots, double SecondsBetweenShots, double SecondsBetweenEffects, int AtOnce)
{
    /// <summary>A single shot, for a weapon whose ammunition says nothing.</summary>
    public static readonly SalvoPattern Single = new(1, 1, 1, 1);

    /// <summary>How long a salvo lasts, from its first shot to its last.</summary>
    public double Seconds => (Math.Max(1, Shots / Math.Max(1, AtOnce)) - 1) * SecondsBetweenShots;
}

/// <summary>
/// One turret of the unit as the viewer shows it: its weapons, the model's nodes it turns about and its weapons fire
/// from, how far and how fast the unit's loadout lets it turn (the open mod's values), how its first weapon fires and
/// recoils, and where it points now.
/// </summary>
internal sealed class ViewTurret
{
    /// <summary>Its number, from its tag: 1 for "tourelle1".</summary>
    public required int Number { get; init; }

    /// <summary>Its weapons' names, as the Armory shows them.</summary>
    public required string Weapons { get; init; }

    /// <summary>The nodes its weapons fire from (fx_tourelleN_tir_...).</summary>
    public required IReadOnlyList<int> FiringPoints { get; init; }

    /// <summary>The node it turns about (tourelle_0N); -1 when the model has none.</summary>
    public required int Centre { get; init; }

    /// <summary>The node turned to aim it left and right, when it moves on the model; -1 otherwise.</summary>
    public required int TurnNode { get; init; }

    /// <summary>The node turned to raise its gun (axe_canon_0N), when it moves on the model; -1 otherwise.</summary>
    public required int RaiseNode { get; init; }

    /// <summary>How far it turns each way, in degrees (180 for all the way round).</summary>
    public double TurnLimit { get; init; } = 180;

    /// <summary>How far its gun goes down, in degrees (below zero).</summary>
    public double LowestRaise { get; init; }

    /// <summary>How far its gun goes up, in degrees.</summary>
    public double HighestRaise { get; init; }

    /// <summary>How fast it turns, in degrees a second.</summary>
    public double DegreesASecond { get; init; } = 60;

    /// <summary>Whether its first weapon is a gun the viewer fires: not a missile or rocket, which it does not fly.</summary>
    public bool Fires { get; init; }

    /// <summary>Its first weapon as the depiction shows it firing; null when the depiction names none.</summary>
    public DepictedWeapon? Weapon { get; init; }

    /// <summary>How its first weapon fires a salvo.</summary>
    public SalvoPattern Salvo { get; init; } = SalvoPattern.Single;

    /// <summary>The nodes its first weapon fires from, in turn: the depiction's anchors, or else the turret's firing points.</summary>
    public IReadOnlyList<int> Muzzles { get; init; } = [];

    /// <summary>The node that moves back when it fires; -1 when its gun does not recoil.</summary>
    public int RecoilNode { get; init; } = -1;

    /// <summary>Which way, in the recoiling node's own space, its gun moves back, scaled to the recoil's length.</summary>
    public Vector3 RecoilBack { get; init; }

    /// <summary>How many back-and-forths a second the recoil makes.</summary>
    public double RecoilSpeed { get; init; } = 4;

    /// <summary>How far into the back-and-forth the gun is fully back, 0 to 1.</summary>
    public double RecoilPeak { get; init; } = 0.5;

    /// <summary>Seconds since the gun last fired, while it recoils; -1 otherwise.</summary>
    public double RecoilTime { get; set; } = -1;

    /// <summary>Where it points now, in degrees from straight ahead.</summary>
    public double Turn { get; set; }

    /// <summary>Where it is turning to.</summary>
    public double TurnTo { get; set; }

    /// <summary>How far its gun is raised now, in degrees.</summary>
    public double Raise { get; set; }

    /// <summary>Where its gun is being raised to.</summary>
    public double RaiseTo { get; set; }

    /// <summary>Whether it moves on the model at all, so the viewer offers to aim it.</summary>
    public bool CanAim => TurnNode >= 0 || RaiseNode >= 0;

    /// <summary>Whether it is still turning towards where it was aimed.</summary>
    public bool IsTurning => Math.Abs(Turn - TurnTo) > 0.01 || Math.Abs(Raise - RaiseTo) > 0.01;

    /// <summary>
    /// How far back its gun is now, 0 to 1: out at the recoil's speed to the peak, then home again in the time left of
    /// the back-and-forth.
    /// </summary>
    public double RecoilAmount
    {
        get
        {
            if (RecoilTime < 0) return 0;
            double through = RecoilTime * RecoilSpeed;
            if (through >= 1) return 0;
            return through < RecoilPeak ? through / RecoilPeak : 1 - (through - RecoilPeak) / (1 - RecoilPeak);
        }
    }

    /// <summary>Moves it towards where it was aimed, at its speed, and its gun's recoil on, for this many seconds.</summary>
    public void Step(double seconds)
    {
        double most = DegreesASecond * seconds;
        Turn += Math.Clamp(TurnTo - Turn, -most, most);
        Raise += Math.Clamp(RaiseTo - Raise, -most, most);
        if (RecoilTime >= 0)
        {
            RecoilTime += seconds;
            if (RecoilTime * RecoilSpeed >= 1) RecoilTime = -1;
        }
    }
}
