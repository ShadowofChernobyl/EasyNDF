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
    /// opened only once, and only the files kept are taken out of a .zip.
    /// </summary>
    public static List<(FileToAdd File, KeptRow Row)> StillThere(IReadOnlyList<KeptRow> rows)
    {
        var found = new List<(FileToAdd, KeptRow)>();
        // a file from an archive is kept as "C:\path\to\mod.zip > folder/name.ndf"
        var fromArchives = new Dictionary<string, List<KeptRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeptRow row in rows)
        {
            int inside = row.Source.IndexOf(" > ", StringComparison.Ordinal);
            if (inside < 0)
            {
                if (File.Exists(row.Source)) found.Add((new FileToAdd(row.Name, row.Folder, row.Source), row));
                continue;
            }
            string archive = row.Source[..inside];
            if (!fromArchives.TryGetValue(archive, out List<KeptRow>? kept)) fromArchives[archive] = kept = [];
            kept.Add(row);
        }
        foreach ((string archive, List<KeptRow> kept) in fromArchives)
        {
            Dictionary<string, FileToAdd> files = KeptFilesOf(archive, kept);
            foreach (KeptRow row in kept)
                if (files.TryGetValue(row.Source, out FileToAdd? file)) found.Add((file, row));
        }
        return found;
    }

    /// <summary>
    /// The files of an archive that rows were kept for, by where they are: from a .zip only those (a game's base.zip
    /// holds thousands); from a .rar, which cannot be listed without reading it, all of them.
    /// </summary>
    private static Dictionary<string, FileToAdd> KeptFilesOf(string archive, IReadOnlyList<KeptRow> kept)
    {
        try
        {
            if (!File.Exists(archive)) return [];
            List<FileToAdd> files;
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var wanted = new HashSet<string>(kept.Select(row => row.Source), StringComparer.Ordinal);
                files = ArchiveReader.WithContent(archive, [.. ArchiveReader.NdfFilesListedIn(archive).Where(file => wanted.Contains(file.Source))]);
            }
            else files = ArchiveReader.NdfFilesIn(archive);
            return files.GroupBy(file => file.Source, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }
        // an archive that can no longer be opened leaves its rows out, as a file no longer there does
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArchiveProblem) { return []; }
    }
}
