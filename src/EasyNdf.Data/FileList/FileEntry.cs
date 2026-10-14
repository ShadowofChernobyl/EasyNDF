using EasyNdf.Engine.Export;

namespace EasyNdf.Data.FileList;

/// <summary>
/// One row of the Files tab's list (the HTML app's "job"): a file the rules can run on, the preset chosen for it, where its
/// export goes, and what its last run did.
/// </summary>
public sealed class FileEntry
{
    /// <summary>A number that tells rows apart, even when two rows hold files of the same name.</summary>
    public int Id { get; init; }

    /// <summary>The file's name, such as "Ammunition.ndf".</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The folders the file sat in, inside the folder or archive it was added from, such as "Generated/Gameplay/Gfx";
    /// "" for a file added on its own. Exports keep these folders under the export folder.
    /// </summary>
    public string Folder { get; set; } = "";

    /// <summary>Where the file is on disk (or inside an archive), or null when the row's file is not loaded.</summary>
    public string? Source { get; set; }

    /// <summary>
    /// The file's bytes when it came out of a .zip or .rar, which is not unpacked to disk; null for a file on disk.
    /// <see cref="Source"/> then names the archive and the path inside it.
    /// </summary>
    public byte[]? ArchivedContent { get; set; }

    /// <summary>
    /// The file as it was first loaded, once the File Editor's "Use in file list" has given the row edited text (kept in
    /// <see cref="ArchivedContent"/>); null while the row has its own file.
    /// </summary>
    public byte[]? FirstContent { get; set; }

    /// <summary>The file's size in bytes, when it is loaded.</summary>
    public long? Size { get; set; }

    /// <summary>When the file was last changed, when it is loaded.</summary>
    public DateTime? Modified { get; set; }

    /// <summary>Whether the file takes part in Preview and Run export (the "On" checkbox).</summary>
    public bool On { get; set; } = true;

    /// <summary>The preset whose rules run on the file, or null for none.</summary>
    public ModPreset? Preset { get; set; }

    /// <summary>The name of a preset the row was given that no longer exists, so its status can say so.</summary>
    public string? MissingPreset { get; set; }

    /// <summary>The folder its export is written to, or null when none is chosen yet.</summary>
    public string? ExportFolder { get; set; }

    /// <summary>How many lines the file has, once it has been read.</summary>
    public int? Lines { get; set; }

    /// <summary>What the file holds, once it has been read, such as "952 descriptors" (see <see cref="FileStatus.Holds"/>).</summary>
    public string Info { get; set; } = "";

    /// <summary>Whether the file has been read.</summary>
    public bool Scanned { get; set; }

    /// <summary>What the last action on the row said, such as "Preview: 120 edits"; "" for none.</summary>
    public string Message { get; set; } = "";

    /// <summary>Whether <see cref="Message"/> reports a failure.</summary>
    public bool IsError { get; set; }

    /// <summary>Whether the last export of the file finished.</summary>
    public bool Done { get; set; }

    /// <summary>How many warnings the last run gave.</summary>
    public int WarningCount { get; set; }

    /// <summary>How many rules changed nothing in the last run (counted only when some rule changed something).</summary>
    public int IdleRuleCount { get; set; }

    /// <summary>
    /// What the file declares and what each declaration refers to, once the file has been read; null before. The File
    /// Editor's descriptor viewer follows references from one file to another with it.
    /// </summary>
    public IReadOnlyList<EasyNdf.Engine.DeclarationReferences>? References { get; set; }

    /// <summary>How many edits the last run made, or null when the file has not been run.</summary>
    public int? Edits { get; set; }

    /// <summary>What the last Preview or Run export did, or null before the file has been run.</summary>
    public RunRecord? LastRun { get; set; }

    /// <summary>Where the last Run export wrote the file, or null when it was not written.</summary>
    public string? ExportedPath { get; set; }

    /// <summary>
    /// What the rules made when it is not valid NDF, so it was held back on export; "Save anyway" saves it so it can be
    /// looked at. Null otherwise.
    /// </summary>
    public string? HeldOutput { get; set; }

    /// <summary>
    /// The row's key: its folder and name in small letters, such as "generated/gameplay/gfx/ammunition.ndf". Two rows with
    /// the same key hold the same file; the search box matches against it.
    /// </summary>
    public string Key => FileReferences.Key(Folder, Name);

    /// <summary>Whether the row has a preset, even one that no longer exists.</summary>
    public bool HasPresetChosen => Preset is not null || MissingPreset is not null;

    /// <summary>Forgets what the last action said (the HTML app's <c>clearMsg</c>).</summary>
    public void ClearMessage()
    {
        Message = "";
        IsError = false;
        Done = false;
        WarningCount = 0;
        IdleRuleCount = 0;
        ExportedPath = null;
        HeldOutput = null;
    }
}
