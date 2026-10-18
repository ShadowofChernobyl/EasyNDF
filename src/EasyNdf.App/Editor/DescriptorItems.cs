using System.Windows;
using EasyNdf.App.Editor;
using EasyNdf.App.Text;

namespace EasyNdf.App.Editor;

/// <summary>A row of the descriptor tree as drawn (the HTML app's <c>.dvi</c>).</summary>
/// <param name="Node">The tree's row.</param>
/// <param name="IsSelected">Whether it is the row chosen.</param>
public sealed record TreeItem(TreeNode Node, bool IsSelected)
{
    /// <summary>4 pixels in, and 12 more for each step down the tree; 6 pixels at the right.</summary>
    public Thickness Indent => new(4 + Node.Depth * 12, 0, 6, 0);

    /// <summary>▾ for an open row, ▸ for a closed one, nothing for one with nothing inside.</summary>
    public string Arrow => Node.IsLeaf ? "" : Node.IsOpen ? "▾" : "▸";

    /// <summary>The name.</summary>
    public string Label => Node.Label;

    /// <summary>The type, dimmed; collapsed when there is none.</summary>
    public string Sub => Node.Sub;

    /// <summary>The other file it is in: "   · Ammunition.ndf"; "" for none.</summary>
    public string FileTag => Node.FileTag != "" ? "   · " + Node.FileTag : "";

    /// <summary>Whether it is a file ("All files"), whose name is drawn in the text colour.</summary>
    public bool IsFile => Node.Kind == TreeNodeKind.File;
}

/// <summary>Where a row of the properties list starts: 18 pixels in, and 14 more for each step down.</summary>
public abstract record PropertyItem(int Depth)
{
    /// <summary>The space before the row.</summary>
    public virtual Thickness Indent => new(18 + Depth * 14, 0, 4, 0);
}

/// <summary>An object or a list (the HTML app's <c>.dvtog</c> row): its key, its type or size, and whether its inside is hidden.</summary>
/// <param name="Index">Its row in the descriptor's rows.</param>
public sealed record ObjectOrListItem(int Depth, int Index, string Key, string Title, bool IsShut, int Count) : PropertyItem(Depth)
{
    /// <summary>▸ while its inside is hidden, ▾ while it shows.</summary>
    public string Arrow => IsShut ? "▸" : "▾";

    /// <summary>"3 items hidden" while hidden; "" otherwise.</summary>
    public string Hidden => IsShut ? Texts.Count("Editor.Rows.Hidden", Count) : "";

    /// <summary>What the arrow says it does.</summary>
    public string ArrowTip => IsShut ? Texts.Get("Editor.Rows.ShowInside") : Texts.Get("Editor.Rows.HideInside");
}

/// <summary>A value (the HTML app's <c>input.dvv</c> row), which can be typed over when the descriptor can be changed.</summary>
/// <param name="Index">Its row in the descriptor's rows.</param>
/// <param name="Shown">The value as shown: one that runs over several lines is put on one.</param>
/// <param name="IsReadOnly">Whether it cannot be typed over here.</param>
public sealed record ValueItem(int Depth, int Index, string Key, string Shown, bool IsReadOnly, bool RunsOverLines) : PropertyItem(Depth)
{
    /// <summary>The value's tooltip: why a value over several lines cannot be typed over here.</summary>
    public string? Tip => RunsOverLines ? Texts.Get("Editor.Rows.OverLines") : null;
}

/// <summary>"Used by": how many descriptors in the read files use this one; it opens to list them.</summary>
public sealed record UsedByItem(int Count, bool IsOpen) : PropertyItem(0)
{
    public override Thickness Indent => new(18, 0, 4, 0);

    /// <summary>▾ while the list is open, ▸ while closed.</summary>
    public string Arrow => IsOpen ? "▾" : "▸";

    /// <summary>"3 descriptors".</summary>
    public string Text => Texts.CountWide("Editor.Viewer.Descriptors", Count);
}

/// <summary>A descriptor that uses this one: its name, type and file.</summary>
/// <param name="User">The descriptor.</param>
public sealed record UserItem(ReferenceIndex.User User) : PropertyItem(0)
{
    public override Thickness Indent => new(32, 0, 6, 0);

    /// <summary>Its type, or "constant".</summary>
    public string Type => User.Type != "" ? User.Type : Texts.Get("Editor.Head.Constant");

    /// <summary>"   · Ammunition.ndf".</summary>
    public string FileTag => "   · " + User.Entry.Name;
}

/// <summary>A note in the list, such as "Nothing inside.", in the dim colour or as a warning.</summary>
public sealed record NoteItem(string Text, bool IsWarning, Thickness Space) : PropertyItem(0);
