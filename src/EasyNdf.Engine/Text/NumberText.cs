using System.Text.RegularExpressions;

namespace EasyNdf.Engine.Text;

/// <summary>Recognising numbers in text, the way the HTML app does.</summary>
public static partial class NumberText
{
    /// <summary>
    /// True for a plain decimal number as NDF writes it: 12, -3, 0.5, .5 and 5. all count.
    /// Hexadecimal such as 0xFF is read from files but never calculated with, so it does not count here.
    /// </summary>
    public static bool IsPlainNumber(string text) => PlainNumber().IsMatch(text);

    /// <summary>
    /// A number typed into a rule: digits with an optional sign and decimal point, and nothing else
    /// (spaces around it are allowed). "1,5" and "2x" are not numbers. Gives NaN for anything else.
    /// </summary>
    public static double ParseTyped(string? text)
    {
        string t = JsText.Trim(text ?? "");
        return TypedNumber().IsMatch(t) ? JsNumber.ParsePlain(t.TrimStart('+')) : double.NaN;
    }

    /// <summary>The message shown when a rule needs a number and got something else.</summary>
    public static string NotANumberMessage(string? value) =>
        "\"" + JsText.Trim(value ?? "") + "\" is not a number. Write decimals with a point, like 1.5, and leave out any other text.";

    [GeneratedRegex(@"\A-?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)\z")]
    private static partial Regex PlainNumber();

    [GeneratedRegex(@"\A[-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)\z")]
    private static partial Regex TypedNumber();
}
