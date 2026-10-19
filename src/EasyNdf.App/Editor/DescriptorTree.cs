using System.Text.RegularExpressions;
using EasyNdf.Data.FileList;
using EasyNdf.Engine;
using EasyNdf.Engine.Viewer;
using EasyNdf.App.Text;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Editor;

/// <summary>What a row of the descriptor tree is.</summary>
public enum TreeNodeKind
{
    /// <summary>A file ("All files"): it opens to the descriptors that start chains in it.</summary>
    File,

    /// <summary>A descriptor of a file.</summary>
    Descriptor,

    /// <summary>An object inside a descriptor.</summary>
    Object,

    /// <summary>A list inside a descriptor.</summary>
    List,

    /// <summary>A descriptor that one of a descriptor's values names; it may be in another file.</summary>
    Reference,
}

/// <summary>
/// A descriptor's property rows, as the properties list shows them: the rows, whether they were cut short, its type and
/// kind, and where it is in the open file's text (null for one in another file).
/// </summary>
public sealed record DescriptorRows(List<PropertyRow> Rows, bool IsCut, string Type, OutlineKind Kind, OutlineEntry? Local);

/// <summary>A row of the descriptor tree (the HTML app's <c>tvNode</c>).</summary>
public sealed class TreeNode
{
    /// <summary>The row that holds it; null at the top.</summary>
    public TreeNode? Parent { get; init; }

    /// <summary>A key that stays the same when the tree is made again, so open rows stay open.</summary>
    public required string Key { get; init; }

    /// <summary>What the row is.</summary>
    public TreeNodeKind Kind { get; init; }

    /// <summary>Its name as shown.</summary>
    public string Label { get; init; } = "";

    /// <summary>What follows the name, dimmed: a type, "constant", or for a file how many chains it starts.</summary>
    public string Sub { get; init; } = "";

    /// <summary>The file it is in, when that is worth saying: " · Ammunition.ndf".</summary>
    public string FileTag { get; set; } = "";

    /// <summary>How deep it is: 0 at the top.</summary>
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;

    /// <summary>Whether it has nothing inside to open.</summary>
    public bool IsLeaf { get; set; }

    /// <summary>Whether it is open.</summary>
    public bool IsOpen { get; set; }

    /// <summary>The rows inside it, once worked out.</summary>
    public List<TreeNode>? Children { get; set; }

    /// <summary>The file the descriptor (or the file row) is in; null for a file opened on its own in the editor.</summary>
    public FileEntry? Entry { get; init; }

    /// <summary>The descriptor's name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Where the descriptor is in the open file's text, when it is there.</summary>
    public OutlineEntry? Outline { get; init; }

    /// <summary>The rows of the descriptor an object or list row is part of, or of the descriptor itself once read.</summary>
    public DescriptorRows? Rows { get; set; }

    /// <summary>For an object, list or reference: the row of <see cref="Rows"/> it stands for; -1 otherwise.</summary>
    public int RowIndex { get; init; } = -1;

    /// <summary>The descriptor whose properties the row lists: itself, or the one it is inside (the HTML app's <c>tvOwner</c>).</summary>
    public TreeNode? Owner
    {
        get
        {
            TreeNode? at = this;
            while (at is not null && at.Kind is not (TreeNodeKind.Descriptor or TreeNodeKind.Reference)) at = at.Parent;
            return at;
        }
    }
}

/// <summary>
/// How the descriptor tree's rows are found (the HTML app's <c>tvKids</c>, <c>tvOfOpen</c>, <c>tvOfJob</c>,
/// <c>tvFileKids</c>, <c>tvResolve</c> and <c>tvRefName</c>). Under a descriptor come the objects and lists inside it, and
/// wherever one of its values is the name of another descriptor, that descriptor, which opens in turn: a unit, its
/// weapons, their ammunition. The other descriptor may be in another file.
/// </summary>
/// <param name="localEntry">The file open in the editor; null for one opened on its own.</param>
/// <param name="hasLocal">Whether a file is open in the editor at all.</param>
/// <param name="localOutline">The descriptors of the file open in the editor.</param>
/// <param name="localText">That file's text.</param>
/// <param name="index">What the read files declare and refer to.</param>
public sealed class DescriptorTree(FileEntry? localEntry, bool hasLocal, IReadOnlyList<OutlineEntry> localOutline, ITextSource localText, ReferenceIndex index)
{
    /// <summary>The open file's descriptors by name: the first of each name.</summary>
    public Dictionary<string, OutlineEntry> LocalByName { get; } = ByName(localOutline);

    /// <summary>What the read files declare and refer to.</summary>
    public ReferenceIndex Index => index;

    private static Dictionary<string, OutlineEntry> ByName(IReadOnlyList<OutlineEntry> outline)
    {
        var byName = new Dictionary<string, OutlineEntry>(StringComparer.Ordinal);
        foreach (OutlineEntry entry in outline)
            if (entry.Name != "") byName.TryAdd(entry.Name, entry);
        return byName;
    }

    /// <summary>Whether a file is the one open in the editor (the HTML app's <c>tvLocal</c>).</summary>
    public bool IsLocal(FileEntry? entry) => hasLocal && entry == localEntry;

    /// <summary>The key of a top-level descriptor row: its file, its name, and which of that name it is.</summary>
    public static string RootKey(FileEntry? entry, string name, int count) =>
        "J" + (entry is null ? "open" : entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "\u0001" + name + "\u0001" + count;

    /// <summary>The "constant" a constant shows as its type.</summary>
    private static string TypeShown(OutlineEntry entry) => entry.Kind == OutlineKind.Constant ? "constant" : entry.Type;

    private static readonly Regex Reference = new(@"^(?:[$~]/)?[A-Za-z_][A-Za-z0-9_/]*\z", RegexOptions.CultureInvariant);

    /// <summary>The name a value refers to: "$/GFX/Weapon/Ammo_X" refers to Ammo_X; "" for a value that is no reference.</summary>
    public static string ReferredName(string value)
    {
        if (value.Length > 200 || !Reference.IsMatch(value)) return "";
        return value[(value.LastIndexOf('/') + 1)..];
    }

    /// <summary>
    /// Where a name is declared (the HTML app's <c>tvResolve</c>): in the open file, or in another read file; null for
    /// none. The type is "" for a constant.
    /// </summary>
    public (FileEntry? Entry, string Type, OutlineEntry? Local)? Resolve(string name)
    {
        if (name == "") return null;
        if (hasLocal && LocalByName.TryGetValue(name, out OutlineEntry? local))
            return (localEntry, local.Kind == OutlineKind.Constant ? "" : local.Type, local);
        if (index.Names.TryGetValue(name, out ReferenceIndex.Declared declared) && !IsLocal(declared.Entry)) return (declared.Entry, declared.Type, null);
        return null;
    }

    /// <summary>Whether a descriptor of the open file has anything inside: an object, or a constant with brackets (the HTML app's <c>tvDeep</c>).</summary>
    public bool IsDeep(OutlineEntry? entry)
    {
        if (entry is null || entry.Kind != OutlineKind.Constant) return true;
        for (int i = entry.Start; i < entry.End && i < localText.TextLength; i++)
            if (localText.GetCharAt(i) is '(' or '[') return true;
        return false;
    }

    /// <summary>The descriptors of the open file as rows (the HTML app's <c>tvOfOpen</c>). Only those named in <paramref name="only"/>, when given; only those whose name or type holds <paramref name="filter"/>.</summary>
    public List<TreeNode> OfOpenFile(TreeNode? parent, IReadOnlySet<string>? only, string filter)
    {
        var rows = new List<TreeNode>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (OutlineEntry entry in localOutline)
        {
            int count = counts.GetValueOrDefault(entry.Name);
            counts[entry.Name] = count + 1;
            string type = TypeShown(entry);
            if (only is not null && !only.Contains(entry.Name)) continue;
            if (filter != "" && !(entry.Name + " " + type).ToLowerInvariant().Contains(filter, StringComparison.Ordinal)) continue;
            rows.Add(new TreeNode
            {
                Parent = parent, Key = RootKey(localEntry, entry.Name, count), Kind = TreeNodeKind.Descriptor,
                Label = entry.Name != "" ? entry.Name : Texts.Get("Editor.Unnamed"), Sub = type, Entry = localEntry, Name = entry.Name, Outline = entry,
                IsLeaf = !IsDeep(entry) || !OpensToSomething(localEntry, entry.Name),
            });
        }
        return rows;
    }

    /// <summary>The descriptors of another file as rows (the HTML app's <c>tvOfJob</c>), from what it declares.</summary>
    public List<TreeNode> OfOtherFile(TreeNode? parent, FileEntry entry, IEnumerable<(string Name, string Type)> declared, IReadOnlySet<string>? only, string filter)
    {
        var rows = new List<TreeNode>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string name, string type) in declared)
        {
            if (name == "") continue;
            int count = counts.GetValueOrDefault(name);
            counts[name] = count + 1;
            string shown = type != "" ? type : "constant";
            if (only is not null && !only.Contains(name)) continue;
            if (filter != "" && !(name + " " + shown).ToLowerInvariant().Contains(filter, StringComparison.Ordinal)) continue;
            rows.Add(new TreeNode
            {
                Parent = parent, Key = RootKey(entry, name, count), Kind = TreeNodeKind.Descriptor, Label = name, Sub = shown, Entry = entry, Name = name,
                IsLeaf = !OpensToSomething(entry, name),
            });
        }
        return rows;
    }

    /// <summary>What a file row opens to (the HTML app's <c>tvFileKids</c>): the descriptors that start chains in it.</summary>
    public List<TreeNode> FileChildren(TreeNode file)
    {
        if (file.Entry is not FileEntry entry || file.Entry.References is null) return [];
        if (!index.ChainsOf().Tops.TryGetValue(entry, out HashSet<string>? tops)) return [];
        return IsLocal(entry) ? OfOpenFile(file, tops, "") : OfOtherFile(file, entry, entry.References.Select(d => (d.Name, d.Type)), tops, "");
    }

    /// <summary>
    /// What is shown under row <paramref name="rowIndex"/> of a descriptor (-1: the descriptor itself) (the HTML app's
    /// <c>tvKids</c>): the objects and lists there, and the descriptors its values name.
    /// </summary>
    public List<TreeNode> Children(TreeNode parent, DescriptorRows rows, int rowIndex)
    {
        if (!ShowsEverythingInside) return OtherFilesNamed(parent, rows);
        var children = new List<TreeNode>();
        List<PropertyRow> all = rows.Rows;
        int depth = rowIndex < 0 ? 0 : all[rowIndex].Depth + 1;
        TreeNode? owner = parent.Owner;
        for (int i = rowIndex + 1; i < all.Count; i++)
        {
            PropertyRow row = all[i];
            if (row.Depth < depth) break;
            if (row.Depth > depth) continue;
            if (row is GroupRow group)
            {
                bool isList = !group.IsObject, bare = row.Key.StartsWith('[') && !isList && group.Title != "";
                // it opens when something inside is a group, or names a descriptor
                bool hasInside = false;
                for (int j = i + 1; j < all.Count && all[j].Depth > row.Depth; j++)
                {
                    if (all[j] is GroupRow || (all[j] is ValueRow inner && Resolve(ReferredName(inner.Value)) is not null)) { hasInside = true; break; }
                }
                children.Add(new TreeNode
                {
                    Parent = parent, Key = parent.Key + "\u0002" + row.Key, Kind = isList ? TreeNodeKind.List : TreeNodeKind.Object,
                    Label = bare ? group.Title : row.Key, Sub = bare ? "" : group.Title, Entry = owner?.Entry, Rows = rows, RowIndex = i, IsLeaf = !hasInside,
                });
                continue;
            }
            var value = (ValueRow)row;
            string name = ReferredName(value.Value);
            if (Resolve(name) is not var (entry, type, local)) continue;
            bool loops = false, far = !IsLocal(entry);
            for (TreeNode? above = parent; above is not null; above = above.Parent)
                if (above.Kind is TreeNodeKind.Descriptor or TreeNodeKind.Reference && above.Name == name) { loops = true; break; }
            children.Add(new TreeNode
            {
                Parent = parent, Key = parent.Key + "\u0002" + row.Key, Kind = TreeNodeKind.Reference, Label = name, Sub = type != "" ? type : "constant",
                FileTag = entry is not null && entry != owner?.Entry ? entry.Name : "", Entry = entry, Name = name, Outline = local, RowIndex = i,
                IsLeaf = loops || parent.Depth > 60 || (type == "" && !far && !IsDeep(local)),
            });
        }
        return children;
    }

    /// <summary>
    /// The "Show everything in the descriptor tree" setting: on, a descriptor opens to its objects and lists and to every
    /// descriptor it names, as the HTML app's tree does; off (the default), only to the descriptors it names in other
    /// files, so the tree shows how the files connect. The properties list shows the rest.
    /// </summary>
    public static bool ShowsEverythingInside => AppData.Current.Settings["treeShowsAll"]?.GetValue<bool>() == true;

    /// <summary>
    /// Whether a descriptor has anything to open to, so it gets an arrow. While the tree shows only the connections
    /// between files, that is whether it names a descriptor in another file, which the files' reference lists tell without
    /// reading the descriptor. When showing everything, or when the lists do not have it (an edited open file), it is
    /// taken to have something; opening it finds out and removes the arrow if not.
    /// </summary>
    private bool OpensToSomething(FileEntry? entry, string name)
    {
        if (ShowsEverythingInside || entry is null || index.RefersOf(entry, name) is not IReadOnlyList<string> refers) return true;
        foreach (string referred in refers)
            if (referred != name && Resolve(referred) is (FileEntry other, _, _) && other != entry) return true;
        return false;
    }

    /// <summary>
    /// The descriptors that a descriptor's values name in other files, wherever the values sit inside it, each once, in
    /// the order they first appear.
    /// </summary>
    private List<TreeNode> OtherFilesNamed(TreeNode parent, DescriptorRows rows)
    {
        var children = new List<TreeNode>();
        var named = new HashSet<string>(StringComparer.Ordinal);
        FileEntry? ownFile = parent.Owner?.Entry;
        for (int i = 0; i < rows.Rows.Count; i++)
        {
            if (rows.Rows[i] is not ValueRow value) continue;
            string name = ReferredName(value.Value);
            if (Resolve(name) is not var (entry, type, local) || entry is null || entry == ownFile || !named.Add(name)) continue;
            bool loops = false;
            for (TreeNode? above = parent; above is not null; above = above.Parent)
                if (above.Kind is TreeNodeKind.Descriptor or TreeNodeKind.Reference && above.Name == name) { loops = true; break; }
            children.Add(new TreeNode
            {
                Parent = parent, Key = parent.Key + "\u0002" + name, Kind = TreeNodeKind.Reference, Label = name, Sub = type != "" ? type : "constant",
                FileTag = entry.Name, Entry = entry, Name = name, Outline = local, RowIndex = i,
                IsLeaf = loops || parent.Depth > 60 || !OpensToSomething(entry, name),
            });
        }
        return children;
    }

    /// <summary>The rows as shown: each row, then what is inside it while it is open (the HTML app's <c>tvFlat</c>).</summary>
    public static List<TreeNode> Flatten(IEnumerable<TreeNode> roots)
    {
        var shown = new List<TreeNode>();
        void Add(IEnumerable<TreeNode> rows)
        {
            foreach (TreeNode row in rows)
            {
                shown.Add(row);
                if (row.IsOpen && row.Children is not null) Add(row.Children);
            }
        }
        Add(roots);
        return shown;
    }
}
