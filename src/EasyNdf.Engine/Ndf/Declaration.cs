namespace EasyNdf.Engine.Ndf;

/// <summary>
/// One top-level declaration in an NDF file, such as <c>export Ammo_X is TAmmunitionDescriptor ( ... )</c>,
/// a constant such as <c>MaxRange is 2100</c>, or an <c>unnamed</c> value.
/// </summary>
/// <remarks>
/// Rules also work on "stand-ins": an object inside a descriptor, or the file's constants taken together, wrapped in a
/// Declaration so conditions and actions can treat it like a descriptor. See <see cref="IsStandIn"/>.
/// </remarks>
public sealed class Declaration
{
    /// <summary>The declaration's name. Empty for an unnamed one.</summary>
    public string Name { get; set; } = "";

    /// <summary>The value after "is". For a descriptor, an <see cref="ObjectValue"/>.</summary>
    public required NdfValue Value { get; set; }

    /// <summary>True when the declaration starts with "export".</summary>
    public bool IsExported { get; init; }

    /// <summary>True for "unnamed value".</summary>
    public bool IsUnnamed { get; init; }

    /// <summary>"private ", "public ", "template " or a mix, as written before the name. Empty when there is none.</summary>
    public string Prefix { get; init; } = "";

    /// <summary>For a template, its parameter list "[ ... ]" as written, without carriage returns. Empty otherwise.</summary>
    public string TemplateParameters { get; init; } = "";

    /// <summary>A comment a rule wrote after the name.</summary>
    public string? NameComment { get; set; }

    /// <summary>Comments from the file on the lines above the declaration.</summary>
    public List<SourceComment>? LeadingComments { get; init; }

    /// <summary>Where the declaration starts in <see cref="Source"/>.</summary>
    public int Start { get; init; }

    /// <summary>Where the declaration ends in <see cref="Source"/> (one past its last character).</summary>
    public int End { get; init; }

    /// <summary>Where the name sits in <see cref="Source"/>; null for an unnamed declaration.</summary>
    public int? NameStart { get; init; }

    /// <summary>How long the name is in the source, so the writer can skip it after a rule renamed the declaration.</summary>
    public int NameLength { get; init; }

    /// <summary>The text all the positions point into: the file, or the text of an Add rule.</summary>
    public string Source { get; init; } = "";

    // ---- used only while rules run ----

    /// <summary>
    /// True for an object inside a descriptor, or the file's constants together, standing in for a descriptor.
    /// A stand-in has no title of its own, and its text is not cached.
    /// </summary>
    public bool IsStandIn { get; init; }

    /// <summary>True for the stand-in that holds the file's top-level constants.</summary>
    public bool IsConstantsStandIn { get; init; }

    /// <summary>How the edit log names a stand-in, such as "Unit_X > TModuleDescriptor Name". Null for a real declaration.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// The declaration's text as conditions read it (comments left out), kept so that several rules testing the
    /// same unchanged descriptor write it out once. Cleared as soon as a rule is about to change the declaration.
    /// </summary>
    public string? CachedConditionText { get; set; }

    /// <summary>The name the edit log uses: the stand-in's label, or the declaration's name.</summary>
    public string LogName => Label ?? Name;

    /// <summary>The object type, for a declaration whose value is an object; empty otherwise.</summary>
    public string TypeName => Value is ObjectValue o ? o.TypeName : "";

    /// <summary>
    /// A copy a rule can change, leaving the original as the file has it. The value is copied deeply; the positions stay,
    /// so the writer knows which text it replaces.
    /// </summary>
    public Declaration CopyForEdit() => new()
    {
        IsExported = IsExported,
        IsUnnamed = IsUnnamed,
        Prefix = Prefix,
        TemplateParameters = TemplateParameters,
        Name = Name,
        NameComment = NameComment,
        Value = Value.CopyForEdit(),
        LeadingComments = LeadingComments,
        Start = Start,
        End = End,
        NameStart = NameStart,
        NameLength = NameLength,
        Source = Source,
    };
}

/// <summary>Everything read from one NDF text: its declarations in order, and the text itself.</summary>
public sealed class NdfDocument(List<Declaration> declarations, string source)
{
    public List<Declaration> Declarations { get; } = declarations;

    /// <summary>The text that was read.</summary>
    public string Source { get; } = source;
}

/// <summary>Thrown when a text is not valid NDF. The message starts with "Line N:".</summary>
public sealed class NdfParseException(string message) : Exception(message);
