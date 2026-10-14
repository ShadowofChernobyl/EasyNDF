using EasyNdf.Engine.Ndf;

namespace EasyNdf.Engine.Rules;

/// <summary>
/// One entry in the list of declarations while rules run: a declaration of the file, a copy a Clone rule made,
/// or text an Add rule added. The writer goes through these in order to make the exported file.
/// </summary>
public sealed class WorkItem
{
    /// <summary>The declaration. Null only for text an Add rule added (see <see cref="AddedText"/>).</summary>
    public Declaration? Declaration { get; set; }

    /// <summary>
    /// True once the declaration is a copy a rule may change. The file's own declarations are copied the first time
    /// a rule is about to change them; until then the writer copies their text unchanged.
    /// </summary>
    public bool IsOwnCopy { get; set; }

    /// <summary>True when a Remove rule took the declaration out.</summary>
    public bool IsRemoved { get; set; }

    /// <summary>True for a copy a Clone rule made.</summary>
    public bool IsClone { get; init; }

    /// <summary>For an Add rule: the text it adds, written as typed.</summary>
    public string? AddedText { get; init; }

    /// <summary>For an Add rule: the names its text declares, plus the name in the rule's name box.</summary>
    public List<string> AddedNames { get; init; } = [];

    /// <summary>For an Add rule: the declarations read from its text, so a later Clone rule can copy one.</summary>
    public List<Declaration> AddedDeclarations { get; init; } = [];

    /// <summary>True for text an Add rule added.</summary>
    public bool IsAdded => AddedText is not null;

    /// <summary>True for a declaration of the file itself, not one a rule added or cloned.</summary>
    public bool IsFromFile => !IsAdded && !IsClone;

    /// <summary>A live descriptor: from the file or a clone, not removed, named, and an object.</summary>
    public bool IsLiveDescriptor =>
        !IsRemoved && !IsAdded && Declaration is { IsUnnamed: false, Value: ObjectValue };

    /// <summary>
    /// The declaration, ready for a rule to change: the first time, the file's declaration is replaced by a copy.
    /// The cached condition text is cleared either way, since the rule is about to change it.
    /// </summary>
    public Declaration Own()
    {
        if (!IsOwnCopy)
        {
            Declaration = Declaration!.CopyForEdit();
            IsOwnCopy = true;
        }
        Declaration!.CachedConditionText = null;
        return Declaration;
    }
}
