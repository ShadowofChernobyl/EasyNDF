using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using EasyNdf.App.Files;
using EasyNdf.App.Rules;
using EasyNdf.Data.FileList;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// Search all files (the HTML app's <c>edAllRun</c>): every file on the Files tab is read and searched, without regard
/// to capitals, and each line found is listed; clicking one opens its file at that line.
/// </summary>
public partial class EditorPage
{
    /// <summary>How many lines found are listed at most.</summary>
    private const int MostResults = 500;

    /// <summary>How much of a line found is listed.</summary>
    private const int LongestResult = 240;

    /// <summary>Counts the searches, so a search started again stops the one before.</summary>
    private int searchNumber;

    /// <summary>What the last search looked for, which Find takes when a result is opened.</summary>
    private string searchedFor = "";

    /// <summary>A line found: its file as the list names it, its number, and its text with the leading spaces left out.</summary>
    public sealed record SearchResult(FileEntry Entry, string File, int Line, string Text)
    {
        /// <summary>The number as shown.</summary>
        public string Number => Line.ToString(CultureInfo.InvariantCulture);

        /// <summary>The numbers' column: six digits wide at least (the HTML app's <c>min-width:6ch</c>).</summary>
        public double NumberWidth => SixDigits.Value;

        private static readonly Lazy<double> SixDigits = new(() =>
            new System.Windows.Media.FormattedText("000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface((System.Windows.Media.FontFamily)Application.Current.Resources["mono"], FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                12, System.Windows.Media.Brushes.Black, 1).WidthIncludingTrailingWhitespace);
    }

    private async void SearchAllKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAll();
    }

    private async void SearchAllClicked(object sender, RoutedEventArgs e) => await SearchAll();

    private void CloseResultsClicked(object sender, RoutedEventArgs e)
    {
        searchNumber++;
        SearchResults.Visibility = Visibility.Collapsed;
        resultsHiddenWithBar = false;
    }

    /// <summary>Searches every file on the Files tab, one at a time, saying how far it has got.</summary>
    internal async Task SearchAll()
    {
        string wanted = SearchAllBox.Text;
        if (wanted.Trim() == "") return;
        int mine = ++searchNumber;
        List<FileEntry> files = [.. (Files?.Entries ?? []).Where(f => f.Source is not null)
            .OrderBy(FileListModel.ScanKey, Comparer<string>.Create(PropertyScans.CompareByFileName))];
        SearchResults.Visibility = Visibility.Visible;
        ResultList.ItemsSource = null;
        ResultsArea.Visibility = Visibility.Visible;
        searchedFor = wanted;
        if (files.Count == 0)
        {
            SearchNote.Text = Texts.Get("Editor.Search.NoFiles");
            return;
        }

        var names = new PropertyScans(AppData.Current.Scans.Scans, [.. files.Select(FileListModel.ScanKey)]);
        var results = new List<SearchResult>();
        int total = 0, filesWithLines = 0, unreadable = 0;
        for (int n = 0; n < files.Count; n++)
        {
            FileEntry entry = files[n];
            SearchNote.Text = "Searching " + (n + 1) + " of " + files.Count + ": " + entry.Name;
            string text;
            try { text = await Task.Run(() => FileContent.Text(entry)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                unreadable++;
                continue;
            }
            if (mine != searchNumber) return;
            int found = FindLines(text, wanted, (line, lineText) =>
            {
                if (results.Count < MostResults) results.Add(new SearchResult(entry, names.Label(FileListModel.ScanKey(entry)), line, Engine.Text.JsText.TrimStart(lineText)));
            });
            total += found;
            if (found > 0) filesWithLines++;
        }

        ResultsArea.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultList.ItemsSource = results;
        string notRead = unreadable > 0 ? " " + Texts.Get("Editor.Search.NotRead", ("count", unreadable)) : "";
        if (total == 0)
        {
            SearchNote.Text = Texts.Get("Editor.Search.NothingFound", ("text", wanted), ("files", Texts.CountWide("Editor.Search.Files", files.Count))) + notRead;
            return;
        }
        string foundSaid = Texts.Get("Editor.Search.Found", ("text", wanted),
            ("lines", Texts.CountWide("Editor.Search.Lines", total)), ("files", Texts.CountWide("Editor.Search.Files", filesWithLines)));
        string first = total > MostResults ? " " + Texts.Get("Editor.Search.ShowingFirst", ("count", MostResults)) : "";
        SearchNote.Text = foundSaid + first + notRead + " " + Texts.Get("Editor.Search.ClickLine");
    }

    /// <summary>
    /// Finds the lines of a text that hold what is looked for, once each, and hands each one's number and text (up to
    /// 240 characters) to <paramref name="foundLine"/>. Returns how many there are.
    /// </summary>
    private static int FindLines(string text, string wanted, Action<int, string> foundLine)
    {
        int count = 0, line = 1, lineStart = 0;
        int at = text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase);
        while (at >= 0)
        {
            int lineEnd = text.IndexOf('\n', lineStart);
            while (lineEnd >= 0 && lineEnd < at)
            {
                line++;
                lineStart = lineEnd + 1;
                lineEnd = text.IndexOf('\n', lineStart);
            }
            int end = lineEnd < 0 ? text.Length : lineEnd;
            string lineText = text[lineStart..end];
            if (lineText.EndsWith('\r')) lineText = lineText[..^1];
            foundLine(line, lineText.Length > LongestResult ? lineText[..LongestResult] : lineText);
            count++;
            at = end >= text.Length ? -1 : text.IndexOf(wanted, end, StringComparison.OrdinalIgnoreCase);
        }
        return count;
    }

    /// <summary>The results' right-click menu: on a line, open it, or copy it or its file's name; and close the results.</summary>
    private void ResultsRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SearchResult? result = null;
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null && result is null; at = at is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            result = (at as FrameworkElement)?.DataContext as SearchResult;
        List<Controls.MenuChoice?> items = [];
        if (result is not null)
        {
            items.Add(new Controls.MenuChoice(Texts.Get("Editor.Search.OpenAtLine"), () => _ = OpenResult(result)));
            items.Add(new Controls.MenuChoice(Texts.Get("Editor.CopyThisLine"), () => Copy(result.Text)));
            items.Add(new Controls.MenuChoice(Texts.Get("Editor.Search.CopyFileName"), () => Copy(result.Entry.Name)));
            items.Add(Controls.Menus.Line);
        }
        items.Add(new Controls.MenuChoice(Texts.Get("Editor.Search.CloseResults"), () => CloseResultsClicked(this, e)));
        Controls.Menus.Show(ResultsBox, items);
    }

    /// <summary>A line found opens its file (the file itself, not its export) at that line, with Find set to what was searched for.</summary>
    private async void ResultClicked(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SearchResult result) await OpenResult(result);
    }

    internal async Task OpenResult(SearchResult result)
    {
        if (Files is null || !Files.Entries.Contains(result.Entry)) return;
        if (shown is { IsExport: true } && shown.Entry == result.Entry) Swap(shown.File);
        else if (shown?.Entry != result.Entry) await Open(result.Entry);
        if (shown?.Entry != result.Entry) return;
        FindBox.Text = searchedFor;
        PointAt(result.Line - 1);
    }
}
