using System.Text.RegularExpressions;
using EasyNdf.Data.Languages;
using Xunit.Abstractions;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Checks the app's texts against its source: every name the app uses has English, every English text is used, and no
/// screen has English typed into it instead of a name. The app's code is read as text, so these run without Windows.
/// </summary>
public partial class TextNameTests(ITestOutputHelper output)
{
    private static readonly string AppFolder = Path.Combine(RepositoryFolder(), "src", "EasyNdf.App");

    /// <summary>The Data library, which has English texts of its own (the file list's Status column, for one).</summary>
    private static readonly string DataFolder = Path.Combine(RepositoryFolder(), "src", "EasyNdf.Data");

    /// <summary>Screen texts that are deliberately not translated: the app's name, and the font window's samples.</summary>
    private static readonly HashSet<string> NotTranslated = ["EasyNDF", "AaBbYy 0123"];

    [Fact]
    public void Every_name_used_has_english()
    {
        TextCatalog english = EnglishTexts();
        string[] missing = [.. NamesUsed().Where(name => !english.English.ContainsKey(name)).Order(StringComparer.Ordinal)];
        Assert.True(missing.Length == 0, "Names with no English in Languages\\en: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_english_text_is_used()
    {
        HashSet<string> used = NamesUsed();
        string[] unused = [.. EnglishTexts().English.Keys.Where(name => !used.Contains(name)).Order(StringComparer.Ordinal)];
        Assert.True(unused.Length == 0, "English texts nothing uses: " + string.Join(", ", unused));
    }

    [Fact]
    public void Screens_have_no_typed_in_english()
    {
        var typedIn = new List<string>();
        foreach (string file in AppFiles("*.xaml"))
        {
            string text = Comments().Replace(File.ReadAllText(file), "");
            // every attribute of every element, since one element can carry several texts (Content and ToolTip...)
            foreach (Match element in Element().Matches(text))
                foreach (Match attribute in TextAttribute().Matches(element.Groups["attributes"].Value))
                {
                    string value = System.Net.WebUtility.HtmlDecode(attribute.Groups["value"].Value);
                    // a Tag is a text only on a text box, where it is the grey hint
                    bool isText = attribute.Groups["name"].Value != "Tag" || element.Groups["element"].Value == "TextBox";
                    if (isText && IsTypedInText(value)) typedIn.Add(Path.GetFileName(file) + ": " + value);
                }
            foreach (Match content in ElementText().Matches(text))
                if (IsTypedInText(content.Groups["text"].Value.Trim())) typedIn.Add(Path.GetFileName(file) + ": " + content.Groups["text"].Value.Trim());
        }
        Assert.True(typedIn.Count == 0, "Use {t:Text Name} instead of:\n" + string.Join("\n", typedIn));
    }

    /// <summary>
    /// Every language built into the app keeps what its English has to keep: each {blank}, the **bold** and `code`
    /// markers, and, for a text that depends on a number, as many plural forms as the language uses.
    /// </summary>
    [Fact]
    public void Built_in_languages_keep_blanks_markers_and_plural_forms()
    {
        // the Help wiki's pages are texts of the packs too, named as Help/WikiPages.cs names them
        var english = new Dictionary<string, string>(EnglishTexts().English, StringComparer.Ordinal);
        foreach ((string name, string text) in WikiEnglish()) english[name] = text;
        var broken = new List<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(AppFolder, "Languages"), "*.json"))
        {
            LanguagePack pack = LanguageStore.ReadPack(File.ReadAllText(file));
            foreach ((string name, PackText text) in pack.Texts)
            {
                if (!english.TryGetValue(name, out string? englishText)) { broken.Add($"{pack.Code} {name}: no such text in English"); continue; }
                if (text.English != englishText) continue; // translated from older English: shown, but counted as "to check"
                string[] Blanks(string value) => [.. Blank().Matches(value).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal)];
                if (!Blanks(englishText).SequenceEqual(Blanks(text.Text))) broken.Add($"{pack.Code} {name}: blanks differ");
                bool plural = englishText.Contains('|') && englishText.Contains("{count}", StringComparison.Ordinal);
                // a text with plural forms is compared form by form: a language may have more forms than English, or fewer
                string[] englishForms = plural ? englishText.Split('|') : [englishText], forms = plural ? text.Text.Split('|') : [text.Text];
                if (forms.Any(form => Count(form, "**") != Count(englishForms[0], "**") || Count(form, "`") != Count(englishForms[0], "`")))
                    broken.Add($"{pack.Code} {name}: ** or ` markers differ");
                if (plural && text.Text.Split('|').Length < PluralFormsOf(pack.Code))
                    broken.Add($"{pack.Code} {name}: fewer plural forms than the language uses");
            }
        }
        Assert.True(broken.Count == 0, string.Join("\n", broken));
    }

    /// <summary>
    /// No built-in language puts a whole text in quotation marks its English does not have: a translation tool does
    /// that to short texts (a Cancel button read «Отмена»).
    /// </summary>
    [Fact]
    public void Built_in_languages_do_not_wrap_a_whole_text_in_quotation_marks()
    {
        (char Open, char Close)[] marks = [('«', '»'), ('„', '“'), ('“', '”'), ('"', '"')];
        var wrapped = new List<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(AppFolder, "Languages"), "*.json"))
        {
            LanguagePack pack = LanguageStore.ReadPack(File.ReadAllText(file));
            foreach ((string name, PackText text) in pack.Texts)
            {
                string translated = text.Text.TrimEnd('.', '。');
                if (translated.Length < 2 || text.English.IndexOfAny(['"', '“', '”']) >= 0) continue;
                foreach ((char open, char close) in marks)
                {
                    if (translated[0] != open || translated[^1] != close) continue;
                    string inside = translated[1..^1];
                    // one pair around everything: quotes inside (a name quoted at each end of a sentence) are another matter
                    if (inside.IndexOf(open) < 0 && inside.IndexOf(close) < 0) wrapped.Add($"{pack.Code} {name}: {text.Text}");
                }
            }
        }
        Assert.True(wrapped.Count == 0, string.Join("\n", wrapped));
    }

    private static int Count(string text, string piece) => text.Split(piece).Length - 1;

    /// <summary>The English of the Help wiki's pages, the About tab's page and the change log, by the names Help/WikiPages.cs gives them.</summary>
    private static Dictionary<string, string> WikiEnglish()
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var wiki = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppFolder, "Help", "wiki.json")))!;
        foreach (var page in wiki["pages"]!.AsArray())
        {
            string id = (string)page!["id"]!, group = (string)page["group"]!;
            texts["Help.Group." + string.Concat(group.Where(char.IsAsciiLetterOrDigit))] = group;
            texts["Help.Page." + id + ".Title"] = (string)page["title"]!;
            texts["Help.Page." + id + ".Html"] = (string)page["html"]!;
        }
        texts["About.Page.Html"] = File.ReadAllText(Path.Combine(AppFolder, "Help", "about.html"));
        // the change log, one text for its opening and one for each version (as Help/WikiPages.cs cuts it)
        string[] logPieces = File.ReadAllText(Path.Combine(AppFolder, "Help", "changelog.html")).Split("<h2>");
        texts["Help.ChangeLog.Intro.Html"] = logPieces[0];
        foreach (string piece in logPieces.Skip(1))
            texts["Help.ChangeLog." + piece[..piece.IndexOf("</h2>", StringComparison.Ordinal)].Trim() + ".Html"] = "<h2>" + piece;
        return texts;
    }

    /// <summary>How many plural forms a language uses: the highest form <see cref="PluralForms"/> gives, plus one.</summary>
    private static int PluralFormsOf(string code) => Enumerable.Range(0, 200).Max(n => PluralForms.FormOf(code, n)) + 1;

    [GeneratedRegex(@"\{[A-Za-z]+\}")]
    private static partial Regex Blank();

    /// <summary>
    /// Not a check yet: lists, per file, the English still typed into the app's code, which is being moved into the
    /// language files part by part. Run with "dotnet test --logger console;verbosity=detailed" to see it.
    /// </summary>
    [Fact]
    public void Report_english_still_typed_into_code()
    {
        int total = 0;
        foreach (string file in AppFiles("*.cs"))
        {
            string code = LineComments().Replace(File.ReadAllText(file), "");
            int count = StringLiteral().Matches(code).Count(literal => LooksLikeSentence(literal.Groups[1].Value));
            if (count == 0) continue;
            total += count;
            output.WriteLine($"{count,4}  {Path.GetRelativePath(AppFolder, file)}");
        }
        output.WriteLine($"{total,4}  in all");
    }

    /// <summary>The built-in English texts, the Data library's and the app's, read as the app reads them.</summary>
    private static TextCatalog EnglishTexts()
    {
        var catalog = new TextCatalog();
        foreach (string folder in new[] { DataFolder, AppFolder })
            foreach (string file in Directory.GetFiles(Path.Combine(folder, "Languages", "en"), "*.json").Order(StringComparer.Ordinal))
                catalog.AddEnglish(File.ReadAllText(file), Path.GetFileName(file));
        return catalog;
    }

    /// <summary>Every name the app's screens and code ask for.</summary>
    private static HashSet<string> NamesUsed()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        // the styles are left out of the typed-in check, as their only words are font names, but a style can ask for a name
        // (the help buttons' tooltip)
        string[] styles = Directory.GetFiles(Path.Combine(AppFolder, "Styles"), "*.xaml");
        foreach (string file in AppFiles("*.xaml").Concat(styles))
            foreach (Match use in ScreenName().Matches(File.ReadAllText(file))) names.Add(use.Groups[1].Value);
        foreach (string file in AppFiles("*.cs").Concat(Directory.GetFiles(DataFolder, "*.cs", SearchOption.AllDirectories)))
        {
            // the Help wiki's names are made from its pages, whose English is the wiki itself (Help/wiki.json), not Languages/en
            if (Path.GetFileName(file) == "WikiPages.cs") continue;
            foreach (Match use in CodeName().Matches(LineComments().Replace(File.ReadAllText(file), ""))) names.Add(use.Groups[1].Value);
        }
        return names;
    }

    /// <summary>
    /// The app's own files of a kind, without what the build makes, the workbench's tools, and the styles (whose only
    /// words are font names).
    /// </summary>
    private static IEnumerable<string> AppFiles(string pattern) =>
        Directory.GetFiles(AppFolder, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}Workbench{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}Styles{Path.DirectorySeparatorChar}"));

    /// <summary>Text with two letters in a row is meant for people; a binding, a symbol or a number is not.</summary>
    private static bool IsTypedInText(string value) =>
        value != "" && !value.StartsWith('{') && TwoLetters().IsMatch(value) && !AllCapitals().IsMatch(value)
        && !NotTranslated.Contains(value) && !value.Contains("MaximumRangeGRU", StringComparison.Ordinal);

    /// <summary>A string in code that reads like words for people, roughly: two words, or one capitalised word.</summary>
    private static bool LooksLikeSentence(string value) =>
        SentenceLike().IsMatch(value) && !value.Contains('{') && !value.EndsWith(".ndf", StringComparison.Ordinal) && !value.EndsWith(".json", StringComparison.Ordinal);

    internal static string RepositoryFolder()
    {
        string folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "EasyNDF.slnx"))) folder = Path.GetDirectoryName(folder)!;
        return folder;
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComments();

    [GeneratedRegex(@"<(?<element>[A-Za-z][A-Za-z0-9:.]*)\b(?<attributes>[^>]*)>", RegexOptions.Singleline)]
    private static partial Regex Element();

    /// <summary>An attribute that holds a text: its name is one of these, or ends in Title (SecondTitle, FoldedTitle...).</summary>
    [GeneratedRegex(@"\s(?<name>Content|Text|ToolTip|Label|Note|Header|Tag|[A-Za-z]*Title)=""(?<value>[^""]*)""")]
    private static partial Regex TextAttribute();

    [GeneratedRegex(@">(?<text>[^<>]*[A-Za-z]{2}[^<>]*)<")]
    private static partial Regex ElementText();

    [GeneratedRegex(@"\{t:Text ([A-Za-z0-9.]+)\}")]
    private static partial Regex ScreenName();

    /// <summary>A text's name written in code: a part of the app, a dot, and the rest, such as "Files.AddFolder".</summary>
    // a file's name such as "Divisions.ndf" is not a text's
    [GeneratedRegex(@"""((?:Main|Files|Rules|Editor|Units|Divisions|Weapons|Armory|Settings|Help|About|Welcome|Common|Tour)\.(?!ndf"")[A-Za-z0-9.]+)""")]
    private static partial Regex CodeName();

    [GeneratedRegex(@"(?<![A-Za-z_@$])""((?:[^""\\\n]|\\.)*)""")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"[a-z]{2,} [a-z]{2,}|^[A-Z][a-z]+(\.\.\.)?\z|^[A-Z][a-z]+ [a-z]")]
    private static partial Regex SentenceLike();

    [GeneratedRegex("[A-Za-z]{2}")]
    private static partial Regex TwoLetters();

    [GeneratedRegex(@"^[A-Z]{2,}\z")]
    private static partial Regex AllCapitals();
}
