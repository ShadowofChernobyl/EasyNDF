using System.ComponentModel;
using System.Runtime.CompilerServices;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.App.Text;

namespace EasyNdf.App.Files;

/// <summary>
/// One row of the file list as the screen shows it: the <see cref="FileEntry"/> it stands for, with its texts ready to
/// show. When the row changes, <see cref="Refresh"/> tells the screen to show it again.
/// </summary>
public sealed class FileRow(FileEntry entry) : INotifyPropertyChanged
{
    /// <summary>The file this row stands for.</summary>
    public FileEntry Entry { get; } = entry;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The row's place in the list as shown, from 1 (the # column).</summary>
    public int Number { get => number; set => Set(ref number, value); }
    private int number;

    /// <summary>Whether the row is selected.</summary>
    public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }
    private bool isSelected;

    /// <summary>Raised when the On checkbox is ticked or cleared, so the folder's line can follow.</summary>
    public event EventHandler? SwitchedOnOrOff;

    /// <summary>Raised when another preset is picked in the row's dropdown.</summary>
    public event EventHandler? PresetChosen;

    /// <summary>The On checkbox: whether the file takes part in runs.</summary>
    public bool IsOn
    {
        get => Entry.On;
        set
        {
            if (Entry.On == value) return;
            Entry.On = value;
            Refresh();
            SwitchedOnOrOff?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The Source file column: the name, and " (not loaded)" when the file is not loaded.</summary>
    public string NameText => Entry.Source is not null ? Entry.Name : Texts.Get("Files.Row.NotLoaded", ("file", Entry.Name));

    /// <summary>Whether EasyNDF exported the file before: its name then has an "edited" tag.</summary>
    public bool WasExported => Entry.ExportStamp is not null;

    /// <summary>The tag's tooltip: what the file's stamp says, and why it matters.</summary>
    public string ExportedTip => Entry.ExportStamp is string stamp ? Texts.Get("Files.Exported.Tip", ("stamp", stamp)) : "";

    /// <summary>How many rows, this one included, hold a file of the same folder and name.</summary>
    public int SameFileCount { get; set; } = 1;

    /// <summary>The line under the name with the folder the file sat in; "" for a file added on its own.</summary>
    /// <remarks>
    /// With "Group by folder", the folder is on the line above instead. In a long list, the size and date follow on the
    /// same line, after "   ·   ".
    /// </remarks>
    public string FolderLine
    {
        get
        {
            string folder = Entry.Source is not null && !IsInFolder ? Entry.Folder : "";
            if (!IsInLongList) return folder;
            return string.Join("   \u00B7   ", new[] { folder, SizeAndDate }.Where(part => part != ""));
        }
    }

    /// <summary>Whether the row is in a long list, which gives each row one second line at most.</summary>
    public bool IsInLongList { get; set; }

    /// <summary>Whether the row is shown under its folder's line ("Group by folder").</summary>
    public bool IsInFolder { get; set; }

    /// <summary>Whether there is a folder line.</summary>
    public bool HasFolderLine => FolderLine != "";

    /// <summary>
    /// The line under the name that tells two rows of the same file apart: its size and date, such as "505 B, modified
    /// 2026-10-11 20:03"; "" when no other row has the same file.
    /// </summary>
    public string DuplicateLine => IsInLongList ? "" : SizeAndDate;

    /// <summary>The size and date that tell two rows of the same file apart; "" when no other row has the same file.</summary>
    private string SizeAndDate
    {
        get
        {
            if (Entry.Source is null || SameFileCount < 2) return "";
            var parts = new List<string>();
            if (Entry.Size is long size) parts.Add(FileStatus.Size(size));
            if (Entry.Modified is DateTime modified) parts.Add(Texts.Get("Files.Row.Modified", ("date", FileStatus.Date(modified))));
            return string.Join(", ", parts);
        }
    }

    /// <summary>Whether there is a duplicate line.</summary>
    public bool HasDuplicateLine => DuplicateLine != "";

    /// <summary>The Lines column, once the file has been read.</summary>
    public string LinesText => Entry.Lines is int lines ? FileStatus.Count(lines) : "";

    /// <summary>The Date modified column.</summary>
    public string ModifiedText => Entry.Source is not null && Entry.Modified is DateTime modified ? FileStatus.Date(modified) : "";

    /// <summary>The Export folder column: the folder's name, or "Not set".</summary>
    public string ExportFolderText => Entry.ExportFolder is string folder ? FileListView.FolderName(folder) : ColumnFit.NoExportFolderText;

    /// <summary>The Export folder column's tooltip: the whole path, or what happens without one.</summary>
    public string ExportFolderTip => Entry.ExportFolder ?? Texts.Get("Files.Row.NoExportFolder.Tip");

    /// <summary>Whether the file has an export folder (then it can be cleared).</summary>
    public bool HasExportFolder => Entry.ExportFolder is not null;

    /// <summary>The Size column.</summary>
    public string SizeText => Entry.Source is not null && Entry.Size is long size ? FileStatus.Size(size) : "";

    /// <summary>The Status column's text.</summary>
    public string StatusText => FileStatus.Of(Entry).Text;

    /// <summary>How the Status column's text is shown.</summary>
    public StatusKind StatusKind => FileStatus.Of(Entry).Kind;

    /// <summary>
    /// What the row's preset dropdown offers: the list's choices, and the row's own preset when it is another mod's.
    /// </summary>
    public IReadOnlyList<PresetChoice> Choices
    {
        get
        {
            if (Entry.Preset is not ModPreset preset || ListChoices.Any(c => ReferenceEquals(c.Preset, preset))) return ListChoices;
            return [.. ListChoices, new PresetChoice(preset, AppData.Current.ModOf(preset)?.Name)];
        }
    }

    /// <summary>The list's choices: "(none)" and the open mod's presets (set by the list).</summary>
    public IReadOnlyList<PresetChoice> ListChoices { get; set; } = [PresetChoice.None];

    /// <summary>The preset dropdown's choice: a preset, or <see cref="PresetChoice.None"/>.</summary>
    public PresetChoice Preset
    {
        get => Entry.Preset is null ? PresetChoice.None : Choices.First(c => ReferenceEquals(c.Preset, Entry.Preset));
        set
        {
            if (ReferenceEquals(value.Preset, Entry.Preset)) return;
            Entry.Preset = value.Preset;
            Entry.MissingPreset = null;
            Entry.ClearMessage();
            Refresh();
            PresetChosen?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether the row has a "Save anyway" button: what the rules made failed the check, and was held back.</summary>
    public bool HasHeldOutput => Entry.HeldOutput is not null;

    /// <summary>Tells the screen that everything about the row may have changed.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// One choice of a row's preset dropdown: a preset, or "(none)". A preset of a mod that is not open is shown with its
/// mod's name in front, "WTO: WTO (Ammunition.ndf)", as the HTML app's <c>otherModOpt</c> shows it.
/// </summary>
public sealed record PresetChoice(ModPreset? Preset, string? OtherModName = null)
{
    /// <summary>The "(none)" choice.</summary>
    public static readonly PresetChoice None = new((ModPreset?)null);

    /// <summary>What the dropdown shows.</summary>
    public override string ToString() => Preset is null ? Texts.Get("Files.Row.NoPreset") : OtherModName is null ? Preset.Name : OtherModName + ": " + Preset.Name;

    /// <summary>Two choices are the same when they name the same preset.</summary>
    public bool Equals(PresetChoice? other) => other is not null && ReferenceEquals(Preset, other.Preset);

    /// <inheritdoc/>
    public override int GetHashCode() => Preset?.GetHashCode() ?? 0;
}

/// <summary>
/// With "Group by folder", the line above a folder's files (the HTML app's <c>tr.fold</c>): a checkbox for all of them,
/// a button that opens or closes the folder, its path, and how many files it holds.
/// </summary>
/// <param name="path">The folder's path; "" for files added on their own.</param>
/// <param name="rows">Its files that the search and filter leave.</param>
/// <param name="isOpen">Whether its files are shown.</param>
public sealed class FolderRow(string path, IReadOnlyList<FileRow> rows, bool isOpen) : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The folder's path; "" for files added on their own.</summary>
    public string Path { get; } = path;

    /// <summary>Its files that the search and filter leave.</summary>
    public IReadOnlyList<FileRow> Rows { get; } = rows;

    /// <summary>Whether its files are shown.</summary>
    public bool IsOpen { get; } = isOpen;

    /// <summary>Tells the screen that the count or the checkbox may have changed.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    /// <summary>The path as shown.</summary>
    public string Label => Path != "" ? Path : Texts.Get("Files.Folder.OnTheirOwn");

    /// <summary>"3 files", and how many are selected.</summary>
    public string CountText
    {
        get
        {
            int picked = Rows.Count(r => r.IsSelected);
            string files = FileListModel.Files(Rows.Count);
            return picked > 0 ? Texts.Get("Files.Folder.CountSelected", ("files", files), ("selected", picked)) : files;
        }
    }

    /// <summary>The checkbox: ticked when every file is on.</summary>
    public bool AllOn => Rows.All(r => r.Entry.On);

    /// <summary>The button: ▾ when open, ▸ when closed.</summary>
    public string Arrow => IsOpen ? "\u25BE" : "\u25B8";
}
