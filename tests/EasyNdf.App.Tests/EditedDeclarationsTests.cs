using EasyNdf.App.Editor;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Tests;

/// <summary>
/// A large file in the editor is read again only where it was typed in: the declarations around each change, whole,
/// from their declaration line to the next one.
/// </summary>
public sealed class EditedDeclarationsTests
{
    private const string Text =
        "export A is T\n(\n    X = 1\n)\n" +
        "export B is T\n(\n    Y = 2\n)\n" +
        "C is 3\n";

    [Fact]
    public void Nothing_read_yet_reads_the_whole_text()
    {
        var document = new TextDocument(Text);
        var edited = new EditedDeclarations();
        edited.Follow(document);
        document.Insert(Text.IndexOf("Y = 2", StringComparison.Ordinal) + 5, "0");
        Assert.Null(edited.Take());
    }

    [Fact]
    public void A_change_inside_a_declaration_reads_that_declaration_alone()
    {
        var document = new TextDocument(Text);
        var edited = new EditedDeclarations();
        edited.Follow(document);
        edited.ReadWhole();
        document.Insert(Text.IndexOf("Y = 2", StringComparison.Ordinal) + 5, "0");
        List<(int Start, int End)> stretches = Assert.IsType<List<(int Start, int End)>>(edited.Take());
        (int start, int end) = Assert.Single(stretches);
        Assert.Equal("export B is T\n(\n    Y = 20\n)\n", document.GetText(start, end - start));
    }

    [Fact]
    public void Changes_in_two_declarations_read_both_and_done_forgets_them()
    {
        var document = new TextDocument(Text);
        var edited = new EditedDeclarations();
        edited.Follow(document);
        edited.ReadWhole();
        document.Insert(Text.IndexOf("X = 1", StringComparison.Ordinal) + 5, "0");
        document.Insert(document.Text.IndexOf("C is 3", StringComparison.Ordinal) + 5, "3");
        List<(int Start, int End)> stretches = edited.Take()!;
        Assert.Equal(["export A is T\n(\n    X = 10\n)\n", "C is 33\n"], stretches.Select(s => document.GetText(s.Start, s.End - s.Start)));
        edited.Done(edited.Count);
        Assert.Empty(edited.Take()!);
    }
}
