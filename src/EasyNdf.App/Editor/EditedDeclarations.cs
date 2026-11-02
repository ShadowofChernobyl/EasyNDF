using ICSharpCode.AvalonEdit.Document;
using EasyNdf.Engine.Editing;

namespace EasyNdf.App.Editor;

/// <summary>
/// Where a text was typed in since it was last read, so only the declarations typed in are read again: Steel Division
/// 2's UniteDescriptor.ndf is 117 MB, and reading it whole after each pause in typing took seconds and gigabytes. Each
/// change leaves a mark that moves with the text; <see cref="Take"/> gives the declarations around the marks, whole.
/// </summary>
public sealed class EditedDeclarations
{
    /// <summary>A text this large is read again a declaration at a time; a smaller one whole, as it is fast.</summary>
    public const int LargeText = 2_000_000;

    /// <summary>More changes than this since the last read (a replace all, a paste over much of it): the text is read whole.</summary>
    private const int MostChanges = 300;

    /// <summary>How many lines a declaration is looked through for its start or end before the text is read whole instead.</summary>
    private const int MostLines = 50_000;

    private readonly List<TextAnchor> marks = [];
    private TextDocument? document;

    /// <summary>Whether too much changed to read declarations one by one: the next read reads the whole text.</summary>
    private bool tooMuch = true;

    /// <summary>Follows a text from now on, as last read whole; null follows none.</summary>
    public void Follow(TextDocument? text)
    {
        if (document is not null) document.Changed -= Changed;
        document = text;
        Forget();
        if (document is not null) document.Changed += Changed;
    }

    /// <summary>The text was read whole: nothing is left to read again.</summary>
    public void ReadWhole()
    {
        marks.Clear();
        tooMuch = false;
    }

    /// <summary>Nothing read yet: the next read reads the whole text.</summary>
    public void Forget()
    {
        marks.Clear();
        tooMuch = true;
    }

    private void Changed(object? sender, DocumentChangeEventArgs e)
    {
        if (tooMuch || document is null) return;
        if (marks.Count >= MostChanges)
        {
            tooMuch = true;
            marks.Clear();
            return;
        }
        // a mark at each end of what was put in, so a change across declarations reads them all
        marks.Add(document.CreateAnchor(e.Offset));
        if (e.InsertionLength > 0) marks.Add(document.CreateAnchor(Math.Min(document.TextLength, e.Offset + e.InsertionLength)));
    }

    /// <summary>
    /// The declarations typed in since the last read, each from the start of its first line to the start of the next
    /// declaration (or the end of the text), joined where they meet, in order; null when the text should be read
    /// whole (nothing read yet, too much changed, or a declaration too long to find the ends of). The marks are kept
    /// until <see cref="Done"/>, so a read the text changed under is made again.
    /// </summary>
    public List<(int Start, int End)>? Take()
    {
        if (tooMuch || document is null) return null;
        var stretches = new List<(int Start, int End)>();
        foreach (TextAnchor mark in marks)
        {
            if (mark.IsDeleted) continue;
            if (Around(document, mark.Offset) is not (int, int) stretch) return null;
            stretches.Add(stretch);
        }
        stretches.Sort();
        var joined = new List<(int Start, int End)>();
        foreach ((int start, int end) in stretches)
        {
            if (joined.Count > 0 && start <= joined[^1].End) joined[^1] = (joined[^1].Start, Math.Max(joined[^1].End, end));
            else joined.Add((start, end));
        }
        return joined;
    }

    /// <summary>The declarations <see cref="Take"/> gave were read: their marks go, those of changes made since stay.</summary>
    /// <param name="taken">How many marks there were when they were taken.</param>
    public void Done(int taken) => marks.RemoveRange(0, Math.Min(taken, marks.Count));

    /// <summary>How many marks there are now, to give <see cref="Done"/>.</summary>
    public int Count => marks.Count;

    /// <summary>The declaration a place is in: from its declaration line to the next one; null when either is too far to find.</summary>
    private static (int Start, int End)? Around(TextDocument text, int offset)
    {
        DocumentLine at = text.GetLineByOffset(Math.Min(offset, text.TextLength));
        DocumentLine? first = at;
        for (int looked = 0; first is not null && !StartsDeclaration(text, first); looked++)
        {
            if (looked > MostLines) return null;
            first = first.PreviousLine;
        }
        DocumentLine? next = at.NextLine;
        for (int looked = 0; next is not null && !StartsDeclaration(text, next); looked++)
        {
            if (looked > MostLines) return null;
            next = next.NextLine;
        }
        return (first?.Offset ?? 0, next?.Offset ?? text.TextLength);
    }

    /// <summary>Whether a line starts a declaration: it starts with a letter, and reads as one (a cheap look first, as most lines are indented).</summary>
    private static bool StartsDeclaration(TextDocument text, DocumentLine line) =>
        line.Length > 0 && (char.IsAsciiLetter(text.GetCharAt(line.Offset)) || text.GetCharAt(line.Offset) == '_') &&NdfChecker.StartsDeclaration(text.GetText(line.Offset, Math.Min(line.Length, 200)));
}
