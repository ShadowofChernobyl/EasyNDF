using EasyNdf.App.Text;

namespace EasyNdf.App.Rules;

/// <summary>The rule types' names as people read them, in the language in use; rules themselves keep the English keys.</summary>
public static class RuleKindNames
{
    /// <summary>"Edit rule", "Add descriptor", "Scale"...: as the New rule menu lists them.</summary>
    public static string Of(string kind) => kind switch
    {
        "Edit" => Texts.Get("Rules.Kind.Edit"),
        "Add" => Texts.Get("Rules.Kind.Add"),
        "Clone" => Texts.Get("Rules.Kind.Clone"),
        "Remove" => Texts.Get("Rules.Kind.Remove"),
        "Replace" => Texts.Get("Rules.Kind.Replace"),
        "Scale" => Texts.Get("Rules.Kind.Scale"),
        "Formula" => Texts.Get("Rules.Kind.Formula"),
        "Table" => Texts.Get("Rules.Kind.Table"),
        "Lookup" => Texts.Get("Rules.Kind.Lookup"),
        "Text" => Texts.Get("Rules.Kind.Text"),
        "File" => Texts.Get("Rules.Kind.File"),
        "Check" => Texts.Get("Rules.Kind.Check"),
        "Group" => Texts.Get("Rules.Kind.Group"),
        "Variable" => Texts.Get("Rules.Kind.Variable"),
        _ => kind,
    };

    /// <summary>A rule type in one word, for the Type column of Details: "Edit", "Scale"...</summary>
    public static string Short(string kind) => kind == "" || kind == "Edit" ? Texts.Get("Rules.Kind.EditShort") : Of(kind);
}
