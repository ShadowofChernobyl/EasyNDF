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
            if (IsWord(i, "template") && IsKind(i + 1, TokenKind.Identifier) && IsPunct(i + 2, "["))
            {
                prefix += "template ";
                i++;
            }

            if (!IsKind(i, TokenKind.Identifier)) throw Error("expected a declaration name");
            int nameStart = tokens[i].Position;
            string name = TextAt(i++);

            string templateParameters = "";
            if (prefix.Contains("template", StringComparison.Ordinal))
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
            if (IsPunct("|"))
            {
                i++;
                combined = new PairValue(PairKind.Or, left, ReadPrimary());
            }
            else if (IsKind(i, TokenKind.Punctuation) && TextAt(i) is "*" or "+" or "-" or "/" or "%")
            {
                string op = TextAt(i++);
                combined = new BinaryValue(op, left, ReadPrimary());
            }
            else if (IsWord(i, "div") && !IsPunct(i + 1, "=") && !IsWord(i + 1, "is"))
            {
                i++;
                combined = new BinaryValue("div", left, ReadPrimary());
            }
            else if (IsKind(i, TokenKind.Number) && TextAt(i)[0] == '-')
            {
                // "5 -3": the minus sign was read as part of the number
                combined = new BinaryValue("", left, ReadPrimary());
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
        return value;
    }

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
        throw Error("unexpected token");
    }

    /// <summary>A value that starts with a word: nil, Name is value, an object, a typed list, or a plain name.</summary>
    private NdfValue ReadWordValue()
    {
        string word = TextAt(i++);
        if (word == "nil") return new ScalarValue(ScalarKind.Nil, "nil");

        if (IsWord(i, "is"))
        {
            i++;
            return new AssignValue(word, ReadValue());
        }

        // An object: the word is followed by "(" and then "Name =", "Name is", or ")".
        bool startsObject = IsPunct("(") &&
            ((IsKind(i + 1, TokenKind.Identifier) && (IsPunct(i + 2, "=") || IsWord(i + 2, "is"))) || IsPunct(i + 1, ")"));
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
            string name = TextAt(i++);
            if (IsWord(i, "is"))
            {
                // a member declared inside the object, as hand-written game files do
                i++;
                properties.Add(new Property(name, ReadValue()) { LeadingComments = leadingComments, IsMemberDeclaration = true });
                continue;
            }
            Expect("=");
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
        }
        var endComments = ClaimComments(i);
        Expect("]");
        return new ListValue(typeName, items) { OpenBracketAt = openBracketAt, EndComments = endComments };
    }
}
