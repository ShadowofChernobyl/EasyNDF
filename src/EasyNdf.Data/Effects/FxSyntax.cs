using System.Globalization;
using System.Text;

namespace EasyNdf.Data.Effects;

/// <summary>
/// One piece of an effect file as read: the game's effect scripts (GameData/Fx, CommonData/Fx) are NDF in which nearly
/// everything is a value worked out as the effect runs, so objects, lists and calculations are all read as one kind of
/// tree, and worked out by <see cref="FxEvaluator"/>.
/// </summary>
public abstract record FxNode;

/// <summary>A number.</summary>
public sealed record FxNumber(double Value) : FxNode;

/// <summary>A quoted text.</summary>
public sealed record FxText(string Value) : FxNode;

/// <summary>A name, such as ParticleLifeRatio, or a path to a declaration ($/VFX_Bank/BANK_FireBalls, ~/Name), kept whole.</summary>
public sealed record FxName(string Value) : FxNode
{
    /// <summary>The name without its path: "$/VFX_Bank/BANK_FireBalls" is "BANK_FireBalls".</summary>
    public string Last { get; } = Value[(Value.LastIndexOf('/') + 1)..];

    /// <summary>Whether it is a path from the top ($/VFX_Bank/...), which no action's parameter can stand for.</summary>
    public bool IsPath { get; } = Value.StartsWith("$/", StringComparison.Ordinal);

    /// <summary>The declaration the effect files have for it, once looked up (Tuned for speed: a name is looked up once, not each time it is worked out).</summary>
    internal FxDeclaration? Declared { get; set; }

    /// <summary>Whether <see cref="Declared"/> has been looked up.</summary>
    internal bool IsLookedUp { get; set; }
}

/// <summary>A template's parameter, &lt;Name&gt;, worked out from the values the template was given.</summary>
public sealed record FxTemplateParameter(string Name) : FxNode;

/// <summary>nil.</summary>
public sealed record FxNil : FxNode
{
    /// <summary>The one nil.</summary>
    public static readonly FxNil Value = new();
}

/// <summary>A call or a typed list: name[a, b], such as RandomRange[0.8, 1.2], float3[0, 0, 1] or MAP[...].</summary>
public sealed record FxCall(FxNode Callee, IReadOnlyList<FxNode> Arguments) : FxNode;

/// <summary>A list: [a, b, c].</summary>
public sealed record FxList(IReadOnlyList<FxNode> Items) : FxNode;

/// <summary>A pair, as a MAP's entries are written: (key, value).</summary>
public sealed record FxPair(FxNode Key, FxNode Value) : FxNode;

/// <summary>An object: Type ( Name = value ... ), with the values declared inside it (Name is value) kept apart.</summary>
public sealed record FxObject(string Type, IReadOnlyDictionary<string, FxNode> Properties, IReadOnlyDictionary<string, FxNode> Members) : FxNode
{
    /// <summary>A property; null when the object does not give it.</summary>
    public FxNode? Get(string name) => Properties.TryGetValue(name, out FxNode? value) ? value : null;
}

/// <summary>An operation on two values: + - * / div % == != &lt; &gt; &lt;= &gt;= | &amp;.</summary>
public sealed record FxBinary(string Operator, FxNode Left, FxNode Right) : FxNode;

/// <summary>A minus sign or a "not" in front of a value.</summary>
public sealed record FxUnary(string Operator, FxNode Operand) : FxNode;

/// <summary>A choice: condition ? a : b.</summary>
public sealed record FxChoice(FxNode Condition, FxNode WhenTrue, FxNode WhenFalse) : FxNode;

/// <summary>One declaration of an effect file: Name is value, or a template with its parameters and their defaults.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Value">Its value.</param>
/// <param name="TemplateParameters">For a template, its parameters with their defaults (null for none); null otherwise.</param>
public sealed record FxDeclaration(string Name, FxNode Value, IReadOnlyDictionary<string, FxNode?>? TemplateParameters);

/// <summary>Reads effect files into <see cref="FxDeclaration"/>s.</summary>
public sealed class FxSyntax
{
    private readonly string text;
    private int at;

    private FxSyntax(string text) => this.text = text;

    /// <summary>Every declaration of a file; throws <see cref="FormatException"/> where the file cannot be read.</summary>
    public static List<FxDeclaration> ReadFile(string text)
    {
        var reader = new FxSyntax(text);
        var declarations = new List<FxDeclaration>();
        while (true)
        {
            reader.SkipSpace();
            if (reader.at >= text.Length) return declarations;
            declarations.Add(reader.ReadDeclaration());
        }
    }

    /// <summary>One value written on its own, such as a calculation.</summary>
    public static FxNode ReadValue(string text)
    {
        var reader = new FxSyntax(text);
        FxNode value = reader.ReadExpression();
        return value;
    }

    // ---- declarations ----

    private FxDeclaration ReadDeclaration()
    {
        string word = ReadWord();
        bool isTemplate = false;
        while (word is "export" or "private" or "public" or "template")
        {
            if (word == "template") isTemplate = true;
            SkipSpace();
            word = ReadWord();
        }
        if (word == "unnamed") return new FxDeclaration("", ReadExpression(), null);
        string name = word;
        Dictionary<string, FxNode?>? parameters = null;
        SkipSpace();
        if (isTemplate && Peek() == '[') parameters = ReadTemplateParameters();
        SkipSpace();
        // "Name: type is value" is allowed; the type is not needed
        if (Peek() == ':')
        {
            at++;
            SkipSpace();
            ReadWord();
            SkipTypeArguments();
            SkipSpace();
        }
        ExpectWord("is");
        FxNode value = ReadExpression();
        return new FxDeclaration(name, value, parameters ?? (isTemplate ? new Dictionary<string, FxNode?>() : null));
    }

    /// <summary>A template's parameters: [ Name : type [options] = default, ... ].</summary>
    private Dictionary<string, FxNode?> ReadTemplateParameters()
    {
        var parameters = new Dictionary<string, FxNode?>(StringComparer.Ordinal);
        at++;
        while (true)
        {
            SkipSpace();
            if (Peek() == ']')
            {
                at++;
                return parameters;
            }
            if (Peek() == ',')
            {
                at++;
                continue;
            }
            string name = ReadWord();
            SkipSpace();
            if (Peek() == ':')
            {
                at++;
                SkipSpace();
                ReadWord();
                SkipTypeArguments();
                SkipSpace();
                if (Peek() == '[') SkipBracketed('[', ']');
                SkipSpace();
            }
            FxNode? defaultValue = null;
            if (Peek() == '=')
            {
                at++;
                defaultValue = ReadExpression();
            }
            parameters[name] = defaultValue;
        }
    }

    /// <summary>A type's arguments, such as the &lt;string, TEvaluable&gt; of MAP&lt;string, TEvaluable&gt;.</summary>
    private void SkipTypeArguments()
    {
        if (Peek() == '<') SkipBracketed('<', '>');
    }

    private void SkipBracketed(char open, char close)
    {
        int depth = 0;
        while (at < text.Length)
        {
            char c = text[at++];
            if (c == open) depth++;
            else if (c == close && --depth == 0) return;
            else if (c is '\'' or '"') SkipQuoted(c);
        }
    }

    // ---- values ----

    private FxNode ReadExpression()
    {
        FxNode condition = ReadOr();
        SkipSpace();
        if (Peek() != '?') return condition;
        at++;
        FxNode whenTrue = ReadExpression();
        SkipSpace();
        Expect(':');
        FxNode whenFalse = ReadExpression();
        return new FxChoice(condition, whenTrue, whenFalse);
    }

    private FxNode ReadOr()
    {
        FxNode left = ReadAnd();
        while (true)
        {
            SkipSpace();
            if (Peek() == '|' && PeekAt(1) != '|') { at++; left = new FxBinary("|", left, ReadAnd()); }
            else if (Peek() == '|' && PeekAt(1) == '|') { at += 2; left = new FxBinary("|", left, ReadAnd()); }
            else if (IsWordAhead("or")) { ReadWord(); left = new FxBinary("|", left, ReadAnd()); }
            else return left;
        }
    }

    private FxNode ReadAnd()
    {
        FxNode left = ReadComparison();
        while (true)
        {
            SkipSpace();
            if (Peek() == '&') { at += PeekAt(1) == '&' ? 2 : 1; left = new FxBinary("&", left, ReadComparison()); }
            else if (IsWordAhead("and")) { ReadWord(); left = new FxBinary("&", left, ReadComparison()); }
            else return left;
        }
    }

    private FxNode ReadComparison()
    {
        FxNode left = ReadSum();
        SkipSpace();
        foreach (string op in new[] { "==", "!=", "<=", ">=", "<", ">" })
        {
            if (string.CompareOrdinal(text, at, op, 0, op.Length) != 0) continue;
            // "<Name>" is a template parameter, never a comparison
            if (op == "<" && IsTemplateParameterAhead()) return left;
            at += op.Length;
            return new FxBinary(op, left, ReadSum());
        }
        return left;
    }

    private FxNode ReadSum()
    {
        FxNode left = ReadProduct();
        while (true)
        {
            SkipSpace();
            char c = Peek();
            if (c is '+' or '-') { at++; left = new FxBinary(c.ToString(), left, ReadProduct()); }
            else return left;
        }
    }

    private FxNode ReadProduct()
    {
        FxNode left = ReadUnary();
        while (true)
        {
            SkipSpace();
            char c = Peek();
            if (c == '*' || (c == '/' && PeekAt(1) != '/' && PeekAt(1) != '*') || c == '%') { at++; left = new FxBinary(c == '/' ? "div" : c.ToString(), left, ReadUnary()); }
            else if (IsWordAhead("div")) { ReadWord(); left = new FxBinary("div", left, ReadUnary()); }
            else if (IsWordAhead("mod")) { ReadWord(); left = new FxBinary("%", left, ReadUnary()); }
            else return left;
        }
    }

    private FxNode ReadUnary()
    {
        SkipSpace();
        if (Peek() == '-') { at++; return new FxUnary("-", ReadUnary()); }
        if (Peek() == '!') { at++; return new FxUnary("!", ReadUnary()); }
        if (Peek() == '+') { at++; return ReadUnary(); }
        return ReadPostfix();
    }

    private FxNode ReadPostfix()
    {
        FxNode value = ReadPrimary();
        while (true)
        {
            // name[...] calls, and a value indexed by another, as MAP[...][<Key>]
            if (Peek() == '[' && (value is FxName or FxCall or FxObject))
            {
                value = new FxCall(value, ReadItems('[', ']'));
                continue;
            }
            return value;
        }
    }

    private FxNode ReadPrimary()
    {
        SkipSpace();
        char c = Peek();
        if (c == '\0') throw Problem("the file ends early");
        if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(PeekAt(1)))) return ReadNumber();
        if (c is '\'' or '"') return new FxText(ReadQuoted());
        if (c == '<' && IsTemplateParameterAhead())
        {
            at++;
            string name = ReadWord();
            Expect('>');
            return new FxTemplateParameter(name);
        }
        if (c == '[') return new FxList(ReadItems('[', ']'));
        if (c == '(')
        {
            at++;
            FxNode first = ReadExpression();
            SkipSpace();
            if (Peek() == ',')
            {
                at++;
                FxNode second = ReadExpression();
                SkipSpace();
                if (Peek() == ',') at++;
                SkipSpace();
                Expect(')');
                return new FxPair(first, second);
            }
            Expect(')');
            return first;
        }
        string word = ReadName();
        if (word.Length == 0) throw Problem($"'{c}' where a value was expected");
        if (word == "nil") return FxNil.Value;
        SkipSpaceOnLine();
        if (Peek() == '(') return ReadObject(word);
        // a value named where it is written, as "Actions = ActionList is [ ... ]": the name is not needed
        if (IsWordAhead("is"))
        {
            ReadWord();
            return ReadExpression();
        }
        // the same, exported or kept private, as the tracers' "Spawner = export Spawner is ParticleSpawner( ... )"
        if (word is "export" or "private" or "public" && (char.IsAsciiLetter(Peek()) || Peek() == '_'))
        {
            int back = at;
            ReadWord();
            SkipSpaceOnLine();
            if (IsWordAhead("is"))
            {
                ReadWord();
                return ReadExpression();
            }
            at = back;
        }
        return new FxName(word);
    }

    /// <summary>Type ( Name = value  Name is value  Name : type ... ).</summary>
    private FxObject ReadObject(string type)
    {
        var properties = new Dictionary<string, FxNode>(StringComparer.Ordinal);
        var members = new Dictionary<string, FxNode>(StringComparer.Ordinal);
        at++;
        while (true)
        {
            SkipSpace();
            if (Peek() == ')')
            {
                at++;
                return new FxObject(type, properties, members);
            }
            if (Peek() == ',')
            {
                at++;
                continue;
            }
            string word = ReadWord();
            if (word is "private" or "export" or "public")
            {
                SkipSpace();
                word = ReadWord();
            }
            SkipSpace();
            if (Peek() == '=' && PeekAt(1) != '=')
            {
                at++;
                properties[word] = ReadExpression();
            }
            else if (IsWordAhead("is"))
            {
                ReadWord();
                members[word] = ReadExpression();
            }
            else if (Peek() == ':')
            {
                at++;
                SkipSpace();
                properties[word] = new FxName(ReadName());
                SkipTypeArguments();
            }
            else throw Problem($"'{Peek()}' after {word} in {type}");
        }
    }

    private List<FxNode> ReadItems(char open, char close)
    {
        var items = new List<FxNode>();
        Expect(open);
        while (true)
        {
            SkipSpace();
            if (Peek() == close)
            {
                at++;
                return items;
            }
            if (Peek() == ',')
            {
                at++;
                continue;
            }
            // a list may declare names inside it: [ private parColor is Template_Param_Float3 (), ... ]
            int before = at;
            string word = char.IsAsciiLetter(Peek()) || Peek() == '_' ? ReadWord() : "";
            if (word is "private" or "export" or "public") { SkipSpace(); word = ReadWord(); }
            if (word.Length > 0)
            {
                SkipSpace();
                if (IsWordAhead("is"))
                {
                    ReadWord();
                    FxNode value = ReadExpression();
                    items.Add(new FxPair(new FxName(word), value));
                    continue;
                }
            }
            at = before;
            items.Add(ReadExpression());
        }
    }

    private FxNumber ReadNumber()
    {
        int start = at;
        if (Peek() == '0' && PeekAt(1) is 'x' or 'X')
        {
            at += 2;
            while (char.IsAsciiHexDigit(Peek())) at++;
            return new FxNumber(long.Parse(text.AsSpan(start + 2, at - start - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        while (char.IsAsciiDigit(Peek()) || Peek() == '.') at++;
        if (Peek() is 'e' or 'E' && (char.IsAsciiDigit(PeekAt(1)) || (PeekAt(1) is '-' or '+' && char.IsAsciiDigit(PeekAt(2)))))
        {
            at += 2;
            while (char.IsAsciiDigit(Peek())) at++;
        }
        string number = text[start..at].TrimEnd('.');
        // a number may carry a type letter, as 1.0f
        if (Peek() is 'f' or 'F' && !char.IsAsciiLetterOrDigit(PeekAt(1))) at++;
        return new FxNumber(double.Parse(number.Length == 0 ? "0" : number, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    // ---- words and spaces ----

    /// <summary>A name: letters, digits and _, or a path such as $/VFX_Bank/Name, ~/Name or EVisionRange/Standard.</summary>
    private string ReadName()
    {
        int start = at;
        if (Peek() is '$' or '~') at++;
        while (at < text.Length)
        {
            char c = text[at];
            if (char.IsAsciiLetterOrDigit(c) || c == '_') at++;
            else if (c == '/' && at + 1 < text.Length && (char.IsAsciiLetter(text[at + 1]) || text[at + 1] == '_') && at > start) at++;
            else if (c == '.' && at + 1 < text.Length && char.IsAsciiLetter(text[at + 1]) && at > start && char.IsAsciiLetter(text[at - 1])) at++;
            else break;
        }
        return text[start..at];
    }

    private string ReadWord()
    {
        int start = at;
        while (at < text.Length && (char.IsAsciiLetterOrDigit(text[at]) || text[at] == '_')) at++;
        if (at == start) throw Problem($"'{Peek()}' where a name was expected");
        return text[start..at];
    }

    private bool IsWordAhead(string word) =>
        string.CompareOrdinal(text, at, word, 0, word.Length) == 0
        && (at + word.Length >= text.Length || !(char.IsAsciiLetterOrDigit(text[at + word.Length]) || text[at + word.Length] == '_'));

    private void ExpectWord(string word)
    {
        SkipSpace();
        if (!IsWordAhead(word)) throw Problem($"'{word}' expected");
        at += word.Length;
    }

    private bool IsTemplateParameterAhead()
    {
        int look = at + 1;
        while (look < text.Length && (char.IsAsciiLetterOrDigit(text[look]) || text[look] == '_')) look++;
        return look > at + 1 && look < text.Length && text[look] == '>';
    }

    private string ReadQuoted()
    {
        char quote = text[at++];
        var value = new StringBuilder();
        while (at < text.Length && text[at] != quote) value.Append(text[at++]);
        at++;
        return value.ToString();
    }

    private void SkipQuoted(char quote)
    {
        while (at < text.Length && text[at] != quote) at++;
        at++;
    }

    private void SkipSpace()
    {
        while (at < text.Length)
        {
            char c = text[at];
            if (char.IsWhiteSpace(c)) at++;
            else if (c == '/' && PeekAt(1) == '/') { while (at < text.Length && text[at] != '\n') at++; }
            else if (c == '/' && PeekAt(1) == '*') { int end = text.IndexOf("*/", at + 2, StringComparison.Ordinal); at = end < 0 ? text.Length : end + 2; }
            else if (c == '(' && PeekAt(1) == '*') { int end = text.IndexOf("*)", at + 2, StringComparison.Ordinal); at = end < 0 ? text.Length : end + 2; }
            else if (c == '{') { int end = text.IndexOf('}', at + 1); at = end < 0 ? text.Length : end + 1; }
            else break;
        }
    }

    /// <summary>Spaces before an object's "(": on the same line or the next, as "ParticleBombSpawner\n(" is written.</summary>
    private void SkipSpaceOnLine() => SkipSpace();

    private char Peek() => at < text.Length ? text[at] : '\0';

    private char PeekAt(int ahead) => at + ahead < text.Length ? text[at + ahead] : '\0';

    private void Expect(char c)
    {
        SkipSpace();
        if (Peek() != c) throw Problem($"'{c}' expected, '{Peek()}' found");
        at++;
    }

    private FormatException Problem(string what)
    {
        int line = 1;
        for (int i = 0; i < Math.Min(at, text.Length); i++) if (text[i] == '\n') line++;
        return new FormatException($"line {line}: {what}");
    }
}
