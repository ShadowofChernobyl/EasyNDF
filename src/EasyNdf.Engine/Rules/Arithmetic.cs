using System.Numerics;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Rules;

/// <summary>
/// The calculations actions do, and how their results are written.
/// </summary>
/// <remarks>
/// A plain number keeps about 15 significant digits. Whole numbers past that are worked out exactly instead (BigInteger);
/// a calculation that cannot be exact on such a number is not written, and the run says so.
/// </remarks>
public static partial class Arithmetic
{
    /// <summary>The four calculations, by the name actions use.</summary>
    public static readonly Dictionary<string, Func<double, double, double>> Operations = new()
    {
        ["Add"] = (x, y) => x + y,
        ["Subtract"] = (x, y) => x - y,
        ["Multiply"] = (x, y) => x * y,
        ["Divide"] = (x, y) => x / y,
    };

    /// <summary>
    /// Rounds a result to the "Decimal places" setting. A result that would round to 0 without being 0 keeps its first
    /// digit instead, so a small value is never wiped out by the setting.
    /// </summary>
    /// <param name="value">The result.</param>
    /// <param name="decimalPlaces">The setting, or null for no limit.</param>
    public static double LimitDecimals(double value, int? decimalPlaces)
    {
        if (decimalPlaces is null || JsNumber.IsInteger(value)) return value;
        double scale = Math.Pow(10, decimalPlaces.Value);
        double rounded = JsNumber.Round(value * scale) / scale;
        return rounded == 0 && value != 0 ? JsNumber.ParseFormatted(JsNumber.ToPrecision(value, 1)) : rounded;
    }

    /// <summary>
    /// Writes a calculated number. About 12 significant digits are kept, which hides floating-point noise such as
    /// 0.30000000000000004. A value that was written with a decimal point keeps one: 2.0 times 2 is written 4.0.
    /// </summary>
    public static string Format(double value, bool wasDecimal)
    {
        string text = JsNumber.IsInteger(value) && Math.Abs(value) <= JsNumber.MaxSafeInteger
            ? JsNumber.ToText(value)
            : JsNumber.ToText(JsNumber.ParseFormatted(JsNumber.ToPrecision(value, 12)));
        if (text.Contains('e')) text = TrailingZeros().Replace(JsNumber.ToFixed(value, 10), "");
        if (wasDecimal && !text.Contains('.')) text += ".0";
        return text;
    }

    /// <summary>True when a whole number has too many digits for a double to hold exactly.</summary>
    public static bool HasTooManyDigits(string text) => SignificantDigits(text) > 15;

    /// <summary>True when a result is beyond the whole numbers a double holds exactly.</summary>
    public static bool IsUnsafe(double value) => Math.Abs(value) > JsNumber.MaxSafeInteger;

    /// <summary>True for a whole number with an optional sign: the numbers worked out exactly when they are long.</summary>
    public static bool IsWholeNumberText(string text) => WholeNumber().IsMatch(text);

    private static int SignificantDigits(string text)
    {
        // JavaScript: text.replace(/^[-+]/, '').replace('.', '').replace(/^0+/, '').replace(/e.*$/i, '').length
        string digits = text.Length > 0 && text[0] is '+' or '-' ? text[1..] : text;
        int point = digits.IndexOf('.');
        if (point >= 0) digits = digits.Remove(point, 1);
        digits = digits.TrimStart('0');
        int e = digits.IndexOfAny(['e', 'E']);
        if (e >= 0) digits = digits[..e];
        return digits.Length;
    }

    /// <summary>
    /// Adds, subtracts or multiplies whole numbers of any length exactly. Divides only when the result is a whole number.
    /// Returns null when the calculation cannot be exact.
    /// </summary>
    public static string? Exact(string operation, string left, string right)
    {
        try
        {
            var x = BigInteger.Parse(left.StartsWith('+') ? left[1..] : left, System.Globalization.CultureInfo.InvariantCulture);
            var y = BigInteger.Parse(right.StartsWith('+') ? right[1..] : right, System.Globalization.CultureInfo.InvariantCulture);
            switch (operation)
            {
                case "Add": return (x + y).ToString(System.Globalization.CultureInfo.InvariantCulture);
                case "Subtract": return (x - y).ToString(System.Globalization.CultureInfo.InvariantCulture);
                case "Multiply": return (x * y).ToString(System.Globalization.CultureInfo.InvariantCulture);
                case "Divide" when !y.IsZero && (x % y).IsZero: return (x / y).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (FormatException) { }
        return null;
    }

    [GeneratedRegex(@"\.?0+\z")]
    private static partial Regex TrailingZeros();

    [GeneratedRegex(@"\A[-+]?[0-9]+\z")]
    private static partial Regex WholeNumber();
}
