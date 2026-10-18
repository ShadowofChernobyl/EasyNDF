using EasyNdf.App.Text;

namespace EasyNdf.App.Units;

/// <summary>How the Units tab writes its numbers: with the language's separators, one decimal at most, "–" for none.</summary>
public static class UnitNumbers
{
    /// <summary>The dash shown where a unit has no such number.</summary>
    public const string None = "–";

    /// <summary>A number with at most <paramref name="decimals"/> decimals: 1234.56 is "1,234.6"; null is "–".</summary>
    public static string Of(double? value, int decimals = 1)
    {
        if (value is not double number) return None;
        string format = decimals == 0 ? "#,0" : "#,0." + new string('#', decimals);
        return number.ToString(format, Texts.Catalog.Culture);
    }

    /// <summary>
    /// How far a value is from the pinned unit's: "+5", "−3" (with a true minus sign), "=" when the same, and "" when
    /// either is missing.
    /// </summary>
    public static string Difference(double? value, double? pinned)
    {
        if (value is not double own || pinned is not double other) return "";
        double difference = own - other;
        if (difference == 0) return "=";
        return (difference > 0 ? "+" : "−") + Of(Math.Abs(difference));
    }

    /// <summary>Seconds, such as "4.5 s"; in a narrow table without the space, "4.5s".</summary>
    public static string Seconds(double? value, bool narrow) =>
        value is null ? None : Texts.Get(narrow ? "Units.Seconds.Short" : "Units.Seconds", ("seconds", Of(value)));
}
