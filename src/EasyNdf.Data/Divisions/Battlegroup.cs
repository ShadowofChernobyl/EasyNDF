namespace EasyNdf.Data.Divisions;

/// <summary>
/// A division's Official Battlegroup: its deck in Decks.ndf, which the game's Battlegroups list shows as
/// "... OFFICIAL BATTLEGROUP" and calls the division's "autofill deck". The game allows one per division a player can
/// pick, so a mod can change it but cannot add a second; its cards must fit the division's rules, or GenerateMod stops.
/// </summary>
public sealed record Battlegroup
{
    /// <summary>The deck's descriptor: "Descriptor_Deck_US_3rd_Arm_multi".</summary>
    public required string Descriptor { get; init; }

    /// <summary>The text key of its name (DeckName): "UNCZVMGPSQ".</summary>
    public string NameToken { get; init; } = "";

    /// <summary>Its name as the game shows it; "" when the game's texts do not have it.</summary>
    public string Name { get; init; } = "";

    /// <summary>Its cards, in the order the game shows them.</summary>
    public IReadOnlyList<BattlegroupCard> Cards { get; init; } = [];

    /// <summary>
    /// For a generated battlegroup, why each card was chosen, in the order of <see cref="Cards"/>
    /// (<see cref="BattlegroupGenerator"/>); null for any other.
    /// </summary>
    public IReadOnlyList<GeneratedCard>? Generated { get; init; }
}

/// <summary>
/// One card of a battlegroup: a unit, at a veterancy level, in a transport or on foot. In the game's files it is a
/// pack of DeckPacks.ndf, which the deck names.
/// </summary>
/// <param name="Unit">The unit's descriptor: "Descriptor_Unit_M1A1_Abrams_US".</param>
/// <param name="Veterancy">Its veterancy level: 0 for the lowest.</param>
/// <param name="Transport">Its transport's descriptor; null for none.</param>
/// <param name="Number">
/// The pack's Number in DeckPacks.ndf. The game's own cards all have 1; some mods write the units the card holds
/// (YSM writes 999 in its Unlimited decks). It is kept as the files give it, and a new card has 1, as the game's.
/// </param>
public sealed record BattlegroupCard(string Unit, int Veterancy, string? Transport, int Number = 1)
{
    /// <summary>
    /// The card's descriptor in DeckPacks.ndf, named as the game names each of its own: the unit, its transport, its
    /// veterancy and its Number, "Descriptor_Deck_Pack_Rifles_US_M113_US_1_1".
    /// </summary>
    public string PackName => "Descriptor_Deck_Pack_" + CoreName(Unit) + (Transport is null ? "" : "_" + CoreName(Transport)) + "_" + Veterancy + "_" + Number;

    /// <summary>A unit's descriptor without "Descriptor_Unit_": "Rifles_US".</summary>
    private static string CoreName(string unit) =>
        unit.StartsWith("Descriptor_Unit_", StringComparison.Ordinal) ? unit["Descriptor_Unit_".Length..] : unit;
}
