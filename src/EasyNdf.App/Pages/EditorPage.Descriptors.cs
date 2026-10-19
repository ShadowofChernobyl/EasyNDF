using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using EasyNdf.App.Editor;
using EasyNdf.App.Files;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Viewer;
using ICSharpCode.AvalonEdit.Document;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The descriptor viewer under the editor (the HTML app's <c>dv</c> and <c>tv</c>). Left: a tree of the descriptors,
/// of the file in the editor ("This file") or of the chains across every read file ("All files"); under a descriptor
/// come the objects and lists inside it, and the descriptors its values name. Right: the chosen descriptor's
/// properties, at every depth. A plain value can be typed over: the change is put into the text at the exact place the
/// value sits, so nothing else in the file moves, and it is first read back, so a value that would break the
/// descriptor is refused.
/// </summary>
public partial class EditorPage
{
    // ---- the open file's descriptors (the HTML app's dv) ----

    /// <summary>The descriptors of the text in the editor, and where each is (the HTML app's <c>dv.list</c>).</summary>
    private List<OutlineEntry> outline = [];

    /// <summary>
    /// The text they were read from (the HTML app's <c>dv.text</c>): a snapshot of the editor's text, which shares the
    /// editor's memory rather than copying a large file's tens of MB.
    /// </summary>
    private ITextSource outlineText = StringTextSource.Empty;

    /// <summary>Why the descriptors could not be read, or "".</summary>
    private string outlineError = "";

    /// <summary>The text shown when they were read (the HTML app's <c>dv.for</c>).</summary>
    private ShownText? outlineFor;

    /// <summary>The descriptor whose properties are listed, by its place in <see cref="outline"/>; -1 for none.</summary>
    private int selectedDescriptor = -1;

    /// <summary>Its property rows, whether they were cut short, and why they could not be read ("" when they could).</summary>
    private List<PropertyRow> propertyRows = [];
    private bool propertiesCut;
    private string propertiesError = "";

    /// <summary>The objects and lists whose inside is hidden, by row.</summary>
    private HashSet<int> closedRows = [];

    /// <summary>Whether the "Used by" list is open, and the descriptors in it.</summary>
    private bool usersOpen;
    private IReadOnlyList<ReferenceIndex.User> users = [];

    /// <summary>A descriptor of another file whose properties are listed (the HTML app's <c>dv.remote</c>); null otherwise.</summary>
    private sealed record OtherDescriptor(FileEntry Entry, string Name, string Type, OutlineKind Kind, List<PropertyRow> Rows, bool IsCut);
    private OtherDescriptor? otherDescriptor;

    /// <summary>Counts the readings of the descriptors and of their properties, so a later one wins over an earlier one still running.</summary>
    private int outlineReading, propertiesReading;

    /// <summary>Counts the changes to the text; the descriptors are stale while a change came after they were read.</summary>
    private int textGeneration;
    private bool descriptorsStale;

    /// <summary>Whether the viewer itself is changing the text (a value typed over), which does not make it stale.</summary>
    private bool changingOwnText;

    /// <summary>Whether a value is being written, so leaving the box and pressing Enter do not both write.</summary>
    private bool committing;

    /// <summary>Reads the descriptors again a moment after the text changes (the HTML app's <c>dvKick</c>).</summary>
    private readonly DispatcherTimer descriptorsLater = new();

    // ---- the tree (the HTML app's tv) ----

    /// <summary>"This file" (false) or "All files" (true).</summary>
    private bool showsAllFiles;

    /// <summary>The file "This file" stays on, after a double-click opened another; null for whichever file is in the editor.</summary>
    private FileEntry? pinnedFile;

    /// <summary>How many descriptors the list would show with no filter.</summary>
    private int descriptorTotal;

    /// <summary>The top rows, and every row shown, in order.</summary>
    private List<TreeNode> treeRoots = [], treeShown = [];

    /// <summary>The keys of the rows open, which stay open when the tree is made again.</summary>
    private readonly HashSet<string> openKeys = new(StringComparer.Ordinal);

    /// <summary>The key of the row chosen; "" when the chosen descriptor stands for it.</summary>
    private string selectedKey = "";

    /// <summary>How the rows are found, for the text as it was last read.</summary>
    private DescriptorTree? tree;

    /// <summary>Descriptors' rows read before, by file and name; the last 400 are kept.</summary>
    private readonly Dictionary<string, DescriptorRows> rowsRead = [];

    /// <summary>Another file's text and descriptors, read when one of its descriptors is opened; the last few are kept.</summary>
    private sealed record OtherFile(string Text, Dictionary<string, OutlineEntry> ByName, List<OutlineEntry> Outline, string? Source, PackedBytes? Content);
    private readonly Dictionary<FileEntry, OtherFile> otherFiles = [];

    /// <summary>Counts the tree's rebuilds, so a later one wins.</summary>
    private int treeBuild;

    /// <summary>A descriptor to choose once the file it is in has opened in the editor (the HTML app's <c>tv.want</c>).</summary>
    private sealed record Wanted(string Name, int Offset, int RowIndex, bool Quiet);
    private Wanted? wanted;

    /// <summary>Whether Scan all is reading, and whether it was asked to stop.</summary>
    private bool scanning, stopScanning;

    /// <summary>What went wrong opening a row, or "".</summary>
    private string treeError = "";

    /// <summary>The last click on a row, for a double-click (the HTML app's <c>tv.last</c>).</summary>
    private (string Key, DateTime At)? lastClick;

    /// <summary>An open row clicked once, which closes unless a second click comes soon (the HTML app's <c>tv.wait</c>).</summary>
    private (TreeNode Node, Point At, DispatcherTimer Timer)? closing;

    /// <summary>The properties follow the keys a moment after they rest.</summary>
    private readonly DispatcherTimer keysRested = new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <summary>The last click on a "Used by" descriptor, for a double-click.</summary>
    private (TreeNode Node, DateTime At)? lastUserClick;

    /// <summary>How long a second click may take to make a double-click on an open row, and how far the mouse may move meanwhile.</summary>
    private const int DoubleClickWait = 260, DoubleClickMove = 6, ShiftStep = 5;

    /// <summary>Sets the viewer up when the page is built.</summary>
    private void StartDescriptors()
    {
        descriptorsLater.Tick += async (_, _) =>
        {
            descriptorsLater.Stop();
            await RefreshDescriptors();
        };
        keysRested.Tick += (_, _) =>
        {
            keysRested.Stop();
            if (treeShown.FirstOrDefault(n => n.Key == selectedKey) is TreeNode node && node.Kind != TreeNodeKind.File) _ = Pick(node, reveal: false);
        };
        Text.TextChanged += DescriptorTextChanged;
        StartFilter();
        DescriptorSplit.Moved += SplitMoved;
        DescriptorSplit.Reset += () => { splitShare = null; ShowSplit(); };
        DescriptorSplit.Settled += () => RememberSplit();
        splitShare = (double?)AppData.Current.State["sizes"]?["dvSplit"];
        ShowSplit();
        SayDescriptors();
    }

    /// <summary>Reads the descriptors again after a moment (0 for at once).</summary>
    private void KickDescriptors(int milliseconds)
    {
        descriptorsLater.Stop();
        descriptorsLater.Interval = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds));
        descriptorsLater.Start();
    }

    /// <summary>
    /// Typing makes the descriptors stale; they are read again a moment after it stops (the HTML app's <c>cmChanged</c>).
    /// A value typed over in the viewer is followed by the viewer itself.
    /// </summary>
    private void DescriptorTextChanged(object? sender, EventArgs e)
    {
        if (shown is not { IsFile: true } || changingOwnText) return;
        textGeneration++;
        if (!descriptorsStale)
        {
            descriptorsStale = true;
            ShowProperties();
        }
        KickDescriptors(shown.Document.LineCount > 20000 ? 1500 : 600);
    }

    /// <summary>The whole text the descriptors are read from: with "Changes only" shown, the whole export.</summary>
    private ITextSource WholeText() => shown is null ? StringTextSource.Empty : (shown.Parent ?? shown).Document.CreateSnapshot();

    /// <summary>A stretch of the text the descriptors were read from, such as one descriptor's.</summary>
    private string OutlineText(int start, int end) => outlineText.GetText(start, end - start);

    /// <summary>Whether values can be typed over: the file itself is shown (the HTML app's <c>dvCanEdit</c>).</summary>
    private bool CanChangeValues => shown is { IsFile: true };

    /// <summary>A descriptor's key in the list: its name and which of that name it is, so it is found again after a change.</summary>
    private string DescriptorKey(int index)
    {
        string name = outline[index].Name;
        int count = 0;
        for (int i = 0; i < index; i++)
            if (outline[i].Name == name) count++;
        return name + "\u0001" + count;
    }

    /// <summary>
    /// Reads the descriptors of the text in the editor again (the HTML app's <c>dvRefresh</c>), and lists them; the one
    /// chosen stays chosen. Another file opened, and not from the list, keeps the list and the properties as they were,
    /// except that "This file" means the new file.
    /// </summary>
    internal async Task RefreshDescriptors()
    {
        if (shown is null)
        {
            outlineReading++;
            outline = [];
            selectedDescriptor = -1;
            propertyRows = [];
            outlineError = "";
            outlineText = StringTextSource.Empty;
            descriptorsStale = false;
            _ = RebuildTree();
            ShowProperties();
            return;
        }
        int mine = ++outlineReading, generation = textGeneration;
        string? keep = selectedDescriptor >= 0 && selectedDescriptor < outline.Count ? DescriptorKey(selectedDescriptor) : null;
        ShownText? before = outlineFor;
        OutlineEntry? oldDescriptor = selectedDescriptor >= 0 && selectedDescriptor < outline.Count ? outline[selectedDescriptor] : null;
        List<PropertyRow> oldRows = propertyRows;
        bool oldCut = propertiesCut;
        ITextSource text = WholeText();
        SayDescriptors(Texts.Get("Editor.Viewer.Reading"));
        List<OutlineEntry> list = [];
        string error = "";
        // the snapshot is made into one string away from the screen's thread, and let go of once read
        try { list = await Task.Run(() => DescriptorViewer.Outline(text.Text)); }
        catch (Exception problem) when (problem is NdfParseException or ArgumentException) { error = problem.Message; }
        if (mine != outlineReading) return;

        outlineText = text;
        outline = list;
        outlineError = error;
        descriptorsStale = textGeneration != generation;
        outlineFor = shown;
        if (before != shown && wanted is null)
        {
            if (!showsAllFiles)
            {
                pinnedFile = null;
                otherDescriptor = null;
                selectedKey = "";
            }
            else if (otherDescriptor is not null && IsLocal(otherDescriptor.Entry)) wanted = new Wanted(otherDescriptor.Name, 0, -1, Quiet: true);
            else if (otherDescriptor is null && selectedKey != "" && before?.Entry is FileEntry oldEntry && oldDescriptor is { Name.Length: > 0 }
                     && Files is not null && Files.Entries.Contains(oldEntry))
                otherDescriptor = new OtherDescriptor(oldEntry, oldDescriptor.Name, oldDescriptor.Type, oldDescriptor.Kind, oldRows, oldCut);
        }

        int chosen = -1;
        if (keep is not null && before == shown)
            for (int i = 0; i < list.Count; i++)
                if (DescriptorKey(i) == keep) { chosen = i; break; }
        if (chosen < 0 && list.Count > 0) chosen = 0;
        bool same = before == shown && keep is not null && chosen >= 0 && DescriptorKey(chosen) == keep;
        selectedDescriptor = -1;
        _ = RebuildTree();
        // the first read of a file just opened: opening and reading a large file left a lot behind, which is given back
        if (before != shown) Memory.GiveBack();
        if (chosen >= 0) await SelectDescriptor(chosen, reveal: false, keepClosed: same);
        else
        {
            propertyRows = [];
            ShowProperties();
        }

        if (mine == outlineReading && wanted is Wanted want)
        {
            wanted = null;
            int found = outline.FindIndex(d => d.Name == want.Name);
            if (found < 0) return;
            otherDescriptor = null;
            await SelectDescriptor(found, reveal: false, keepClosed: false);
            if (!want.Quiet) RevealAt(outline[found].Start + want.Offset);
            if (want.RowIndex >= 0) ShowPropertyRow(want.RowIndex);
            ShowTree();
        }
    }

    /// <summary>Lists a descriptor's properties (the HTML app's <c>dvSelect</c>); with <paramref name="reveal"/>, shows it in the text too.</summary>
    private async Task SelectDescriptor(int index, bool reveal, bool keepClosed)
    {
        if (index < 0 || index >= outline.Count) return;
        OutlineEntry descriptor = outline[index];
        selectedDescriptor = index;
        ShowTree();
        if (reveal) RevealAt(descriptor.Start);
        int mine = ++propertiesReading;
        string text = OutlineText(descriptor.Start, descriptor.End);
        PropertyList? read = null;
        string error = "";
        try { read = await Task.Run(() => DescriptorViewer.Properties(text)); }
        catch (Exception problem) when (problem is NdfParseException or ArgumentException) { error = problem.Message; }
        if (mine != propertiesReading) return;
        // folded parts stay folded only while the rows are the same ones
        if (!keepClosed || read is null || read.Rows.Count != propertyRows.Count) closedRows = [];
        propertyRows = read?.Rows ?? [];
        propertiesCut = read?.IsCut ?? false;
        propertiesError = error;
        ShowProperties();
    }

    /// <summary>
    /// Shows a place of the whole text in the editor and marks its line (the HTML app's <c>edRevealAt</c>). With "Changes
    /// only" shown, the whole export is shown first. Returns the line, from 0.
    /// </summary>
    private int RevealAt(int offset)
    {
        if (shown?.Parent is ShownText whole) Swap(whole);
        if (shown is null) return 0;
        TextDocument document = shown.Document;
        int line = document.GetLineByOffset(Math.Clamp(offset, 0, document.TextLength)).LineNumber - 1;
        PointAt(line);
        return line;
    }

    // ---- the tree ----

    /// <summary>Whether a file is the one in the editor (the HTML app's <c>tvLocal</c>).</summary>
    private bool IsLocal(FileEntry? entry) => shown is not null && entry == shown.Entry;

    /// <summary>
    /// Another file's text and descriptors (the HTML app's <c>tvFile</c>). The last few files read are kept, up to 60
    /// million characters in all.
    /// </summary>
    private async Task<OtherFile> ReadOtherFile(FileEntry entry)
    {
        if (otherFiles.TryGetValue(entry, out OtherFile? known) && known.Source == entry.Source && ReferenceEquals(known.Content, entry.ArchivedContent))
        {
            // the one used last is kept longest
            otherFiles.Remove(entry);
            otherFiles[entry] = known;
            return known;
        }
        string text = Normalised(await Task.Run(() => FileContent.Text(entry)));
        List<OutlineEntry> list = await Task.Run(() => DescriptorViewer.Outline(text));
        var byName = new Dictionary<string, OutlineEntry>(StringComparer.Ordinal);
        foreach (OutlineEntry descriptor in list)
            if (descriptor.Name != "") byName.TryAdd(descriptor.Name, descriptor);
        var read = new OtherFile(text, byName, list, entry.Source, entry.ArchivedContent);
        otherFiles.Remove(entry);
        otherFiles[entry] = read;
        long total = otherFiles.Values.Sum(f => (long)f.Text.Length);
        foreach (FileEntry older in otherFiles.Keys.ToList())
        {
            if (total <= 60_000_000 || otherFiles.Count <= 1) break;
            if (older == entry) continue;
            total -= otherFiles[older].Text.Length;
            otherFiles.Remove(older);
        }
        return read;
    }

    /// <summary>A descriptor's rows, as the properties list has them (the HTML app's <c>tvRows</c>); null when it cannot be found.</summary>
    private async Task<DescriptorRows?> RowsOf(TreeNode node)
    {
        bool local = IsLocal(node.Entry);
        OutlineEntry? descriptor;
        if (local)
        {
            descriptor = node.Outline is not null && outline.Contains(node.Outline) ? node.Outline : tree?.LocalByName.GetValueOrDefault(node.Name);
            if (descriptor is null) return null;
        }
        else if (node.Entry is null) return null;
        else descriptor = null;

        string key = (local ? "L\u0001" + descriptor!.Start : node.Entry!.Id.ToString(CultureInfo.InvariantCulture)) + "\u0001" + node.Name;
        if (rowsRead.TryGetValue(key, out DescriptorRows? known)) return known;
        string text;
        if (local) text = OutlineText(descriptor!.Start, descriptor.End);
        else
        {
            OtherFile file = await ReadOtherFile(node.Entry!);
            if (!file.ByName.TryGetValue(node.Name, out descriptor)) return null;
            text = file.Text[descriptor.Start..descriptor.End];
        }
        PropertyList read = await Task.Run(() => DescriptorViewer.Properties(text));
        var rows = new DescriptorRows(read.Rows, read.IsCut, read.Type, descriptor.Kind, local ? descriptor : null);
        if (rowsRead.Count > 400) rowsRead.Remove(rowsRead.Keys.First());
        rowsRead[key] = rows;
        return rows;
    }

    /// <summary>
    /// Opens a row (the HTML app's <c>tvExpand</c>), working out what is inside it the first time. With
    /// <paramref name="chain"/>, when only one thing inside can be opened, it is opened along with what holds it;
    /// another descriptor is not.
    /// </summary>
    private async Task Expand(TreeNode node, bool chain)
    {
        if (node.IsLeaf || tree is null) return;
        if (node.Children is null)
        {
            if (node.Kind == TreeNodeKind.File) node.Children = tree.FileChildren(node);
            else if (node.Kind is TreeNodeKind.Descriptor or TreeNodeKind.Reference)
            {
                DescriptorRows? rows = null;
                try { rows = await RowsOf(node); }
                catch (Exception problem) when (problem is NdfParseException or ArgumentException or IOException or UnauthorizedAccessException)
                {
                    treeError = (IsLocal(node.Entry) || node.Entry is null
                        ? Texts.Get("Editor.Viewer.CouldNotRead", ("name", node.Name))
                        : Texts.Get("Editor.Viewer.CouldNotReadFrom", ("name", node.Name), ("file", node.Entry.Name)))
                        + " " + problem.Message;
                }
                node.Rows = rows;
                node.Children = rows is null ? [] : tree.Children(node, rows, -1);
            }
            else node.Children = tree.Children(node, node.Rows!, node.RowIndex);
            if (node.Children.Count == 0)
            {
                node.IsLeaf = true;
                openKeys.Remove(node.Key);
                return;
            }
        }
        node.IsOpen = true;
        openKeys.Add(node.Key);
        if (chain && node.Depth < 60)
        {
            List<TreeNode> canOpen = [.. node.Children.Where(child => !child.IsLeaf)];
            if (canOpen.Count == 1 && canOpen[0].Kind != TreeNodeKind.Reference) await Expand(canOpen[0], chain: true);
        }
    }

    /// <summary>Opens a row, or closes it (the HTML app's <c>tvToggle</c>).</summary>
    internal async Task Toggle(TreeNode node)
    {
        int mine = treeBuild;
        if (node.IsOpen)
        {
            node.IsOpen = false;
            openKeys.Remove(node.Key);
        }
        else
        {
            if (node.Kind != TreeNodeKind.File && node.Children is null && node.Entry is not null && !IsLocal(node.Entry)) SayDescriptors(Texts.Get("Editor.Viewer.ReadingFile", ("file", node.Entry.Name)));
            treeError = "";
            await Expand(node, chain: true);
            if (mine != treeBuild) return;
            SayDescriptors();
            if (treeError != "") SayDescriptors(treeError, warning: true);
        }
        treeShown = DescriptorTree.Flatten(treeRoots);
        ShowTree();
    }

    /// <summary>
    /// Makes the tree again from the files as they are now (the HTML app's <c>tvRebuild</c>). What was open stays open,
    /// and the list stays where it was scrolled to.
    /// </summary>
    internal async Task RebuildTree()
    {
        int mine = ++treeBuild;
        foreach (string key in rowsRead.Keys.Where(k => k.StartsWith('L')).ToList()) rowsRead.Remove(key);
        List<FileEntry> entries = [.. Files?.Entries ?? []];
        tree = new DescriptorTree(shown?.Entry, shown is not null, outline, outlineText, ReferenceIndex.Of(entries));
        if (pinnedFile is not null && (!entries.Contains(pinnedFile) || pinnedFile.Source is null)) pinnedFile = null;
        string filter = DescriptorFind.Text.Trim().ToLowerInvariant();
        FileEntry? pin = !showsAllFiles && pinnedFile is not null && !IsLocal(pinnedFile) ? pinnedFile : null;
        ModeOneText.Text = pin is not null ? pin.Name : Texts.Get("Editor.ThisFile");
        List<TreeNode> roots;
        if (!showsAllFiles)
        {
            if (pin is not null)
            {
                List<(string, string)> declared = [];
                try
                {
                    OtherFile file = await ReadOtherFile(pin);
                    declared = [.. file.Outline.Select(d => (d.Name, d.Kind == OutlineKind.Constant ? "" : d.Type))];
                }
                catch (Exception problem) when (problem is NdfParseException or IOException or UnauthorizedAccessException) { }
                if (mine != treeBuild) return;
                descriptorTotal = declared.Count(d => d.Item1 != "");
                roots = tree.OfOtherFile(null, pin, declared, null, filter);
                if (Filter.IsOn && otherFiles.TryGetValue(pin, out OtherFile? read))
                {
                    Dictionary<string, IndexEntry> index = await OtherFileFilterIndex(pin, read);
                    if (mine != treeBuild) return;
                    roots = Passing(roots, index);
                }
            }
            else
            {
                descriptorTotal = outline.Count;
                roots = tree.OfOpenFile(null, null, filter);
                if (Filter.IsOn && shown is not null)
                {
                    Dictionary<string, IndexEntry> index = await OpenFileFilterIndex();
                    if (mine != treeBuild) return;
                    roots = Passing(roots, index);
                }
            }
        }
        else if (await AllFilesRoots(tree, filter, mine) is List<TreeNode> all) roots = all;
        else return;

        if (openKeys.Count > 0)
        {
            // what was open before is opened again before anything is drawn, so nothing is seen to close
            int budget = 400;
            async Task Reopen(IEnumerable<TreeNode> rows)
            {
                foreach (TreeNode row in rows)
                {
                    if (mine != treeBuild || budget <= 0) return;
                    if (!openKeys.Contains(row.Key)) continue;
                    budget--;
                    await Expand(row, chain: false);
                    if (row.Children is not null && row.IsOpen) await Reopen(row.Children);
                }
            }
            await Reopen(roots);
            if (mine != treeBuild) return;
        }
        treeRoots = roots;
        treeShown = DescriptorTree.Flatten(roots);
        ShowTree();
        SayDescriptors();
        MainView.Find(this)?.Filters.ShowCount();
    }

    /// <summary>
    /// "All files": a row for each file that starts chains, with how many and how many files they reach; while the name
    /// filter is typed in or the property filter is on, every descriptor of every read file that passes instead,
    /// whatever refers to it (at most 3,000). Null when another rebuild started meanwhile.
    /// </summary>
    private async Task<List<TreeNode>?> AllFilesRoots(DescriptorTree descriptors, string filter, int build)
    {
        var roots = new List<TreeNode>();
        ReferenceIndex index = descriptors.Index;
        bool filtering = Filter.IsOn;
        if (filter != "" || filtering)
        {
            if (shown is not null)
            {
                Dictionary<string, IndexEntry>? openIndex = filtering ? await OpenFileFilterIndex() : null;
                if (build != treeBuild) return null;
                foreach (TreeNode row in descriptors.OfOpenFile(null, null, filter))
                {
                    if (roots.Count >= 3000) break;
                    if (row.Name == "" || (openIndex is not null && !Filter.Passes(openIndex.GetValueOrDefault(row.Name)))) continue;
                    row.FileTag = shown.Name;
                    roots.Add(row);
                }
            }
            foreach (FileEntry entry in index.Files)
            {
                if (roots.Count >= 3000) break;
                if (IsLocal(entry)) continue;
                Dictionary<string, IndexEntry>? fileIndex = null;
                if (filtering)
                {
                    try { fileIndex = await OtherFileFilterIndex(entry, await ReadOtherFile(entry)); }
                    catch (Exception problem) when (problem is NdfParseException or IOException or UnauthorizedAccessException) { continue; }
                    if (build != treeBuild) return null;
                }
                foreach (TreeNode row in descriptors.OfOtherFile(null, entry, entry.References!.Select(d => (d.Name, d.Type)), null, filter))
                {
                    if (roots.Count >= 3000) break;
                    if (fileIndex is not null && !Filter.Passes(fileIndex.GetValueOrDefault(row.Name))) continue;
                    row.FileTag = entry.Name;
                    roots.Add(row);
                }
            }
            return roots;
        }
        ReferenceIndex.Chains chains = index.ChainsOf();
        foreach (FileEntry entry in index.Files)
        {
            if (!chains.Tops.TryGetValue(entry, out HashSet<string>? tops)) continue;
            int reach = chains.Into.GetValueOrDefault(entry);
            roots.Add(new TreeNode
            {
                Key = "F\u0001" + entry.Id.ToString(CultureInfo.InvariantCulture), Kind = TreeNodeKind.File, Label = entry.Name, Entry = entry,
                Sub = (entry.Folder != "" ? entry.Folder + "   · " : "")
                    + Texts.Get("Editor.Viewer.ChainsInto", ("chains", Texts.CountWide("Editor.Viewer.Chains", tops.Count)), ("files", Texts.CountWide("Editor.Search.Files", reach)))
                    + (IsLocal(entry) ? "   · " + Texts.Get("Editor.Viewer.OpenInEditor") : ""),
            });
        }
        return roots;
    }

    /// <summary>Draws the tree's rows (the HTML app's <c>tvDraw</c>), keeping where it was scrolled to.</summary>
    private void ShowTree()
    {
        OutlineEntry? current = otherDescriptor is null && selectedDescriptor >= 0 && selectedDescriptor < outline.Count ? outline[selectedDescriptor] : null;
        List<TreeItem> items = [.. treeShown.Select(node => new TreeItem(node,
            selectedKey != "" ? node.Key == selectedKey : node.Kind == TreeNodeKind.Descriptor && current is not null && node.Outline == current))];
        TreeList.ItemsSource = items;
    }

    /// <summary>The row chosen, by its place among the rows shown; -1 for none (the HTML app's <c>tvAt</c>).</summary>
    private int ChosenRow()
    {
        if (selectedKey != "") return treeShown.FindIndex(n => n.Key == selectedKey);
        OutlineEntry? current = otherDescriptor is null && selectedDescriptor >= 0 && selectedDescriptor < outline.Count ? outline[selectedDescriptor] : null;
        return current is null ? -1 : treeShown.FindIndex(n => n.Kind == TreeNodeKind.Descriptor && n.Outline == current);
    }

    /// <summary>
    /// A row clicked lists the properties of the descriptor it belongs to (the HTML app's <c>tvPick</c>); the editor is
    /// left alone. With <paramref name="reveal"/> (a double-click, or Alt+click), the row is shown in the editor, its
    /// file opened there first when it is another one.
    /// </summary>
    internal async Task Pick(TreeNode node, bool reveal)
    {
        if (node.Owner is not TreeNode owner) return;
        selectedKey = node.Key;
        ShowTree();
        DescriptorRows? rows = null;
        try { rows = await RowsOf(owner); }
        catch (Exception problem) when (problem is NdfParseException or ArgumentException or IOException or UnauthorizedAccessException) { }
        if (selectedKey != node.Key) return;
        if (rows is null)
        {
            string name = owner.Name != "" ? owner.Name : Texts.Get("Editor.Viewer.It");
            SayDescriptors(IsLocal(owner.Entry) || owner.Entry is null
                ? Texts.Get("Editor.Viewer.CouldNotRead", ("name", name))
                : Texts.Get("Editor.Viewer.CouldNotReadFrom", ("name", name), ("file", owner.Entry.Name)), warning: true);
            return;
        }
        bool inside = node != owner;
        PropertyRow? row = inside ? rows.Rows[node.RowIndex] : null;
        int offset = row?.Start ?? 0;
        if (IsLocal(owner.Entry))
        {
            int index = rows.Local is null ? -1 : outline.IndexOf(rows.Local);
            if (index < 0) return;
            otherDescriptor = null;
            await SelectDescriptor(index, reveal: false, keepClosed: false);
            if (selectedKey != node.Key) return;
            if (reveal) RevealAt(rows.Local!.Start + offset);
            if (inside) ShowPropertyRow(node.RowIndex);
            return;
        }
        if (reveal && owner.Entry is FileEntry other)
        {
            // the list stays on the file it is on
            if (!showsAllFiles && pinnedFile is null && shown?.Entry is FileEntry current) pinnedFile = current;
            wanted = new Wanted(owner.Name, offset, inside ? node.RowIndex : -1, Quiet: false);
            await Open(other);
            if (shown?.Entry != other) wanted = null;
            return;
        }
        otherDescriptor = new OtherDescriptor(owner.Entry!, owner.Name, rows.Type, rows.Kind, rows.Rows, rows.IsCut);
        closedRows = [];
        ShowProperties();
        if (inside) ShowPropertyRow(node.RowIndex);
    }

    // ---- clicks and keys on the tree ----

    /// <summary>The tree's row under the mouse, and whether the click was on its arrow.</summary>
    private (TreeNode? Node, bool OnArrow) RowUnder(MouseEventArgs e)
    {
        bool onArrow = false;
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null; at = at is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is FrameworkElement { Tag: "arrow" }) onArrow = true;
            if (at is FrameworkElement { DataContext: TreeItem item } && at is ListBoxItem) return (item.Node, onArrow);
        }
        return (null, false);
    }

    /// <summary>
    /// One click opens or closes a row and lists its properties; a double-click (or Alt+click) shows the row in the
    /// editor. Opening happens on the first click, at once, and the second click of a double-click does not undo it.
    /// Closing waits a moment for a second click: if one comes, the row stays open and the editor goes to it. Moving the
    /// mouse away ends the wait and closes the row at once. The arrow itself opens or closes at once, every click.
    /// </summary>
    private async void TreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        (TreeNode? node, bool onArrow) = RowUnder(e);
        if (node is null) return;
        e.Handled = true;
        TreeList.Focus();
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (node.Kind == TreeNodeKind.File)
        {
            CloseNow();
            await Toggle(node);
            return;
        }
        if (!node.IsLeaf && !alt && onArrow)
        {
            if (closing?.Node == node) DropClosing();
            else CloseNow();
            lastClick = null;
            selectedKey = node.Key;
            await Toggle(node);
            if (selectedKey == node.Key) _ = Pick(node, reveal: false);
            return;
        }
        bool second = e.ClickCount >= 2 && lastClick is var (key, at) && key == node.Key && (DateTime.Now - at).TotalMilliseconds < 700;
        if (alt || second)
        {
            DropClosing();
            lastClick = null;
            _ = Pick(node, reveal: true);
            return;
        }
        CloseNow();
        lastClick = (node.Key, DateTime.Now);
        selectedKey = node.Key;
        if (node.IsLeaf) ShowTree();
        else if (!node.IsOpen) await Toggle(node);
        else
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DoubleClickWait) };
            timer.Tick += (_, _) => CloseNow();
            closing = (node, e.GetPosition(TreeList), timer);
            timer.Start();
            TreeList.MouseMove += MovedWhileClosing;
            ShowTree();
        }
        if (selectedKey == node.Key) _ = Pick(node, reveal: false);
    }

    private void MovedWhileClosing(object sender, MouseEventArgs e)
    {
        if (closing is not var (_, at, _)) return;
        Point now = e.GetPosition(TreeList);
        if (Math.Abs(now.X - at.X) > DoubleClickMove || Math.Abs(now.Y - at.Y) > DoubleClickMove) CloseNow();
    }

    /// <summary>The open row clicked once closes now (the HTML app's <c>tvCloseNow</c>).</summary>
    private void CloseNow()
    {
        if (closing is not var (node, _, _)) return;
        DropClosing();
        if (treeShown.Contains(node) && node.IsOpen) _ = Toggle(node);
    }

    /// <summary>The open row clicked once stays open after all (the HTML app's <c>tvCloseDrop</c>).</summary>
    private void DropClosing()
    {
        if (closing is not var (_, _, timer)) return;
        closing = null;
        timer.Stop();
        TreeList.MouseMove -= MovedWhileClosing;
    }

    /// <summary>Goes to a row: it is chosen and scrolled into sight; the properties follow once the keys rest (the HTML app's <c>tvGo</c>).</summary>
    private void GoToRow(int index)
    {
        if (index < 0 || index >= treeShown.Count) return;
        TreeNode node = treeShown[index];
        selectedKey = node.Key;
        lastClick = null;
        ShowTree();
        TreeList.ScrollIntoView(TreeList.Items[index]);
        keysRested.Stop();
        if (node.Kind != TreeNodeKind.File) keysRested.Start();
    }

    /// <summary>
    /// The keyboard, while the tree has the focus. Up and Down move through the rows (Shift: five at a time), Right opens a
    /// row (or, on an open one, steps into it), Left closes a row (or, on a closed one, steps out to the row that holds
    /// it). Home, End, Page Up and Page Down move further. Enter shows the row in the editor.
    /// </summary>
    private async void TreeKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        Key key = e.Key;
        int count = treeShown.Count;
        if (count == 0 || key is not (Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Enter)) return;
        e.Handled = true;
        CloseNow();
        int at = ChosenRow();
        int page = Math.Max(1, (int)Math.Floor(TreeList.ActualHeight / 17) - 1);
        if (at < 0)
        {
            GoToRow(key is Key.Up or Key.End ? count - 1 : 0);
            return;
        }
        TreeNode node = treeShown[at];
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? ShiftStep : 1;
        switch (key)
        {
            case Key.Down: GoToRow(Math.Min(count - 1, at + step)); break;
            case Key.Up: GoToRow(Math.Max(0, at - step)); break;
            case Key.Home: GoToRow(0); break;
            case Key.End: GoToRow(count - 1); break;
            case Key.PageDown: GoToRow(Math.Min(count - 1, at + page)); break;
            case Key.PageUp: GoToRow(Math.Max(0, at - page)); break;
            case Key.Enter:
                if (node.Kind == TreeNodeKind.File) await Toggle(node);
                else _ = Pick(node, reveal: true);
                break;
            case Key.Right:
                if (node.IsLeaf) return;
                if (!node.IsOpen)
                {
                    selectedKey = node.Key;
                    await Toggle(node);
                    if (selectedKey == node.Key) GoToRow(treeShown.IndexOf(node));
                }
                else if (at + 1 < count && treeShown[at + 1].Parent == node) GoToRow(at + 1);
                break;
            case Key.Left:
                if (node.IsOpen && !node.IsLeaf)
                {
                    selectedKey = node.Key;
                    await Toggle(node);
                    if (selectedKey == node.Key) GoToRow(treeShown.IndexOf(node));
                }
                else if (node.Parent is TreeNode parent) GoToRow(treeShown.IndexOf(parent));
                break;
        }
    }

    // ---- This file, All files, the filter box and Scan all ----

    /// <summary>"This file", or "All files" (the HTML app's <c>tvMode</c>).</summary>
    private void ShowMode(bool allFiles)
    {
        showsAllFiles = allFiles;
        ModeOneButton.IsChecked = !allFiles;
        ModeAllButton.IsChecked = allFiles;
        DescriptorFind.Tag = allFiles ? Texts.Get("Editor.DescriptorFind.AllFilesHint") : Texts.Get("Editor.DescriptorFind.Hint");
        if (VisualTreeHelperFindScroller(TreeList) is ScrollViewer list) list.ScrollToTop();
        _ = RebuildTree();
    }

    private static ScrollViewer? VisualTreeHelperFindScroller(DependencyObject element)
    {
        if (element is ScrollViewer found) return found;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            if (VisualTreeHelperFindScroller(System.Windows.Media.VisualTreeHelper.GetChild(element, i)) is ScrollViewer inner) return inner;
        return null;
    }

    /// <summary>"This file" again, on a file it stays on, goes back to the file in the editor.</summary>
    internal void ModeOneClicked(object sender, RoutedEventArgs e)
    {
        if (!showsAllFiles && pinnedFile is not null) pinnedFile = null;
        ShowMode(allFiles: false);
    }

    internal void ModeAllClicked(object sender, RoutedEventArgs e) => ShowMode(allFiles: true);

    private void DescriptorFindChanged(object sender, TextChangedEventArgs e) => _ = RebuildTree();

    /// <summary>
    /// "Refresh": reads, one after another, every file on the Files tab not read yet, for what it declares and what each
    /// declaration refers to. Pressed again, it stops.
    /// </summary>
    private async void ScanClicked(object sender, RoutedEventArgs e)
    {
        if (scanning)
        {
            stopScanning = true;
            return;
        }
        await ReadUnreadFiles(sayWhenNothing: true);
    }

    /// <summary>Whether the Descriptor Tree pane has been opened since the app started, which refreshes it once.</summary>
    private bool treeOpenedOnce;

    /// <summary>
    /// The first time the Descriptor Tree pane shows (opened, or open when the Editor tab is first shown), it is
    /// refreshed, as "Refresh" does, so it lists the chains across files without a click.
    /// </summary>
    private void RefreshTreeOnFirstOpen()
    {
        if (treeOpenedOnce || Panes.IsFolded || scanning) return;
        treeOpenedOnce = true;
        _ = ReadUnreadFiles(sayWhenNothing: false);
    }

    /// <summary>
    /// Reads, one after another, every file on the Files tab not read yet. With <paramref name="sayWhenNothing"/>, says so
    /// when there is nothing to read.
    /// </summary>
    private async Task ReadUnreadFiles(bool sayWhenNothing)
    {
        List<FileEntry> waiting = [.. (Files?.Entries ?? []).Where(f => f.Source is not null && f.Name.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase) && f.References is null)];
        if (waiting.Count == 0)
        {
            if (sayWhenNothing)
                SayDescriptors(Files is { } list && list.Entries.Any(f => f.Source is not null) ? Texts.Get("Editor.Scan.AllRead") : Texts.Get("Editor.Scan.NoFiles"));
            return;
        }
        scanning = true;
        stopScanning = false;
        ScanButton.Content = Texts.Get("Editor.Scan.Stop");
        int done = 0, failed = 0;
        DateTime lastSaid = DateTime.MinValue;
        // every file is read, as the Files tab reads its files: smoothly, and giving back what that left behind
        using IDisposable longWork = Memory.DuringLongWork();
        try
        {
            foreach (FileEntry entry in waiting)
            {
                if (stopScanning) break;
                if (Files is null || !Files.Entries.Contains(entry)) continue;
                if ((DateTime.Now - lastSaid).TotalMilliseconds > 120)
                {
                    lastSaid = DateTime.Now;
                    SayDescriptors(Texts.Get("Editor.Scan.ReadingFile", ("number", Texts.Number(done + 1)), ("total", Texts.Number(waiting.Count)), ("file", entry.Name)));
                }
                if (await Task.Run(() => FileListModel.Read(entry)) is null)
                {
                    failed++;
                    entry.References = [];
                }
                Files.RowChanged(entry);
                done++;
            }
        }
        finally
        {
            scanning = false;
            ScanButton.Content = Texts.Get("Editor.ScanAll");
        }
        await RebuildTree();
        ShowProperties();
        string said = string.Concat(DescriptorNote.Inlines.OfType<Run>().Select(r => r.Text));
        string read = failed > 0
            ? Texts.CountWide("Editor.Scan.ReadSomeFailed", done - failed, ("failed", Texts.Number(failed)))
            : Texts.CountWide("Editor.Scan.Read", done - failed);
        SayDescriptors((stopScanning ? Texts.Get("Editor.Scan.Stopped") + " " : "") + read + " " + said);
    }

    // ---- the note ----

    /// <summary>The note above the viewer (the HTML app's <c>dvSay</c>): how many descriptors, and how to use the viewer.</summary>
    private void SayDescriptors()
    {
        string how = " " + Texts.Get("Editor.Viewer.How");
        List<FileEntry> entries = [.. Files?.Entries ?? []];
        if (showsAllFiles)
        {
            ReferenceIndex index = ReferenceIndex.Of(entries);
            ReferenceIndex.Chains chains = index.ChainsOf();
            int unread = entries.Count(f => f.Source is not null && f.Name.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase) && f.References is null);
            bool finding = DescriptorFind.Text.Trim() != "" || Filter.IsOn;
            if (Filter.IsOn)
            {
                SayDescriptors(Texts.Get("Editor.Viewer.FilterOn",
                    ("descriptors", Texts.CountWide("Editor.Viewer.Descriptors", treeRoots.Count)), ("files", Texts.CountWide("Editor.Viewer.ReadFiles", index.Files.Count))) + how);
                return;
            }
            string descriptors = Texts.CountWide("Editor.Viewer.Descriptors", index.Names.Count);
            string readFiles = Texts.CountWide("Editor.Viewer.ReadFiles", index.Files.Count);
            string found = unread > 0
                ? Texts.Get("Editor.Viewer.InReadFilesSomeUnread", ("descriptors", descriptors), ("files", readFiles), ("unread", Texts.CountWide("Editor.Search.Files", unread)))
                : Texts.Get("Editor.Viewer.InReadFiles", ("descriptors", descriptors), ("files", readFiles));
            string chainsSaid = finding ? "" : chains.Count > 0 ? " " + Texts.CountWide("Editor.Viewer.ChainsStart", chains.Count) : " " + Texts.Get("Editor.Viewer.NoChains");
            SayDescriptors(found + chainsSaid + how);
            return;
        }
        FileEntry? pin = pinnedFile is not null && !IsLocal(pinnedFile) ? pinnedFile : null;
        if (shown is null && pin is null)
        {
            SayDescriptors(Texts.Get("Editor.Viewer.OpenAFile"));
            return;
        }
        if (outlineError != "" && pin is null)
        {
            SayDescriptors(Texts.Get("Editor.Viewer.NotClean", ("problem", outlineError)), warning: true);
            return;
        }
        int total = descriptorTotal, listed = treeRoots.Count;
        string count = listed == total
            ? Texts.CountWide("Editor.Viewer.Descriptors", total)
            : Texts.CountWide("Editor.Viewer.ShowingOf", total, ("listed", Texts.Number(listed)));
        string said = pin is not null ? Texts.Get("Editor.Viewer.CountInFile", ("count", count), ("file", pin.Name)) : Texts.Get("Editor.Viewer.Count", ("count", count));
        string change = pin is not null || shown is null ? "" : " " + (CanChangeValues ? Texts.Get("Editor.Viewer.TypeOver") : Texts.Get("Editor.Viewer.NoChangeInExport"));
        SayDescriptors(said + how + change);
    }

    /// <summary>The note says something else, plain or as a warning.</summary>
    private void SayDescriptors(string text, bool warning = false)
    {
        DescriptorNote.Inlines.Clear();
        DescriptorNote.Inlines.Add(warning ? Coloured(text, "warn") : new Run(text));
    }

    // ---- the properties ----

    /// <summary>Beside the descriptor's name: whether its values can be changed here, and why not when they cannot (the HTML app's <c>dvState</c>).</summary>
    private void ShowState()
    {
        string state = "";
        bool readOnly = true;
        if (otherDescriptor is not null) state = Texts.Get("Editor.State.InOtherFile", ("file", otherDescriptor.Entry.Name));
        else if (shown is not null && selectedDescriptor >= 0 && selectedDescriptor < outline.Count)
        {
            if (shown.IsExport) state = Texts.Get("Editor.State.Export");
            else { state = Texts.Get("Editor.State.CanChange"); readOnly = false; }
        }
        DescriptorState.Text = state;
        if (readOnly && state != "") DescriptorState.Foreground = Mixed("caution", "muted", 0.7);
        else DescriptorState.SetResourceReference(TextBlock.ForegroundProperty, "muted");
    }

    /// <summary>Two of the theme's colours mixed (CSS <c>color-mix(in srgb, a share, b)</c>).</summary>
    private static System.Windows.Media.Brush Mixed(string first, string second, double share)
    {
        var a = ((System.Windows.Media.SolidColorBrush)Application.Current.Resources[first]).Color;
        var b = ((System.Windows.Media.SolidColorBrush)Application.Current.Resources[second]).Color;
        byte Mix(byte x, byte y) => (byte)Math.Round(x * share + y * (1 - share));
        var mixed = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B)));
        mixed.Freeze();
        return mixed;
    }

    /// <summary>Lists the chosen descriptor's properties (the HTML app's <c>dvDrawProps</c>).</summary>
    private void ShowProperties()
    {
        ShowState();
        OtherDescriptor? other = otherDescriptor;
        OutlineEntry? local = other is null && selectedDescriptor >= 0 && selectedDescriptor < outline.Count ? outline[selectedDescriptor] : null;
        if (other is null && local is null)
        {
            DescriptorHead.Inlines.Clear();
            PropertyList.ItemsSource = null;
            return;
        }
        string name = other?.Name ?? local!.Name;
        string type = other?.Type ?? local!.Type;
        bool isConstant = (other?.Kind ?? local!.Kind) == OutlineKind.Constant;
        List<PropertyRow> rows = other?.Rows ?? propertyRows;
        bool canChange = other is null && CanChangeValues && !descriptorsStale;

        DescriptorHead.Inlines.Clear();
        DescriptorHead.Inlines.Add(Coloured(name != "" ? name : Texts.Get("Editor.Unnamed"), "s-name"));
        DescriptorHead.Inlines.Add(new Run(isConstant ? "  " + Texts.Get("Editor.Head.Constant") : type != "" ? "  " + Texts.Get("Editor.Head.Is", ("type", type)) : ""));

        var items = new List<PropertyItem>();
        FileEntry? mine = other?.Entry ?? shown?.Entry;
        users = name != "" ? [.. ReferenceIndex.Of([.. Files?.Entries ?? []]).UsersOf(name).Where(u => !(u.Entry == mine && u.Name == name))] : [];
        if (users.Count > 0)
        {
            items.Add(new UsedByItem(users.Count, usersOpen));
            if (usersOpen)
            {
                foreach (ReferenceIndex.User user in users.Take(300)) items.Add(new UserItem(user));
                if (users.Count > 300) items.Add(new NoteItem(Texts.Get("Editor.Users.First300"), false, new Thickness(32, 2, 32, 2)));
            }
        }
        if (propertiesError != "" && other is null)
        {
            items.Add(new NoteItem(propertiesError, true, new Thickness(10)));
            PropertyList.ItemsSource = items;
            return;
        }
        int hideBelow = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            PropertyRow row = rows[i];
            if (hideBelow >= 0)
            {
                if (row.Depth > hideBelow) continue;
                hideBelow = -1;
            }
            if (row is GroupRow group)
            {
                bool shut = closedRows.Contains(i);
                if (shut) hideBelow = row.Depth;
                items.Add(new ObjectOrListItem(row.Depth, i, row.Key, group.Title, shut, group.Count));
                continue;
            }
            var value = (ValueRow)row;
            bool overLines = value.Value.Contains('\n');
            string shownValue = overLines ? System.Text.RegularExpressions.Regex.Replace(value.Value, @"[ \t\r\n\f\v]+", " ") : value.Value;
            items.Add(new ValueItem(row.Depth, i, row.Key, shownValue, !canChange || overLines, overLines));
        }
        if (other?.IsCut ?? propertiesCut)
            items.Add(new NoteItem(Texts.Get("Editor.Rows.FirstOnly", ("count", Texts.Number(rows.Count))) + (other is null ? " " + Texts.Get("Editor.Rows.RestInText") : ""), false, new Thickness(6)));
        if (rows.Count == 0) items.Add(new NoteItem(Texts.Get("Editor.Rows.Empty"), false, new Thickness(10)));
        PropertyList.ItemsSource = items;
    }

    /// <summary>Scrolls a row of the properties list into sight (the HTML app's <c>dvShowRow</c>).</summary>
    private void ShowPropertyRow(int rowIndex)
    {
        if (PropertyList.ItemsSource is not List<PropertyItem> items) return;
        PropertyItem? item = items.FirstOrDefault(i => i is ObjectOrListItem g && g.Index == rowIndex || i is ValueItem v && v.Index == rowIndex);
        if (item is not null) PropertyList.ScrollIntoView(item);
    }

    /// <summary>A click on an arrow of the properties list: an object's or list's inside is hidden or shown; "Used by" opens or closes.</summary>
    private void PropertiesMouseDown(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null; at = at is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is FrameworkElement { Tag: "toggle", DataContext: ObjectOrListItem group })
            {
                e.Handled = true;
                if (!closedRows.Remove(group.Index)) closedRows.Add(group.Index);
                ShowProperties();
                return;
            }
            if (at is FrameworkElement { Tag: "users" })
            {
                e.Handled = true;
                usersOpen = !usersOpen;
                ShowProperties();
                return;
            }
            if (at is ListBoxItem { DataContext: UserItem user })
            {
                e.Handled = true;
                UserClicked(user.User, e.ClickCount);
                return;
            }
            if (at is ListBox) return;
        }
    }

    /// <summary>A descriptor in "Used by" clicked: its properties are listed; a double-click (or Alt+click) shows it in the text.</summary>
    private void UserClicked(ReferenceIndex.User user, int clickCount)
    {
        if (clickCount >= 2 && lastUserClick is var (node, at) && (DateTime.Now - at).TotalMilliseconds < 700)
        {
            lastUserClick = null;
            _ = Pick(node, reveal: true);
            return;
        }
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        TreeNode row = NodeOf(user);
        lastUserClick = alt ? null : (row, DateTime.Now);
        _ = Pick(row, reveal: alt);
    }

    /// <summary>
    /// Shows a descriptor of a file on the Files tab, for the Units tab's "Show Unit in File Editor" and the like: the
    /// Editor tab comes to the front with the file open, the descriptor selected in the Descriptor Tree and shown in
    /// the text.
    /// </summary>
    public async Task ShowDescriptor(FileEntry entry, string name)
    {
        MainView view = MainView.Of(this);
        view.Tabs.SelectedItem = view.EditorTabItem;
        if (IsLocal(entry))
        {
            int index = outline.FindIndex(descriptor => descriptor.Name == name);
            if (index < 0) return;
            otherDescriptor = null;
            await SelectDescriptor(index, reveal: true, keepClosed: false);
            ShowTree();
            return;
        }
        // shown once the file is open and its descriptors are listed
        wanted = new Wanted(name, 0, -1, Quiet: false);
        await Open(entry);
        if (shown?.Entry != entry) wanted = null;
    }

    /// <summary>A descriptor in "Used by" as a tree row, so it can be picked and shown like one.</summary>
    private static TreeNode NodeOf(ReferenceIndex.User user) => new()
    {
        Key = "U\u0001" + user.Entry.Id.ToString(CultureInfo.InvariantCulture) + "\u0001" + user.Name, Kind = TreeNodeKind.Descriptor,
        Label = user.Name, Sub = user.Type, Entry = user.Entry, Name = user.Name,
    };

    // ---- what the viewer shows ----

    /// <summary>The tree's top rows.</summary>
    internal IReadOnlyList<TreeNode> TreeRoots => treeRoots;

    /// <summary>The properties list's rows.</summary>
    internal IReadOnlyList<PropertyItem> PropertyItems => PropertyList.ItemsSource as List<PropertyItem> ?? [];

    /// <summary>The note above the viewer.</summary>
    internal string DescriptorNoteText => string.Concat(DescriptorNote.Inlines.OfType<Run>().Select(r => r.Text));

    /// <summary>The descriptor whose properties are listed: its name; "" for none.</summary>
    internal string ListedDescriptor => otherDescriptor?.Name ?? (selectedDescriptor >= 0 && selectedDescriptor < outline.Count ? outline[selectedDescriptor].Name : "");

    // ---- right-click menus ----

    /// <summary>The tree's right-click menu: on a row, show it in the File Editor, open or close it, copy its name; then the bar's choices.</summary>
    private void TreeRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        (TreeNode? node, _) = RowUnder(e);
        List<Controls.MenuChoice?> items = [];
        if (node is not null)
        {
            if (node.Kind != TreeNodeKind.File)
                items.Add(new Controls.MenuChoice(Texts.Get("Editor.Menu.ShowInEditor"), () => { CloseNow(); selectedKey = node.Key; _ = Pick(node, reveal: true); }));
            if (!node.IsLeaf)
                items.Add(new Controls.MenuChoice(node.IsOpen ? Texts.Get("Editor.Menu.Collapse") : Texts.Get("Editor.Menu.Expand"), () => { CloseNow(); _ = Toggle(node); }));
            items.Add(new Controls.MenuChoice(Texts.Get("Editor.Menu.CopyName"), () => Copy(node.Name != "" ? node.Name : node.Label)));
            items.Add(Controls.Menus.Line);
        }
        items.Add(new Controls.MenuChoice(Texts.Get("Editor.ThisFile"), () => ModeOneClicked(this, e)));
        items.Add(new Controls.MenuChoice(Texts.Get("Editor.AllFiles"), () => ModeAllClicked(this, e)));
        items.Add(new Controls.MenuChoice(scanning ? Texts.Get("Editor.Scan.Stop") : Texts.Get("Editor.ScanAll"), () => ScanClicked(this, e)));
        Controls.Menus.Show(TreeList, items);
    }

    /// <summary>
    /// The properties' right-click menu: on a row, copy its property name or value; on a descriptor in "Used by", show
    /// it in the File Editor or copy its name; elsewhere, copy the descriptor's name. A value being typed over keeps its
    /// own menu.
    /// </summary>
    private void PropertiesRightClicked(object sender, MouseButtonEventArgs e)
    {
        PropertyItem? item = null;
        TextBox? box = null;
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null; at = at is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is TextBox found) box = found;
            if (at is ListBoxItem { DataContext: PropertyItem row }) { item = row; break; }
        }
        if (box is { IsReadOnly: false }) return;
        e.Handled = true;
        string selected = box?.SelectedText ?? "";
        List<Controls.MenuChoice?> items = [];
        if (selected != "") items.Add(new Controls.MenuChoice(Texts.Get("Editor.Menu.CopySelected"), () => Copy(selected)));
        if (item is ValueItem or ObjectOrListItem)
        {
            string key = item is ValueItem v ? v.Key : ((ObjectOrListItem)item).Key;
            items.Add(new Controls.MenuChoice(Texts.Get("Editor.Menu.CopyPropertyName"), () => Copy(key)));
            items.Add(new Controls.MenuChoice(Texts.Get("Editor.Menu.CopyValue"), () => Copy(((ValueItem)item).Shown), item is ValueItem));
        }
        else if (item is UserItem user)
        {
            items.Add(new Controls.MenuChoice(Texts.Get("Editor.Menu.ShowInEditor"), () => _ = Pick(NodeOf(user.User), reveal: true)));
            items.Add(new Controls.MenuChoice(Texts.Get("Editor.Menu.CopyName"), () => Copy(user.User.Name)));
        }
        else
        {
            string name = otherDescriptor?.Name ?? (selectedDescriptor >= 0 && selectedDescriptor < outline.Count ? outline[selectedDescriptor].Name : "");
            items.Add(new Controls.MenuChoice(Texts.Get("Editor.Menu.CopyName"), () => Copy(name), name != ""));
        }
        Controls.Menus.Show(PropertyList, items);
    }

    // ---- typing over a value ----

    private void ValueKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ValueItem item } box) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Commit(box, item);
        }
        else if (e.Key == Key.Escape && box.Text != item.Shown)
        {
            // the value as it was; Escape goes no further while there was something to undo
            e.Handled = true;
            box.Text = item.Shown;
        }
    }

    /// <summary>Leaving a value that was typed over writes it, as a web page's "change" does.</summary>
    private void ValueLeft(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: ValueItem item } box && box.Text != item.Shown) Commit(box, item);
    }

    private void ValueTyped(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box) box.Tag = null;
    }

    /// <summary>
    /// A value typed over (the HTML app's <c>dvCommit</c>): put into the descriptor's text, read back, and only then
    /// written into the editor, at the exact place the value sits.
    /// </summary>
    internal void Commit(TextBox box, ValueItem item)
    {
        if (otherDescriptor is not null || box.IsReadOnly || committing) return;
        if (item.Index >= propertyRows.Count || propertyRows[item.Index] is not ValueRow row) return;
        if (selectedDescriptor < 0 || selectedDescriptor >= outline.Count) return;
        OutlineEntry descriptor = outline[selectedDescriptor];
        string typed = box.Text.Trim();
        if (typed == row.Value)
        {
            box.Text = row.Value;
            return;
        }
        void Refuse(string why)
        {
            box.Text = item.Shown;
            box.Tag = "bad";
            SayDescriptors(Texts.Get("Editor.Value.NotChanged", ("why", why)), warning: true);
        }
        if (!CanChangeValues || descriptorsStale)
        {
            box.Text = item.Shown;
            return;
        }
        if (typed == "")
        {
            Refuse(Texts.Get("Editor.Value.Empty"));
            return;
        }
        TextDocument document = shown!.Document;
        int start = descriptor.Start + row.Start, end = descriptor.Start + row.End;
        bool Fresh() => !descriptorsStale && CanChangeValues && ReferenceEquals(Text.Document, document) && document.TextLength == outlineText.TextLength
            && OutlineText(start, end) == row.Value && outline.Contains(descriptor);
        if (!Fresh())
        {
            // the text moved on: it is listed again first
            descriptorsStale = true;
            KickDescriptors(0);
            return;
        }
        committing = true;
        try
        {
            string old = OutlineText(descriptor.Start, descriptor.End);
            string changed = old[..row.Start] + typed + old[row.End..];
            PropertyList read;
            try { read = DescriptorViewer.Properties(changed); }
            catch (Exception problem) when (problem is NdfParseException or ArgumentException)
            {
                Refuse(Texts.Get("Editor.Value.WouldNotRead", ("value", typed.Length > 60 ? typed[..57] + "..." : typed), ("problem", problem.Message)));
                return;
            }
            // a value inside a folded block: the block is unfolded, then the value changed
            Unfold(start, end);
            if (document.GetText(start, end - start) != row.Value)
            {
                descriptorsStale = true;
                KickDescriptors(0);
                return;
            }
            bool hadFocus = Text.TextArea.IsKeyboardFocusWithin;
            changingOwnText = true;
            try { document.Replace(start, end - start, typed); }
            finally { changingOwnText = false; }
            int line = document.GetLineByOffset(start).LineNumber - 1;
            int delta = typed.Length - (end - start);
            // the text the descriptors were read from was the editor's text before this change, so now it is the editor's text
            outlineText = document.CreateSnapshot();
            int at = outline.IndexOf(descriptor);
            outline[at] = descriptor with { End = descriptor.End + delta };
            for (int k = at + 1; k < outline.Count; k++) outline[k] = outline[k] with { Start = outline[k].Start + delta, End = outline[k].End + delta };
            shown.PointedAt = document.CreateAnchor(document.GetLineByNumber(line + 1).Offset);
            shading.PointedAt = shown.PointedAt;
            Text.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
            if (selectedDescriptor == at)
            {
                if (read.Rows.Count != propertyRows.Count) closedRows = [];
                propertyRows = read.Rows;
                propertiesCut = read.IsCut;
                ShowProperties();
                if (!hadFocus) FocusValueAgain(item.Index);
            }
            _ = RebuildTree();
            int userCount = users.Count;
            string said = Texts.Get("Editor.Value.Changed",
                ("descriptor", descriptor.Name != "" ? descriptor.Name : Texts.Get("Editor.Unnamed")), ("property", row.Key),
                ("before", Shortened(row.Value)), ("after", Shortened(typed)), ("line", Texts.Number(line + 1)));
            SayDescriptors(said + (userCount > 0 ? " " + Texts.CountWide("Editor.Value.UsersGetIt", userCount) : ""));
        }
        finally { committing = false; }
    }

    /// <summary>A value of at most 40 characters, for the note.</summary>
    private static string Shortened(string value) => value.Length > 40 ? value[..37] + "..." : value;

    /// <summary>After a change, the value's box has the focus again, so Enter can be pressed on the next one.</summary>
    private void FocusValueAgain(int rowIndex)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (PropertyList.ItemsSource is not List<PropertyItem> items) return;
            PropertyItem? item = items.FirstOrDefault(i => i is ValueItem v && v.Index == rowIndex);
            if (item is null || PropertyList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) return;
            if (FindChild<TextBox>(container) is TextBox box) box.Focus();
        }, DispatcherPriority.Loaded);
    }

    private static T? FindChild<T>(DependencyObject element) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
            if (child is T found) return found;
            if (FindChild<T>(child) is T inner) return inner;
        }
        return null;
    }

    // ---- sizes, the bar between the lists, and folding the section away ----

    /// <summary>The tree's share of the width, from 0.12 to 0.8; null for half.</summary>
    private double? splitShare;

    /// <summary>The bar between the lists dragged: what one gains in width the other loses (the HTML app's <c>dvSplit</c>).</summary>
    private void SplitMoved(double x)
    {
        double width = DescriptorBox.ActualWidth - 9;
        if (width < 200) return;
        splitShare = Math.Max(0.12, Math.Min(0.8, Math.Round((x - 4) / width * 1000) / 1000));
        ShowSplit();
    }

    private void RememberSplit() => FilesPage.RememberSize("dvSplit", splitShare);

    /// <summary>The two lists' widths: shares of the room in whole thousandths, each at least its least width.</summary>
    private void ShowSplit()
    {
        int share = splitShare is double s && s > 0 && s < 1 ? (int)Math.Round(s * 1000) : 500;
        TreeColumn.Width = new GridLength(share, GridUnitType.Star);
        PropertiesColumn.Width = new GridLength(1000 - share, GridUnitType.Star);
    }

    /// <summary>The least widths: the tree min(9rem, 30%), the properties min(14rem, 40%).</summary>
    private void DescriptorBoxResized(object sender, SizeChangedEventArgs e)
    {
        double width = e.NewSize.Width;
        TreeColumn.MinWidth = Math.Min(144, 0.3 * width);
        PropertiesColumn.MinWidth = Math.Min(224, 0.4 * width);
        // the property rows follow the width by themselves (Editor/KeyWidth.cs): building the list again on every step
        // of a resize made resizing the window slow
    }
}
