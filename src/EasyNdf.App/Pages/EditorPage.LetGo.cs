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
/// A large file in the editor is let go of while another tab of the app is in front (option A of the
/// memory report): the editor keeps a record of every line of a text, some 150 MB for UniteDescriptor.ndf's 711,000,
/// which stays in memory for as long as the file is open. A while after the Editor tab is left, each open unedited file
/// of the Files tab larger than 2 MB is put away, keeping where it was scrolled to, the caret and the blocks folded;
/// coming back to the tab reads again the ones shown, which takes a second or two, and the others when their tab is
/// shown. A file with edits not used in the file list, an export and a small file stay as they are.
/// </summary>
public partial class EditorPage
{
    /// <summary>How long after the tab is left the file is let go of, so a quick look at another tab costs nothing.</summary>
    private static readonly TimeSpan LetGoAfter = TimeSpan.FromSeconds(20);

    /// <summary>Counts the time from the tab being left; stopped when it comes back.</summary>
    private DispatcherTimer? letGoTimer;

    /// <summary>A file put away: which it was, and where the user was in it.</summary>
    private sealed record LetGoFile(FileEntry Entry, double Top, int TopLine, int Caret, List<(int Start, int End)> Folds);

    /// <summary>The Editor tab was left: the files are let go of after <see cref="LetGoAfter"/>, unless the tab comes back first.</summary>
    public void TabLeft()
    {
        letGoTimer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher) { Interval = LetGoAfter };
        letGoTimer.Tick -= LetGoNow;
        letGoTimer.Tick += LetGoNow;
        letGoTimer.Stop();
        letGoTimer.Start();
    }

    /// <summary>Every open file that may be let go of is (one in each tab), then the memory is given back once.</summary>
    private void LetGoNow(object? sender, EventArgs e)
    {
        letGoTimer?.Stop();
        if (IsVisible) return;
        bool any = false;
        foreach (FileView v in openViews) any |= LetGo(v);
        if (any) Memory.GiveBack();
    }

    /// <summary>Lets go of a tab's file, when it is a large unedited file of the Files tab; whether it did.</summary>
    private bool LetGo(FileView v)
    {
        if (v.Shown is not { IsFile: true, IsEdited: false, Parent: null, Entry: FileEntry entry } open
            || open.Document.TextLength <= EditedDeclarations.LargeText || Files is null || !Files.Entries.Contains(entry)) return false;
        PutFoldingAway(v);
        var kept = new LetGoFile(entry, v.Editor.VerticalOffset, PlaceOf(v).Line, v.Editor.CaretOffset, open.Folds);
        v.Shown = null;
        v.LetGo = kept;
        ShowText(v);
        // the editor's own record of the lines goes with its document
        puttingIn = true;
        try { v.Editor.Document = new TextDocument(); }
        finally { puttingIn = false; }
        return true;
    }

    /// <summary>
    /// The Editor tab is in front again: the files let go of that are shown are read again, each where it was; the others
    /// once their tab is shown (see <see cref="ShowSideBySide"/>).
    /// </summary>
    private async Task BringBackLetGo()
    {
        letGoTimer?.Stop();
        foreach (FileView v in shownViews.ToList())
            if (v.LetGo is not null && v.Shown is null) await BringBack(v);
    }

    /// <summary>Reads a tab's file let go of again and shows it where it was, unless another file was put in that tab meanwhile.</summary>
    private async Task BringBack(FileView v)
    {
        if (v.LetGo is not LetGoFile kept) return;
        if (Files is null || !Files.Entries.Contains(kept.Entry))
        {
            CloseNow(v);
            return;
        }
        int loads = v.Loads;
        if (v == view) Note.Text = Texts.Get("Editor.Viewer.ReadingFile", ("file", kept.Entry.Name));
        try
        {
            string text = await Task.Run(() => FileContent.Text(kept.Entry));
            if (v.LetGo != kept || v.Loads != loads) return;
            await LoadAside(v, kept.Entry.Name, text, kept.Entry, kept.Folds);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Show();
            return;
        }
        if (v.Shown?.Entry != kept.Entry) return;
        v.Editor.CaretOffset = Math.Min(kept.Caret, v.Editor.Document.TextLength);
        // the caret moved puts the line on screen; the old place is put back once laid out
        Dispatcher.BeginInvoke(() => v.Editor.ScrollToVerticalOffset(kept.Top), DispatcherPriority.Loaded);
    }

    /// <summary>For the checks: the files let go of at once, as if the tab had been left a while ago.</summary>
    internal void LetGoNowForChecks() => LetGoNow(null, EventArgs.Empty);

    /// <summary>For the checks: the files brought back, as coming back to the tab does.</summary>
    internal Task BringBackForChecks() => BringBackLetGo();
}
