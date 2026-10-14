using System.Collections;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Editor;

/// <summary>
/// The lines of an editor's text as a list, read from the text when asked for, so the folding can look at a few lines
/// of a long file without copying them all.
/// </summary>
/// <param name="document">The text.</param>
public sealed class DocumentLines(TextDocument document) : IReadOnlyList<string>
{
    /// <summary>A line, counting from 0, without its line end.</summary>
    public string this[int index] => document.GetText(document.GetLineByNumber(index + 1));

    /// <summary>How many lines there are.</summary>
    public int Count => document.LineCount;

    public IEnumerator<string> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
