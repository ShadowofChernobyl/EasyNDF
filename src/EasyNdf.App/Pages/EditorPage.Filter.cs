using System.IO;
using System.Windows;
using System.Windows.Threading;
using EasyNdf.App.Editor;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Viewer;
using EasyNdf.App.Text;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Pages;

/// <summary>
/// The descriptor viewer's property filter (the HTML app's <c>dfIndex</c>, <c>dfPass</c>, <c>dfBtn</c>, <c>dfProps</c>
/// and <c>dfCount</c>): the filter button beside the name filter, and what the Filter Descriptors window needs.
/// </summary>
public partial class EditorPage : IDescriptorFilterOwner
{
    /// <summary>The filter, as it was left last time.</summary>
    public DescriptorFilterState Filter { get; set; } = DescriptorFilterState.Load();

    /// <summary>
    /// Each text's filter index, by "open" or a file's number, kept while the text is the same: the same snapshot of the
    /// editor's text, or the same text of another file. That is held weakly: with "All files", a strong hold would keep
    /// every read file's text in memory as long as the app runs.
    /// </summary>
    private readonly Dictionary<string, (WeakReference<object> Text, Dictionary<string, IndexEntry> ByName)> filterIndexes = [];

    /// <summary>The tree is filtered again a moment after the filter stops changing.</summary>
    private readonly DispatcherTimer filterLater = new() { Interval = TimeSpan.FromMilliseconds(150) };

    /// <summary>Sets up the filter button when the page is built.</summary>
    private void StartFilter()
    {
        filterLater.Tick += (_, _) =>
        {
            filterLater.Stop();
            _ = RebuildTree();
        };
        ShowFilterButton();
    }

    /// <summary>The filter changed (the HTML app's <c>dfChanged</c>): it is kept, the button shows it, and the tree follows.</summary>
    public void FilterChanged()
    {
        Filter.Save();
        ShowFilterButton();
        filterLater.Stop();
        filterLater.Start();
    }

    /// <summary>
    /// The filter button (the HTML app's <c>dfBtn</c>): pressed in, in the accent colour and with how many conditions,
    /// while the filter is on.
    /// </summary>
    private void ShowFilterButton()
    {
        int on = Filter.Rules.Count(r => r.IsComplete);
        FilterButton.IsChecked = on > 0;
        FilterBadge.Text = on > 0 ? on.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        FilterBadge.Visibility = on > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterButton.ToolTip = on > 0 ? Texts.Count("Editor.FilterButton.On", on) : Texts.Get("Editor.Filter.Tip");
    }

    /// <summary>The filter button opens the Filter Descriptors window, or closes it.</summary>
    private void FilterClicked(object sender, RoutedEventArgs e)
    {
        ShowFilterButton();
        var window = MainView.Of(this).Filters;
        if (window.IsOpen) window.Close();
        else _ = window.Open(this);
    }

    /// <summary>The filter index of the text the descriptors of the open file were read from.</summary>
    private Task<Dictionary<string, IndexEntry>> OpenFileFilterIndex()
    {
        ITextSource text = outlineText;
        return FilterIndex("open", text, () => text.Text);
    }

    /// <summary>The filter index of another file's text.</summary>
    private Task<Dictionary<string, IndexEntry>> OtherFileFilterIndex(FileEntry entry, OtherFile file) =>
        FilterIndex(IndexKey(entry), file.Text, () => file.Text);

    /// <summary>
    /// A text's filter index (the HTML app's <c>dfIndex</c>): its descriptors and their properties at every depth, the
    /// first of each name. It is made again only when <paramref name="text"/> is no longer the same object.
    /// </summary>
    private async Task<Dictionary<string, IndexEntry>> FilterIndex(string key, object text, Func<string> wholeText)
    {
        if (filterIndexes.TryGetValue(key, out var known) && known.Text.TryGetTarget(out object? indexed) && ReferenceEquals(indexed, text)) return known.ByName;
        List<IndexEntry> entries = await Task.Run(() => DescriptorViewer.Index(wholeText()));
        var byName = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        foreach (IndexEntry entry in entries) byName.TryAdd(entry.Name, entry);
        filterIndexes[key] = (new WeakReference<object>(text), byName);
        return byName;
    }

    /// <summary>The key a file's filter index is kept under.</summary>
    private static string IndexKey(FileEntry entry) => entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The rows that pass the filter, by the index of their file.</summary>
    private List<TreeNode> Passing(IEnumerable<TreeNode> rows, Dictionary<string, IndexEntry> index) =>
        [.. rows.Where(row => Filter.Passes(index.GetValueOrDefault(row.Name)))];

    /// <summary>The properties the descriptors being viewed have (the HTML app's <c>dfProps</c>), sorted, after "(Name)" and "(Type)".</summary>
    public async Task<List<string>> FilterProperties()
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        void AddAll(Dictionary<string, IndexEntry> index)
        {
            foreach (IndexEntry entry in index.Values)
                foreach ((string name, _) in entry.Properties) found.Add(name);
        }
        try
        {
            FileEntry? pin = pinnedFile is not null && !IsLocal(pinnedFile) ? pinnedFile : null;
            if (!showsAllFiles && pin is not null) AddAll(await OtherFileFilterIndex(pin, await ReadOtherFile(pin)));
            else if (shown is not null && outlineText.TextLength > 0) AddAll(await OpenFileFilterIndex());
            if (showsAllFiles)
                foreach (FileEntry entry in ReferenceIndex.Of([.. Files?.Entries ?? []]).Files)
                    if (!IsLocal(entry)) AddAll(await OtherFileFilterIndex(entry, await ReadOtherFile(entry)));
        }
        catch (Exception problem) when (problem is NdfParseException or IOException or UnauthorizedAccessException) { }
        return ["(Name)", "(Type)", .. found.Order(StringComparer.CurrentCulture)];
    }

    /// <summary>How many descriptors the filter shows (the HTML app's <c>dfCount</c>).</summary>
    public (string Before, string Bold, string After) FilterCount()
    {
        string text = showsAllFiles
            ? Texts.Count("Editor.FilterCount.AllFiles", treeRoots.Count, ("count", Texts.Number(treeRoots.Count)))
            : Texts.Get("Editor.FilterCount.ThisFile", ("count", Texts.Number(treeRoots.Count)), ("total", Texts.Number(descriptorTotal)));
        // the text marks the number in bold with **, which splits it into the three parts
        string[] parts = text.Split("**");
        return parts.Length == 3 ? (parts[0], parts[1], parts[2]) : ("", text, "");
    }
}
