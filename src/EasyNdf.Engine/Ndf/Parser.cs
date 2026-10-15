using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Ndf;

/// <summary>
/// Reads NDF text into declarations and values, keeping where every value sits in the text and which comments go where.
/// </summary>
/// <remarks>
/// Comments are handed to the next place the writer starts a new line: a declaration, a property, a list item,
/// or a closing bracket. That is what <see cref="ClaimComments"/> does.
/// </remarks>
public sealed class Parser
{
    private readonly string source;
    private readonly List<Token> tokens;
    private readonly List<(int TokenIndex, List<SourceComment> Comments)> commentGroups;
    private int nextCommentGroup; // the first comment group not handed out yet
    private int i;                // the token being read

    private Parser(string source)
    {
        this.source = source;
        var (readTokens, commentsBefore) = Tokenizer.Read(source);
        tokens = readTokens;
        commentGroups = commentsBefore.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Reads a whole NDF text. Throws <see cref="NdfParseException"/> when the text is not valid NDF.</summary>
    public static NdfDocument Parse(string text) => new Parser(text).ReadDocument();

    // ---- small helpers over the token list ----

    private bool IsKind(int index, TokenKind kind) => index < tokens.Count && tokens[index].Kind == kind;

    private bool IsWord(int index, string word) => IsKind(index, TokenKind.Identifier) && tokens[index].Text == word;

    private bool IsPunct(int index, string punctuation) => IsKind(index, TokenKind.Punctuation) && tokens[index].Text == punctuation;

    /// <summary>A keyword in any capitals: NDF is not case-sensitive, and some game files write DIV.</summary>
    private bool IsKeyword(int index, string word) =>
        IsKind(index, TokenKind.Identifier) && string.Equals(tokens[index].Text, word, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How many tokens the comparison or "and" at <paramref name="index"/> takes: "&lt;" or "&amp;" is one; "==", "!=",
    /// "&lt;=", "&gt;=", "&amp;&amp;" and "||" are two signs written together (the tokenizer reads each sign on its own, as the
    /// HTML app's does). 0 when there is none.
    /// </summary>
    private int ConditionSignAt(int index)
    {
        if (!IsKind(index, TokenKind.Punctuation)) return 0;
        string sign = TextAt(index);
        bool nextTouches = IsKind(index + 1, TokenKind.Punctuation) && tokens[index + 1].Position == tokens[index].Position + 1;
        string next = nextTouches ? TextAt(index + 1) : "";
        if (sign is "=" or "!" or "<" or ">" && next == "=") return 2;
        if ((sign == "&" && next == "&") || (sign == "|" && next == "|")) return 2;
        return sign is "<" or ">" or "&" ? 1 : 0;
    }

    private bool IsPunct(string punctuation) => IsPunct(i, punctuation);

    private string TextAt(int index) => tokens[index].Text;

    /// <summary>Where the previous token ends: the end of the value just read.</summary>
    private int EndOfPreviousToken => tokens[i - 1].Position + tokens[i - 1].Text.Length;

    private NdfParseException Error(string message)
    {
        bool atEnd = i >= tokens.Count;
        int line = JsText.LineNumberAt(source, atEnd ? source.Length : tokens[i].Position);
        return new NdfParseException("Line " + line + ": " + message + (atEnd ? " (end of file)" : " (found \"" + tokens[i].Text + "\")"));
    }

    private void Expect(string punctuation)
    {
        if (!IsPunct(punctuation)) throw Error("expected \"" + punctuation + "\"");
        i++;
    }

    /// <summary>All comments that come before the token at <paramref name="tokenIndex"/> and were not claimed yet.</summary>
    private List<SourceComment>? ClaimComments(int tokenIndex)
    {
        List<SourceComment>? claimed = null;
        while (nextCommentGroup < commentGroups.Count && commentGroups[nextCommentGroup].TokenIndex <= tokenIndex)
        {
            var group = commentGroups[nextCommentGroup++].Comments;
            claimed = claimed is null ? group : [.. claimed, .. group];
        }
        return claimed;
    }

    // ---- declarations ----

    private NdfDocument ReadDocument()
    {
        var declarations = new List<Declaration>();
        while (i < tokens.Count)
        {
            var leadingComments = ClaimComments(i);
            int first = i;
            bool exported = false;
            if (IsWord(i, "export")) { exported = true; i++; }

            if (IsWord(i, "unnamed"))
            {
                i++;
                var value = ReadValue();
                declarations.Add(new Declaration
                {
                    IsUnnamed = true,
                    Value = value,
                    LeadingComments = leadingComments,
                    Start = tokens[first].Position,
                    End = EndOfPreviousToken,
                    Source = source,
                });
                continue;
            }

            string prefix = "";
            if (IsWord(i, "private") || IsWord(i, "public"))
                if (IsKind(i + 1, TokenKind.Identifier) && TextAt(i + 1) != "is")
                    prefix = TextAt(i++) + " ";
            if (IsKeyword(i, "template") && IsKind(i + 1, TokenKind.Identifier) && IsPunct(i + 2, "["))
            {
                // the word as written: some game files write "Template"
                prefix += TextAt(i) + " ";
                i++;
            }

            if (!IsKind(i, TokenKind.Identifier)) throw Error("expected a declaration name");
            int nameStart = tokens[i].Position;
            string name = TextAt(i++);

            string templateParameters = "";
            if (prefix.Contains("template", StringComparison.OrdinalIgnoreCase))
                templateParameters = ReadTemplateParameters();

            if (!IsWord(i, "is")) throw Error("expected \"is\"");
            i++;

            var declarationValue = ReadValue();
            declarations.Add(new Declaration
            {
                IsExported = exported,
                Name = name,
                Value = declarationValue,
                LeadingComments = leadingComments,
                Prefix = prefix,
                TemplateParameters = templateParameters,
                Start = tokens[first].Position,
                End = EndOfPreviousToken,
                NameStart = nameStart,
                NameLength = name.Length,
                Source = source,
            });
        }
        return new NdfDocument(declarations, source);
    }

    /// <summary>A template's parameter list, copied as written. The comments inside it stay inside it.</summary>
    private string ReadTemplateParameters()
    {
        int open = i, depth = 0;
        for (; i < tokens.Count; i++)
        {
            if (IsPunct(i, "[")) depth++;
            else if (IsPunct(i, "]") && --depth == 0) break;
        }
        if (i >= tokens.Count) throw Error("expected \"]\"");
        string text = source[tokens[open].Position..(tokens[i].Position + 1)].Replace("\r", "");
        ClaimComments(i);
        i++;
        return text;
    }

    // ---- values ----

    /// <summary>A value, including "a | b" and calculations such as "5 * 60".</summary>
    private NdfValue ReadValue()
    {
        NdfValue left = ReadPrimary();
        while (true)
        {
            NdfValue combined;
            if (IsPunct("|") && ConditionSignAt(i) == 0)
            {
                i++;
                combined = new PairValue(PairKind.Or, left, ReadPrimary());
            }
            else if (IsKind(i, TokenKind.Punctuation) && TextAt(i) is "*" or "+" or "-" or "/" or "%")
            {
                string op = TextAt(i++);
                combined = new BinaryValue(op, left, ReadPrimary());
            }
            else if (IsKeyword(i, "div") && !IsPunct(i + 1, "=") && !IsWord(i + 1, "is"))
            {
                i++;
                combined = new BinaryValue("div", left, ReadPrimary());
            }
            else if (IsKind(i, TokenKind.Number) && TextAt(i)[0] == '-')
            {
                // "5 -3": the minus sign was read as part of the number
                combined = new BinaryValue("", left, ReadPrimary());
            }
            else if (ConditionSignAt(i) > 0 || IsPunct("?"))
            {
                left = ReadCondition(left);
                continue;
            }
            else break;

            combined.Start = left.Start;
            combined.End = combined switch
            {
                PairValue p => p.Second.End,
                BinaryValue b => b.Right.End,
                _ => null,
            };
            left = combined;
        }
        return left;
    }

    /// <summary>A single value, with its position. A value in brackets keeps the position of what is inside them.</summary>
    private NdfValue ReadPrimary()
    {
        int first = i;
        NdfValue value = ReadPrimaryValue();
        if (value.Start is null)
        {
            value.Start = tokens[first].Position;
            value.End = EndOfPreviousToken;
        }
        // Object.Member and Name[2]: a member of another object, or an item of a list, kept as written. A plain name
        // followed by [ is a typed list (float3[0, 0, 0]), read already.
        bool indexes = value is ScalarValue { Kind: ScalarKind.Reference or ScalarKind.Path or ScalarKind.Expression } && IsPunct("[");
        if ((IsPunct(".") && IsKind(i + 1, TokenKind.Identifier)) || indexes)
        {
            while (true)
            {
                if (IsPunct(".") && IsKind(i + 1, TokenKind.Identifier)) i += 2;
                else if (IsPunct("[")) ReadList(typeName: null);
                else break;
            }
            return KeptAsWritten(value.Start!.Value);
        }
        return value;
    }

    /// <summary>
    /// The rest of a condition or comparison after its first value: "!= nil", "&lt; 6500.0 &amp; ...", "? A : B". Rules
    /// do not look inside it; it is kept as the file writes it (<see cref="ScalarKind.Expression"/>).
    /// </summary>
    private NdfValue ReadCondition(NdfValue first)
    {
        int start = first.Start!.Value;
        while (true)
        {
            int sign = ConditionSignAt(i);
            if (sign > 0)
            {
                i += sign;
                ReadPrimary();
                continue;
            }
            // the arithmetic and "|" between the parts of a condition
            if (IsPunct("|") || (IsKind(i, TokenKind.Punctuation) && TextAt(i) is "*" or "+" or "-" or "/" or "%") || IsKeyword(i, "div"))
            {
                i++;
                ReadPrimary();
                continue;
            }
            if (IsPunct("?"))
            {
                i++;
                ReadValue();
                Expect(":");
                ReadValue();
            }
            break;
        }
        return KeptAsWritten(start);
    }

    /// <summary>The text from <paramref name="start"/> to the token just read, as one value kept as written.</summary>
    private ScalarValue KeptAsWritten(int start) =>
        new(ScalarKind.Expression, source[start..EndOfPreviousToken]) { Start = start, End = EndOfPreviousToken };

    private NdfValue ReadPrimaryValue()
    {
        if (i >= tokens.Count) throw Error("unexpected token");
        var token = tokens[i];
        switch (token.Kind)
        {
            case TokenKind.Number: i++; return new ScalarValue(ScalarKind.Number, token.Text);
            case TokenKind.String: i++; return new ScalarValue(ScalarKind.String, token.Text);
            case TokenKind.Guid: i++; return new ScalarValue(ScalarKind.Guid, token.Text);
            case TokenKind.Path: i++; return new ScalarValue(ScalarKind.Path, token.Text);
            case TokenKind.RelativeReference:
            case TokenKind.AbsoluteReference: i++; return new ScalarValue(ScalarKind.Reference, token.Text);
            case TokenKind.Identifier: return ReadWordValue();
        }

        if (IsPunct("[")) return ReadList(typeName: null);
        if (IsPunct("("))
        {
            i++;
            NdfValue first = ReadValue();
            if (IsPunct(","))
            {
                i++;
                NdfValue second = ReadValue();
                Expect(")");
                return new PairValue(PairKind.Pair, first, second);
            }
            Expect(")");
            // Brackets around a calculation are kept; brackets around a single value are not.
            return first is BinaryValue ? new WrappedValue(WrapKind.Parentheses, first) : first;
        }
        if (IsPunct("-"))
        {
            i++;
            return new WrappedValue(WrapKind.Negative, ReadPrimary());
        }
        if (IsPunct("!"))
        {
            // !Value: a "not", kept as written
            int start = tokens[i].Position;
            i++;
            ReadPrimary();
            return KeptAsWritten(start);
        }
        throw Error("unexpected token");
    }

    /// <summary>A value that starts with a word: nil, Name is value, an object, a typed list, or a plain name.</summary>
    private NdfValue ReadWordValue()
    {
        string word = TextAt(i++);
        if (word == "nil") return new ScalarValue(ScalarKind.Nil, "nil");

        // "private Name is Value" or "export Name is Value" inside a list or an object: the word in front is kept with
        // the text around the value, as written
        if (word is "private" or "public" or "export" && IsKind(i, TokenKind.Identifier) && IsWord(i + 1, "is"))
            word = TextAt(i++);

        if (IsWord(i, "is"))
        {
            i++;
            return new AssignValue(word, ReadValue());
        }

        // An object: the word is followed by "(" and then "Name =", "Name is", or ")".
        // (The first member may be "export Name is", "private Name is" or "public Name is".)
        int firstName = IsKind(i + 1, TokenKind.Identifier) && TextAt(i + 1) is "export" or "private" or "public" && IsKind(i + 2, TokenKind.Identifier) ? i + 2 : i + 1;
        bool startsObject = IsPunct("(") &&
            ((IsKind(firstName, TokenKind.Identifier) && (IsPunct(firstName + 1, "=") || IsPunct(firstName + 1, ":") || IsWord(firstName + 1, "is"))) || IsPunct(i + 1, ")"));
        if (startsObject) return ReadObject(word);

        if (IsPunct("[")) return ReadList(word);
        return new ScalarValue(ScalarKind.Identifier, word);
    }

    private ObjectValue ReadObject(string typeName)
    {
        var headComments = ClaimComments(i);
        i++; // "("
        var properties = new List<Property>();
        while (!IsPunct(")"))
        {
            var leadingComments = ClaimComments(i);
            if (!IsKind(i, TokenKind.Identifier)) throw Error("expected a property name");
            // "export Name is Value" or "private Name is Value": the word in front stays in the text around the value
            if (TextAt(i) is "export" or "private" or "public" && IsKind(i + 1, TokenKind.Identifier) && IsWord(i + 2, "is")) i++;
            string name = TextAt(i++);
            if (IsWord(i, "is"))
            {
                // a member declared inside the object, as hand-written game files do
                i++;
                properties.Add(new Property(name, ReadValue()) { LeadingComments = leadingComments, IsMemberDeclaration = true });
                continue;
            }
            // "Name: Type" names the type of a template's or an alias's value; it is read as "Name = Type"
            if (IsPunct(":")) i++;
            else Expect("=");
            properties.Add(new Property(name, ReadValue()) { LeadingComments = leadingComments });
        }
        var endComments = ClaimComments(i);
        i++; // ")"
        return new ObjectValue(typeName, properties)
        {
            HeadComments = headComments,
            EndComments = endComments,
            SourceTypeNameLength = typeName.Length,
        };
    }

    private ListValue ReadList(string? typeName)
    {
        int openBracketAt = tokens[i].Position;
        Expect("[");
        var items = new List<NdfValue>();
        while (!IsPunct("]"))
        {
            var leadingComments = ClaimComments(i);
            var item = ReadValue();
            if (leadingComments is not null) item.LeadingComments = leadingComments;
            items.Add(item);
            if (IsPunct(",")) i++;
            else break;
            // a doubled comma, as in "A",, : the empty place holds nothing
            while (IsPunct(",")) i++;
        }
        var endComments = ClaimComments(i);
        Expect("]");
        return new ListValue(typeName, items) { OpenBracketAt = openBracketAt, EndComments = endComments };
    }
}
