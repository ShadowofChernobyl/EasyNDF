using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Pages;

/// <summary>
/// While a file loads in the Editor (opened, its export shown, its changes only, back to its source), the loading card
/// covers the app (see <see cref="LoadingCover"/>): what loads, from where or with which rules, the bar and the step under
/// way.
/// </summary>
public partial class EditorPage
{
    /// <summary>A text this long (in characters) holds the screen while it is shown: the card shows at once.</summary>
    private const int BigText = 2_000_000;

    private LoadingCover? loadingCover;

    private LoadingCover Loading => loadingCover ??= new LoadingCover(this);

    private int StartLoading(string title, string where, params string[] steps) => Loading.Start(title, where, steps);

    private void ShowLoadingStep(int load, int step) => Loading.Step(load, step);

    /// <summary>A step that holds the screen while a text of this length is laid out.</summary>
    private Task ShowStepThatHolds(int load, int step, int textLength) => Loading.StepThatHolds(load, step, textLength >= BigText);

    private void EndLoading(int load) => Loading.End(load);

    /// <summary>The load whose card stays until the descriptor asked for is shown (see <see cref="HoldUntilFound"/>); 0 for none.</summary>
    private int findingLoad;

    private System.Windows.Threading.DispatcherTimer? findingTooLong;

    /// <summary>
    /// A file was opened to show a descriptor: its card stays, on its last step, until the descriptor is shown
    /// (<see cref="EndFinding"/>), so the text is not seen at its first line and then again at the descriptor.
    /// </summary>
    private void HoldUntilFound(int load, int step)
    {
        EndFinding();
        findingLoad = load;
        ShowLoadingStep(load, step);
        // whatever happens to the descriptor asked for, the card does not stay over the text
        findingTooLong ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        findingTooLong.Tick -= FindingTookTooLong;
        findingTooLong.Tick += FindingTookTooLong;
        findingTooLong.Stop();
        findingTooLong.Start();
    }

    private void FindingTookTooLong(object? sender, EventArgs e) => EndFinding();

    /// <summary>The descriptor asked for is shown, or will not be: the card kept for it goes.</summary>
    private void EndFinding()
    {
        findingTooLong?.Stop();
        if (findingLoad == 0) return;
        int load = findingLoad;
        findingLoad = 0;
        EndLoading(load);
    }

    /// <summary>
    /// Shows the loading card, with the Editor tab in front, while a file not on the Files tab yet is added from the
    /// Source: the Armory's "Show in File Editor" would else seem to do nothing until the file is there. With no file
    /// added, the tab shown before comes back.
    /// </summary>
    internal async Task<FileEntry?> AddingFromSource(string file, Func<Task<FileEntry?>> add)
    {
        MainView main = MainView.Of(this);
        object? before = main.Tabs.SelectedItem;
        main.ShowTab(main.EditorTabItem);
        int loading = StartLoading(Texts.Get("Editor.Loading.Opening", ("file", file)), "", Texts.Get("Editor.Loading.Adding"));
        FileEntry? added = null;
        try { added = await add(); }
        finally
        {
            EndLoading(loading);
            if (added is null && main.Tabs.SelectedItem == main.EditorTabItem) main.Tabs.SelectedItem = before;
        }
        return added;
    }
}
