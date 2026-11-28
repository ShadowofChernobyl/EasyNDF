using System.Text.Json;
using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// Number formatting must match JavaScript's exactly. Cases/js-numbers.json holds about 3,000 numbers with the text
/// Node.js gives for String(x), x.toPrecision(p), x.toFixed(f) and Math.round(x), including exact halves such as 0.25.
/// </summary>
public class JsNumberTests
{
    private sealed record NumberCase(string X, string Bits, string S, string[] P, string[] F, string R);

    private static readonly int[] Precisions = [1, 2, 3, 6, 12];
    private static readonly int[] FractionDigits = [0, 1, 2, 5, 10];

    private static List<NumberCase> Load() =>
        JsonSerializer.Deserialize<List<NumberCase>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", "js-numbers.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    /// <summary>The number exactly as Node.js had it, from its bytes.</summary>
    private static double ValueOf(NumberCase c) => BitConverter.ToDouble(Convert.FromHexString(c.Bits));

    [Fact]
    public void String_of_a_number_matches_JavaScript()
    {
        foreach (var c in Load()) Assert.Equal(c.S, JsNumber.ToText(ValueOf(c)));
    }

    [Fact]
    public void ToPrecision_matches_JavaScript()
    {
        foreach (var c in Load())
            for (int i = 0; i < Precisions.Length; i++)
                Assert.True(c.P[i] == JsNumber.ToPrecision(ValueOf(c), Precisions[i]), $"{c.X}.toPrecision({Precisions[i]}): expected {c.P[i]}, got {JsNumber.ToPrecision(ValueOf(c), Precisions[i])}");
    }

    [Fact]
    public void ToFixed_matches_JavaScript()
    {
        foreach (var c in Load())
            for (int i = 0; i < FractionDigits.Length; i++)
                Assert.True(c.F[i] == JsNumber.ToFixed(ValueOf(c), FractionDigits[i]), $"{c.X}.toFixed({FractionDigits[i]}): expected {c.F[i]}, got {JsNumber.ToFixed(ValueOf(c), FractionDigits[i])}");
    }

    [Fact]
    public void Round_matches_JavaScript()
    {
        foreach (var c in Load()) Assert.Equal(c.R, JsNumber.ToText(JsNumber.Round(ValueOf(c))));
    }

    [Theory]
    [InlineData(0.25, 1, "0.3")]   // an exact half rounds up, as in JavaScript (.NET would give 0.2)
    [InlineData(-0.25, 1, "-0.3")]
    [InlineData(9.99, 2, "10")]
    [InlineData(1e21, 3, "1.00e+21")]
    public void ToPrecision_examples(double value, int precision, string expected) =>
        Assert.Equal(expected, JsNumber.ToPrecision(value, precision));
}
