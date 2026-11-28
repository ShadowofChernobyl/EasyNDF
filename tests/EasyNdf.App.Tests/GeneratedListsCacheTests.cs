using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;
using EasyNdf.App.Files;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Tests;

/// <summary>
/// The generated divisions' cache (<c>GeneratedListsMaker</c>): results read back from the cache folder are the same as
/// those made, the file read is left as it was but marked as the newest (so it is the last let go of), and no more than
/// eight sets are kept. The maker is internal, so it is reached by reflection.
/// </summary>
public sealed class GeneratedListsCacheTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "EasyNdf.App.Tests", Guid.NewGuid().ToString("N"));

    private static readonly Type Maker = typeof(FileRunner).Assembly.GetType("EasyNdf.App.Files.GeneratedListsMaker")!;
    private static readonly Type InputType = Maker.GetNestedType("Input", BindingFlags.NonPublic)!;
    private static readonly Type ModRulesType = typeof(FileRunner).GetNestedType("ModRules", BindingFlags.NonPublic)!;

    public GeneratedListsCacheTests()
    {
        Directory.CreateDirectory(folder);
        ForgetResultsInMemory();
    }

    public void Dispose()
    {
        ForgetResultsInMemory();
        Directory.Delete(folder, recursive: true);
    }

    private string CacheFolder => Path.Combine(folder, "Cache");

    private string ResultsFolder => Path.Combine(CacheFolder, "Generated divisions");

    /// <summary>The results the maker keeps in memory are let go of, so the next call reads the cache folder.</summary>
    private static void ForgetResultsInMemory()
    {
        var recent = (System.Collections.IList)Maker.GetField("recent", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        recent.Clear();
        Maker.GetField("lastRosters", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
    }

    /// <summary>The inputs of a units file of one made-up unit, with no rules run on it.</summary>
    private object Inputs(string unitName)
    {
        string path = Path.Combine(folder, "UniteDescriptor.ndf");
        File.WriteAllText(path, "export Descriptor_Unit_" + unitName + " is TEntityDescriptor\n(\n    ClassNameForDebug = 'Unit_" + unitName + "'\n)\n");
        var entry = new FileEntry { Name = "UniteDescriptor.ndf", Folder = "", Source = path };
        object stack = Activator.CreateInstance(typeof(List<>).MakeGenericType(ModRulesType))!;
        object input = Activator.CreateInstance(InputType, entry, stack, new Dictionary<string, string>())!;
        var inputs = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(InputType))!;
        inputs.Add(input);
        return inputs;
    }

    /// <summary>The results for these inputs, read with each file's own text (no rules), using the test's cache folder.</summary>
    private object ResultsFor(object inputs)
    {
        ParameterExpression input = Expression.Parameter(InputType);
        Delegate textAfterRules = Expression.Lambda(typeof(Func<,>).MakeGenericType(InputType, typeof(string)),
            Expression.Call(typeof(FileContent).GetMethod(nameof(FileContent.Text), [typeof(FileEntry)])!, Expression.Property(input, "Entry")), input).Compile();
        MethodInfo method = Maker.GetMethod("ResultsFor", BindingFlags.NonPublic | BindingFlags.Static)!;
        return method.Invoke(null, [new JsonArray(), inputs, textAfterRules, CacheFolder])!;
    }

    /// <summary>The units of a set of results, as "descriptor=name" texts in order.</summary>
    private static List<string> UnitsOf(object results)
    {
        var units = (System.Collections.IDictionary)results.GetType().GetProperty("Units")!.GetValue(results)!;
        return [.. units.Keys.Cast<string>().Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void Results_read_from_the_cache_leave_its_file_as_it_was_but_newest()
    {
        object inputs = Inputs("Test");
        object made = ResultsFor(inputs);
        string file = Assert.Single(Directory.GetFiles(ResultsFolder, "*.json"));
        byte[] written = File.ReadAllBytes(file);
        DateTime longAgo = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, longAgo);

        ForgetResultsInMemory();
        object read = ResultsFor(inputs);

        Assert.Equal(UnitsOf(made), UnitsOf(read));
        Assert.Equal(written, File.ReadAllBytes(file));
        Assert.True(File.GetLastWriteTimeUtc(file) > longAgo.AddYears(1));
    }

    [Fact]
    public void Results_are_kept_in_the_cache_as_they_always_were_written()
    {
        var lists = new Dictionary<string, IReadOnlyList<EasyNdf.Data.Divisions.DivisionUnit>>(StringComparer.Ordinal)
        {
            ["Descriptor_Deck_A"] =
            [
                new() { Unit = "Descriptor_Unit_Tank", Cards = 2, UnitsPerCard = 3, VeterancyMultipliers = [1, 0.5], Transports = ["Descriptor_Unit_Truck"] },
                new() { Unit = "Descriptor_Unit_\"Quoted\"_é", Cards = 1, UnitsPerCardByPhase = [4, 5], AvailableWithoutTransport = false },
            ],
            ["Descriptor_Deck_B"] = [],
        };
        var battlegroups = new Dictionary<string, IReadOnlyList<EasyNdf.Data.Divisions.BattlegroupCard>>(StringComparer.Ordinal)
        {
            ["Descriptor_Deck_A"] = [new("Descriptor_Unit_Tank", 2, "Descriptor_Unit_Truck", 3), new("Descriptor_Unit_Inf", 0, null)],
        };
        Type resultsType = Maker.GetNestedType("Results", BindingFlags.NonPublic)!;
        Type knownUnitType = Maker.GetNestedType("KnownUnit", BindingFlags.NonPublic)!;
        var units = (System.Collections.IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), knownUnitType), StringComparer.Ordinal)!;
        units["Descriptor_Unit_Tank"] = Activator.CreateInstance(knownUnitType, "Tank", (int?)12)!;
        units["Descriptor_Unit_Inf"] = Activator.CreateInstance(knownUnitType, "", (int?)null)!;
        object results = Activator.CreateInstance(resultsType, lists, battlegroups, new HashSet<string> { "Pack_B", "Pack_A" }, units)!;

        Maker.GetMethod("WriteCached", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [CacheFolder, "key", results]);

        // the file as the maker has always written it: each list and battlegroup as a mod file's division entry keeps them
        var expected = new JsonObject
        {
            ["Lists"] = new JsonObject(lists.Select(list => KeyValuePair.Create(list.Key,
                new EasyNdf.Data.Divisions.ModDivision { Added = [.. list.Value] }.ToJson()["Added"]?.DeepClone()))),
            ["Battlegroups"] = new JsonObject(battlegroups.Select(cards => KeyValuePair.Create(cards.Key,
                new EasyNdf.Data.Divisions.ModDivision { BattlegroupCards = [.. cards.Value] }.ToJson()["Battlegroup"]?["Cards"]?.DeepClone()))),
            ["PacksInFile"] = new JsonArray("Pack_A", "Pack_B"),
            ["Units"] = new JsonObject
            {
                ["Descriptor_Unit_Tank"] = new JsonArray("Tank", 12),
                ["Descriptor_Unit_Inf"] = new JsonArray("", null),
            },
        };
        Assert.Equal(expected.ToJsonString(), File.ReadAllText(Path.Combine(ResultsFolder, "key.json")));

        // and they read back as they were
        object read = Maker.GetMethod("ReadCached", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [CacheFolder, "key"])!;
        var readLists = (Dictionary<string, IReadOnlyList<EasyNdf.Data.Divisions.DivisionUnit>>)resultsType.GetProperty("Lists")!.GetValue(read)!;
        Assert.Equal(lists.Keys, readLists.Keys);
        Assert.Equal(lists["Descriptor_Deck_A"].Select(unit => unit.Unit + unit.Cards + unit.UnitsPerCard + string.Join(",", unit.Transports)),
            readLists["Descriptor_Deck_A"].Select(unit => unit.Unit + unit.Cards + unit.UnitsPerCard + string.Join(",", unit.Transports)));
    }

    [Fact]
    public void Reading_from_the_cache_lets_go_of_the_oldest_past_eight()
    {
        object inputs = Inputs("Test");
        ResultsFor(inputs);
        string file = Assert.Single(Directory.GetFiles(ResultsFolder, "*.json"));
        // nine older sets of results from earlier exports
        for (int k = 0; k < 9; k++)
        {
            string old = Path.Combine(ResultsFolder, "old" + k + ".json");
            File.WriteAllText(old, "{}");
            File.SetLastWriteTimeUtc(old, new DateTime(2020, 1, 1 + k, 0, 0, 0, DateTimeKind.Utc));
        }
        File.SetLastWriteTimeUtc(file, new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        ForgetResultsInMemory();
        ResultsFor(inputs);

        List<string> kept = [.. Directory.GetFiles(ResultsFolder, "*.json").Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];
        Assert.Equal(8, kept.Count);
        Assert.Contains(Path.GetFileName(file), kept);
        Assert.DoesNotContain("old0.json", kept);
        Assert.DoesNotContain("old1.json", kept);
    }
}
