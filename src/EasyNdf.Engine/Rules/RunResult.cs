namespace EasyNdf.Engine.Rules;

/// <summary>What one rule did in one run.</summary>
public sealed class RuleStats
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required bool Enabled { get; init; }

    /// <summary>How many descriptors the rule applied to.</summary>
    public int Matched { get; set; }

    /// <summary>How many values it changed (or descriptors it added, cloned or removed).</summary>
    public int Edits { get; set; }

    /// <summary>How many times the rule's actions changed a variable: it wrote nothing for those, but did something.</summary>
    public int VariableChanges { get; set; }

    /// <summary>Its edits as shown to the user: the values it changed and the variables it changed.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int AllEdits => Edits + VariableChanges;

    /// <summary>How many times it could not do what it was asked, for any reason.</summary>
    public int Skipped { get; set; }

    /// <summary>True when the rule did not run because its group is switched off.</summary>
    public bool OffByGroup { get; set; }
}

/// <summary>A warning shown on the Files tab after a run.</summary>
/// <param name="Rule">The rule's name.</param>
/// <param name="Message">What went wrong, for the user.</param>
/// <param name="Type">"number" for warnings about numbers that could not be used; null otherwise.</param>
public sealed record RunWarning(string Rule, string Message, string? Type = null);

/// <summary>Everything a run produces.</summary>
public sealed class RunResult
{
    /// <summary>The exported file, or null when the text was not asked for.</summary>
    public string? Text { get; init; }

    /// <summary>
    /// Empty when everything the rules wrote reads back as valid NDF; otherwise the error, with the line number in the
    /// exported file.
    /// </summary>
    public string ParseError { get; init; } = "";

    /// <summary>One entry per rule, in the order of the rules.</summary>
    public required List<RuleStats> Stats { get; init; }

    /// <summary>One edit log per rule, in the order of the rules.</summary>
    public required List<List<EditLogEntry>> Logs { get; init; }

    public required List<RunWarning> Warnings { get; init; }

    /// <summary>The variables as the last rule left them, by name: a rule's action may change one.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    /// <summary>The total of all rules' edits.</summary>
    public int Edits => Stats.Sum(s => s.Edits);

    /// <summary>The edits as shown to the user: <see cref="Edits"/> and the variables the rules changed.</summary>
    public int AllEdits => Stats.Sum(s => s.AllEdits);
}

/// <summary>Settings for a run.</summary>
public sealed class RunOptions
{
    /// <summary>Produce the exported text. Without it, only counts and logs are made (for Preview).</summary>
    public bool WantText { get; init; } = true;

    /// <summary>How many edit log entries each rule keeps. Counting goes on past it.</summary>
    public int LogLimit { get; init; } = 500;

    /// <summary>
    /// Other loaded files a Lookup rule can read, by lower-case file name.
    /// </summary>
    public IReadOnlyDictionary<string, string> OtherFiles { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The game's own version of the file, asked for only when a Clone's source is not in the file (an earlier mod
    /// removed it): the copy is then made from the game's. Null, or giving null, for none.
    /// </summary>
    public Func<string?>? GameOriginal { get; init; }

    /// <summary>"Keep source comments". When false, rules run on the file as it reads without its comments.</summary>
    public bool KeepSourceComments { get; init; } = true;

    /// <summary>Read back what the rules wrote, to catch text that is not valid NDF before it is saved.</summary>
    public bool CheckOutput { get; init; } = true;

    /// <summary>The mod's name, for "Edited by" comments. Empty for none.</summary>
    public string ModName { get; init; } = "";

    /// <summary>The "Decimal places" setting, or null for no limit.</summary>
    public int? DecimalPlaces { get; init; }

    /// <summary>
    /// Where each rule set begins among the rules, in order, with the shared variables (those of a mod, or of all mods)
    /// as they stand when it begins. With none, the rules are one rule set and every Variable rule is its own.
    /// </summary>
    public IReadOnlyList<RuleSegment> Segments { get; init; } = [];
}

/// <summary>A rule set among the rules of a run (<see cref="RunOptions.Segments"/>).</summary>
/// <param name="Start">The index of its first rule.</param>
/// <param name="Shared">The shared variables it can use, by name, with their values as it begins.</param>
public sealed record RuleSegment(int Start, IReadOnlyDictionary<string, string> Shared);
