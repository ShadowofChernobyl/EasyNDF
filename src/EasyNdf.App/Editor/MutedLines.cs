using System.Windows;
using System.Windows.Media;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>"Changes only" shows the "⋯" where lines are left out in the dim colour (the HTML app's <c>.edl.gap</c>).</summary>
/// <param name="marks">Each line's mark, by line number from 0.</param>
public sealed class MutedLines(IReadOnlyList<LineMark> marks) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        int index = line.LineNumber - 1;
        if (index >= marks.Count || marks[index] != LineMark.Gap || line.Length == 0) return;
        var muted = (Brush)Application.Current.Resources["muted"];
        ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(muted));
    }
}
