using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Ndf;

/// <summary>A comment read from the file: // or /* */.</summary>
/// <param name="Text">The comment as written, including its // or /* */.</param>
/// <param name="SameLine">True when the comment sits on the same line as the code before it.</param>
public sealed record SourceComment(string Text, bool SameLine);

/// <summary>
/// One value in an NDF file: a number, a text, an object, a list, and so on.
/// </summary>
/// <remarks>
/// <para>
/// Positions matter more than anything else here. The exported file is not written from scratch: everything a rule
/// did not touch is copied from the source text character for character, using <see cref="Start"/> and <see cref="End"/>.
/// </para>
/// <para>
/// A value read from the file has Start and End. A value a rule wrote has neither; when it took the place of a value
/// from the file it has <see cref="ReplacedStart"/> and <see cref="ReplacedEnd"/> instead: the stretch of text it replaces.
/// </para>
/// </remarks>
public abstract class NdfValue
{
    // A large game file has over half a million values, so each holds as little as it can: its place in the file as two
    // plain numbers (NoPosition for none), and what only rules and a few list items need in an object of its own,
    // made only for the values that have any of it.
    private const int NoPosition = int.MinValue;
    private int start = NoPosition, end = NoPosition;
    private RareParts? rare;

    /// <summary>What few values have: a rule's changes, and comments kept with a list item.</summary>
    private sealed class RareParts
    {
        public int? ReplacedStart, ReplacedEnd;
        public string? AddedComment, TrailingLineComment;
        public List<SourceComment>? LeadingComments;
    }

    /// <summary>The value's rare parts, made the first time one is set to something.</summary>
    private RareParts Rare => rare ??= new RareParts();

    /// <summary>Where the value starts in the source text, or null for a value a rule wrote.</summary>
    public int? Start
    {
        get => start == NoPosition ? null : start;
        set => start = value ?? NoPosition;
    }

    /// <summary>Where the value ends in the source text (one past its last character).</summary>
    public int? End
    {
        get => end == NoPosition ? null : end;
        set => end = value ?? NoPosition;
    }

    /// <summary>For a value a rule wrote in place of one from the file: where the replaced text starts.</summary>
    public int? ReplacedStart
    {
        get => rare?.ReplacedStart;
        set { if (value is not null || rare is not null) Rare.ReplacedStart = value; }
    }

    /// <summary>For a value a rule wrote in place of one from the file: where the replaced text ends.</summary>
    public int? ReplacedEnd
    {
        get => rare?.ReplacedEnd;
        set { if (value is not null || rare is not null) Rare.ReplacedEnd = value; }
    }

    /// <summary>Comment text a rule adds after the value, such as " /* Multiplied by 2 */". Null when there is none.</summary>
    public string? AddedComment
    {
        get => rare?.AddedComment;
        set { if (value is not null || rare is not null) Rare.AddedComment = value; }
    }

    /// <summary>Comments from the file that stand above this value, when it is a list item.</summary>
    public List<SourceComment>? LeadingComments
    {
        get => rare?.LeadingComments;
        set { if (value is not null || rare is not null) Rare.LeadingComments = value; }
    }

    /// <summary>A // comment a rule wrote after a list item it inserted. It goes at the end of that item's line.</summary>
    public string? TrailingLineComment
    {
        get => rare?.TrailingLineComment;
        set { if (value is not null || rare is not null) Rare.TrailingLineComment = value; }
    }

    /// <summary>Where the value starts: in the file, or in the text it replaced.</summary>
    public int? StartOrReplaced => Start ?? ReplacedStart;

    /// <summary>Where the value ends: in the file, or in the text it replaced.</summary>
    public int? EndOrReplaced => Start is not null ? End : ReplacedEnd;

    /// <summary>
    /// True for values a rule sees as one piece of text: everything except objects, lists, pairs, "a | b" and "Name is value".
    /// A sum such as 5 * 60 counts as one value: a rule can replace it whole but cannot calculate with it.
    /// </summary>
    public abstract bool IsPrimitive { get; }

    /// <summary>A copy for a rule to change. The copy keeps the positions, so the writer knows where it came from.</summary>
    public abstract NdfValue CopyForEdit();

    /// <summary>Copies the position fields onto a copy, the "sameAt" step of every copy.</summary>
    protected T WithSamePosition<T>(T copy) where T : NdfValue
    {
        copy.Start = Start;
        copy.End = End;
        if (ReplacedStart is not null)
        {
            copy.ReplacedStart = ReplacedStart;
            copy.ReplacedEnd = ReplacedEnd;
        }
        copy.AddedComment = AddedComment;
        copy.LeadingComments = LeadingComments;
        return copy;
    }

    /// <summary>
    /// Marks a value a rule wrote as taking the place of <paramref name="replaced"/>, so the writer puts it where the old one was.
    /// </summary>
    public NdfValue TakesPlaceOf(NdfValue replaced)
    {
        int? start = replaced.StartOrReplaced;
        if (start is not null)
        {
            ReplacedStart = start;
            ReplacedEnd = replaced.EndOrReplaced;
        }
        return this;
    }
}

/// <summary>The kinds of single values.</summary>
public enum ScalarKind
{
    /// <summary>A number such as 12, -0.5 or 0xFF.</summary>
    Number,
    /// <summary>A quoted text: 'Text' or "Text". The quotes are part of <see cref="ScalarValue.Text"/>.</summary>
    String,
    /// <summary>GUID:{...}</summary>
    Guid,
    /// <summary>A name with slashes such as EVisionRange/Standard, or &lt;Name&gt;.</summary>
    Path,
    /// <summary>A reference to another declaration: $/GFX/Weapon/Name or ~/Name.</summary>
    Reference,
    /// <summary>A plain name such as True or MyConstant.</summary>
    Identifier,
    /// <summary>The word nil.</summary>
    Nil,
    /// <summary>Text a rule wrote that is not a plain number. It is written as typed and may hold a // comment.</summary>
    Raw,
    /// <summary>
    /// A calculation rules do not look inside, kept as the file writes it: a condition (Value ? A : B), a comparison
    /// (A != nil), a "not" (!Value), or a member of another object (Object.Member). Game templates and interface files
    /// use them.
    /// </summary>
    Expression,
}

/// <summary>A single value: a number, a quoted text, a GUID, a reference, a name, nil, or text a rule wrote.</summary>
public sealed class ScalarValue(ScalarKind kind, string text) : NdfValue
{
    public ScalarKind Kind { get; } = kind;

    /// <summary>The value as written. For a string this includes its quotes; for nil it is "nil".</summary>
    public string Text { get; set; } = text;

    /// <summary>
    /// Whether a value a rule wrote on several lines takes the indentation of the line it lands on (Set To a list or
    /// an object, Add Property). Other values are written exactly as typed, as the HTML app writes them.
    /// </summary>
    public bool FitsIndentation { get; set; }

    public override bool IsPrimitive => true;

    public override NdfValue CopyForEdit()
    {
        var copy = WithSamePosition(new ScalarValue(Kind, Text));
        copy.TrailingLineComment = TrailingLineComment;
        copy.FitsIndentation = FitsIndentation;
        return copy;
    }

    /// <summary>A number when the text is a plain number, raw text otherwise: what a rule writes when it sets a value.</summary>
    public static ScalarValue Written(string text) =>
        new(NumberText.IsPlainNumber(text) ? ScalarKind.Number : ScalarKind.Raw, text);
}

/// <summary>One property of an object: Name = value, or a member declared inside the object, Name is value.</summary>
public sealed class Property(string name, NdfValue value)
{
    public string Name { get; } = name;
    public NdfValue Value { get; set; } = value;

    /// <summary>Comments from the file on the lines above the property.</summary>
    public List<SourceComment>? LeadingComments { get; init; }

    /// <summary>True for "Name is value" inside an object, as hand-written game files sometimes have.</summary>
    public bool IsMemberDeclaration { get; init; }

    /// <summary>True for a property a rule added ("Add Property"): the writer writes its name and "=" too.</summary>
    public bool IsAddedByRule { get; init; }

    public Property CopyForEdit() => new(Name, Value.CopyForEdit())
    {
        LeadingComments = LeadingComments,
        IsMemberDeclaration = IsMemberDeclaration,
        IsAddedByRule = IsAddedByRule,
    };
}

/// <summary>An object: TypeName ( Name = value ... ).</summary>
public sealed class ObjectValue(string typeName, List<Property> properties) : NdfValue
{
    /// <summary>The object's type, such as TAmmunitionDescriptor. A rule can change it.</summary>
    public string TypeName { get; set; } = typeName;

    /// <summary>How long the type name is in the source text, so the writer can skip over it after a rule renamed it.</summary>
    public int SourceTypeNameLength { get; init; }

    /// <summary>A comment a rule wrote after the type name.</summary>
    public string? TypeNameComment { get; set; }

    public List<Property> Properties { get; } = properties;

    /// <summary>Comments between the type name and "(".</summary>
    public List<SourceComment>? HeadComments { get; init; }

    /// <summary>Comments after the last property, before ")".</summary>
    public List<SourceComment>? EndComments { get; init; }

    /// <summary>Properties from the file a rule took out (Remove Property): the writer leaves out their text.</summary>
    public List<Property>? RemovedProperties { get; set; }

    public override bool IsPrimitive => false;

    public override NdfValue CopyForEdit() =>
        WithSamePosition(new ObjectValue(TypeName, Properties.Select(p => p.CopyForEdit()).ToList())
        {
            SourceTypeNameLength = SourceTypeNameLength,
            TypeNameComment = TypeNameComment,
            HeadComments = HeadComments,
            EndComments = EndComments,
            RemovedProperties = RemovedProperties is null ? null : [.. RemovedProperties],
        });
}

/// <summary>A list: [ a, b, c ], or one with a type name in front, TypeName[ a, b ].</summary>
public sealed class ListValue(string? typeName, List<NdfValue> items) : NdfValue
{
    /// <summary>The type in front of the list, or null for a plain list.</summary>
    public string? TypeName { get; } = typeName;

    public List<NdfValue> Items { get; } = items;

    /// <summary>
    /// The items as the file has them. A rule may add or take out items; the writer compares the two lists
    /// to copy the spacing and comments around the items that stayed.
    /// </summary>
    public List<NdfValue>? SourceItems { get; init; }

    /// <summary>Where the "[" is in the source text.</summary>
    public int OpenBracketAt { get; init; }

    /// <summary>Comments after the last item, before "]". A rule that removes the last item moves its comments here.</summary>
    public List<SourceComment>? EndComments { get; set; }

    public override bool IsPrimitive => false;

    /// <summary>
    /// Takes out one item. The comments above it move to whatever follows, or to the end of the list, so no
    /// comment is lost with the item.
    /// </summary>
    public void RemoveItemAt(int index)
    {
        NdfValue removed = Items[index];
        Items.RemoveAt(index);
        if (removed.LeadingComments is null) return;
        if (index < Items.Count)
            Items[index].LeadingComments = [.. removed.LeadingComments, .. Items[index].LeadingComments ?? []];
        else
            EndComments = [.. removed.LeadingComments, .. EndComments ?? []];
    }

    public override NdfValue CopyForEdit() =>
        WithSamePosition(new ListValue(TypeName, Items.Select(i => i.CopyForEdit()).ToList())
        {
            SourceItems = SourceItems ?? Items,
            OpenBracketAt = OpenBracketAt,
            EndComments = EndComments,
        });
}

/// <summary>Kinds of values made of two values.</summary>
public enum PairKind
{
    /// <summary>( a, b )</summary>
    Pair,
    /// <summary>a | b</summary>
    Or,
}

/// <summary>A pair, ( a, b ), or an "or" of flags, a | b.</summary>
public sealed class PairValue(PairKind kind, NdfValue first, NdfValue second) : NdfValue
{
    public PairKind Kind { get; } = kind;
    public NdfValue First { get; set; } = first;
    public NdfValue Second { get; set; } = second;

    public override bool IsPrimitive => false;

    public override NdfValue CopyForEdit() =>
        WithSamePosition(new PairValue(Kind, First.CopyForEdit(), Second.CopyForEdit()));
}

/// <summary>A name given to a value inside another value: Name is value.</summary>
public sealed class AssignValue(string name, NdfValue value) : NdfValue
{
    public string Name { get; } = name;
    public NdfValue Value { get; set; } = value;

    public override bool IsPrimitive => false;

    public override NdfValue CopyForEdit() => WithSamePosition(new AssignValue(Name, Value.CopyForEdit()));
}

/// <summary>
/// A calculation written in the file, such as 5 * 60 or 2 div 3. Rules treat it as one value.
/// </summary>
public sealed class BinaryValue(string op, NdfValue left, NdfValue right) : NdfValue
{
    /// <summary>The operator: *, +, -, /, %, div, or "" for "5 -3" where the minus was read as part of the number.</summary>
    public string Operator { get; } = op;
    public NdfValue Left { get; set; } = left;
    public NdfValue Right { get; set; } = right;

    public override bool IsPrimitive => true;

    public override NdfValue CopyForEdit() =>
        WithSamePosition(new BinaryValue(Operator, Left.CopyForEdit(), Right.CopyForEdit()));
}

/// <summary>Kinds of values that wrap one value.</summary>
public enum WrapKind
{
    /// <summary>A calculation in brackets: ( a * b ).</summary>
    Parentheses,
    /// <summary>A minus sign in front of a value that is not a plain number: -Value.</summary>
    Negative,
}

/// <summary>A calculation in brackets, or a value with a minus sign in front.</summary>
public sealed class WrappedValue(WrapKind kind, NdfValue inner) : NdfValue
{
    public WrapKind Kind { get; } = kind;
    public NdfValue Inner { get; set; } = inner;

    public override bool IsPrimitive => true;

    public override NdfValue CopyForEdit() => WithSamePosition(new WrappedValue(Kind, Inner.CopyForEdit()));
}
