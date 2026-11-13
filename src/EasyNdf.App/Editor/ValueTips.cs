using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Text;
using EasyNdf.Engine.Editing;

namespace EasyNdf.App.Editor;

/// <summary>
/// The File Editor's tip for a name or a value, as the mock chose: what is wrong first (on a marked value), then the
/// name and its value type, what it does, its value's words, the choices written in the files, what the value on the
/// line means, and a line when what it does is not confirmed.
/// </summary>
public static class ValueTips
{
    /// <summary>The most choices listed; the rest are counted.</summary>
    private const int MostChoices = 8;

    /// <summary>The tip's width, as the Armory's tips.</summary>
    internal const double Width = 380;

    /// <summary>A tip with a problem and a name's guide; either may be missing. Null when there is nothing to say.</summary>
    /// <param name="problem">What is wrong at the place, and what it does; null for nothing.</param>
    /// <param name="name">The name the tip is about.</param>
    /// <param name="info">What the name takes; null when the guide doesn't know it.</param>
    /// <param name="value">The value on the line, for "On this line"; null for none (a suggestion's tip).</param>
    public static FrameworkElement? For((string Message, string Why, bool IsWarning)? problem, string name, ValueInfo? info, string? value)
    {
        if (problem is null && info is null) return null;
        var tip = new StackPanel { MaxWidth = Width };
        if (problem is { } wrong) tip.Children.Add(ProblemPart(wrong, hasMore: info is not null));
        if (info is null) return tip;
        tip.Children.Add(Heading(name, info.ValueType));
        if (info.Description != "") tip.Children.Add(Words(info.Description));
        foreach (string rule in info.Rules) tip.Children.Add(Dim(Words(rule)));
        if (info.Choices.Count > 0) tip.Children.Add(Choices(info.Choices));
        if (value is not null && Reading(name, value, info) is { Length: > 0 } reading) tip.Children.Add(OnThisLine(reading));
        if (CertaintyLine(info.Certainty) is { } sure) tip.Children.Add(sure);
        return tip;
    }

    /// <summary>"● What is wrong" in the error colour (▲ and the warning colour for a warning), with what it does under it, dim.</summary>
    internal static FrameworkElement ProblemPart((string Message, string Why, bool IsWarning) problem, bool hasMore)
    {
        string colour = problem.IsWarning ? "caution" : "warn";
        var what = new TextBlock { Text = (problem.IsWarning ? "▲ " : "● ") + problem.Message, TextWrapping = TextWrapping.Wrap };
        what.SetResourceReference(TextBlock.ForegroundProperty, colour);
        var part = new StackPanel();
        part.Children.Add(what);
        if (problem.Why != "")
        {
            var why = new TextBlock { Text = problem.Why, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
            why.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            part.Children.Add(why);
        }
        if (!hasMore) return part;
        // a line under the problem, between it and the name's own tip
        var underlined = new Border { Child = part, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 5), Margin = new Thickness(0, 0, 0, 5) };
        underlined.SetResourceReference(Border.BorderBrushProperty, "line");
        return underlined;
    }

    /// <summary>"AimingTime: float", the name in the types' colour and the value type dim, in the code font.</summary>
    internal static TextBlock Heading(string name, string valueType)
    {
        var heading = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        var nameRun = new Run(name);
        nameRun.SetResourceReference(TextElement.ForegroundProperty, "s-type");
        var typeRun = new Run(": " + valueType);
        typeRun.SetResourceReference(TextElement.ForegroundProperty, "muted");
        heading.Inlines.Add(nameRun);
        heading.Inlines.Add(typeRun);
        return heading;
    }

    internal static TextBlock Words(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };

    internal static TextBlock Dim(TextBlock text)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        text.Margin = new Thickness(0, 4, 0, 0);
        return text;
    }

    /// <summary>"Choices: Fake, Obus, …, and 2 more", the choices in the code font and the references' colour.</summary>
    private static TextBlock Choices(IReadOnlyList<string> choices)
    {
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        var label = new Run(Texts.Get("Editor.Tip.Choices"));
        label.SetResourceReference(TextElement.ForegroundProperty, "muted");
        line.Inlines.Add(label);
        for (int k = 0; k < Math.Min(MostChoices, choices.Count); k++)
        {
            if (k > 0) line.Inlines.Add(new Run(", "));
            var choice = new Run(ValueMessages.LastPart(choices[k]));
            choice.SetResourceReference(TextElement.FontFamilyProperty, "mono");
            choice.SetResourceReference(TextElement.ForegroundProperty, "s-ref");
            line.Inlines.Add(choice);
        }
        if (choices.Count > MostChoices) line.Inlines.Add(new Run(Texts.Get("Editor.Tip.MoreChoices", ("count", choices.Count - MostChoices))));
        return line;
    }

    /// <summary>"On this line: 3 seconds.", under a line.</summary>
    private static FrameworkElement OnThisLine(string reading)
    {
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var label = new Run(Texts.Get("Editor.Tip.OnThisLine"));
        label.SetResourceReference(TextElement.ForegroundProperty, "muted");
        line.Inlines.Add(label);
        line.Inlines.Add(new Run(reading));
        var part = new Border { Child = line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 0), Margin = new Thickness(0, 5, 0, 0) };
        part.SetResourceReference(Border.BorderBrushProperty, "line");
        return part;
    }

    /// <summary>The Armory's certainty line, for a name whose effect is very likely or not known; none when confirmed.</summary>
    internal static TextBlock? CertaintyLine(string certainty)
    {
        string? key = certainty switch
        {
            "approx" or "plain" => "Weapons.Certainty.Likely",
            "unclear" => "Weapons.Certainty.Unknown",
            _ => null,
        };
        if (key is null) return null;
        var line = new TextBlock { Text = Texts.Get(key), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        line.SetResourceReference(TextBlock.ForegroundProperty, certainty == "unclear" ? "caution" : "muted");
        return line;
    }

    // ---- what the value on the line means ----

    /// <summary>What a value means, in plain words: "True", "Refers to X.", "3 seconds."; "" when nothing helpful can be said.
    /// A true / false value is named as the files write it, as the descriptions' "If True:" and "If False:" do.</summary>
    public static string Reading(string name, string value, ValueInfo info)
    {
        value = value.Trim();
        if (value == "") return "";
        if (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase)) return Texts.Get("Editor.Reading.True");
        if (string.Equals(value, "False", StringComparison.OrdinalIgnoreCase)) return Texts.Get("Editor.Reading.False");
        if (value == "nil") return Texts.Get("Editor.Reading.None");
        if (value.StartsWith("GUID:", StringComparison.Ordinal)) return Texts.Get("Editor.Reading.Guid");
        if (IsTypeName(value)) return Texts.Get("Editor.Reading.Object", ("type", value));
        if (value.Length >= 2 && value[0] is '\'' or '"') return QuotedReading(value[1..^1], info);
        if (value.StartsWith("~/", StringComparison.Ordinal) && value.IndexOf('/', 2) < 0) return Texts.Get("Editor.Reading.RefersTo", ("name", value[2..]));
        if (ValueChecker.ChoiceMember(value) is { } member)
        {
            string memberName = ValueMessages.LastPart(member);
            return info.Choices.Count > 0
                ? Texts.Get("Editor.Reading.Choice", ("member", memberName), ("count", info.Choices.Count))
                : Texts.Get("Editor.Reading.Member", ("member", memberName));
        }
        if (double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number))
            return NumberReading(name, number, info);
        return "";
    }

    /// <summary>Whether a value names an object's type, written in the block that follows: T, a capital, then letters or digits.</summary>
    private static bool IsTypeName(string value) =>
        value.Length > 2 && value[0] == 'T' && char.IsAsciiLetterUpper(value[1]) && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>A quoted value: a dictionary key, or a name.</summary>
    private static string QuotedReading(string inside, ValueInfo info) =>
        info.ValueType.Contains("text key", StringComparison.Ordinal)
            ? Texts.Get("Editor.Reading.TextKey", ("key", inside))
            : Texts.Get("Editor.Reading.Name", ("name", inside));

    /// <summary>A number: what -1 or 0 means where the description says, else the number with its unit.</summary>
    private static string NumberReading(string name, double number, ValueInfo info)
    {
        string description = info.Description, rules = string.Join(" ", info.Rules);
        if (number == -1 && MeaningAfter(description, "-1 (", ")") is { } minusOne) return Texts.Get("Editor.Reading.MinusOne", ("meaning", minusOne));
        if (number == 0 && MeaningAfter(description, "0 if ", ".;") is { } zero) return Texts.Get("Editor.Reading.Zero", ("meaning", zero));
        string shown = Shown(number);
        if ((description + " " + rules).Contains("radians", StringComparison.Ordinal))
            return Texts.Get("Editor.Reading.Radians", ("value", shown), ("degrees", Shown(Math.Round(number * 180 / Math.PI, 1))));
        if (name.EndsWith("GRU", StringComparison.Ordinal)) return Texts.Get("Editor.Reading.Gru", ("value", shown));
        if (description.StartsWith("Seconds", StringComparison.Ordinal)) return Texts.Count("Editor.Reading.Seconds", number == 1 ? 1 : 2, ("value", shown));
        return Texts.Get("Editor.Reading.Number", ("value", shown));
    }

    /// <summary>
    /// What a description says a value means, after <paramref name="start"/> up to the first of <paramref name="ends"/>:
    /// "instant" in "…; -1 (instant)"; null when it doesn't say.
    /// </summary>
    private static string? MeaningAfter(string description, string start, string ends)
    {
        int at = description.IndexOf(start, StringComparison.Ordinal);
        if (at < 0) return null;
        // "0 if" must start a word, not end a number such as "10 if"
        if (start[0] == '0' && at > 0 && char.IsAsciiDigit(description[at - 1])) return null;
        int from = at + start.Length;
        int to = description.IndexOfAny(ends.ToCharArray(), from);
        return to > from ? description[from..to] : null;
    }

    /// <summary>A number as the tips show it: thousands grouped, at most three decimals.</summary>
    private static string Shown(double number) => number.ToString("#,0.###", CultureInfo.InvariantCulture);
}
