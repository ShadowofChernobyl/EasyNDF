namespace EasyNdf.Engine.Editing;

/// <summary>
/// Where a place in NDF text is, for the File Editor's tips and suggestions: in an object of which type, and on a name
/// ("AimingTime" in "AimingTime = 3.0") or in the value given to one. Found without reading the whole text: from the
/// start of the declaration the place is in, the text is scanned to the end of the object around the place.
/// </summary>
public sealed class NdfPlace
{
    /// <summary>The furthest the text is scanned, so a place in a huge declaration gives no tip rather than a wait.</summary>
    private const int MostScanned = 8_000_000;

    /// <summary>The type of the object the place is in ("TAmmunitionDescriptor", or a template's name); "" when not in an object.</summary>
    public string ObjectType { get; private init; } = "";

    /// <summary>On a name, that name; in a value, the name it is given to; "" for neither.</summary>
    public string Name { get; private init; } = "";

    /// <summary>True in the value after "Name =" (on the same line); false on a name, or where a name would be typed.</summary>
    public bool IsInValue { get; private init; }

    /// <summary>True on a name that "=" follows; false in a value, or on a word being typed where a name goes.</summary>
    public bool IsName { get; private init; }

    /// <summary>Where the word at the place starts: the name, or the value written so far.</summary>
    public int WordStart { get; private init; }

    /// <summary>Where the word at the place ends.</summary>
    public int WordEnd { get; private init; }

    /// <summary>The value given to <see cref="Name"/> on this line when it is a single word, number or text; "" otherwise.</summary>
    public string ValueText { get; private init; } = "";

    /// <summary>The names set in the object, each with where its value starts the first time.</summary>
    public IReadOnlyDictionary<string, int> NamesSet { get; private init; } = new Dictionary<string, int>();

    /// <summary>Where a place in a text is; null when it is not inside an object, or the declaration is too big to scan.</summary>
    public static NdfPlace? At(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        int start = DeclarationStartBefore(text, offset);
        if (start < 0) return null;
        return new Scan(text, start, offset).Run();
    }

    /// <summary>The start of the line that starts the declaration a place is in; -1 when it is further back than is scanned.</summary>
    private static int DeclarationStartBefore(string text, int offset)
    {
        int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        while (lineStart > 0)
        {
            if (offset - lineStart > MostScanned) return -1;
            if (StartsDeclaration(text, lineStart)) return lineStart;
            lineStart = text.LastIndexOf('\n', lineStart - 2) + 1;
        }
        return 0;
    }

    /// <summary>Whether the line at a place starts a declaration ("Name is", "export Name is", "unnamed"…).</summary>
    private static bool StartsDeclaration(string text, int lineStart)
    {
        // most lines are indented or blank: only one starting with a letter can start a declaration
        if (lineStart >= text.Length || !(char.IsAsciiLetter(text[lineStart]) || text[lineStart] == '_')) return false;
        int lineEnd = text.IndexOf('\n', lineStart);
        string line = text.Substring(lineStart, Math.Min((lineEnd < 0 ? text.Length : lineEnd) - lineStart, 200));
        return NdfChecker.StartsDeclaration(line);
    }

    // ---- the scan ----

    /// <summary>One object (or list, or brackets) open where the scan is: its type, and the names set in it so far.</summary>
    private sealed class Frame(string type)
    {
        public string Type { get; } = type;
        public Dictionary<string, int> NamesSet { get; } = new(StringComparer.Ordinal);

        /// <summary>The name whose "=" was the last one met in this frame, and where that "=" is.</summary>
        public string LastName = "";
        public int LastEquals = -1;
    }

    /// <summary>The kinds of pieces of text the scan tells apart.</summary>
    private enum PieceKind { Word, Number, Text, Open, Close, Equals, Other }

    private readonly record struct Piece(PieceKind Kind, int Start, int End);

    /// <summary>A scan from a declaration's start to the end of the object around a place.</summary>
    private sealed class Scan
    {
        private readonly string text;
        private readonly int start, offset;
        private readonly Stack<Frame> frames = new();

        /// <summary>Where the scan has read to.</summary>
        private int at;

        /// <summary>A scan of <paramref name="text"/> from a declaration's start, for the place at <paramref name="offset"/>.</summary>
        public Scan(string text, int start, int offset)
        {
            this.text = text;
            this.start = start;
            this.offset = offset;
            at = start;
        }

        public NdfPlace? Run()
        {
            Piece previous = new(PieceKind.Other, start, start);
            Frame? around = null;
            int depthAround = 0;
            NdfPlace? found = null;
            while (NextPiece() is Piece piece)
            {
                if (piece.Start - start > MostScanned) return null;
                // the place is reached: the first piece past it, or the word it is at the end of (a word being typed)
                bool isWordLike = piece.Kind is PieceKind.Word or PieceKind.Number or PieceKind.Text;
                if (around is null && (piece.End > offset || (piece.End == offset && isWordLike)))
                {
                    // between pieces, nothing is typed there yet
                    Piece atPlace = piece.Start <= offset && isWordLike ? piece : new Piece(PieceKind.Other, offset, offset);
                    if (frames.Count == 0 || frames.Peek().Type == "") return null;
                    around = frames.Peek();
                    depthAround = frames.Count;
                    found = Describe(around, atPlace);
                }
                Take(previous, piece);
                if (around is not null && frames.Count < depthAround) break; // the object around the place has closed
                previous = piece;
            }
            if (found is null && around is null && frames.Count > 0 && frames.Peek().Type != "")
            {
                // the text ends at the place, inside an object
                around = frames.Peek();
                found = Describe(around, new Piece(PieceKind.Other, offset, offset));
            }
            return found is null || around is null ? null : found.WithNamesSet(around.NamesSet);
        }

        /// <summary>What is at the place: a name, a value, or the start of a name being typed.</summary>
        private NdfPlace Describe(Frame frame, Piece piece)
        {
            int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
            bool equalsOnThisLine = frame.LastEquals >= lineStart && frame.LastEquals < offset;
            int wordStart = piece.Kind == PieceKind.Other ? offset : piece.Start;
            int wordEnd = piece.Kind == PieceKind.Other ? offset : piece.End;
            // a word followed by "=" is a name, even after another name's value on the same line (Family=X Index=24)
            int afterWord = SkipSpaces(wordEnd);
            bool isName = piece.Kind == PieceKind.Word && afterWord < text.Length && text[afterWord] == '=' && (afterWord + 1 >= text.Length || text[afterWord + 1] != '=');
            if (isName)
                return new NdfPlace { ObjectType = frame.Type, Name = text[wordStart..wordEnd], IsName = true, WordStart = wordStart, WordEnd = wordEnd, ValueText = SingleValueAfter(afterWord) };
            if (equalsOnThisLine && wordStart > frame.LastEquals)
                return new NdfPlace { ObjectType = frame.Type, Name = frame.LastName, IsInValue = true, WordStart = ValueStart(frame.LastEquals + 1, wordStart), WordEnd = wordEnd, ValueText = SingleValueAfter(frame.LastEquals) };
            // where a name goes: a word being typed, or nothing yet
            string typed = piece.Kind == PieceKind.Word ? text[wordStart..wordEnd] : "";
            return new NdfPlace { ObjectType = frame.Type, Name = typed, WordStart = wordStart, WordEnd = wordEnd };
        }

        /// <summary>Where the value being typed starts: after "=" and its spaces, or at the word when it starts later.</summary>
        private int ValueStart(int afterEquals, int wordStart) => Math.Min(SkipSpaces(afterEquals), wordStart);

        /// <summary>
        /// The value after an "=" when it is one piece (a word, number or text) ending the line or followed by ")", ","
        /// or another name; "" for a list, an object, a calculation or nothing.
        /// </summary>
        private string SingleValueAfter(int equals)
        {
            int valueStart = SkipSpaces(equals + 1);
            Piece? value = PieceAt(valueStart);
            if (value is not { Kind: PieceKind.Word or PieceKind.Number or PieceKind.Text } piece) return "";
            int next = SkipSpaces(piece.End);
            bool endsHere = next >= text.Length || text[next] is '\n' or '\r' or ')' or ',' || text.AsSpan(next).StartsWith("//") || NameFollows(next);
            return endsHere ? text[piece.Start..piece.End] : "";
        }

        /// <summary>Whether a name and its "=" start at a place.</summary>
        private bool NameFollows(int place)
        {
            if (PieceAt(place) is not { Kind: PieceKind.Word } word) return false;
            int after = SkipSpaces(word.End);
            return after < text.Length && text[after] == '=';
        }

        private int SkipSpaces(int place)
        {
            while (place < text.Length && text[place] is ' ' or '\t') place++;
            return place;
        }

        /// <summary>Follows the brackets and notes each name set, with where its value starts.</summary>
        private void Take(Piece previous, Piece piece)
        {
            switch (piece.Kind)
            {
                case PieceKind.Open:
                    bool typed = text[piece.Start] == '(' && previous.Kind == PieceKind.Word && IsTypeName(text[previous.Start..previous.End]);
                    frames.Push(new Frame(typed ? text[previous.Start..previous.End] : ""));
                    break;
                case PieceKind.Close:
                    if (frames.Count > 0) frames.Pop();
                    break;
                case PieceKind.Equals when previous.Kind == PieceKind.Word && frames.Count > 0:
                    Frame frame = frames.Peek();
                    string name = text[previous.Start..previous.End];
                    frame.NamesSet.TryAdd(name, SkipSpaces(piece.End));
                    frame.LastName = name;
                    frame.LastEquals = piece.Start;
                    break;
            }
        }

        /// <summary>Whether a word before "(" names a type: a plain name, not a reference or a choice.</summary>
        private static bool IsTypeName(string word) =>
            word.Length > 0 && (char.IsAsciiLetter(word[0]) || word[0] == '_') && !word.Contains('/', StringComparison.Ordinal) && word != "is";

        // ---- reading pieces ----

        /// <summary>The piece starting at a place, without moving the scan.</summary>
        private Piece? PieceAt(int place)
        {
            int saved = at;
            at = place;
            Piece? piece = NextPiece(skipComments: false);
            at = saved;
            return piece is { } found && found.Start == place ? found : null;
        }

        /// <summary>The next piece of text, past spaces and comments; null at the end of the text.</summary>
        private Piece? NextPiece(bool skipComments = true)
        {
            while (at < text.Length)
            {
                char c = text[at];
                if (char.IsWhiteSpace(c)) { at++; continue; }
                if (skipComments && SkipComment()) continue;
                int pieceStart = at;
                if (c is '(' or '[') { at++; return new Piece(PieceKind.Open, pieceStart, at); }
                if (c is ')' or ']') { at++; return new Piece(PieceKind.Close, pieceStart, at); }
                if (c == '=')
                {
                    at++;
                    if (at < text.Length && text[at] == '=') { at++; return new Piece(PieceKind.Other, pieceStart, at); }
                    return new Piece(PieceKind.Equals, pieceStart, at);
                }
                if (c is '\'' or '"') return ReadText(c);
                if (text.AsSpan(at).StartsWith("GUID:{", StringComparison.Ordinal)) return ReadUntil('}', PieceKind.Word);
                if (char.IsAsciiDigit(c) || (c is '-' or '.' && at + 1 < text.Length && char.IsAsciiDigit(text[at + 1]))) return ReadNumber();
                if (IsWordStart(c)) return ReadWord();
                at++;
                return new Piece(PieceKind.Other, pieceStart, at);
            }
            return null;
        }

        /// <summary>Skips a comment at the scan's place: // to the line's end, /* */, (* *) or { }.</summary>
        private bool SkipComment()
        {
            ReadOnlySpan<char> rest = text.AsSpan(at);
            if (rest.StartsWith("//")) { at = LineEnd(at); return true; }
            if (rest.StartsWith("/*")) { at = After("*/", at + 2); return true; }
            if (rest.StartsWith("(*")) { at = After("*)", at + 2); return true; }
            if (rest.StartsWith("{")) { at = After("}", at + 1); return true; }
            return false;
        }

        private int LineEnd(int place)
        {
            int end = text.IndexOf('\n', place);
            return end < 0 ? text.Length : end;
        }

        private int After(string end, int place)
        {
            int found = text.IndexOf(end, place, StringComparison.Ordinal);
            return found < 0 ? text.Length : found + end.Length;
        }

        /// <summary>A quoted text, to its closing quote on the same line.</summary>
        private Piece ReadText(char quote)
        {
            int pieceStart = at++;
            while (at < text.Length && text[at] != quote && text[at] != '\n') at++;
            if (at < text.Length && text[at] == quote) at++;
            return new Piece(PieceKind.Text, pieceStart, at);
        }

        private Piece ReadUntil(char end, PieceKind kind)
        {
            int pieceStart = at;
            int found = text.IndexOf(end, at);
            at = found < 0 ? text.Length : found + 1;
            return new Piece(kind, pieceStart, at);
        }

        private Piece ReadNumber()
        {
            int pieceStart = at++;
            while (at < text.Length && (char.IsAsciiLetterOrDigit(text[at]) || text[at] == '.' || (text[at] is '-' or '+' && text[at - 1] is 'e' or 'E'))) at++;
            return new Piece(PieceKind.Number, pieceStart, at);
        }

        /// <summary>A word: a name, a choice (EProjectileType/Obus), a reference (~/Name, $/A/B) or a parameter (&lt;Name&gt;).</summary>
        private Piece ReadWord()
        {
            int pieceStart = at++;
            while (at < text.Length && (char.IsAsciiLetterOrDigit(text[at]) || text[at] is '_' or '/' or '$' or '~' or '.' or '<' or '>')) at++;
            return new Piece(PieceKind.Word, pieceStart, at);
        }

        private static bool IsWordStart(char c) => char.IsAsciiLetter(c) || c is '_' or '$' or '~' or '<';
    }

    /// <summary>The same place in a larger text, where the text it was found in starts at <paramref name="shift"/>.</summary>
    public NdfPlace ShiftedBy(int shift) => new()
    {
        ObjectType = ObjectType, Name = Name, IsInValue = IsInValue, IsName = IsName, WordStart = WordStart + shift, WordEnd = WordEnd + shift, ValueText = ValueText,
        NamesSet = NamesSet.ToDictionary(pair => pair.Key, pair => pair.Value + shift, StringComparer.Ordinal),
    };

    /// <summary>The same place with the names set in its object, once the scan has read the whole object.</summary>
    private NdfPlace WithNamesSet(IReadOnlyDictionary<string, int> names) => new()
    {
        ObjectType = ObjectType, Name = Name, IsInValue = IsInValue, IsName = IsName, WordStart = WordStart, WordEnd = WordEnd, ValueText = ValueText, NamesSet = names,
    };
}
