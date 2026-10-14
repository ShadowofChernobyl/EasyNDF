using System.IO;
using EasyNdf.Data.FileList;


namespace EasyNdf.App.Files;

/// <summary>
/// Two files of the same name side by side, for the question about a file already in the list (the HTML app's
/// <c>compareFiles</c>): their sizes, dates and line counts, and whether their contents are the same.
/// </summary>
public sealed class FileComparison
{
    /// <summary>One line of the comparison: what is compared, and each file's value.</summary>
    public sealed record Line(string Label, string Listed, string Picked)
    {
        /// <summary>Whether the two values are the same.</summary>
        public bool IsSame => Listed == Picked;
    }

    /// <summary>Whether the contents are the same, in a sentence.</summary>
    public string Verdict { get; }

    /// <summary>Size, Modified and Lines.</summary>
    public IReadOnlyList<Line> Lines { get; }

    /// <summary>Compares the file in the list with the one just picked.</summary>
    public FileComparison(FileEntry listed, FileToAdd picked)
    {
        string? listedText = ReadOrNull(() => FileContent.Text(listed)), pickedText = ReadOrNull(() => FileContent.Text(picked));
        Verdict = listedText is null || pickedText is null
            ? "One of the two files could not be read, so their contents could not be compared."
            : listedText == pickedText ? "The two files have identical contents." : "The two files have different contents.";
        Lines =
        [
            new Line("Size", SizeText(() => FileContent.Bytes(listed).LongLength), SizeText(() => picked.Size)),
            new Line("Modified", DateText(() => listed.ArchivedContent is null ? File.GetLastWriteTime(listed.Source!) : listed.Modified ?? DateTime.MinValue),
                DateText(() => picked.Modified)),
            new Line("Lines", LineCount(listedText), LineCount(pickedText)),
        ];
    }

    private static string? ReadOrNull(Func<string> read)
    {
        try { return read(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    // Size and date as they are now: the listed file may have changed on disk since it was added.
    private static string SizeText(Func<long> size)
    {
        try { return FileStatus.Count(size()) + " bytes"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return "unknown"; }
    }

    private static string DateText(Func<DateTime> date)
    {
        try { return date().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return "unknown"; }
    }

    /// <summary>Lines as the HTML app counts them: one more than the line breaks, or none for an empty file.</summary>
    private static string LineCount(string? text)
    {
        if (text is null) return "could not be read";
        int count = text.Length > 0 ? 1 : 0;
        foreach (char character in text)
            if (character == '\n') count++;
        return FileStatus.Count(count);
    }
}
