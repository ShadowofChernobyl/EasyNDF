using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Viewer;

/// <summary>One condition of the Filter Descriptors window.</summary>
/// <param name="On">The condition's switch.</param>
/// <param name="Property">A property name, or "(Name)" for the descriptor's name, or "(Type)" for its type.</param>
/// <param name="Test">contains, is, is not, starts with, ends with, is greater than, is less than, exists or is missing.</param>
/// <param name="Value">What to test against. Quotes around it are ignored, and so is the difference between capital and small letters.</param>
public sealed record FilterCondition(bool On, string? Property, string Test, string? Value);

/// <summary>
/// Which descriptors the Editor tab's descriptor list shows when Filter Descriptors is in use.
/// </summary>
/// <remarks>Matching uses the filter index (<see cref="DescriptorViewer.Index"/>).</remarks>
public static class DescriptorFilter
{
    /// <summary>
    /// A condition counts only once it is complete: switched on, with a property, and with a value unless the test is
    /// "exists" or "is missing". Until then it filters nothing.
    /// </summary>
    public static bool IsComplete(FilterCondition c) =>
        c.On && !string.IsNullOrEmpty(c.Property) &&
        (c.Test is "exists" or "is missing" || JsText.Trim(c.Value ?? "") != "");

    /// <summary>True when the descriptor passes the filter: every complete condition with "All", one of them otherwise.</summary>
    /// <param name="descriptor">The descriptor's index entry, or null when it is not in the index.</param>
    /// <param name="conditions">The filter's conditions.</param>
    /// <param name="gateIsAny">The "Show descriptors where any/all of these match" choice.</param>
    public static bool Passes(IndexEntry? descriptor, IEnumerable<FilterCondition> conditions, bool gateIsAny)
    {
        var complete = conditions.Where(IsComplete).ToList();
        if (complete.Count == 0) return true;
        if (descriptor is null) return false;

        List<string> ValuesOf(FilterCondition c) => c.Property switch
        {
            "(Name)" => [descriptor.Name],
            "(Type)" => [descriptor.Type],
            _ => descriptor.Properties.Where(p => p.Name == c.Property).Select(p => p.Value).ToList(),
        };
        return gateIsAny
            ? complete.Any(c => Test(c, ValuesOf(c)))
            : complete.All(c => Test(c, ValuesOf(c)));
    }

    /// <summary>One condition against the values of its property.</summary>
    public static bool Test(FilterCondition c, List<string> values)
    {
        string wanted = Unquoted(c.Value).ToLowerInvariant();
        double wantedNumber = JsNumber.ParseFloat(wanted);

        switch (c.Test)
        {
            case "exists": return values.Count > 0;
            case "is missing": return values.Count == 0;
            case "is not": return !values.Any(v => Unquoted(v).ToLowerInvariant() == wanted);
        }

        return values.Any(v =>
        {
            string t = Unquoted(v).ToLowerInvariant();
            double x = JsNumber.ParseFloat(t);
            return c.Test switch
            {
                "contains" => t.Contains(wanted, StringComparison.Ordinal),
                "is" => t == wanted,
                "starts with" => t.StartsWith(wanted, StringComparison.Ordinal),
                "ends with" => t.EndsWith(wanted, StringComparison.Ordinal),
                "is greater than" => !double.IsNaN(x) && !double.IsNaN(wantedNumber) && x > wantedNumber,
                "is less than" => !double.IsNaN(x) && !double.IsNaN(wantedNumber) && x < wantedNumber,
                _ => false,
            };
        });
    }

    /// <summary>A value trimmed and without the quotes around it, if it has a pair: 'Text' gives Text.</summary>
    public static string Unquoted(string? value)
    {
        string v = JsText.Trim(value ?? "");
        bool quoted = v.Length > 1 && v[0] is '\'' or '"' && v[^1] == v[0] && v.IndexOfAny(['\n', '\r', '\u2028', '\u2029']) < 0;
        return quoted ? v[1..^1] : v;
    }
}
