using System.Diagnostics;
using EasyNdf.Engine.Editing;
using Xunit.Abstractions;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The value check over the game's own files, which mod generation takes, so nothing in them may be marked. Runs only
/// where the game's files and the editor's guide data are (set EASYNDF_GAMEDATA to a GameData folder and EASYNDF_GUIDE
/// to the guide's JSON); elsewhere it passes without checking.
/// </summary>
public class ValueCheckerRealDataTests(ITestOutputHelper output)
{
    [Fact]
    public void The_game_files_have_no_marked_values()
    {
        string? gameData = Environment.GetEnvironmentVariable("EASYNDF_GAMEDATA"), guidePath = Environment.GetEnvironmentVariable("EASYNDF_GUIDE");
        if (gameData is null || guidePath is null || !Directory.Exists(gameData) || !File.Exists(guidePath)) return;
        ValueGuide guide;
        using (FileStream json = File.OpenRead(guidePath)) guide = ValueGuide.Read(json);

        var marked = new List<(string Kind, string Place)>();
        var slowest = (Time: 0L, File: "");
        int files = 0;
        // WARNO v210942's checks of arrays hold from that version on: an older version's files (no TacticalUnits.ndf)
        // may have what it still allowed, such as one empty pack in its DivisionRules.ndf
        bool before210942 = !Directory.EnumerateFiles(gameData, "TacticalUnits.ndf", SearchOption.AllDirectories).Any();
        // every name the files declare, for the bare words in numbers: none of the game's may be marked
        var declared = new HashSet<string>(StringComparer.Ordinal);
        // and the bare words the app knows of without a declaration (src/EasyNdf.App/Assets/KnownWords.txt)
        string known = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "EasyNdf.App", "Assets", "KnownWords.txt");
        if (File.Exists(known)) declared.UnionWith(File.ReadAllLines(known).Select(line => line.Trim()).Where(line => line != ""));
        foreach (string path in Directory.EnumerateFiles(gameData, "*.ndf", SearchOption.AllDirectories))
            foreach (System.Text.RegularExpressions.Match name in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), @"(?m)^[ \t]*(?:export[ \t]+|private[ \t]+|public[ \t]+)?(\w+)[ \t]+is\b"))
                declared.Add(name.Groups[1].Value);
        foreach (string path in Directory.EnumerateFiles(gameData, "*.ndf", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            var clock = Stopwatch.StartNew();
            List<ValueProblem> problems = ValueChecker.Check(text, guide, isDeclared: declared.Contains);
            if (before210942) problems.RemoveAll(problem => problem.Kind is ValueProblemKind.NoAmmoBox or ValueProblemKind.EmptyAmmoBox or ValueProblemKind.EmptyPack);
            if (clock.ElapsedMilliseconds > slowest.Time) slowest = (clock.ElapsedMilliseconds, Path.GetFileName(path));
            files++;
            foreach (ValueProblem problem in problems)
            {
                string kind = problem.Comparison is { } comparison
                    ? $"{problem.Kind} {problem.Name} {comparison.Operator} {comparison.Other}"
                    : $"{problem.Kind} {problem.Name}";
                marked.Add((kind, $"{Path.GetRelativePath(gameData, path)}:{text[..problem.Position].Count(c => c == '\n') + 1} {problem.Value} vs {problem.OtherValue}"));
            }
        }
        if (Environment.GetEnvironmentVariable("EASYNDF_WORDS_OUT") is { } wordsOut) File.WriteAllLines(wordsOut, marked.Where(mark => mark.Kind.StartsWith("UnknownName")).Select(mark => mark.Place.Split(' ')[1]).Distinct().Order(StringComparer.Ordinal));
        output.WriteLine($"{files} files; slowest {slowest.File} in {slowest.Time} ms");
        // each kind of mark once, with how many and a few places
        foreach (IGrouping<string, (string Kind, string Place)> kind in marked.GroupBy(mark => mark.Kind))
            output.WriteLine($"{kind.Count(),5}  {kind.Key}: {string.Join("; ", kind.Take(3).Select(mark => mark.Place))}");
        Assert.Empty(marked);
    }
}
