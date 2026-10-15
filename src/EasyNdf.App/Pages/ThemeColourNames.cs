using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The theme colours' names in the language in use, for the Theme Designer. The Data library keeps the English names
/// beside the colours' keys; this gives the translated ones.
/// </summary>
public static class ThemeColourNames
{
    /// <summary>A colour's full name, such as "Editor: comments": the colour window's title and the swatch's tooltip.</summary>
    public static string Of(string key) => key switch
    {
        "bg" => Texts.Get("Settings.ThemeColour.Bg"),
        "panel" => Texts.Get("Settings.ThemeColour.Panel"),
        "well" => Texts.Get("Settings.ThemeColour.Well"),
        "ink" => Texts.Get("Settings.ThemeColour.Ink"),
        "muted" => Texts.Get("Settings.ThemeColour.Muted"),
        "line" => Texts.Get("Settings.ThemeColour.Line"),
        "accent" => Texts.Get("Settings.ThemeColour.Accent"),
        "ok" => Texts.Get("Settings.ThemeColour.Ok"),
        "warn" => Texts.Get("Settings.ThemeColour.Warn"),
        "caution" => Texts.Get("Settings.ThemeColour.Caution"),
        "sel" => Texts.Get("Settings.ThemeColour.Sel"),
        "hover" => Texts.Get("Settings.ThemeColour.Hover"),
        "toggle" => Texts.Get("Settings.ThemeColour.Toggle"),
        "hi" => Texts.Get("Settings.ThemeColour.Hi"),
        "lo" => Texts.Get("Settings.ThemeColour.Lo"),
        "s-comment" => Texts.Get("Settings.ThemeColour.EditorComment"),
        "s-string" => Texts.Get("Settings.ThemeColour.EditorString"),
        "s-number" => Texts.Get("Settings.ThemeColour.EditorNumber"),
        "s-keyword" => Texts.Get("Settings.ThemeColour.EditorKeyword"),
        "s-type" => Texts.Get("Settings.ThemeColour.EditorType"),
        "s-func" => Texts.Get("Settings.ThemeColour.EditorFunc"),
        "s-const" => Texts.Get("Settings.ThemeColour.EditorConst"),
        "s-ref" => Texts.Get("Settings.ThemeColour.EditorRef"),
        "s-param" => Texts.Get("Settings.ThemeColour.EditorParam"),
        "s-name" => Texts.Get("Settings.ThemeColour.EditorName"),
        "s-field" => Texts.Get("Settings.ThemeColour.EditorField"),
        "d-add" => Texts.Get("Settings.ThemeColour.ChangesAdd"),
        "d-del" => Texts.Get("Settings.ThemeColour.ChangesDel"),
        _ => key,
    };

    /// <summary>
    /// A colour's name as its row shows it: an editor colour without "Editor: ", since it sits in the editor's own box.
    /// </summary>
    public static string Row(string key) => key switch
    {
        "s-comment" => Texts.Get("Settings.ThemeColour.EditorComment.Row"),
        "s-string" => Texts.Get("Settings.ThemeColour.EditorString.Row"),
        "s-number" => Texts.Get("Settings.ThemeColour.EditorNumber.Row"),
        "s-keyword" => Texts.Get("Settings.ThemeColour.EditorKeyword.Row"),
        "s-type" => Texts.Get("Settings.ThemeColour.EditorType.Row"),
        "s-func" => Texts.Get("Settings.ThemeColour.EditorFunc.Row"),
        "s-const" => Texts.Get("Settings.ThemeColour.EditorConst.Row"),
        "s-ref" => Texts.Get("Settings.ThemeColour.EditorRef.Row"),
        "s-param" => Texts.Get("Settings.ThemeColour.EditorParam.Row"),
        "s-name" => Texts.Get("Settings.ThemeColour.EditorName.Row"),
        "s-field" => Texts.Get("Settings.ThemeColour.EditorField.Row"),
        "d-add" => Texts.Get("Settings.ThemeColour.ChangesAdd.Row"),
        "d-del" => Texts.Get("Settings.ThemeColour.ChangesDel.Row"),
        _ => Of(key),
    };
}
