using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Text;

namespace EasyNdf.App.Editor;

/// <summary>
/// The File Editor's tips for the coloured words that aren't a setting or its value, as the chosen mock
/// (2026-11-11): a type, a template, a name or reference with its file, line and first settings, a template's value, a
/// keyword, a comment. They are made of the same parts as a setting's tip (<see cref="ValueTips"/>) and open as it does.
/// </summary>
public static class SyntaxTips
{
    /// <summary>A tip: what is wrong first (on a marked word), then the word and what it is ("export: keyword").</summary>
    public static StackPanel Start((string Message, string Why, bool IsWarning)? problem, string word, string kind)
    {
        var tip = new StackPanel { MaxWidth = ValueTips.Width };
        if (problem is { } wrong) tip.Children.Add(ValueTips.ProblemPart(wrong, hasMore: true));
        tip.Children.Add(ValueTips.Heading(word, kind));
        return tip;
    }

    /// <summary>A tip of a word, its kind and one sentence.</summary>
    public static StackPanel Note((string Message, string Why, bool IsWarning)? problem, string word, string kind, string sentence) =>
        Add(Start(problem, word, kind), sentence);

    /// <summary>Adds a sentence to a tip.</summary>
    public static StackPanel Add(StackPanel tip, string sentence)
    {
        tip.Children.Add(ValueTips.Words(sentence));
        return tip;
    }

    /// <summary>Adds a sentence under a line, as "On this line" sits in a setting's tip.</summary>
    public static void AddUnderLine(StackPanel tip, UIElement part)
    {
        var underLine = new Border { Child = part, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 0), Margin = new Thickness(0, 5, 0, 0) };
        underLine.SetResourceReference(Border.BorderBrushProperty, "line");
        tip.Children.Add(underLine);
    }

    /// <summary>Adds an object's first settings under a line, in the code font ("Arme = TDamageTypeRTTI(…)"), then how many more it has; a constant's value as "Name is 10.0".</summary>
    public static void AddKeyValues(StackPanel tip, string name, KeyValues values)
    {
        var part = new StackPanel();
        if (values.ConstantValue is { } constant) part.Children.Add(Code(name + " is", constant, joiner: " "));
        foreach ((string key, string value) in values.Shown) part.Children.Add(Code(key, value, joiner: " = "));
        int more = values.Total - values.Shown.Count;
        if (values.ConstantValue is null && more > 0)
        {
            var rest = new TextBlock { Text = Texts.Count("Editor.Syntax.MoreSettings", more, ("number", Texts.Number(more))), TextWrapping = TextWrapping.Wrap, FontSize = 11 };
            rest.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            part.Children.Add(rest);
        }
        if (part.Children.Count > 0) AddUnderLine(tip, part);
    }

    /// <summary>"Values = LineName, HeaderToken, …" under a line, in the code font.</summary>
    public static void AddList(StackPanel tip, string label, IEnumerable<string> names)
    {
        string list = string.Join(", ", names);
        if (list != "") AddUnderLine(tip, Code(label, list, joiner: " = "));
    }

    /// <summary>One line in the code font: the key in the settings' colour, then the joiner and the value.</summary>
    private static TextBlock Code(string key, string value, string joiner)
    {
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        var keyRun = new Run(key);
        keyRun.SetResourceReference(TextElement.ForegroundProperty, "s-field");
        line.Inlines.Add(keyRun);
        line.Inlines.Add(new Run(joiner + value));
        return line;
    }
}
