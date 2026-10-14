using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EasyNdf.Engine.Rules;

/// <summary>
/// A mod file: <c>{ ModName, FormatVersion, Presets: [ { PresetName, Rules: [...] } ] }</c>.
/// </summary>
/// <remarks>
/// The format is the HTML app's (FormatVersion 1) and must stay readable by both apps. Plain Edit rules are also the
/// format of the original WinForms app.
/// </remarks>
public sealed class ModFile
{
    public string? ModName { get; set; }
    public int FormatVersion { get; set; } = 1;
    public List<Preset> Presets { get; set; } = [];

    /// <summary>Reads a mod file's JSON.</summary>
    public static ModFile Read(string json) =>
        JsonSerializer.Deserialize<ModFile>(json, RuleJson.Options) ?? new ModFile();
}

/// <summary>A named list of rules, usually one per game file.</summary>
public sealed class Preset
{
    public string? PresetName { get; set; }
    public List<Rule?> Rules { get; set; } = [];
}

/// <summary>
/// One rule. <see cref="Kind"/> decides what it does; a rule without a kind is an ordinary Edit rule.
/// </summary>
public sealed class Rule
{
    [JsonConverter(typeof(LooseTextConverter))] public string? Name { get; set; }

    /// <summary>Edit, Add, Clone, Remove, Replace, Scale, Formula, Table, Lookup, Check, Group or Variable.</summary>
    [JsonConverter(typeof(LooseTextConverter))] public string? Kind { get; set; }

    /// <summary>A rule is switched off only by an explicit false.</summary>
    [JsonConverter(typeof(OnlyFalseIsFalseConverter))] public bool Enabled { get; set; } = true;

    /// <summary>"All" means every condition must match; anything else means one is enough.</summary>
    [JsonConverter(typeof(LooseTextConverter))] public string? LogicGate { get; set; }

    public List<Condition?>? Conditions { get; set; }
    public List<RuleAction?>? Actions { get; set; }

    /// <summary>The settings of the rule types other than Edit, by name. Values may be text, numbers or true/false.</summary>
    public Dictionary<string, JsonNode?>? Params { get; set; }

    /// <summary>The rule's name as the results list shows it.</summary>
    [JsonIgnore] public string DisplayName => string.IsNullOrEmpty(Name) ? "(unnamed)" : Name;

    /// <summary>The rule's kind, with Edit for a rule that has none.</summary>
    [JsonIgnore] public string KindOrEdit => string.IsNullOrEmpty(Kind) ? "Edit" : Kind;

    /// <summary>Reads the Params, with the JavaScript app's rules for missing values.</summary>
    [JsonIgnore] public RuleParams P => new(Params);
}

/// <summary>A condition: which descriptors a rule applies to.</summary>
public sealed class Condition
{
    [JsonConverter(typeof(LooseTextConverter))] public string? Name { get; set; }

    /// <summary>What to test: a property name, or Entire Descriptor, Descriptor Title or Descriptor Type.</summary>
    [JsonConverter(typeof(LooseTextConverter))] public string? Operand { get; set; }

    /// <summary>How to test it, such as "Starts With" or "Is Greater Than".</summary>
    [JsonConverter(typeof(LooseTextConverter))] public string? Operator { get; set; }

    [JsonConverter(typeof(LooseTextConverter))] public string? Value { get; set; }

    /// <summary>When on, <see cref="CustomOperand"/> is used instead of <see cref="Operand"/>.</summary>
    [JsonConverter(typeof(TruthyConverter))] public bool ManualOverride { get; set; }

    [JsonConverter(typeof(LooseTextConverter))] public string? CustomOperand { get; set; }

    /// <summary>The operand in force: the custom one when Manual Override is on.</summary>
    [JsonIgnore] public string? OperandInUse => ManualOverride ? CustomOperand : Operand;

    public Condition With(string? value) => new()
    {
        Name = Name, Operand = Operand, Operator = Operator, Value = value,
        ManualOverride = ManualOverride, CustomOperand = CustomOperand,
    };
}

/// <summary>An action: what a rule changes in each matching descriptor.</summary>
public sealed class RuleAction
{
    [JsonConverter(typeof(LooseTextConverter))] public string? Name { get; set; }

    /// <summary>The property to change, or Descriptor Title or Descriptor Type.</summary>
    [JsonConverter(typeof(LooseTextConverter))] public string? Target { get; set; }

    /// <summary>
    /// What to do, such as "Multiply [int, float]". The part in brackets only says which values it suits and is ignored.
    /// </summary>
    [JsonConverter(typeof(LooseTextConverter))] public string? Operator { get; set; }

    [JsonConverter(typeof(LooseTextConverter))] public string? Value { get; set; }

    /// <summary>When on, <see cref="CustomTarget"/> is used instead of <see cref="Target"/>.</summary>
    [JsonConverter(typeof(TruthyConverter))] public bool ManualOverride { get; set; }

    [JsonConverter(typeof(LooseTextConverter))] public string? CustomTarget { get; set; }

    /// <summary>The comment written next to each value this action changes.</summary>
    public ActionComment? Comment { get; set; }

    // ---- set by the engine for the Scale and Formula rule types, never read from a mod file ----

    [JsonIgnore] internal ScaleSettings? Scale { get; init; }
    [JsonIgnore] internal Func<double, double?>? Formula { get; init; }

    /// <summary>The target in force: the custom one when Manual Override is on.</summary>
    [JsonIgnore] public string? TargetInUse => ManualOverride ? CustomTarget : Target;

    public RuleAction With(string? value) => new()
    {
        Name = Name, Target = Target, Operator = Operator, Value = value,
        ManualOverride = ManualOverride, CustomTarget = CustomTarget, Comment = Comment,
    };
}

/// <summary>The comment an action writes next to each value it changes.</summary>
public sealed class ActionComment
{
    [JsonConverter(typeof(TruthyConverter))] public bool Enabled { get; set; }

    /// <summary>The comment text. When empty, the engine writes what the action did, such as "Multiplied by 2".</summary>
    [JsonConverter(typeof(LooseTextConverter))] public string? Text { get; set; }

    [JsonConverter(typeof(TruthyConverter))] public bool IncludeTimestamp { get; set; }
    [JsonConverter(typeof(TruthyConverter))] public bool IncludeOriginalValue { get; set; }

    /// <summary>Adds "Edited by" and the mod's name.</summary>
    [JsonConverter(typeof(TruthyConverter))] public bool IncludeMod { get; set; }
}

/// <summary>The settings of a Scale rule, worked out before the rule runs.</summary>
internal sealed record ScaleSettings(Func<double, double, double>? Operation, double? Value, double Step, string? RoundMode, double? Min, double? Max);

/// <summary>JSON settings shared by everything that reads or writes rules.</summary>
public static class RuleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };
}
