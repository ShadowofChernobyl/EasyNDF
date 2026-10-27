namespace EasyNdf.App.Units;

/// <summary>The game files units, their weapons and divisions come from, as named on the Files tab.</summary>
public static class UnitFiles
{
    /// <summary>The weapon sets' file.</summary>
    public const string WeaponSets = "WeaponDescriptor.ndf";

    /// <summary>The missiles' file.</summary>
    public const string Missiles = "MissileDescriptors.ndf";

    /// <summary>The ammunition's file, where a weapon does not name its own.</summary>
    public const string Ammunition = "Ammunition.ndf";

    /// <summary>The divisions' file, and where it is in the game's files.</summary>
    public const string Divisions = "Divisions.ndf", DivisionsPath = "GameData/Generated/Gameplay/Decks/Divisions.ndf";
}
