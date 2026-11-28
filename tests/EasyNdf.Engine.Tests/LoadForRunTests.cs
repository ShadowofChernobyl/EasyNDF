using EasyNdf.Engine.Ndf;
using EngineCheck;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// Run export reads each file with <see cref="NdfEngine.LoadForRun"/>, which counts what the file holds without listing
/// its names, types, titles and references. Its counts must be those <see cref="NdfEngine.Load"/> gives, and the rules
/// must run on it as they do after Load: checked here on the text of every engine and viewer case.
/// </summary>
public class LoadForRunTests
{
    private static IEnumerable<EngineCase> Cases(string file) =>
        EngineCases.ReadCases(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", file)));

    [Fact]
    public void Counts_are_those_Load_gives_and_the_lists_are_empty()
    {
        int read = 0;
        foreach (EngineCase c in Cases("engine-cases.json").Concat(Cases("viewer-cases.json")))
        {
            LoadSummary full, counted;
            try
            {
                full = new NdfEngine().Load(c.Text);
            }
            catch (NdfParseException loadError)
            {
                var countError = Assert.Throws<NdfParseException>(() => new NdfEngine().LoadForRun(c.Text));
                Assert.Equal(loadError.Message, countError.Message);
                continue;
            }
            counted = new NdfEngine().LoadForRun(c.Text);
            Assert.Equal((full.Declarations, full.Descriptors, full.Constants, full.Unnamed, full.Lines),
                (counted.Declarations, counted.Descriptors, counted.Constants, counted.Unnamed, counted.Lines));
            Assert.Empty(counted.Names);
            Assert.Empty(counted.Types);
            Assert.Empty(counted.Titles);
            Assert.Empty(counted.References);
            read++;
        }
        Assert.True(read > 100);
    }

    [Fact]
    public void A_byte_order_mark_is_counted_as_Load_counts_it()
    {
        const string text = "﻿A is 1\nB is T ( X = 2 )\nunnamed C ( )\n";
        LoadSummary full = new NdfEngine().Load(text), counted = new NdfEngine().LoadForRun(text);
        Assert.Equal((3, 1, 1, 1, 4), (counted.Declarations, counted.Descriptors, counted.Constants, counted.Unnamed, counted.Lines));
        Assert.Equal((full.Declarations, full.Descriptors, full.Constants, full.Unnamed, full.Lines),
            (counted.Declarations, counted.Descriptors, counted.Constants, counted.Unnamed, counted.Lines));
    }

    [Fact]
    public void Rules_run_the_same_after_either()
    {
        foreach (EngineCase c in Cases("engine-cases.json"))
        {
            List<Rules.Rule?> rules = System.Text.Json.JsonSerializer.Deserialize<List<Rules.Rule?>>(c.Rules, Rules.RuleJson.Options) ?? [];
            var options = new Rules.RunOptions { WantText = c.WantText, LogLimit = c.Cap, OtherFiles = c.Aux, KeepSourceComments = c.KeepComments, CheckOutput = c.Check, ModName = c.Mod, DecimalPlaces = c.Dec };
            var loaded = new NdfEngine();
            var counted = new NdfEngine();
            try { loaded.Load(c.Text); }
            catch (NdfParseException) { continue; }
            counted.LoadForRun(c.Text);
            Assert.Equal(loaded.Run(rules, options).Text, counted.Run(System.Text.Json.JsonSerializer.Deserialize<List<Rules.Rule?>>(c.Rules, Rules.RuleJson.Options) ?? [], options).Text);
        }
    }
}
