using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Editor;
using EasyNdf.App.Text;
using EasyNdf.Engine.Editing;

namespace EasyNdf.App.Help;

/// <summary>
/// The File Editor's tips on the words of an NDF sample in Help: a property's name or value has its tip from the
/// guide (what it does, its kind, its limits), as in the File Editor; a type, keyword, comment, True or False, nil,
/// number or text has the File Editor's short note. Each tip is made when the mouse first rests on the word.
/// </summary>
internal static class CodeTips
{
    /// <summary>Gives a coloured word of a sample its tip.</summary>
    /// <param name="word">The word's run.</param>
    /// <param name="text">The whole sample.</param>
    /// <param name="offset">Where the word starts in the sample.</param>
    /// <param name="colour">Its colour's name, as NdfColouring gives it ("field", "type", "number"...).</param>
    /// <param name="value">The word.</param>
    /// <param name="line">The line it is on.</param>
    public static void Attach(Run word, string text, int offset, string colour, string value, string line)
    {
        // a tip to be: the real one is made when it is first shown
        word.ToolTip = "";
        word.ToolTipOpening += (_, e) =>
        {
            if (word.ToolTip is not string) return;
            object? tip = TipFor(text, offset, colour, value, line);
            if (tip is null)
            {
                e.Handled = true;
                // the guide is read the first time it is needed; the next hover has the tip
                if (EditorGuide.IfRead is null) _ = EditorGuide.Get();
                return;
            }
            word.ToolTip = new ToolTip { Content = tip };
        };
    }

    private static object? TipFor(string text, int offset, string colour, string value, string line)
    {
        ValueGuide? guide = EditorGuide.IfRead;
        if (guide is null) return null;
        // a property's name, or its value: what the guide says about it, as in the File Editor
        if (colour is "field" or "number" or "string" or "ref" or "const" && NdfPlace.At(text, offset) is { } place
            && (place.IsName || place.IsInValue) && guide.Find(place.ObjectType, place.Name) is { } info)
            return ValueTips.For(null, place.Name, info, place.IsInValue ? place.ValueText : null);
        return colour switch
        {
            "type" => SyntaxTips.Note(null, value, Texts.Get("Editor.Syntax.Kind.Type"),
                (guide.AboutType(value) is string about ? about + " " : "")
                + Texts.Count("Editor.Syntax.Settings", guide.NamesOf(value).Count(), ("number", Texts.Number(guide.NamesOf(value).Count())))),
            "keyword" => SyntaxWords.KeywordNote(value) is { } note ? SyntaxTips.Note(null, value, Texts.Get("Editor.Syntax.Kind.Keyword"), Texts.Get(note)) : null,
            "comment" => SyntaxTips.Note(null, SyntaxWords.CommentMark(value, false), Texts.Get("Editor.Syntax.Kind.Comment"), Texts.Get("Editor.Syntax.Comment")),
            "const" when value is "True" or "False" or "true" or "false" => SyntaxTips.Note(null, value, Texts.Get("Editor.Syntax.Kind.YesNo"), Texts.Get("Editor.Syntax.TrueFalse")),
            "const" when value == "nil" => SyntaxTips.Note(null, value, Texts.Get("Editor.Syntax.Kind.NoValue"), Texts.Get("Editor.Syntax.Nil")),
            "number" => SyntaxTips.Note(null, value, Texts.Get("Editor.Syntax.Kind.Number"), Texts.Get("Editor.Syntax.Number")),
            "string" => SyntaxTips.Note(null, value.Length > 40 ? value[..40] + "…" : value, Texts.Get("Editor.Syntax.Kind.Text"), Texts.Get("Editor.Syntax.Text")),
            _ => null,
        };
    }
}
