using System.IO;
using System.Windows;
using System.Windows.Threading;
using EasyNdf.App.Editor;
using EasyNdf.App.Files;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Pages;

/// <summary>
/// A large file in the editor is let go of while another tab is in front (Paul's choice, option A of the memory
/// report): the editor keeps a record of every line of a text, some 150 MB for UniteDescriptor.ndf's 711,000, which
/// stays in memory for as long as the file is open. A while after the Editor tab is left, an unedited file of the Files
/// tab larger than 2 MB is put away, keeping where it was scrolled to, the caret and the blocks folded; coming back to
/// the tab reads it again, which takes a second or two. A file with edits not used in the file list, an export and a
/// small file stay as they are.
/// </summary>
public partial class EditorPage
{
    /// <summary>How long after the tab is left the file is let go of, so a quick look at another tab costs nothing.</summary>
    private static readonly TimeSpan LetGoAfter = TimeSpan.FromSeconds(20);

    /// <summary>Counts the time from the tab being left; stopped when it comes back.</summary>
    private DispatcherTimer? letGoTimer;

    /// <summary>The file let go of, with where it was; null while nothing is.</summary>
    private LetGoFile? letGo;

    /// <summary>A file put away: which it was, and where the user was in it.</summary>
    private sealed record LetGoFile(FileEntry Entry, double Top, int TopLine, int Caret, List<(int Start, int End)> Folds);

    /// <summary>The Editor tab was left: the file is let go of after <see cref="LetGoAfter"/>, unless the tab comes back first.</summary>
    public void TabLeft()
    {
        letGoTimer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher) { Interval = LetGoAfter };
        letGoTimer.Tick -= LetGoNow;
        letGoTimer.Tick += LetGoNow;
        letGoTimer.Stop();
        letGoTimer.Start();
    }

    private void LetGoNow(object? sender, EventArgs e)
    {
        letGoTimer?.Stop();
        if (IsVisible || shown is not { IsFile: true, IsEdited: false, Parent: null, Entry: FileEntry entry } open
            || open.Document.TextLength <= EditedDeclarations.LargeText || Files is null || !Files.Entries.Contains(entry)) return;
        PutFoldingAway();
        letGo = new LetGoFile(entry, Text.VerticalOffset, TopLine, Text.CaretOffset, open.Folds);
        shown = null;
        ShowText();
        // the editor's own record of the lines goes with its document
        puttingIn = true;
        try { Text.Document = new TextDocument(); }
        finally { puttingIn = false; }
        Memory.GiveBack();
    }

    /// <summary>
    /// The Editor tab is in front again: the file let go of is read again and shown where it was, unless another file
    /// was opened meanwhile.
    /// </summary>
    private async Task BringBackLetGo()
    {
        letGoTimer?.Stop();
        if (letGo is not LetGoFile kept) return;
        letGo = null;
        if (shown is not null || Files is null || !Files.Entries.Contains(kept.Entry)) return;
        Note.Text = Texts.Get("Editor.Viewer.ReadingFile", ("file", kept.Entry.Name));
        try
        {
            string text = await Task.Run(() => FileContent.Text(kept.Entry));
            await LoadAside(kept.Entry.Name, text, kept.Entry, kept.Folds);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Show();
            return;
        }
        if (shown?.Entry != kept.Entry) return;
        Text.CaretOffset = Math.Min(kept.Caret, Text.Document.TextLength);
        // the caret moved puts the line on screen; the old place is put back once laid out
        Dispatcher.BeginInvoke(() => Text.ScrollToVerticalOffset(kept.Top), DispatcherPriority.Loaded);
    }

    /// <summary>For the checks: the file let go of at once, as if the tab had been left a while ago.</summary>
    internal void LetGoNowForChecks() => LetGoNow(null, EventArgs.Empty);

    /// <summary>For the checks: the file brought back, as coming back to the tab does.</summary>
    internal Task BringBackForChecks() => BringBackLetGo();

    /// <summary>The file of the Files tab open, or let go of for now, and its top line: kept for "Reopen the last file".</summary>
    private (FileEntry? Entry, int Line) OpenFilePlace => letGo is LetGoFile kept && shown is null ? (kept.Entry, kept.TopLine) : (shown?.Entry, TopLine);
}
