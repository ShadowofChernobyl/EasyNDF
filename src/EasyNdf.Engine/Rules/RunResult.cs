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

    /// <summary>The total of all rules' edits.</summary>
    public int Edits => Stats.Sum(s => s.Edits);
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

    /// <summary>"Keep source comments". When false, rules run on the file as it reads without its comments.</summary>
    public bool KeepSourceComments { get; init; } = true;

    /// <summary>Read back what the rules wrote, to catch text that is not valid NDF before it is saved.</summary>
    public bool CheckOutput { get; init; } = true;

    /// <summary>The mod's name, for "Edited by" comments. Empty for none.</summary>
    public string ModName { get; init; } = "";

    /// <summary>The "Decimal places" setting, or null for no limit.</summary>
    public int? DecimalPlaces { get; init; }
}
