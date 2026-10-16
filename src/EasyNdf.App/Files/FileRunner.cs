using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.Engine;
using EasyNdf.Engine.Export;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Rules;
using EasyNdf.App.Text;

namespace EasyNdf.App.Files;

/// <summary>
/// Preview and Run export (the HTML app's <c>runJobs</c>): runs each file's preset on it, one file at a time, and says
/// on each row what happened. Run export writes each edited file into its export folder, keeping the folders it sat in.
/// </summary>
public sealed class FileRunner(FileListModel model, RunSettings settings)
{
    /// <summary>
    /// Runs the rules on every file that is on and has a preset (with "every file" exports, every file that is on).
    /// </summary>
    /// <param name="write">True for Run export, false for Preview.</param>
    /// <param name="chooseExportFolder">
    /// Asks for an export folder for the files that have none; null when the user cancels, which stops the export.
    /// </param>
    public async Task Run(bool write, Func<Task<string?>> chooseExportFolder)
    {
        if (model.IsBusy) return;
        bool everyFile = write && settings.ExportEveryFile;
        List<FileEntry> list = model.Entries.Where(e => e.On && e.Source is not null && (everyFile || e.Preset is not null)).ToList();
        if (list.Count == 0)
        {
            model.Warn(model.Entries.Any(e => e.On && e.Source is null)
                ? Texts.Get("Files.Run.NotLoaded")
                : Texts.Get("Files.Run.NothingToRun"));
            return;
        }

        if (write)
        {
            // Files with no export folder get one now, chosen once for all of them.
            List<FileEntry> withoutFolder = list.Where(e => e.ExportFolder is null).ToList();
            if (withoutFolder.Count > 0)
            {
                string? folder = await chooseExportFolder();
                if (folder is null) return;
                model.SetExportFolder(withoutFolder, folder);
            }
            // (Asking about the mod's watermark first, when it says to ask, comes with the watermark window.)
        }

        model.IsBusy = true;
        try
        {
            model.Say("");
            foreach (FileEntry entry in list) entry.ClearMessage();
            // A row with no preset that is not part of this run drops the message left by an earlier "every file" export.
            foreach (FileEntry entry in model.Entries.Except(list))
                if (entry.Message != "" && !entry.IsError && entry.Preset is null) entry.ClearMessage();

            // Every row says where it is in the run: waiting, working, then what happened.
            foreach (FileEntry entry in list)
            {
                entry.Message = Texts.Get("Files.Run.Waiting");
                model.RowChanged(entry);
            }
            foreach (FileEntry entry in list)
            {
                entry.Message = Texts.Get("Files.Run.Working");
                model.RowChanged(entry);
                Dictionary<string, string> otherFiles = OtherFilesFor(entry);
                await Task.Run(() => RunOne(entry, write, everyFile, otherFiles));
                model.DetailsRow ??= entry;
                model.RowChanged(entry);
            }
        }
        finally
        {
            model.IsBusy = false;
            Memory.GiveBack();
        }
    }

    /// <summary>Runs one file's preset, and writes the result when exporting. It runs in the background.</summary>
    private void RunOne(FileEntry entry, bool write, bool everyFile, Dictionary<string, string> otherFiles)
    {
        try
        {
            if (entry.Preset is not ModPreset preset)
            {
                // only reached when every file is exported: it is copied as it is
                Deliver(entry, FileContent.Bytes(entry), Texts.Get("Files.Run.UnchangedCopy"));
                return;
            }

            Mod? mod = AppData.Current.ModOf(preset);
            Watermark? watermark = WatermarkOf(mod);
            var clock = Stopwatch.StartNew();
            var engine = new NdfEngine();
            LoadSummary info = engine.Load(FileContent.Text(entry));
            RunResult result = engine.Run(RuleLoading.LoadForEngine(preset.Rules), new RunOptions
            {
                WantText = write,
                LogLimit = settings.LogLimit,
                OtherFiles = otherFiles,
                KeepSourceComments = settings.KeepSourceComments,
                CheckOutput = true,
                ModName = ExportFinishing.ModNameForComments(mod?.Name, watermark),
                DecimalPlaces = settings.DecimalPlaces,
            });
            clock.Stop();

            string? text = result.Text;
            if (write && text is not null)
            {
                // The export comes back with the source's line endings; the setting decides what is saved.
                text = ExportFinishing.WithLineEndings(text, settings.LineEndings);
                if (result.Edits > 0 && result.ParseError == "")
                    text = ExportFinishing.AddHeader(text, MainView.AppVersion, settings.EditedWithLine, mod?.Name, watermark);
            }

            entry.Scanned = true;
            entry.Lines = info.Lines;
            entry.Info = FileStatus.Holds(info.Descriptors, info.Constants, info.Unnamed);
            entry.LastRun = new RunRecord
            {
                Result = result,
                Info = info,
                Milliseconds = clock.ElapsedMilliseconds,
                Preset = preset,
                Held = write && result.ParseError != "",
                Unchanged = write && result.ParseError == "" && result.Edits == 0,
            };
            entry.Edits = result.Edits;
            entry.WarningCount = result.Warnings.Count;
            // When nothing at all changed, the status already says so.
            entry.IdleRuleCount = result.Edits > 0 ? result.Stats.Count(RunRecord.IsIdle) : 0;

            string edits = Texts.Count("Files.Run.Edits", result.Edits, ("count", FileStatus.Count(result.Edits)));
            if (result.ParseError != "")
            {
                // held back: not written to the export folder
                entry.IsError = true;
                if (write) entry.HeldOutput = text;
                entry.Message = write ? Texts.Get("Files.Run.InvalidNotExported", ("problem", result.ParseError)) : Texts.Get("Files.Run.InvalidPreview", ("problem", result.ParseError));
            }
            else if (!write) entry.Message = Texts.Get("Files.Run.Preview", ("edits", edits));
            else if (result.Edits == 0 && !everyFile) entry.Message = Texts.Get("Files.Run.NoChanges");
            else if (result.Edits == 0) Deliver(entry, FileContent.Bytes(entry), Texts.Get("Files.Run.UnchangedCopyNoChanges"));
            else Deliver(entry, System.Text.Encoding.UTF8.GetBytes(text!), edits);
        }
        catch (Exception error)
        {
            // Whatever goes wrong with one file is said on its row, and the run goes on with the next, as in the HTML app.
            entry.Message = Texts.Get("Files.Run.Failed", ("problem", error.Message));
            entry.IsError = true;
        }
    }

    /// <summary>
    /// What a file's preset would export now (the File Editor's "Show export"): the rules run on the given text, with
    /// the header lines added when they edit something. Nothing is written, and the file's row is left as it is.
    /// </summary>
    /// <param name="entry">The file, which has a preset.</param>
    /// <param name="text">The text to run the rules on: the file's, or the edits typed in the editor.</param>
    /// <returns>What the engine gave back, and the text it would export.</returns>
    public (RunResult Result, string Text) WouldExport(FileEntry entry, string text)
    {
        ModPreset preset = entry.Preset!;
        Mod? mod = AppData.Current.ModOf(preset);
        Watermark? watermark = WatermarkOf(mod);
        var engine = new NdfEngine();
        engine.Load(text);
        RunResult result = engine.Run(RuleLoading.LoadForEngine(preset.Rules), new RunOptions
        {
            WantText = true,
            LogLimit = 0,
            OtherFiles = OtherFilesFor(entry),
            KeepSourceComments = settings.KeepSourceComments,
            CheckOutput = true,
            ModName = ExportFinishing.ModNameForComments(mod?.Name, watermark),
            DecimalPlaces = settings.DecimalPlaces,
        });
        string exported = result.Text ?? "";
        if (result.Edits > 0 && result.ParseError == "")
            exported = ExportFinishing.AddHeader(exported, MainView.AppVersion, settings.EditedWithLine, mod?.Name, watermark);
        return (result, exported);
    }

    /// <summary>
    /// Writes a finished file into its export folder, in the folders it sat in when it was added from a folder.
    /// </summary>
    /// <param name="what">What the status says was written, such as "12 edits" or "unchanged copy".</param>
    private static void Deliver(FileEntry entry, byte[] content, string what)
    {
        string exportFolder = entry.ExportFolder!;
        string folder = entry.Folder == "" ? exportFolder : Path.Combine([exportFolder, .. entry.Folder.Split('/')]);
        string path = Path.Combine(folder, entry.Name);
        string shownName = FileListView.FolderName(exportFolder);
        if (entry.ArchivedContent is null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(entry.Source!), StringComparison.OrdinalIgnoreCase))
        {
            entry.Message = Texts.Get("Files.Run.FolderHoldsSource");
            entry.IsError = true;
            return;
        }
        try
        {
            Directory.CreateDirectory(folder);
            // "Back up overwritten files": the file this export replaces is kept beside it first
            if (AppData.Current.Settings["backupOverwritten"]?.GetValue<bool>() == true && File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllBytes(path, content);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            entry.Message = Texts.Get("Files.Run.WriteFailed", ("folder", shownName), ("reason", error.Message));
            entry.IsError = true;
            return;
        }
        if (entry.LastRun is not null) entry.LastRun.Wrote = true;
        entry.Done = true;
        entry.ExportedPath = path;
        entry.Message = Texts.Get("Files.Run.Exported", ("folder", shownName), ("what", what));
    }

    /// <summary>
    /// The other listed files the preset's Lookup rules read, by the name the rule gives (the HTML app's <c>jobByRef</c>):
    /// a file whose folder and name are that name, or end with it.
    /// </summary>
    private Dictionary<string, string> OtherFilesFor(FileEntry entry)
    {
        var found = new Dictionary<string, string>();
        if (entry.Preset is null) return found;
        foreach (JsonNode? rule in entry.Preset.Rules)
        {
            if ((string?)rule?["Kind"] != "Lookup" || rule["Enabled"]?.GetValue<bool>() == false || rule["Params"] is not JsonObject parameters) continue;
            string key = ((string?)parameters["SourceFile"] ?? "").Trim().ToLowerInvariant();
            if (key == "" || found.ContainsKey(key)) continue;
            List<FileEntry> loaded = model.Entries.Where(e => e.Source is not null).ToList();
            FileEntry? source = loaded.FirstOrDefault(e => e.Key == key) ?? loaded.FirstOrDefault(e => e.Key.EndsWith("/" + key, StringComparison.Ordinal));
            if (source is null) continue;
            try { found[key] = FileContent.Text(source); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return found;
    }

    /// <summary>A mod's watermark as the engine takes it, or null when it has none.</summary>
    private static Watermark? WatermarkOf(Mod? mod) =>
        mod?.Watermark is ModWatermark mark ? new Watermark(mark.On, mark.Author, mark.Version, mark.Game) : null;
}
