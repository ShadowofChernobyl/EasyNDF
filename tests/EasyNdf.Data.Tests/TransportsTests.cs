using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

public class TransportsTests
{
    private static Unit Rider(string side = "NATO", params string[] tags) =>
        new() { Descriptor = "Descriptor_Unit_Squad", Name = "Squad", Side = side, Tags = tags.Length > 0 ? tags : ["Infanterie", "Crew"], SeatsTaken = 1, HasDeckNumber = true };

    private static Unit Transport(string descriptor = "Descriptor_Unit_Truck", string side = "NATO", double seats = 1, bool hasDeckNumber = true, params string[] carries) =>
        new() { Descriptor = descriptor, Name = descriptor, Side = side, CarriesTags = carries.Length > 0 ? carries : ["Crew"], Seats = seats, HasDeckNumber = hasDeckNumber };

    [Fact]
    public void A_transport_carrying_one_of_the_units_tags_can_carry_it() =>
        Assert.True(Transports.CanCarry(Transport(), Rider()));

    [Fact]
    public void A_transport_carrying_none_of_its_tags_cannot() =>
        Assert.False(Transports.CanCarry(Transport(carries: "Unite_transportable"), Rider()));

    [Fact]
    public void A_unit_that_is_not_a_transport_carries_nothing() =>
        Assert.False(Transports.CanCarry(new Unit { Descriptor = "Descriptor_Unit_Tank", Name = "Tank", Side = "NATO", HasDeckNumber = true }, Rider()));

    [Fact]
    public void A_unit_that_cannot_ride_is_never_carried()
    {
        var tank = new Unit { Descriptor = "Descriptor_Unit_Tank", Name = "Tank", Side = "NATO", Tags = ["Crew"], HasDeckNumber = true };
        Assert.False(Transports.CanCarry(Transport(), tank));
    }

    [Fact]
    public void The_other_side_too_few_seats_or_no_deck_number_rule_a_transport_out()
    {
        Assert.False(Transports.CanCarry(Transport(side: "PACT"), Rider()));
        Assert.False(Transports.CanCarry(Transport(seats: 0.5), Rider()));
        Assert.False(Transports.CanCarry(Transport(hasDeckNumber: false), Rider()));
    }

    [Fact]
    public void Warno_offers_every_transport_that_can_carry_the_unit()
    {
        Unit squad = Rider();
        var catalog = new UnitCatalog(GameTitle.Warno, [squad, Transport("Descriptor_Unit_Truck"), Transport("Descriptor_Unit_Gun_Tractor", carries: "Unite_transportable")],
            new Dictionary<string, string>(), [], []);
        Assert.Equal(["Descriptor_Unit_Truck"], Transports.ValidFor(squad, catalog).Select(unit => unit.Descriptor));
    }

    [Fact]
    public void Steel_division_2_offers_only_the_transports_its_divisions_give_the_unit()
    {
        Unit squad = Rider();
        var division = new Division
        {
            Descriptor = "Descriptor_Deck_Division_Test", Name = "Test",
            Units = [new DivisionUnit { Unit = squad.Descriptor, Transports = ["Descriptor_Unit_Halftrack"] }],
        };
        var catalog = new UnitCatalog(GameTitle.SteelDivision2, [squad, Transport("Descriptor_Unit_Truck"), Transport("Descriptor_Unit_Halftrack")],
            new Dictionary<string, string>(), [], [division]);
        Assert.Equal(["Descriptor_Unit_Halftrack"], Transports.ValidFor(squad, catalog).Select(unit => unit.Descriptor));
    }
    [Fact]
    public void Steel_division_2_offers_a_unit_in_none_of_its_divisions_every_transport_that_can_carry_it()
    {
        // a copy of a game unit that a mod adds: no division of the game gives it a transport yet
        var copy = new Unit { Descriptor = "Descriptor_Unit_EZ_Squad", Name = "Squad", Side = "NATO", Tags = ["Infanterie", "Crew"], SeatsTaken = 1, HasDeckNumber = true };
        var catalog = new UnitCatalog(GameTitle.SteelDivision2, [copy, Transport("Descriptor_Unit_Truck"), Transport("Descriptor_Unit_Gun_Tractor", carries: "Unite_transportable")],
            new Dictionary<string, string>(), [], []);
        Assert.Equal(["Descriptor_Unit_Truck"], Transports.ValidFor(copy, catalog).Select(unit => unit.Descriptor));
    }
}
