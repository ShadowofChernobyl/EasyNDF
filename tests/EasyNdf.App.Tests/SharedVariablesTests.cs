using System.Text.Json.Nodes;
using EasyNdf.App.Files;
using EasyNdf.Data;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Rules;
using Xunit;

namespace EasyNdf.App.Tests;

/// <summary>
/// Variables shared between rule sets: which clash, and what each rule set starts with on each file when an action
/// changes one, in the order the rule sets run.
/// </summary>
public class SharedVariablesTests
{
    private const string FileA = "export Unit_A is TUnit\n(\n    Speed = 10\n)\nexport Unit_B is TUnit\n(\n    Speed = 20\n)\n";
    private const string FileB = "export Tank_C is TUnit\n(\n    Speed = 30\n)\n";

    private static JsonObject Variable(string name, string value, string scope) => new()
    {
        ["Name"] = name, ["Kind"] = "Variable", ["Enabled"] = true,
        ["Params"] = new JsonObject { ["VarName"] = name, ["Value"] = value, ["VarScope"] = scope },
    };

    private static JsonObject Change(string variable, string op, string value, bool perMatch = false, string? titleContains = null) => new()
    {
        ["Name"] = "change " + variable, ["Kind"] = "Edit", ["Enabled"] = true, ["LogicGate"] = "Any",
        ["Conditions"] = titleContains is null ? [] : new JsonArray(new JsonObject { ["Operand"] = "Descriptor Title", ["Operator"] = "Contains", ["Value"] = titleContains }),
        ["Actions"] = new JsonArray(new JsonObject { ["Target"] = "{" + variable + "}", ["Operator"] = op, ["Value"] = value, ["PerMatch"] = perMatch }),
    };

    private static ModPreset RuleSet(string name, params JsonObject[] rules) => new() { Name = name, Rules = new JsonArray([.. rules]) };

    private static Mod ModOf(string name, params ModPreset[] ruleSets) => new() { Name = name, Presets = [.. ruleSets] };

    // each rule set runs on the file its name ends with: "... A" on file A, "... B" on file B, "... AB" on both
    private static readonly SharedVariables.SharedFile A = new("A", () => FileA), B = new("B", () => FileB);

    private static SharedVariables Build(params Mod[] mods) => SharedVariables.Build(mods, [A, B],
        (file, mod) => [.. mod.Presets.Where(ruleSet => ruleSet.Name.Split(' ')[^1].Contains((string)file.Key))],
        (mod, ruleSet, file, text, shared) =>
        {
            var engine = new NdfEngine();
            engine.Load(text);
            return engine.Run(RuleLoading.LoadForEngine(ruleSet.Rules.DeepClone()), new RunOptions { Segments = [new RuleSegment(0, shared)] });
        });

    [Fact]
    public void TwoModsWithAnAllModsVariableOfOneNameClash()
    {
        Mod one = ModOf("One", RuleSet("x A", Variable("Bonus", "1", "AllMods")));
        Mod two = ModOf("Two", RuleSet("y A", Variable("Bonus", "2", "AllMods")));
        SharedVariables.Clash clash = Assert.Single(Build(one, two).Clashes);
        Assert.Equal("One", clash.First.Mod.Name);
        Assert.Equal("Two", clash.Second.Mod.Name);
    }

    [Fact]
    public void AModsVariablesAreApartFromAnotherMods()
    {
        Mod one = ModOf("One", RuleSet("x A", Variable("Bonus", "1", "Mod")));
        Mod two = ModOf("Two", RuleSet("y A", Variable("Bonus", "2", "Mod")), RuleSet("z B", Variable("Local", "3", "RuleSet")));
        SharedVariables shared = Build(one, two);
        Assert.Empty(shared.Clashes);
        Assert.Equal("1", shared.At(one, one.Presets[0], "A")["Bonus"]);
        Assert.Equal("2", shared.At(two, two.Presets[0], "A")["Bonus"]);
        // a rule set's own variable is not shared
        Assert.False(shared.At(two, two.Presets[1], "B").ContainsKey("Local"));
    }

    [Fact]
    public void ARuleSetsVariableClashesWithItsModsOfTheSameName()
    {
        Mod mod = ModOf("One", RuleSet("x A", Variable("Bonus", "1", "Mod")), RuleSet("y B", Variable("Bonus", "2", "RuleSet")));
        Assert.Single(Build(mod).Clashes);
    }

    [Fact]
    public void ANameIsTakenWhereBothCouldBeSeen()
    {
        Mod one = ModOf("One", RuleSet("x A", Variable("Bonus", "1", "AllMods")), RuleSet("y B"));
        Mod two = ModOf("Two", RuleSet("z A"));
        Assert.NotNull(SharedVariables.TakenBy("Bonus", "RuleSet", two, two.Presets[0], null, [one, two]));
        Assert.Null(SharedVariables.TakenBy("Other", "RuleSet", two, two.Presets[0], null, [one, two]));
        // the rule that has the name does not clash with itself
        var itself = (JsonObject)one.Presets[0].Rules[0]!;
        Assert.Null(SharedVariables.TakenBy("Bonus", "AllMods", one, one.Presets[0], itself, [one, two]));
    }

    [Fact]
    public void ARuleSetBelowTheOneThatChangesAVariableStartsWithTheNewValue()
    {
        Mod mod = ModOf("One",
            RuleSet("reads first B"),
            RuleSet("changes A", Variable("Bonus", "5", "Mod"), Change("Bonus", "Add [int, float]", "2")),
            RuleSet("reads after B"));
        SharedVariables shared = Build(mod);
        Assert.Equal("5", shared.At(mod, mod.Presets[0], "B")["Bonus"]);
        Assert.Equal("5", shared.At(mod, mod.Presets[1], "A")["Bonus"]);
        Assert.Equal("7", shared.At(mod, mod.Presets[2], "B")["Bonus"]);
    }

    [Fact]
    public void ALaterModSeesWhatAnEarlierModDidToAnAllModsVariable()
    {
        Mod one = ModOf("One", RuleSet("changes A", Variable("Bonus", "5", "AllMods"), Change("Bonus", "Multiply [int, float]", "3")));
        Mod two = ModOf("Two", RuleSet("reads B"));
        Assert.Equal("15", Build(one, two).At(two, two.Presets[0], "B")["Bonus"]);
    }

    [Fact]
    public void ForEachMatchCountsOnEachFileInTheFilesOrder()
    {
        // file A has two units, file B none of that name: after A the count is 2, and B starts with it
        Mod mod = ModOf("One", RuleSet("counts AB", Variable("Count", "0", "Mod"), Change("Count", "Add [int, float]", "1", perMatch: true, titleContains: "Unit_")),
            RuleSet("reads B"));
        SharedVariables shared = Build(mod);
        Assert.Equal("0", shared.At(mod, mod.Presets[0], "A")["Count"]);
        Assert.Equal("2", shared.At(mod, mod.Presets[0], "B")["Count"]);
        Assert.Equal("2", shared.At(mod, mod.Presets[1], "B")["Count"]);
    }

    [Fact]
    public void WithNothingAimedAtASharedVariableEveryRuleSetStartsWithItsFirstValue()
    {
        Mod mod = ModOf("One", RuleSet("x A", Variable("Bonus", "5", "Mod")), RuleSet("y B"));
        Assert.Equal("5", Build(mod).At(mod, mod.Presets[1], "B")["Bonus"]);
    }
}
