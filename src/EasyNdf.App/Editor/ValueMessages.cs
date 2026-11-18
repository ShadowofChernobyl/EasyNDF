using EasyNdf.App.Text;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Editor;

/// <summary>What the File Editor says about a value its check marks: what is wrong, and what that does to mod generation.</summary>
public static class ValueMessages
{
    /// <summary>What is wrong: "ShotsCountPerSalvo expects an int (a whole number), but 1.5 is a float."</summary>
    public static string MessageOf(ValueProblem problem, TextDocument document) =>
        MessageOf(problem, problem.Kind == ValueProblemKind.WrittenTwice ? document.GetLineByOffset(Math.Min(problem.FirstPosition, document.TextLength)).LineNumber : 0);

    /// <summary>What is wrong, for a name set twice with the line of its first value.</summary>
    public static string MessageOf(ValueProblem problem, int firstLine) => problem.Kind switch
    {
        ValueProblemKind.WrongKind => Texts.Get("Editor.Value.WrongKind", ("name", problem.Name), ("value", problem.Value),
            ("expected", Expected(problem.Expected)), ("found", Found(problem.Found))),
        ValueProblemKind.NoSuchChoice => NoSuchChoice(problem),
        ValueProblemKind.OutsideLimit => OutsideLimit(problem),
        ValueProblemKind.WrittenTwice => Texts.Get("Editor.Value.WrittenTwice", ("name", problem.Name), ("line", firstLine)),
        ValueProblemKind.BrokenComparison => BrokenComparison(problem),
        ValueProblemKind.NoAmmoBox => Texts.Get("Editor.Value.NoAmmoBox", ("name", problem.Name)),
        ValueProblemKind.EmptyAmmoBox => Texts.Get("Editor.Value.EmptyAmmoBox", ("name", problem.Name), ("box", problem.Index + 1), ("value", problem.Value)),
        // the level numbered from 1, as the Armory's veterancy boxes number them
        ValueProblemKind.EmptyPack => Texts.Get("Editor.Value.EmptyPack", ("name", problem.Name), ("level", problem.Index + 1), ("value", problem.Value),
            ("units", problem.OtherValue)),
        ValueProblemKind.MountWithoutName => Texts.Get("Editor.Value.MountWithoutName"),
        ValueProblemKind.NoSuchImpact => Texts.Get("Editor.Value.NoSuchImpact", ("value", problem.Value.Trim('\'', '"'))),
        ValueProblemKind.MissingValue => Texts.Get("Editor.Value.MustBeSet", ("name", problem.Name)),
        _ => Texts.Get("Editor.Value.Negative", ("name", problem.Name)),
    };

    /// <summary>
    /// What is wrong in a few words, under the value it is about: the message without the name it starts with, "must be
    /// from 0 to 0.785398 (45°)."; for a choice a list does not have, without the list's choices.
    /// </summary>
    public static string ShortOf(ValueProblem problem, int firstLine)
    {
        if (problem.Kind == ValueProblemKind.NoSuchChoice)
        {
            string[] member = MemberOf(problem);
            return Texts.Get("Editor.Value.NoSuchChoice.Short", ("list", member[0]), ("member", member[^1]));
        }
        string message = MessageOf(problem, firstLine);
        // a value left out is marked on its object's type name, so the message keeps the name it is about
        if (problem.Kind == ValueProblemKind.MissingValue || !message.StartsWith(problem.Name, StringComparison.Ordinal)) return message;
        string rest = message[problem.Name.Length..].TrimStart(' ', ':', '\u00A0', '\u202F');
        return rest == "" ? message : rest;
    }

    /// <summary>
    /// What it does: generation fails on an error, or the game crashes on one generation lets through, and generation
    /// works with a warning, which says what the value becomes.
    /// </summary>
    public static string WhyOf(ValueProblem problem) =>
        Texts.Get(problem.IsWarning ? "Editor.Value.NegativeWhy"
            : problem.CrashesGame ? "Editor.Value.GameCrashes"
            : problem.Kind is ValueProblemKind.NoAmmoBox or ValueProblemKind.EmptyAmmoBox or ValueProblemKind.EmptyPack or ValueProblemKind.MountWithoutName
                ? "Editor.Value.GenerationFailsFrom210942"
            : "Editor.Value.GenerationFails");

    /// <summary>"MinimumRangeGRU must be less than MaximumRangeGRU (1925)."</summary>
    private static string BrokenComparison(ValueProblem problem)
    {
        (string, object?)[] blanks = [("name", problem.Name), ("other", problem.Comparison?.Other), ("otherValue", problem.OtherValue)];
        return problem.Comparison?.Operator switch
        {
            "<" => Texts.Get("Editor.Value.LessThan", blanks),
            "<=" => Texts.Get("Editor.Value.AtMost", blanks),
            ">" => Texts.Get("Editor.Value.MoreThan", blanks),
            _ => Texts.Get("Editor.Value.AtLeast", blanks),
        };
    }

    /// <summary>The kind a name takes, in words: "an int (a whole number)".</summary>
    private static string Expected(string kind) => kind switch
    {
        "int" => Texts.Get("Editor.Value.Expected.int"),
        "float" => Texts.Get("Editor.Value.Expected.float"),
        _ => Texts.Get("Editor.Value.Expected.boolean"),
    };

    /// <summary>The kind a value is, in words: "a float".</summary>
    private static string Found(string kind) => kind switch
    {
        "int" => Texts.Get("Editor.Value.Found.int"),
        "float" => Texts.Get("Editor.Value.Found.float"),
        "string" => Texts.Get("Editor.Value.Found.string"),
        _ => Texts.Get("Editor.Value.Found.boolean"),
    };

    /// <summary>"EProjectileType has no NotAChoice. Its choices: Fake, Obus, Artillerie…"</summary>
    private static string NoSuchChoice(ValueProblem problem)
    {
        string[] member = MemberOf(problem);
        string choices = string.Join(", ", problem.Choices.Where(choice => choice != "nil").Select(LastPart));
        return Texts.Get("Editor.Value.NoSuchChoice", ("list", member[0]), ("member", member[^1]), ("choices", choices));
    }

    /// <summary>
    /// The value's own words for its limits, after its name: "AngleDispersion must be from 0 to 0.785398 (45°)." from
    /// "In radians. Must be from 0 to 0.785398 (45°)."; words that don't say "Must" follow the name and a colon.
    /// </summary>
    private static string OutsideLimit(ValueProblem problem)
    {
        string rule = string.Join(" ", problem.Rules);
        int must = MustAt(rule);
        return must >= 0
            ? Texts.Get("Editor.Value.LimitMust", ("name", problem.Name), ("rest", rule[(must + "Must ".Length)..]))
            : Texts.Get("Editor.Value.Limit", ("name", problem.Name), ("rule", rule));
    }

    /// <summary>Where a sentence starting "Must " starts in a value's words; -1 when none does.</summary>
    private static int MustAt(string rule)
    {
        if (rule.StartsWith("Must ", StringComparison.Ordinal)) return 0;
        int at = rule.IndexOf(". Must ", StringComparison.Ordinal);
        return at < 0 ? -1 : at + 2;
    }

    /// <summary>The list and member of a choice: ["EProjectileType", "Obus"] for "~/EProjectileType/Obus".</summary>
    private static string[] MemberOf(ValueProblem problem) => (ValueChecker.ChoiceMember(problem.Value) ?? problem.Value).Split('/');

    /// <summary>A choice or reference without what is before its last "/": "Obus" for "EProjectileType/Obus".</summary>
    public static string LastPart(string value) => value[(value.LastIndexOf('/') + 1)..];
}

/// <summary>Where a place in the File Editor's text is (see <see cref="NdfPlace"/>), reading only the declaration it is in.</summary>
public static class EditorPlaces
{
    /// <summary>The place at an offset; null when it isn't inside an object, or its declaration is too long to look through.</summary>
    public static NdfPlace? At(TextDocument document, int offset)
    {
        if (EditedDeclarations.Around(document, offset) is not (int start, int end)) return null;
        return NdfPlace.At(document.GetText(start, end - start), offset - start)?.ShiftedBy(start);
    }
}
