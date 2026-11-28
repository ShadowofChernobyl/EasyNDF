using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>A template written "is Type meta ( ... ) ( ... )", as the game's script templates are, is read and written back as it was.</summary>
public class MetaBlockTests
{
    private const string Text =
        "template AbstractGameRules\n[\n] is TGDAbstractGameRules\nmeta\n(\n    Abstract     = 'true',\n    Family       = '25_Base',\n)\n(\n)\n" +
        "template BankObject\n[\n    Value : int = 1,\n] is TBank\nmeta\n(\n    Parent = 'TBaseClass',\n)\n(\n    Amount = <Value>\n)\n";

    [Fact]
    public void It_is_read_without_an_error_and_written_back_unchanged()
    {
        var engine = new NdfEngine();
        LoadSummary summary = engine.Load(Text);
        Assert.Equal(2, summary.Declarations);
        Assert.Equal(Text, engine.Run([], new RunOptions()).Text);
    }
}
