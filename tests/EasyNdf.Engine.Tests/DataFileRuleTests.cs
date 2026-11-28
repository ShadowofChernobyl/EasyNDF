using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// "Read from a file" on Table and Variable rules: the rule reads its table, or its variables (a name and a value a
/// row), from a data file at every run, instead of its own text; a file that cannot be read warns and gives nothing.
/// </summary>
public class DataFileRuleTests
{
    private const string Text = "export Unit_A is TUnit\n(\n    Speed = 10\n    Fuel = 100\n)\nexport Unit_B is TUnit\n(\n    Speed = 20\n    Fuel = 200\n)\n";

    private static RunResult Run(List<Rule?> rules, Dictionary<string, string> files)
    {
        var engine = new NdfEngine();
        engine.Load(Text);
        return engine.Run(rules, new RunOptions { WantText = true, ReadDataFile = path => files.GetValueOrDefault(path) });
    }

    private static Rule Table(string dataFile, string ownText = "") => new()
    {
        Name = "table", Kind = "Table", LogicGate = "All",
        Params = new() { ["Op"] = "Set To", ["Text"] = ownText, ["DataFile"] = dataFile },
    };

    [Fact]
    public void A_table_is_read_from_its_file_instead_of_its_own_text()
    {
        RunResult result = Run([Table("speeds.csv", "Name,Speed\nUnit_A,1")], new() { ["speeds.csv"] = "Name,Speed\nUnit_A,70\nUnit_B,80" });
        Assert.Contains("Speed = 70", result.Text);
        Assert.Contains("Speed = 80", result.Text);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_table_without_a_file_uses_its_own_text_as_before() =>
        Assert.Contains("Speed = 1\n", Run([Table("", "Name,Speed\nUnit_A,1")], []).Text);

    [Fact]
    public void A_file_that_cannot_be_read_warns_and_changes_nothing()
    {
        RunResult result = Run([Table("missing.csv", "Name,Speed\nUnit_A,1")], []);
        Assert.Equal(Text, result.Text);
        Assert.Contains(result.Warnings, warning => warning.Message.Contains("missing.csv", StringComparison.Ordinal));
    }

    [Fact]
    public void A_variable_rule_reading_a_file_sets_one_variable_a_row()
    {
        var variables = new Rule
        {
            Name = "values", Kind = "Variable",
            Params = new() { ["VarName"] = "", ["Value"] = "", ["DataFile"] = "values.csv" },
        };
        var edit = new Rule
        {
            Name = "edit", Kind = "Edit", LogicGate = "All",
            Conditions = [new Condition { Operand = "Descriptor Title", Operator = "Is Equal To", Value = "Unit_A" }],
            Actions = [new RuleAction { Target = "Speed", Operator = "Set To", Value = "{FastSpeed}" }, new RuleAction { Target = "Fuel", Operator = "Set To", Value = "{BigTank}" }],
        };
        RunResult result = Run([variables, edit], new() { ["values.csv"] = "Name,Value\nFastSpeed,95\nBigTank,400\n" });
        Assert.Contains("Speed = 95", result.Text);
        Assert.Contains("Fuel = 400", result.Text);
    }

    [Fact]
    public void The_rows_of_a_variable_file_skip_a_heading_and_empty_names() =>
        Assert.Equal([("A", "1"), ("B", "")], RuleRunner.VariableRows(TableText.Read("Name,Value\nA,1\n,5\nB")));
}
