using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// What kinds of transports a unit has, told from the tags the game gives each transport: "Helo" for a helicopter,
/// "Vehicule_Transport_Arme" for an armed vehicle (an APC or IFV), "Vehicule_Transport" for an unarmed one (a truck
/// or a jeep). Both games tag their transports so.
/// </summary>
public static class TransportKinds
{
    /// <summary>A transport's kind, in the order lists show them.</summary>
    public enum Kind { ArmedVehicle, UnarmedVehicle, Helicopter, Other }

    /// <summary>"2 armed vehicles · 1 unarmed vehicle": how many of each kind, most first; "" for no transports.</summary>
    public static string Summary(IEnumerable<Unit> transports)
    {
        IEnumerable<string> parts = transports.GroupBy(KindOf)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => Texts.Count(TextName(group.Key), group.Count()));
        return string.Join(" · ", parts);
    }

    /// <summary>A transport's kind; a helicopter first, as a transport helicopter can carry the vehicle tags too.</summary>
    public static Kind KindOf(Unit transport)
    {
        bool Has(string tag) => transport.Tags.Contains(tag, StringComparer.Ordinal);
        if (Has("Helo")) return Kind.Helicopter;
        if (Has("Vehicule_Transport_Arme")) return Kind.ArmedVehicle;
        if (Has("Vehicule_Transport")) return Kind.UnarmedVehicle;
        return Kind.Other;
    }

    /// <summary>A kind's name as a list's heading: "Armed vehicles".</summary>
    public static string Heading(Kind kind) => Texts.Get(kind switch
    {
        Kind.ArmedVehicle => "Divisions.Transports.Group.Armed",
        Kind.UnarmedVehicle => "Divisions.Transports.Group.Unarmed",
        Kind.Helicopter => "Divisions.Transports.Group.Helicopter",
        _ => "Divisions.Transports.Group.Other",
    });

    private static string TextName(Kind kind) => kind switch
    {
        Kind.ArmedVehicle => "Divisions.Transports.Kind.Armed",
        Kind.UnarmedVehicle => "Divisions.Transports.Kind.Unarmed",
        Kind.Helicopter => "Divisions.Transports.Kind.Helicopter",
        _ => "Divisions.Transports.Kind.Other",
    };
}
