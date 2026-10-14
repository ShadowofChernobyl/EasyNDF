using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Editor;

/// <summary>
/// The Theme Designer's sample of "Changes only" (the HTML app's <c>#pvDiff</c>), drawn with the File Editor's own parts:
/// the shown line numbers, the + and − marks, the faint backgrounds of added and removed lines, and the NDF colours. It
/// cannot be edited.
/// </summary>
public sealed class ChangesSample : ContentControl
{
    private readonly TextEditor editor = new();

    /// <summary>Shows the sample lines, each with the number shown for it and its mark.</summary>
    public ChangesSample(IReadOnlyList<(string Number, LineMark Mark, string Text)> lines)
    {
        var document = new TextDocument(string.Join("\n", lines.Select(line => line.Text)));
        List<LineMark> marks = [.. lines.Select(line => line.Mark)];
        editor.Document = document;
        editor.IsReadOnly = true;
        editor.SetResourceReference(Control.FontFamilyProperty, "mono");
        editor.SetResourceReference(Control.BackgroundProperty, "well");
        editor.SetResourceReference(Control.ForegroundProperty, "ink");
        editor.FontSize = 12;
        editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        editor.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.TextArea.SetResourceReference(ICSharpCode.AvalonEdit.Editing.TextArea.SelectionBrushProperty, "sel");
        editor.TextArea.SelectionForeground = null;
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.SelectionCornerRadius = 0;
        editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
        editor.TextArea.TextView.LineTransformers.Add(new NdfColourizer(document));
        editor.TextArea.TextView.LineTransformers.Add(new MutedLines(marks));
        editor.TextArea.TextView.BackgroundRenderers.Add(new LineShading { Marks = marks });
        editor.TextArea.TextView.ElementGenerators.Add(new LineHeight());
        // the number and its mark fill the HTML app's 5ch, and the text follows with no gap
        editor.TextArea.LeftMargins.Insert(0, new LineNumbers { NumberOf = number => lines[number - 1].Number, Digits = 2 });
        editor.TextArea.LeftMargins.Insert(1, new MarkColumn { Marks = marks });
        editor.TextArea.LeftMargins.Insert(2, new Border { Width = 4 });
        Loaded += (_, _) => Theming.ThemeManager.Applied += RedrawInTheme;
        Unloaded += (_, _) => Theming.ThemeManager.Applied -= RedrawInTheme;
        var box = new Bevel { Child = editor, Height = lines.Count * 17 + 2 };
        box.SetResourceReference(Bevel.TopLeftProperty, "lo");
        box.SetResourceReference(Bevel.BottomRightProperty, "hi");
        box.SetResourceReference(Bevel.BackgroundProperty, "well");
        Content = box;
    }

    /// <summary>A new theme: the colours, numbers and marks are drawn again in it.</summary>
    private void RedrawInTheme()
    {
        editor.TextArea.TextView.Redraw();
        foreach (UIElement margin in editor.TextArea.LeftMargins) margin.InvalidateVisual();
    }
}
