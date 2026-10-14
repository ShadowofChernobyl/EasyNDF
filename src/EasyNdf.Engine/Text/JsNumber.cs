using System.Globalization;
using System.Numerics;
using System.Text;

namespace EasyNdf.Engine.Text;

/// <summary>
/// Number formatting and rounding that behave exactly like JavaScript's.
/// </summary>
/// <remarks>
/// Numbers a rule calculates are written into the exported file as text, so the text must be the same as the
/// HTML app writes. C# and JavaScript format numbers differently:
/// <list type="bullet">
/// <item>JavaScript writes 1e+21 where C# writes 1E+21, and switches to exponent form at different sizes.</item>
/// <item>C# writes negative zero as "-0"; JavaScript writes "0".</item>
/// <item>Math.round in JavaScript rounds halves up (-2.5 becomes -2); Math.Round in C# rounds halves to even.</item>
/// </list>
/// The digits themselves agree: both languages produce the shortest text that reads back as the same number.
/// </remarks>
public static class JsNumber
{
    /// <summary>The largest whole number a double holds exactly: JavaScript's Number.MAX_SAFE_INTEGER.</summary>
    public const double MaxSafeInteger = 9007199254740991;

    /// <summary>JavaScript's String(number).</summary>
    public static string ToText(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0) return "0"; // also negative zero

        string sign = value < 0 ? "-" : "";
        var (digits, pointPosition) = ShortestDigits(Math.Abs(value));
        int k = digits.Length, n = pointPosition;

        // These four cases are the ones in the ECMAScript specification, Number::toString.
        if (k <= n && n <= 21)
            return sign + digits + new string('0', n - k);
        if (0 < n && n <= 21)
            return sign + digits[..n] + "." + digits[n..];
        if (-6 < n && n <= 0)
            return sign + "0." + new string('0', -n) + digits;

        int exponent = n - 1;
        string exponentText = (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
        return k == 1
            ? sign + digits + "e" + exponentText
            : sign + digits[0] + "." + digits[1..] + "e" + exponentText;
    }

    /// <summary>JavaScript's number.toPrecision(precision).</summary>
    public static string ToPrecision(double value, int precision)
    {
        if (!double.IsFinite(value)) return ToText(value);
        if (value == 0) value = 0; // negative zero has no sign in JavaScript

        string sign = value < 0 ? "-" : "";
        var (digits, exponent) = RoundedDigits(Math.Abs(value), precision);

        if (exponent < -6 || exponent >= precision)
        {
            string head = precision == 1 ? digits : digits[0] + "." + digits[1..];
            return sign + head + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
        }
        if (exponent == precision - 1) return sign + digits;
        if (exponent >= 0) return sign + digits[..(exponent + 1)] + "." + digits[(exponent + 1)..];
        return sign + "0." + new string('0', -(exponent + 1)) + digits;
    }

    /// <summary>JavaScript's number.toFixed(fractionDigits).</summary>
    public static string ToFixed(double value, int fractionDigits)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= 1e21) return ToText(value);
        string sign = value < 0 ? "-" : ""; // negative zero has no sign in JavaScript, but -0.0000001 keeps its sign

        // The whole number n closest to value × 10^fractionDigits, with halves rounded up.
        var (digits, pointPosition) = ExactDigits(Math.Abs(value));
        int wholeDigits = pointPosition + fractionDigits;
        string n = RoundToDigits(digits, wholeDigits, out _).TrimStart('0');
        if (n == "") n = "0";

        if (fractionDigits == 0) return sign + n;
        if (n.Length <= fractionDigits) n = new string('0', fractionDigits + 1 - n.Length) + n;
        return sign + n[..^fractionDigits] + "." + n[^fractionDigits..];
    }

    /// <summary>JavaScript's Math.round: the nearest whole number, with halves rounded up (towards +infinity).</summary>
    public static double Round(double value)
    {
        if (!double.IsFinite(value)) return value;
        double floor = Math.Floor(value);
        return value - floor >= 0.5 ? floor + 1 : floor;
    }

    /// <summary>JavaScript's Number.isInteger.</summary>
    public static bool IsInteger(double value) => double.IsFinite(value) && Math.Floor(value) == value;

    /// <summary>JavaScript's parseFloat, for text already checked to be a plain number such as "-12.5", ".5" or "5.".</summary>
    public static double ParsePlain(string text) =>
        double.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    /// <summary>
    /// JavaScript's parseFloat in general: the longest number at the start of the text (after white space), ignoring
    /// whatever follows, so "12abc" gives 12 and "1e3x" gives 1000. NaN when the text does not start with a number.
    /// </summary>
    public static double ParseFloat(string text)
    {
        text = JsText.TrimStart(text);
        int i = 0;
        if (i < text.Length && text[i] is '+' or '-') i++;
        if (string.CompareOrdinal(text, i, "Infinity", 0, 8) == 0)
            return text[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;

        int digitsStart = i;
        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        bool digitsBefore = i > digitsStart, digitsAfter = false;
        if (i < text.Length && text[i] == '.')
        {
            int j = i + 1;
            while (j < text.Length && char.IsAsciiDigit(text[j])) j++;
            digitsAfter = j > i + 1;
            if (digitsBefore || digitsAfter) i = j;
        }
        if (!digitsBefore && !digitsAfter) return double.NaN;

        // An exponent counts only when digits follow it.
        if (i < text.Length && text[i] is 'e' or 'E')
        {
            int j = i + 1;
            if (j < text.Length && text[j] is '+' or '-') j++;
            int exponentStart = j;
            while (j < text.Length && char.IsAsciiDigit(text[j])) j++;
            if (j > exponentStart) i = j;
        }
        return double.Parse(text[..i], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads back a number this class formatted, which may be in exponent form such as "1e-7".</summary>
    public static double ParseFormatted(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// The shortest digits that read back as the value, and where the decimal point goes:
    /// value = 0.digits × 10^pointPosition. Leading and trailing zeros are left out.
    /// </summary>
    private static (string Digits, int PointPosition) ShortestDigits(double positiveValue)
    {
        // "R" gives the shortest round-trip text, such as "123.45", "1E+21" or "1.5E-07".
        string text = positiveValue.ToString("R", CultureInfo.InvariantCulture);
        int exponent = 0;
        int e = text.IndexOf('E');
        if (e >= 0)
        {
            exponent = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..e];
        }
        int point = text.IndexOf('.');
        string allDigits = point < 0 ? text : text.Remove(point, 1);
        int pointPosition = (point < 0 ? text.Length : point) + exponent;

        int lead = 0;
        while (lead < allDigits.Length - 1 && allDigits[lead] == '0') { lead++; pointPosition--; }
        string digits = allDigits[lead..].TrimEnd('0');
        return (digits.Length == 0 ? "0" : digits, pointPosition);
    }

    /// <summary>The value rounded to a number of significant digits, and the power of ten of the first digit.</summary>
    private static (string Digits, int Exponent) RoundedDigits(double positiveValue, int precision)
    {
        if (positiveValue == 0) return (new string('0', precision), 0);
        var (digits, pointPosition) = ExactDigits(positiveValue);
        string rounded = RoundToDigits(digits, precision, out bool carried);
        // Rounding 9.99 up gives 10.0: one more digit, so the first digit is a power of ten higher.
        return carried ? (rounded[..precision], pointPosition) : (rounded, pointPosition - 1);
    }

    /// <summary>
    /// The exact decimal digits of a positive double, without leading zeros: value = 0.digits × 10^pointPosition.
    /// Every double is a whole number times a power of two, so its decimal form always ends.
    /// </summary>
    private static (string Digits, int PointPosition) ExactDigits(double positiveValue)
    {
        long bits = BitConverter.DoubleToInt64Bits(positiveValue);
        int exponentBits = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponentBits == 0) exponentBits = 1; // subnormal numbers
        else mantissa |= 1L << 52;
        int power = exponentBits - 1075; // value = mantissa × 2^power

        string digits;
        int pointPosition;
        if (power >= 0)
        {
            digits = (new BigInteger(mantissa) << power).ToString(CultureInfo.InvariantCulture);
            pointPosition = digits.Length;
        }
        else
        {
            // mantissa / 2^k = mantissa × 5^k / 10^k
            digits = (new BigInteger(mantissa) * BigInteger.Pow(5, -power)).ToString(CultureInfo.InvariantCulture);
            pointPosition = digits.Length + power;
        }
        string trimmed = digits.TrimStart('0');
        return (trimmed, pointPosition - (digits.Length - trimmed.Length));
    }

    /// <summary>
    /// Keeps the first <paramref name="count"/> digits, rounding halves up, as JavaScript does ("pick the larger n").
    /// Pads with zeros when there are fewer. <paramref name="carried"/> is true when rounding up added a digit in front.
    /// </summary>
    private static string RoundToDigits(string digits, int count, out bool carried)
    {
        carried = false;
        if (count < 0) return "";
        if (digits.Length <= count) return digits + new string('0', count - digits.Length);

        var kept = new StringBuilder(digits[..count]);
        if (digits[count] >= '5')
        {
            int i = count - 1;
            while (i >= 0 && kept[i] == '9') kept[i--] = '0';
            if (i >= 0) kept[i]++;
            else { kept.Insert(0, '1'); carried = true; }
        }
        return kept.ToString();
    }
}
