using System.IO;
using System.Windows;
using System.Windows.Threading;
using EasyNdf.App.Editor;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Viewer;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The descriptor viewer's property filter (the HTML app's <c>dfIndex</c>, <c>dfPass</c>, <c>dfBtn</c>, <c>dfProps</c>
/// and <c>dfCount</c>): the filter button beside the name filter, and what the Filter Descriptors window needs.
/// </summary>
public partial class EditorPage : IDescriptorFilterOwner
{
    /// <summary>The filter, as it was left last time.</summary>
    public DescriptorFilterState Filter { get; set; } = DescriptorFilterState.Load();

    /// <summary>Each text's filter index, by "open" or a file's number, kept while the text is the same.</summary>
    private readonly Dictionary<string, (string Text, Dictionary<string, IndexEntry> ByName)> filterIndexes = [];

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

    /// <summary>A text's filter index (the HTML app's <c>dfIndex</c>): its descriptors and their properties at every depth, the first of each name.</summary>
    private async Task<Dictionary<string, IndexEntry>> FilterIndex(string key, string text)
    {
        if (filterIndexes.TryGetValue(key, out var known) && known.Text == text) return known.ByName;
        List<IndexEntry> entries = await Task.Run(() => DescriptorViewer.Index(text));
        var byName = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        foreach (IndexEntry entry in entries) byName.TryAdd(entry.Name, entry);
        filterIndexes[key] = (text, byName);
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
            if (!showsAllFiles && pin is not null) AddAll(await FilterIndex(IndexKey(pin), (await ReadOtherFile(pin)).Text));
            else if (shown is not null && outlineText != "") AddAll(await FilterIndex("open", outlineText));
            if (showsAllFiles)
                foreach (FileEntry entry in ReferenceIndex.Of([.. Files?.Entries ?? []]).Files)
                    if (!IsLocal(entry)) AddAll(await FilterIndex(IndexKey(entry), (await ReadOtherFile(entry)).Text));
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
