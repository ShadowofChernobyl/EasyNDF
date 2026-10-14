using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Files;

/// <summary>
/// "Reopen the last files" (the desktop app's own; the browser could not do it): the file list is kept when the app
/// closes, in State["session"], and comes back when it starts, with the files that are still where they were. Each row
/// keeps its preset, its On box and its export folder. A file from a .zip or .rar is read from the archive again.
/// Text handed over with the File Editor's "Use in file list" is not kept: the file comes back as it is on disk.
/// </summary>
public static class FileSession
{
    /// <summary>One row as kept: where its file is, its folder and name, and its settings.</summary>
    public sealed record KeptRow(string Source, string Folder, string Name, bool On, string? Preset, string? ExportFolder);

    /// <summary>The rows of the file list, as kept.</summary>
    public static JsonArray Keep(IEnumerable<FileEntry> rows)
    {
        var kept = new JsonArray();
        foreach (FileEntry row in rows)
        {
            if (row.Source is null) continue;
            kept.Add(new JsonObject
            {
                ["source"] = row.Source,
                ["folder"] = row.Folder,
                ["name"] = row.Name,
                ["on"] = row.On,
                ["preset"] = row.Preset?.Name ?? row.MissingPreset,
                ["exportFolder"] = row.ExportFolder,
            });
        }
        return kept;
    }

    /// <summary>The rows kept last time.</summary>
    public static List<KeptRow> Rows(JsonNode? kept)
    {
        var rows = new List<KeptRow>();
        if (kept is not JsonArray all) return rows;
        foreach (JsonNode? node in all)
        {
            if (node is not JsonObject row || JsonValues.AsText(row["source"]) is not { Length: > 0 } source) continue;
            rows.Add(new KeptRow(source, JsonValues.AsText(row["folder"]), JsonValues.AsText(row["name"]), row["on"]?.GetValue<bool>() != false,
                row["preset"] is JsonValue p && p.TryGetValue(out string? preset) ? preset : null,
                row["exportFolder"] is JsonValue f && f.TryGetValue(out string? folder) ? folder : null));
        }
        return rows;
    }

    /// <summary>
    /// The files of the rows kept that are still there, each with its row; read in the background, as each archive is
    /// opened only once.
    /// </summary>
    public static List<(FileToAdd File, KeptRow Row)> StillThere(IReadOnlyList<KeptRow> rows)
    {
        var found = new List<(FileToAdd, KeptRow)>();
        var archives = new Dictionary<string, List<FileToAdd>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeptRow row in rows)
        {
            // a file from an archive is kept as "C:\path\to\mod.zip > folder/name.ndf"
            int inside = row.Source.IndexOf(" > ", StringComparison.Ordinal);
            if (inside < 0)
            {
                if (File.Exists(row.Source)) found.Add((new FileToAdd(row.Name, row.Folder, row.Source), row));
                continue;
            }
            string archive = row.Source[..inside];
            if (!archives.TryGetValue(archive, out List<FileToAdd>? files))
            {
                try { files = File.Exists(archive) ? ArchiveReader.NdfFilesIn(archive) : []; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { files = []; }
                archives[archive] = files;
            }
            if (files.FirstOrDefault(f => f.Source == row.Source) is FileToAdd file) found.Add((file, row));
        }
        return found;
    }
}
