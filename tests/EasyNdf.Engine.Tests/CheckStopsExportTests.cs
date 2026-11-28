using System.Text.Json.Nodes;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// A Check rule with "Stop the export" ticked: its failure is a warning of its own type, which the app reads to stop the
/// export before any file is written. Without the tick, or when the check passes, nothing changes.
/// </summary>
public class CheckStopsExportTests
{
    private const string Text = "export Unit_A is TUnit\n(\n    Speed = 10\n)\nexport Unit_B is TUnit\n(\n    Speed = 20\n)\n";

    private static RunResult Run(bool stop, string count)
    {
        var engine = new NdfEngine();
        engine.Load(Text);
        var rule = new Rule
        {
            Name = "Two units",
            Kind = "Check",
            LogicGate = "All",
            Conditions = [new Condition { Operand = "Descriptor Title", Operator = "Starts With", Value = "Unit_" }],
            Params = new() { ["Expect"] = "exactly", ["Count"] = count, ["Message"] = "", ["StopExport"] = stop },
        };
        return engine.Run([rule], new RunOptions());
    }

    [Fact]
    public void A_failed_check_set_to_stop_the_export_warns_with_the_stop_type()
    {
        RunWarning warning = Assert.Single(Run(stop: true, count: "3").Warnings);
        Assert.Equal(RuleRunner.StopsExport, warning.Type);
        Assert.Equal("Two units", warning.Rule);
        Assert.Contains("Expected exactly 3, found 2.", warning.Message);
    }

    [Fact]
    public void A_failed_check_without_the_tick_is_an_ordinary_warning()
    {
        RunWarning warning = Assert.Single(Run(stop: false, count: "3").Warnings);
        Assert.Null(warning.Type);
    }

    [Fact]
    public void A_check_that_passes_gives_no_warning_even_when_set_to_stop_the_export()
    {
        Assert.Empty(Run(stop: true, count: "2").Warnings);
    }
}
