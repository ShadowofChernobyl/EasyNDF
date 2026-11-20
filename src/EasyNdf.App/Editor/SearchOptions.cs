using System.Text.RegularExpressions;

namespace EasyNdf.App.Editor;

/// <summary>
/// The File Editor's search options, shared by Find, Replace and Search all files: Match Case, Whole Word and Regular
/// Expression, kept between runs in the app's state. (In Selection belongs to Find alone; see EditorPage.Find.)
/// </summary>
public static class SearchOptions
{
    /// <summary>Only text in the same capitals.</summary>
    public static bool MatchCase { get => Read("findMatchCase"); set => Write("findMatchCase", value); }

    /// <summary>Not inside a longer name: letters, digits and underscores may not touch the match.</summary>
    public static bool WholeWord { get => Read("findWholeWord"); set => Write("findWholeWord", value); }

    /// <summary>What is typed is a regular expression rather than plain text.</summary>
    public static bool UseRegex { get => Read("findRegex"); set => Write("findRegex", value); }

    /// <summary>Raised when an option changes, so every box showing the options shows the change.</summary>
    public static event Action? Changed;

    private static bool Read(string key) => AppData.Current.State[key]?.GetValue<bool>() == true;

    private static void Write(string key, bool value)
    {
        AppData.Current.State[key] = value;
        AppData.Current.SaveState();
        Changed?.Invoke();
    }

    /// <summary>
    /// What is typed, as the pattern the options make; null with the reason when a regular expression cannot be read.
    /// </summary>
    public static Regex? Pattern(string wanted, out string? problem)
    {
        problem = null;
        string source = UseRegex ? wanted : Regex.Escape(wanted);
        // a word's edge: no letter, digit or underscore just before or after
        if (WholeWord) source = @"(?<![\w])(?:" + source + @")(?![\w])";
        var options = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (!MatchCase) options |= RegexOptions.IgnoreCase;
        try
        {
            return new Regex(source, options, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException error)
        {
            problem = error.Message;
            return null;
        }
    }

    /// <summary>The matches of a pattern between two places, leaving out empty ones (such as ^ alone).</summary>
    public static IEnumerable<Match> Matches(Regex pattern, string text, int start, int end)
    {
        for (Match match = pattern.Match(text, start, end - start); match.Success; match = match.NextMatch())
            if (match.Length > 0) yield return match;
    }
}
