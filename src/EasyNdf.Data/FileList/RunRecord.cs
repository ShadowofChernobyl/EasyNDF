using EasyNdf.Engine;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.FileList;

/// <summary>
/// What a file's last Preview or Run export did (the HTML app's <c>j.res</c>), for its Details: each rule's counts, the
/// edits it made, warnings, and whether the file was exported.
/// </summary>
public sealed class RunRecord
{
    /// <summary>What the engine gave back.</summary>
    public required RunResult Result { get; init; }

    /// <summary>What the file holds, as read for this run.</summary>
    public required LoadSummary Info { get; init; }

    /// <summary>How long reading the file and running the rules took.</summary>
    public required long Milliseconds { get; init; }

    /// <summary>The rule sets whose rules ran, by name, joined with commas, as Details' heading names them.</summary>
    public required string RuleSets { get; init; }

    /// <summary>Whether the file was written to its export folder.</summary>
    public bool Wrote { get; set; }

    /// <summary>Whether it was held back on export because what the rules made is not valid NDF.</summary>
    public bool Held { get; init; }

    /// <summary>Whether this was an export in which the rules changed nothing.</summary>
    public bool Unchanged { get; init; }

    /// <summary>The rule whose edits Details lists, by its place in the preset.</summary>
    public int? ShownRule { get; set; }

    /// <summary>
    /// A rule that changed nothing (the HTML app's <c>idleRule</c>): it is on, it is not a Group, Variable or Check (which
    /// change nothing by design) or a Text or File rule (which write outside the NDF file), and it made no edits.
    /// </summary>
    public static bool IsIdle(RuleStats rule) =>
        rule.Enabled && !rule.OffByGroup && rule.Kind is not ("Group" or "Variable" or "Check" or "Text" or "File") && rule.Edits == 0;
}
