using System.Text.Json;

namespace EasyNdf.Engine.Editing;

/// <summary>One side of a number's allowed range: at least (or above) a value, or at most (or below) one.</summary>
/// <param name="IsLowest">True for the lowest value allowed; false for the highest.</param>
/// <param name="Value">The limit itself.</param>
/// <param name="IsExcluded">True when the limit itself is not allowed ("above 0" rather than "0 or more").</param>
public sealed record ValueLimit(bool IsLowest, double Value, bool IsExcluded)
{
    /// <summary>Whether a number is on the wrong side of this limit.</summary>
    public bool IsBrokenBy(double number) =>
        IsLowest ? number < Value || (IsExcluded && number == Value) : number > Value || (IsExcluded && number == Value);
}

/// <summary>A comparison a value must keep with another value of the same object: "MinimumRangeGRU &lt; MaximumRangeGRU".</summary>
/// <param name="Operator">"&lt;", "&lt;=", "&gt;" or "&gt;=".</param>
/// <param name="Other">The other value's name.</param>
public sealed record ValueComparison(string Operator, string Other)
{
    /// <summary>A value of the other that makes the comparison not apply: 0 for "less than MaximumRangeGRU when that isn't 0"; null for none.</summary>
    public double? UnlessOtherIs { get; init; }

    /// <summary>A name whose being set in the object makes the comparison not apply ("without a master turret"); null for none.</summary>
    public string? UnlessSet { get; init; }

    /// <summary>Whether two numbers keep the comparison (the first is the value's own, the second the other's).</summary>
    public bool Holds(double value, double other) => Operator switch
    {
        "<" => value < other,
        "<=" => value <= other,
        ">" => value > other,
        _ => value >= other,
    };
}

/// <summary>What the File Editor knows about one name in one type: its value type, what it does, and the values it takes.</summary>
public sealed class ValueInfo
{
    /// <summary>The value type as shown: "float", "int (0 or more)", "boolean", "enum", "reference to TEntityDescriptor"…</summary>
    public string ValueType { get; init; } = "";

    /// <summary>Whether the value type is certain; only certain types are checked.</summary>
    public bool IsTypeCertain { get; init; }

    /// <summary>What the value does, in plain words; empty when not known.</summary>
    public string Description { get; init; } = "";

    /// <summary>How sure the description is: "clear" (confirmed), "approx" or "plain" (very likely), "unclear" (not known), or empty.</summary>
    public string Certainty { get; init; } = "";

    /// <summary>The values it can take that the files show, as written; empty for numbers and text.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>
    /// True when <see cref="Choices"/> are every value it can take (True and False, or every member of a choice's own
    /// list), so a value outside them is an error.
    /// </summary>
    public bool ChoicesAreComplete { get; init; }

    /// <summary>What its value must be, in words: "Must be 0 or more."</summary>
    public IReadOnlyList<string> Rules { get; init; } = [];

    /// <summary>The limits a value breaking stops mod generation, as tests confirmed.</summary>
    public IReadOnlyList<ValueLimit> Limits { get; init; } = [];

    /// <summary>The comparisons with other values of the same object that stop mod generation when broken, as tests confirmed.</summary>
    public IReadOnlyList<ValueComparison> Comparisons { get; init; } = [];

    /// <summary>Whether mod generation fails when the value is left out or set to nil, as a test confirmed.</summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// A True / False value of the same object that makes this one required while it is True, as a test confirmed
    /// (UniformDrawer while HasBackground is True); null for none.
    /// </summary>
    public string? RequiredWhen { get; init; }

    /// <summary>The value type without " (differs in some types)", which the name has when it takes other values in other types.</summary>
    public string BaseValueType => ValueType.EndsWith(DiffersSuffix, StringComparison.Ordinal) ? ValueType[..^DiffersSuffix.Length] : ValueType;

    private const string DiffersSuffix = " (differs in some types)";
}

/// <summary>
/// What each type's names take, for the File Editor's checks, tips and suggestions: for every type used in the game's
/// .ndf files, the names found under it in the files, each with its <see cref="ValueInfo"/>. Read from the editor's
/// guide data (a JSON file the app carries).
/// </summary>
public sealed class ValueGuide
{
    private readonly Dictionary<string, Dictionary<string, ValueInfo>> types;

    // Tuned for speed: each object checked asks for its type's required names, so they are listed once here instead of
    // going through every name of the type each time.
    private readonly Dictionary<string, string[]> required;

    private ValueGuide(Dictionary<string, Dictionary<string, ValueInfo>> types)
    {
        this.types = types;
        required = types
            .Select(type => (type.Key, Names: type.Value.Where(name => name.Value.IsRequired || name.Value.RequiredWhen is not null).Select(name => name.Key).ToArray()))
            .Where(type => type.Names.Length > 0)
            .ToDictionary(type => type.Key, type => type.Names, StringComparer.Ordinal);
    }

    /// <summary>A guide with nothing in it, for tests and for games it has no data for.</summary>
    public static ValueGuide Empty { get; } = new([]);

    /// <summary>What a name takes in a type; null when the files never give that type the name.</summary>
    public ValueInfo? Find(string type, string name) =>
        types.TryGetValue(type, out Dictionary<string, ValueInfo>? names) && names.TryGetValue(name, out ValueInfo? info) ? info : null;

    /// <summary>Whether the guide knows a type.</summary>
    public bool Knows(string type) => types.ContainsKey(type);

    /// <summary>The names a type takes, in the order of the data (by name).</summary>
    public IEnumerable<string> NamesOf(string type) => types.TryGetValue(type, out Dictionary<string, ValueInfo>? names) ? names.Keys : [];

    /// <summary>The names of a type that are required, always or while another value is True (<see cref="ValueInfo.IsRequired"/>).</summary>
    public IReadOnlyList<string> RequiredNamesOf(string type) => required.TryGetValue(type, out string[]? names) ? names : [];

    /// <summary>
    /// Reads the guide data, <c>{"types": {type: {name: {valueType, typeCertain, …}}}}</c>, a little at a time: the whole
    /// file is never held at once (read whole, its buffers stayed in memory long after).
    /// </summary>
    public static ValueGuide Read(Stream json)
    {
        GuideData data = JsonSerializer.Deserialize<GuideData>(json, ReadingOptions) ?? new GuideData();
        var sharing = new SharingReader();
        var types = new Dictionary<string, Dictionary<string, ValueInfo>>(StringComparer.Ordinal);
        foreach ((string type, Dictionary<string, EntryData> entries) in data.Types)
        {
            var names = new Dictionary<string, ValueInfo>(entries.Count, StringComparer.Ordinal);
            foreach ((string name, EntryData entry) in entries) names[sharing.Shared(name)] = sharing.InfoFrom(entry);
            types[type] = names;
        }
        return new ValueGuide(types);
    }

    private static readonly JsonSerializerOptions ReadingOptions = new() { PropertyNameCaseInsensitive = true };

    // ---- the data as written ----

    /// <summary>The guide data as the file writes it.</summary>
    private sealed class GuideData
    {
        public Dictionary<string, Dictionary<string, EntryData>> Types { get; set; } = [];
    }

    /// <summary>One name's entry as the file writes it.</summary>
    private sealed class EntryData
    {
        public string? ValueType { get; set; }
        public bool TypeCertain { get; set; }
        public string? Description { get; set; }
        public string? Certainty { get; set; }
        public string[]? Choices { get; set; }
        public bool ChoicesComplete { get; set; }
        public string[]? Rules { get; set; }
        public LimitData[]? Limits { get; set; }
        public ComparisonData[]? Comparisons { get; set; }
        public bool Required { get; set; }
        public string? RequiredWhen { get; set; }
    }

    /// <summary>One comparison as the file writes it: its operator and the other value's name.</summary>
    private sealed class ComparisonData
    {
        public string? Op { get; set; }
        public string? Other { get; set; }
        public double? UnlessOtherIs { get; set; }
        public string? UnlessSet { get; set; }
    }

    /// <summary>One limit as the file writes it: its side ("min" or "max"), value, whether open, whether confirmed.</summary>
    private sealed class LimitData
    {
        public string? Side { get; set; }
        public double Value { get; set; }
        public bool Open { get; set; }
        public bool Confirmed { get; set; }
    }

    /// <summary>
    /// Makes the entries, keeping each text and each list once: a child type repeats its parents' names with the same
    /// words and choices.
    /// </summary>
    private sealed class SharingReader
    {
        private readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<string>> lists = new(StringComparer.Ordinal);

        /// <summary>One name's entry, with only its confirmed limits: the editor never marks the others.</summary>
        public ValueInfo InfoFrom(EntryData entry) => new()
        {
            ValueType = Shared(entry.ValueType ?? ""),
            IsTypeCertain = entry.TypeCertain,
            Description = Shared(entry.Description ?? ""),
            Certainty = Shared(entry.Certainty ?? ""),
            Choices = SharedList(entry.Choices),
            ChoicesAreComplete = entry.ChoicesComplete,
            Rules = SharedList(entry.Rules),
            Limits = [.. (entry.Limits ?? []).Where(limit => limit.Confirmed).Select(limit => new ValueLimit(limit.Side == "min", limit.Value, limit.Open))],
            Comparisons = [.. (entry.Comparisons ?? []).Where(comparison => comparison is { Op: not null, Other: not null })
                .Select(comparison => new ValueComparison(comparison.Op!, Shared(comparison.Other!))
                {
                    UnlessOtherIs = comparison.UnlessOtherIs,
                    UnlessSet = comparison.UnlessSet is null ? null : Shared(comparison.UnlessSet),
                })],
            IsRequired = entry.Required,
            RequiredWhen = entry.RequiredWhen is null ? null : Shared(entry.RequiredWhen),
        };

        /// <summary>The copy of a text kept already, or this one, kept from now on.</summary>
        public string Shared(string text)
        {
            if (texts.TryGetValue(text, out string? kept)) return kept;
            texts[text] = text;
            return text;
        }

        /// <summary>The copy of a list kept already (the same items in the same order), or this one, its texts shared.</summary>
        private IReadOnlyList<string> SharedList(string[]? items)
        {
            if (items is null || items.Length == 0) return [];
            string key = string.Join('\n', items);
            if (lists.TryGetValue(key, out IReadOnlyList<string>? kept)) return kept;
            for (int k = 0; k < items.Length; k++) items[k] = Shared(items[k]);
            lists[key] = items;
            return items;
        }
    }
}
