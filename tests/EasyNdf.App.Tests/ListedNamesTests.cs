using EasyNdf.App.Editor;

namespace EasyNdf.App.Tests;

/// <summary>The names a listed file declares, read without parsing it, for the File Editor's tips and Go to Descriptor.</summary>
public class ListedNamesTests
{
    [Fact]
    public void Declarations_at_line_starts_give_their_type_and_line()
    {
        string text = string.Join("\n",
            "// comment",
            "TAcknowUnitType_SAM is 92",
            "export Descriptor_Unit_A is TEntityDescriptor",
            "(",
            "    Inner is TNotTopLevel",
            ")",
            "private Helper is TModule()",
            "template MyTemplate",
            "[",
            "]");
        Dictionary<string, (string Type, int Line)> names = ListedNames.DeclarationsIn(text);
        Assert.Equal(("", 2), names["TAcknowUnitType_SAM"]);
        Assert.Equal(("TEntityDescriptor", 3), names["Descriptor_Unit_A"]);
        Assert.Equal(("TModule", 7), names["Helper"]);
        Assert.Equal(8, names["MyTemplate"].Line);
        Assert.False(names.ContainsKey("Inner"));
    }
}
