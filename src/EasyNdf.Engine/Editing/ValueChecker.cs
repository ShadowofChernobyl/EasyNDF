using System.Globalization;
using EasyNdf.Engine.Ndf;

namespace EasyNdf.Engine.Editing;

/// <summary>What is wrong with a value the File Editor marks.</summary>
public enum ValueProblemKind
{
    /// <summary>A value of the wrong kind: 1.5 in an int, 1 in a boolean, text in a float.</summary>
    WrongKind,

    /// <summary>A choice its list doesn't have: EProjectileType/NotAChoice.</summary>
    NoSuchChoice,

    /// <summary>A number outside a limit that stops mod generation.</summary>
    OutsideLimit,

    /// <summary>A name set a second time in the same object.</summary>
    WrittenTwice,

    /// <summary>A number on the wrong side of another value of the same object: a minimum range above the maximum.</summary>
    BrokenComparison,

    /// <summary>A warning, not an error: a negative number in a whole number of 0 or more, which becomes a very large one.</summary>
    NegativeInZeroOrMore,

    /// <summary>A weapon set (TWeaponManagerModuleDescriptor) whose Salves holds no ammo box. WARNO v210942 on refuses it.</summary>
    NoAmmoBox,

    /// <summary>An ammo box of Salves with 0 salvos or fewer. WARNO v210942 on refuses it.</summary>
    EmptyAmmoBox,

    /// <summary>
    /// A veterancy level of NumberOfUnitInPackXPMultiplier that is offered but gives the card no unit: NumberOfUnitInPack
    /// times it rounds to 0. WARNO v210942 on refuses it; 0 leaves the level out.
    /// </summary>
    EmptyPack,

    /// <summary>
    /// A mounted weapon (TMountedWeaponDescriptor) with no Name, in a file whose mounted weapons carry their names: from
    /// WARNO v210942 on a weapon's name is its mounted weapon's, not its ammunition's, and the game refuses one without.
    /// </summary>
    MountWithoutName,

    /// <summary>
    /// An ammunition's ImpactHappening that MimeticImpactMapping.ndf does not map, letter case and all: mod generation
    /// works, but the game crashes when the ammunition is fired ("requested and missing in Mimetic World Descriptor").
    /// </summary>
    NoSuchImpact,

    /// <summary>
    /// A value mod generation can't do without, left out or set to nil ("Property 'LobbyFullSound' must be defined"), as
    /// a test confirmed; for some, only while another value of the object is True.
    /// </summary>
    MissingValue,

    /// <summary>
    /// An object of an older or other form of a class the game's files use, such as TUIResourceTexture_Common, which
    /// WARNO renamed TUIResourceTexture: mod generation stops on it ("Unknown class"). Only Export's check finds it, from
    /// the classes of the game's own files; Value is the game's class.
    /// </summary>
    UnknownClass,
}

/// <summary>A value the File Editor marks, and what is wrong with it.</summary>
/// <param name="Position">Where the value starts in the text.</param>
/// <param name="Length">How many characters the value takes.</param>
/// <param name="Kind">What is wrong.</param>
/// <param name="Name">The name the value is given to: "ShotsCountPerSalvo".</param>
/// <param name="Value">The value as written: "1.5".</param>
public sealed record ValueProblem(int Position, int Length, ValueProblemKind Kind, string Name, string Value)
{
    /// <summary>For <see cref="ValueProblemKind.WrongKind"/>: the kind the name takes, "int", "float" or "boolean".</summary>
    public string Expected { get; init; } = "";

    /// <summary>For <see cref="ValueProblemKind.WrongKind"/>: the kind the value is, "int", "float", "string" or "boolean".</summary>
    public string Found { get; init; } = "";

    /// <summary>For <see cref="ValueProblemKind.NoSuchChoice"/>: the choices it has, as written.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>For <see cref="ValueProblemKind.OutsideLimit"/>: what the value must be, in words.</summary>
    public IReadOnlyList<string> Rules { get; init; } = [];

    /// <summary>For <see cref="ValueProblemKind.WrittenTwice"/>: where the name's first value starts.</summary>
    public int FirstPosition { get; init; }

    /// <summary>For <see cref="ValueProblemKind.BrokenComparison"/>: the comparison broken, and the other value as written.</summary>
    public ValueComparison? Comparison { get; init; }

    /// <summary>For <see cref="ValueProblemKind.BrokenComparison"/>: the other value as written: "1925". For <see cref="ValueProblemKind.EmptyPack"/>: NumberOfUnitInPack.</summary>
    public string OtherValue { get; init; } = "";

    /// <summary>For <see cref="ValueProblemKind.EmptyAmmoBox"/> and <see cref="ValueProblemKind.EmptyPack"/>: the place in the array, from 0.</summary>
    public int Index { get; init; }

    /// <summary>Whether this is a warning (mod generation works) rather than an error (it fails).</summary>
    public bool IsWarning => Kind == ValueProblemKind.NegativeInZeroOrMore;

    /// <summary>Whether this error lets mod generation work but crashes the game in play.</summary>
    public bool CrashesGame => Kind == ValueProblemKind.NoSuchImpact;
}

/// <summary>
/// The File Editor's value check: in every object of a type the <see cref="ValueGuide"/> knows, each value whose type
/// is certain is checked against its kind (an int, a float, a boolean), its choices (when the list is complete), and
/// the limits tests showed stop mod generation; a name set twice in one object is marked too, and, given the names
/// MimeticImpactMapping.ndf maps, an ammunition's ImpactHappening it doesn't. References, constants and calculations are
/// never marked, as they can't be judged from the text.
/// </summary>
public static partial class ValueChecker
{
    /// <summary>The most problems listed, as for the syntax check.</summary>
    public const int MostProblems = NdfChecker.MostProblems * 10;

    /// <summary>The problems in a read text, in the order of the text; <paramref name="shift"/> is added to every position.</summary>
    /// <param name="mountsNamed">
    /// Whether the file the text is from gives its mounted weapons their names (<see cref="NamesMountedWeapons"/>), so a
    /// mounted weapon without one is marked; an older file's have none, which is right for it.
    /// </param>
    /// <param name="impacts">The impact names the file's MimeticImpactMapping.ndf maps; null checks no ImpactHappening.</param>
    public static List<ValueProblem> Check(NdfDocument document, ValueGuide guide, int shift = 0, bool mountsNamed = false, IReadOnlySet<string>? impacts = null)
    {
        var problems = new List<ValueProblem>();
        var context = new CheckContext(guide, shift, mountsNamed, impacts);
        foreach (Declaration declaration in document.Declarations)
        {
            CheckValue(declaration.Value, context, problems);
            if (problems.Count >= MostProblems) break;
        }
        problems.Sort((a, b) => a.Position.CompareTo(b.Position));
        return problems;
    }

    /// <summary>
    /// Whether a text's mounted weapons (TMountedWeaponDescriptor) carry their names, as WARNO's files do from v210942 on:
    /// one of the first few has a Name. Before v210942 the names were the ammunition's, and no mounted weapon had one.
    /// </summary>
    public static bool NamesMountedWeapons(string text)
    {
        int looked = 0;
        for (int at = text.IndexOf("TMountedWeaponDescriptor", StringComparison.Ordinal); at >= 0 && looked < 20;
             at = text.IndexOf("TMountedWeaponDescriptor", at + 1, StringComparison.Ordinal), looked++)
        {
            int next = text.IndexOf("TMountedWeaponDescriptor", at + 1, StringComparison.Ordinal);
            int end = next < 0 ? Math.Min(text.Length, at + 3000) : Math.Min(next, at + 3000);
            if (MountName().IsMatch(text.AsSpan(at, end - at))) return true;
        }
        return false;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\n\s*Name\s*=")]
    private static partial System.Text.RegularExpressions.Regex MountName();

    /// <summary>
    /// Checks a text one declaration at a time, so a large file never has all its values read at once; a declaration
    /// with a syntax problem is left to the syntax check. <paramref name="shift"/> is added to every position, for a
    /// stretch of a larger text.
    /// </summary>
    /// <param name="impacts">The impact names the file's MimeticImpactMapping.ndf maps; null checks no ImpactHappening.</param>
    public static List<ValueProblem> Check(string text, ValueGuide guide, int shift = 0, IReadOnlySet<string>? impacts = null)
    {
        var problems = new List<ValueProblem>();
        bool mountsNamed = NamesMountedWeapons(text);
        foreach ((int start, int end) in NdfChecker.DeclarationStretches(text))
        {
            try { problems.AddRange(Check(Parser.Parse(text[start..end]), guide, start + shift, mountsNamed, impacts)); }
            catch (NdfParseException) { }
            if (problems.Count >= MostProblems) break;
        }
        return problems;
    }

    /// <summary>
    /// What is wrong with one value given to a name in a type, as <see cref="Check(NdfDocument, ValueGuide, int, bool)"/>
    /// would mark it (a name set twice aside); null when nothing is, or when it can't be judged. Suggestions use it so
    /// they offer nothing the check would mark.
    /// </summary>
    public static ValueProblem? ProblemWith(string type, string name, string value, ValueGuide guide)
    {
        if (guide.Find(type, name) is not { IsTypeCertain: true } info) return null;
        return ProblemWith(info, name, ScalarOf(value.Trim()), 0, value.Trim().Length);
    }

    // ---- walking the values ----

    /// <summary>What a check goes by, the same for every value of a text.</summary>
    /// <param name="Guide">The values' types and limits.</param>
    /// <param name="Shift">What is added to every position.</param>
    /// <param name="MountsNamed">Whether the file gives its mounted weapons their names (<see cref="NamesMountedWeapons"/>).</param>
    /// <param name="Impacts">The impact names the file's MimeticImpactMapping.ndf maps; null checks no ImpactHappening.</param>
    private sealed record CheckContext(ValueGuide Guide, int Shift, bool MountsNamed, IReadOnlySet<string>? Impacts);

    /// <summary>Checks a value and every value inside it.</summary>
    private static void CheckValue(NdfValue value, CheckContext context, List<ValueProblem> problems)
    {
        switch (value)
        {
            case ObjectValue item:
                CheckObject(item, context, problems);
                break;
            case ListValue list:
                foreach (NdfValue entry in list.Items) CheckValue(entry, context, problems);
                break;
            case PairValue pair:
                CheckValue(pair.First, context, problems);
                CheckValue(pair.Second, context, problems);
                break;
            case AssignValue assign:
                CheckValue(assign.Value, context, problems);
                break;
        }
    }

    /// <summary>
    /// Checks an object's values against its type's names, then the objects inside them. A name set twice is marked in
    /// any object, as mod generation fails on it whatever the type.
    /// </summary>
    private static void CheckObject(ObjectValue item, CheckContext context, List<ValueProblem> problems)
    {
        (ValueGuide guide, int shift) = (context.Guide, context.Shift);
        var firstSet = new Dictionary<string, NdfValue>(StringComparer.Ordinal);
        var marked = new HashSet<NdfValue>();
        foreach (Property property in item.Properties)
        {
            if (property.IsMemberDeclaration) continue;
            NdfValue value = property.Value;
            if (value.Start is int start && value.End is int end)
            {
                ValueProblem? problem = null;
                if (firstSet.TryGetValue(property.Name, out NdfValue? first) && first.Start is int firstStart)
                    problem = new ValueProblem(start + shift, end - start, ValueProblemKind.WrittenTwice, property.Name, "") { FirstPosition = firstStart + shift };
                else if (value is ScalarValue scalar && guide.Find(item.TypeName, property.Name) is { IsTypeCertain: true } info)
                    problem = ProblemWith(info, property.Name, scalar, start + shift, end - start);
                if (problem is null && IsUnmappedImpact(item.TypeName, property.Name, value, context.Impacts))
                    problem = new ValueProblem(start + shift, end - start, ValueProblemKind.NoSuchImpact, property.Name, ((ScalarValue)value).Text);
                if (problem is not null)
                {
                    problems.Add(problem);
                    marked.Add(value);
                }
            }
            firstSet.TryAdd(property.Name, value);
            CheckValue(value, context, problems);
        }
        CheckComparisons(item.TypeName, firstSet, marked, guide, shift, problems);
        CheckArrays(item.TypeName, firstSet, guide, shift, problems);
        // WARNO v210942 on: a mounted weapon carries its weapon's name, and the game refuses one without ("Property
        // 'Name' must be defined"); marked on its type name, where its other values were written in the older way
        if (context.MountsNamed && item.TypeName == "TMountedWeaponDescriptor" && !firstSet.ContainsKey("Name") && item.Start is int at)
            problems.Add(new ValueProblem(at + shift, Math.Max(1, item.SourceTypeNameLength), ValueProblemKind.MountWithoutName, "Name", ""));
        CheckRequired(item, firstSet, guide, shift, problems);
    }

    /// <summary>
    /// The values of an object mod generation can't do without (<see cref="ValueInfo.IsRequired"/>): one left out is
    /// marked on the object's type name, as there is no value to underline; one set to nil, on the nil. A value
    /// required only while another is True (<see cref="ValueInfo.RequiredWhen"/>) is checked only when that other is
    /// written True.
    /// </summary>
    private static void CheckRequired(ObjectValue item, Dictionary<string, NdfValue> firstSet, ValueGuide guide, int shift, List<ValueProblem> problems)
    {
        foreach (string name in guide.RequiredNamesOf(item.TypeName))
        {
            if (guide.Find(item.TypeName, name)?.RequiredWhen is { } flag
                && !(firstSet.TryGetValue(flag, out NdfValue? on) && on is ScalarValue onText && onText.Text.Equals("True", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!firstSet.TryGetValue(name, out NdfValue? value))
            {
                if (item.Start is int at)
                    problems.Add(new ValueProblem(at + shift, Math.Max(1, item.SourceTypeNameLength), ValueProblemKind.MissingValue, name, ""));
            }
            else if (value is ScalarValue { Text: "nil" } && value.Start is int start && value.End is int end)
                problems.Add(new ValueProblem(start + shift, end - start, ValueProblemKind.MissingValue, name, "nil"));
        }
    }

    /// <summary>
    /// Whether a value is an ammunition's ImpactHappening, written as a text, that the impact names don't have. The game
    /// looks the name up letter case and all: 'MIssileAASIDEWINDER' is not 'MissileAASIDEWINDER'.
    /// </summary>
    private static bool IsUnmappedImpact(string type, string name, NdfValue value, IReadOnlySet<string>? impacts) =>
        impacts is { Count: > 0 } && type == "TAmmunitionDescriptor" && name == "ImpactHappening"
        && value is ScalarValue { Kind: ScalarKind.String } text && text.Text.Length >= 2 && !impacts.Contains(text.Text[1..^1]);

    /// <summary>
    /// WARNO v210942's checks of arrays at mod generation, which no single value's limit can say (the game's messages:
    /// "At least one salvo must be defined", "Salves elements must be > 0", "XP multiplier ... leads to an empty pack"):
    /// a weapon set's Salves holds at least one ammo box, each with salvos above 0; and each veterancy level of
    /// NumberOfUnitInPackXPMultiplier is 0 (not offered) or gives the card at least one unit, NumberOfUnitInPack times it
    /// rounded to the nearest whole number (halves away from 0). Only for a type the guide knows the array of.
    /// </summary>
    private static void CheckArrays(string type, Dictionary<string, NdfValue> firstSet, ValueGuide guide, int shift, List<ValueProblem> problems)
    {
        if (firstSet.TryGetValue("Salves", out NdfValue? salves) && salves is ListValue boxes && guide.Find(type, "Salves") is not null
            && type == "TWeaponManagerModuleDescriptor" && salves.Start is int start && salves.End is int end)
        {
            if (boxes.Items.Count == 0) problems.Add(new ValueProblem(start + shift, end - start, ValueProblemKind.NoAmmoBox, "Salves", "[]"));
            for (int index = 0; index < boxes.Items.Count; index++)
                if (boxes.Items[index] is ScalarValue { Kind: ScalarKind.Number } box && TryReadNumber(box.Text, out double salvos) && salvos <= 0
                    && box.Start is int boxStart && box.End is int boxEnd)
                    problems.Add(new ValueProblem(boxStart + shift, boxEnd - boxStart, ValueProblemKind.EmptyAmmoBox, "Salves", box.Text) { Index = index });
        }
        if (firstSet.TryGetValue("NumberOfUnitInPackXPMultiplier", out NdfValue? multipliers) && multipliers is ListValue levels
            && guide.Find(type, "NumberOfUnitInPackXPMultiplier") is not null
            && firstSet.TryGetValue("NumberOfUnitInPack", out NdfValue? pack) && pack is ScalarValue { Kind: ScalarKind.Number } units
            && TryReadNumber(units.Text, out double perPack))
        {
            for (int index = 0; index < levels.Items.Count; index++)
                if (levels.Items[index] is ScalarValue { Kind: ScalarKind.Number } level && TryReadNumber(level.Text, out double share)
                    && share != 0 && Math.Round(perPack * share, MidpointRounding.AwayFromZero) == 0 && level.Start is int levelStart && level.End is int levelEnd)
                    problems.Add(new ValueProblem(levelStart + shift, levelEnd - levelStart, ValueProblemKind.EmptyPack, "NumberOfUnitInPackXPMultiplier", level.Text)
                    {
                        OtherValue = units.Text, Index = index,
                    });
        }
    }

    /// <summary>
    /// Checks each number against the other values of its object it must compare with (a minimum range below the
    /// maximum), when both are plain numbers; a value already marked for something else is not marked again.
    /// </summary>
    private static void CheckComparisons(string type, Dictionary<string, NdfValue> firstSet, HashSet<NdfValue> marked, ValueGuide guide, int shift, List<ValueProblem> problems)
    {
        foreach ((string name, NdfValue value) in firstSet)
        {
            if (marked.Contains(value) || guide.Find(type, name) is not { Comparisons.Count: > 0 } info) continue;
            if (NumberOf(value) is not double number || value.Start is not int start || value.End is not int end) continue;
            foreach (ValueComparison comparison in info.Comparisons)
            {
                if (!firstSet.TryGetValue(comparison.Other, out NdfValue? other) || NumberOf(other) is not double otherNumber || comparison.Holds(number, otherNumber)) continue;
                // a comparison that holds only in some cases: not where its words say it doesn't apply
                if (otherNumber == comparison.UnlessOtherIs || (comparison.UnlessSet is { } unless && firstSet.ContainsKey(unless))) continue;
                problems.Add(new ValueProblem(start + shift, end - start, ValueProblemKind.BrokenComparison, name, ((ScalarValue)value).Text)
                {
                    Comparison = comparison,
                    OtherValue = ((ScalarValue)other).Text,
                });
                break;
            }
        }
    }

    /// <summary>A value's number when it is a plain decimal number; null for anything else (a constant, a calculation).</summary>
    private static double? NumberOf(NdfValue value) =>
        value is ScalarValue { Kind: ScalarKind.Number } scalar && TryReadNumber(scalar.Text, out double number) ? number : null;

    // ---- one value ----

    /// <summary>What is wrong with a single value given to a name; null when nothing is, or when it can't be judged.</summary>
    private static ValueProblem? ProblemWith(ValueInfo info, string name, ScalarValue value, int position, int length)
    {
        string type = info.BaseValueType;
        string found = KindOf(value);
        ValueProblem Problem(ValueProblemKind kind) => new(position, length, kind, name, value.Text);

        // the kind: only a value that is plainly of another kind (a number, a text, True or False) is marked
        string expected = type switch
        {
            "int" or "int (0 or more)" => "int",
            "float" => "float",
            "boolean" => "boolean",
            _ => "",
        };
        if (expected != "" && found != "" && !Fits(expected, found))
            return Problem(ValueProblemKind.WrongKind) with { Expected = expected, Found = found };

        if (type == "enum" && info.ChoicesAreComplete && ChoiceMember(value.Text) is { } member
            && !info.Choices.Any(choice => ChoiceMember(choice) == member))
            return Problem(ValueProblemKind.NoSuchChoice) with { Choices = info.Choices };

        if (value.Kind != ScalarKind.Number || !TryReadNumber(value.Text, out double number)) return null;
        if (info.Limits.Any(limit => limit.IsBrokenBy(number))) return Problem(ValueProblemKind.OutsideLimit) with { Rules = info.Rules };
        if (type == "int (0 or more)" && found == "int" && number < 0) return Problem(ValueProblemKind.NegativeInZeroOrMore);
        return null;
    }

    /// <summary>Whether a value of one kind is right for a name taking another: an int fits a float.</summary>
    private static bool Fits(string expected, string found) => expected == found || (expected == "float" && found == "int");

    /// <summary>The kind a value plainly is: "int", "float", "string", "boolean", or "" for anything else.</summary>
    private static string KindOf(ScalarValue value) => value.Kind switch
    {
        ScalarKind.Number => IsWholeNumber(value.Text) ? "int" : "float",
        ScalarKind.String => "string",
        ScalarKind.Identifier when IsBoolean(value.Text) => "boolean",
        _ => "",
    };

    /// <summary>Whether a number has no decimal point and no exponent (0x1F counts as whole).</summary>
    private static bool IsWholeNumber(string text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || text.StartsWith("-0x", StringComparison.OrdinalIgnoreCase)
        || (!text.Contains('.', StringComparison.Ordinal) && !text.Contains('e', StringComparison.OrdinalIgnoreCase));

    private static bool IsBoolean(string text) =>
        string.Equals(text, "True", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "False", StringComparison.OrdinalIgnoreCase);

    /// <summary>A plain decimal number's value; hexadecimal numbers aren't checked against limits.</summary>
    private static bool TryReadNumber(string text, out double number) =>
        double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out number);

    /// <summary>
    /// A choice's list and member, however it is written: "EProjectileType/Obus", "~/ListAxis/Horizontal" and
    /// "$/A/ListAxis/Horizontal" all give "ListAxis/Horizontal"; null for anything else.
    /// </summary>
    public static string? ChoiceMember(string text)
    {
        string[] parts = text.Split('/');
        if (parts.Length < 2) return null;
        string list = parts[^2], member = parts[^1];
        if (!IsName(list) || !IsName(member)) return null;
        // what comes before the list must be a reference's start: nothing, "~", or "$" and folders
        string start = parts[0];
        bool isReference = parts.Length == 2 || start == "~" || start == "$";
        return isReference ? list + "/" + member : null;
    }

    private static bool IsName(string text) =>
        text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>A value typed into a suggestion's place, read as the parser would read it.</summary>
    private static ScalarValue ScalarOf(string text)
    {
        if (TryReadNumber(text, out _) || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return new ScalarValue(ScalarKind.Number, text);
        if (text.Length >= 2 && (text[0] == '\'' || text[0] == '"')) return new ScalarValue(ScalarKind.String, text);
        if (text == "nil") return new ScalarValue(ScalarKind.Nil, text);
        if (text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith("$/", StringComparison.Ordinal)) return new ScalarValue(ScalarKind.Reference, text);
        return new ScalarValue(text.Contains('/', StringComparison.Ordinal) ? ScalarKind.Path : ScalarKind.Identifier, text);
    }
}
