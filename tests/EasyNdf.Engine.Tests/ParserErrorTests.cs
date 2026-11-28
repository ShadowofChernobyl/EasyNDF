using EasyNdf.Engine.Ndf;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The parser reads tokens as it goes, but reports errors as when the whole file was split into tokens first, as the
/// HTML app's engine does: text that is not NDF anywhere in the file is the error, before any mistake in its order.
/// </summary>
public class ParserErrorTests
{
    [Fact]
    public void Text_that_is_not_NDF_further_on_is_the_error_reported()
    {
        const string text = "Unit_A is TEntity\n(\n    Speed = = 12\n)\nUnit_B is TEntity\n(\n    Name = §\n)\n";

        var error = Assert.Throws<NdfParseException>(() => Parser.Parse(text));

        Assert.StartsWith("Line 7: unrecognized text", error.Message);
    }

    [Fact]
    public void A_mistake_in_order_is_reported_when_all_the_text_is_NDF()
    {
        const string text = "Unit_A is TEntity\n(\n    Speed = = 12\n)\n";

        var error = Assert.Throws<NdfParseException>(() => Parser.Parse(text));

        Assert.StartsWith("Line 3: ", error.Message);
        Assert.DoesNotContain("unrecognized", error.Message);
    }

    [Fact]
    public void Comments_go_where_they_went_when_the_file_was_split_first()
    {
        const string text = "// head\nUnit_A is TEntity\n(\n    // above Speed\n    Speed = 12 // after 12\n    Items = [\n        // above 1\n        1,\n    ]\n)\n// at the end\n";

        NdfDocument document = Parser.Parse(text);

        Assert.Equal("// head", Assert.Single(document.Declarations[0].LeadingComments!).Text);
        var unit = (ObjectValue)document.Declarations[0].Value;
        Assert.Equal("// above Speed", Assert.Single(unit.Properties[0].LeadingComments!).Text);
        Assert.Equal("// above 1", Assert.Single(((ListValue)unit.Properties[1].Value).Items[0].LeadingComments!).Text);
    }
}
