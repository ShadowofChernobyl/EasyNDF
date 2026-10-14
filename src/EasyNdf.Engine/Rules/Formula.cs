using System.Globalization;
using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Rules;

/// <summary>
/// The Formula rule's expressions: numbers, property names, [names/with/slashes], + - * /, brackets, and the functions
/// min, max, abs, floor, ceil and round. Nothing is evaluated as code.
/// </summary>
public sealed class Formula
{
    /// <summary>Looks up a name's value; throws <see cref="MissingValueException"/> when it has none.</summary>
    public delegate double Lookup(string name);

    /// <summary>Thrown by a lookup when a name has no numeric value; the descriptor is then skipped.</summary>
    public sealed class MissingValueException : Exception;

    private readonly Func<Lookup, double> evaluate;

    /// <summary>The names the formula reads, in the order they first appear.</summary>
    public IReadOnlyList<string> Names { get; }

    private Formula(Func<Lookup, double> evaluate, List<string> names)
    {
        this.evaluate = evaluate;
        Names = names;
    }

    /// <summary>Works the formula out.</summary>
    public double Evaluate(Lookup lookup) => evaluate(lookup);

    private abstract record Token;
    private sealed record NumberToken(double Value) : Token;
    private sealed record NameToken(string Name, bool InBrackets) : Token;
    private sealed record SymbolToken(char Symbol) : Token;

    /// <summary>Reads a formula. Throws <see cref="FormatException"/> with a message for the user when it cannot be read.</summary>
    public static Formula Compile(string? source) => new Compiler(Tokenize(source ?? "")).Compile();

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < source.Length)
        {
            if (JsText.Trim(source[i..]) == "") break;
            int start = i;
            while (i < source.Length && JsText.IsWhiteSpace(source[i])) i++;
            char c = source[i];

            if (char.IsAsciiDigit(c))
            {
                int s = i;
                while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                if (i + 1 < source.Length && source[i] == '.' && char.IsAsciiDigit(source[i + 1]))
                {
                    i++;
                    while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                }
                tokens.Add(new NumberToken(double.Parse(source[s..i], CultureInfo.InvariantCulture)));
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                int s = i;
                while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(new NameToken(source[s..i], InBrackets: false));
            }
            else if (c == '[' && source.IndexOf(']', i + 1) is int close && close > i + 1)
            {
                tokens.Add(new NameToken(JsText.Trim(source[(i + 1)..close]), InBrackets: true));
                i = close + 1;
            }
            else if ("-+*/(),".Contains(c))
            {
                tokens.Add(new SymbolToken(c));
                i++;
            }
            else
            {
                string rest = JsText.Trim(source[start..]);
                throw new FormatException("The formula cannot be read at \"" + rest[..Math.Min(12, rest.Length)] + "\".");
            }
        }
        if (tokens.Count == 0) throw new FormatException("The formula is empty.");
        return tokens;
    }

    /// <summary>
    /// Turns the tokens into a function, with the usual order: brackets and functions, then * and /, then + and -.
    /// </summary>
    private sealed class Compiler(List<Token> tokens)
    {
        private int i;
        private readonly List<string> names = [];

        private bool IsSymbol(char c) => i < tokens.Count && tokens[i] is SymbolToken s && s.Symbol == c;

        public Formula Compile()
        {
            var e = Expression();
            if (i < tokens.Count) throw new FormatException("Unexpected text at the end of the formula.");
            return new Formula(e, names);
        }

        private Func<Lookup, double> Expression()
        {
            var left = Term();
            while (IsSymbol('+') || IsSymbol('-'))
            {
                char op = ((SymbolToken)tokens[i++]).Symbol;
                var right = Term();
                var a = left;
                left = op == '+' ? env => a(env) + right(env) : env => a(env) - right(env);
            }
            return left;
        }

        private Func<Lookup, double> Term()
        {
            var left = Primary();
            while (IsSymbol('*') || IsSymbol('/'))
            {
                char op = ((SymbolToken)tokens[i++]).Symbol;
                var right = Primary();
                var a = left;
                left = op == '*' ? env => a(env) * right(env) : env => a(env) / right(env);
            }
            return left;
        }

        private Func<Lookup, double> Primary()
        {
            if (i >= tokens.Count) throw new FormatException("The formula ends too early.");
            Token token = tokens[i];

            if (token is NumberToken number)
            {
                i++;
                double v = number.Value;
                return _ => v;
            }
            if (token is NameToken nameToken)
            {
                i++;
                if (!nameToken.InBrackets && IsSymbol('(')) return FunctionCall(nameToken.Name);
                string name = nameToken.Name;
                if (!names.Contains(name)) names.Add(name);
                return env => env(name);
            }
            if (IsSymbol('('))
            {
                i++;
                var inner = Expression();
                if (!IsSymbol(')')) throw new FormatException("A \")\" is missing in the formula.");
                i++;
                return inner;
            }
            if (IsSymbol('-'))
            {
                i++;
                var inner = Primary();
                return env => -inner(env);
            }
            throw new FormatException("Unexpected \"" + ((SymbolToken)token).Symbol + "\" in the formula.");
        }

        private Func<Lookup, double> FunctionCall(string name)
        {
            Func<double[], double>? function = name.ToLowerInvariant() switch
            {
                "min" => a => a.Length == 0 ? double.PositiveInfinity : a.Aggregate(double.PositiveInfinity, JsMin),
                "max" => a => a.Length == 0 ? double.NegativeInfinity : a.Aggregate(double.NegativeInfinity, JsMax),
                "abs" => a => Math.Abs(Arg(a, 0)),
                "floor" => a => Math.Floor(Arg(a, 0)),
                "ceil" => a => Math.Ceiling(Arg(a, 0)),
                "round" => a => a.Length > 1 && a[1] != 0 && !double.IsNaN(a[1])
                    ? JsNumber.Round(Arg(a, 0) / a[1]) * a[1]
                    : JsNumber.Round(Arg(a, 0)),
                _ => null,
            };
            if (function is null) throw new FormatException("\"" + name + "\" is not a known function.");

            i++; // "("
            var arguments = new List<Func<Lookup, double>>();
            if (!IsSymbol(')'))
            {
                arguments.Add(Expression());
                while (IsSymbol(','))
                {
                    i++;
                    arguments.Add(Expression());
                }
            }
            if (!IsSymbol(')')) throw new FormatException("A \")\" is missing in the formula.");
            i++;
            return env => function(arguments.Select(a => a(env)).ToArray());
        }

        /// <summary>A missing argument is undefined in JavaScript, which calculates as NaN.</summary>
        private static double Arg(double[] args, int index) => index < args.Length ? args[index] : double.NaN;

        /// <summary>Math.min in JavaScript: NaN wins, and -0 counts as smaller than 0.</summary>
        private static double JsMin(double a, double b) =>
            double.IsNaN(a) || double.IsNaN(b) ? double.NaN : (a == b && a == 0 ? (double.IsNegative(a) ? a : b) : Math.Min(a, b));

        /// <summary>Math.max in JavaScript: NaN wins, and 0 counts as larger than -0.</summary>
        private static double JsMax(double a, double b) =>
            double.IsNaN(a) || double.IsNaN(b) ? double.NaN : (a == b && a == 0 ? (double.IsNegative(a) ? b : a) : Math.Max(a, b));
    }
}
