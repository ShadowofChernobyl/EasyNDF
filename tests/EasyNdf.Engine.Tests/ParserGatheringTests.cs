using EasyNdf.Engine.Ndf;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The parser gathers a list's items and an object's properties in lists it reuses, one for each level of nesting, and
/// gives each value a list of its own at the end. Lists and objects inside each other must each keep their own items,
/// in order, and a list must not share its items with the next one read.
/// </summary>
public class ParserGatheringTests
{
    private static string Shape(NdfValue value) => value switch
    {
        ListValue list => "[" + string.Join(",", list.Items.Select(Shape)) + "]",
        ObjectValue obj => obj.TypeName + "(" + string.Join(" ", obj.Properties.Select(p => p.Name + "=" + Shape(p.Value))) + ")",
        PairValue pair => "(" + Shape(pair.First) + "," + Shape(pair.Second) + ")",
        ScalarValue scalar => scalar.Text,
        _ => value.GetType().Name,
    };

    [Fact]
    public void Lists_and_objects_inside_each_other_keep_their_own_items_in_order()
    {
        NdfDocument document = Parser.Parse("""
            A is [ [1, 2], T ( X = [3, [4, U ( Y = 5 Z = [] )]] W = 6 ), [], (7, [8]) ]
            B is T ( P = [9] Q = V ( R = [10, 11] ) S = 12 )
            C is [13]
            """);

        Assert.Equal("[[1,2],T(X=[3,[4,U(Y=5 Z=[])]] W=6),[],(7,[8])]", Shape(document.Declarations[0].Value));
        Assert.Equal("T(P=[9] Q=V(R=[10,11]) S=12)", Shape(document.Declarations[1].Value));
        Assert.Equal("[13]", Shape(document.Declarations[2].Value));
    }

    [Fact]
    public void Every_list_and_object_has_its_own_list_of_items()
    {
        NdfDocument document = Parser.Parse("A is [ [1], [2] ]\nB is T ( X = 1 )\nC is T ( X = 2 )\n");
        var outer = (ListValue)document.Declarations[0].Value;
        var first = (ListValue)outer.Items[0];
        var second = (ListValue)outer.Items[1];
        Assert.NotSame(first.Items, second.Items);
        first.Items.Add(new ScalarValue(ScalarKind.Number, "3"));
        Assert.Equal("[2]", Shape(second));

        var b = (ObjectValue)document.Declarations[1].Value;
        var c = (ObjectValue)document.Declarations[2].Value;
        Assert.NotSame(b.Properties, c.Properties);
        Assert.Equal("T(X=1)", Shape(b));
        Assert.Equal("T(X=2)", Shape(c));
    }

    [Fact]
    public void A_list_read_after_an_error_elsewhere_is_read_whole()
    {
        Assert.Throws<NdfParseException>(() => Parser.Parse("A is [1, [2, 3"));
        Assert.Equal("[1,[2,3]]", Shape(Parser.Parse("A is [1, [2, 3]]").Declarations[0].Value));
    }
}
