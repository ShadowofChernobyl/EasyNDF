using System.Text.Json.Nodes;

namespace EasyNdf.Data;

/// <summary>
/// The Settings folder: <c>settings.json</c> holds what the Settings tab sets, <c>state.json</c> what the app remembers
/// between sessions (the open mod, column widths, folded sections...). Both are JSON objects with the HTML app's keys.
/// <c>session.json</c> holds the file list to reopen at the next start.
/// </summary>
public sealed class SettingsStore(string settingsFolder)
{
    /// <summary>
    /// The settings the app starts with, the same as the HTML app's <c>DEF</c>. Custom themes are not here: each is a file in
    /// the Themes folder.
    /// </summary>
    public static JsonObject DefaultSettings() => new()
    {
        ["autoFit"] = true,
        ["cols"] = new JsonObject
        {
            ["order"] = new JsonArray("on", "num", "file", "size", "lines", "modified", "preset", "dir", "status"),
            ["hidden"] = new JsonArray("lines", "modified", "dir"),
        },
        ["sort"] = new JsonObject { ["id"] = "file", ["dir"] = 1 },
        ["groupFolders"] = false,
        ["exportScope"] = "changed",
        ["folderMode"] = "all",
        ["keepComments"] = true,
        ["dlOnExport"] = true,
        ["wrapRows"] = false,
        ["dupes"] = "ask",
        ["logCap"] = 500,
        ["decimals"] = 2,
        ["stamp"] = true,
        ["eol"] = "lf",
        ["multi"] = "zip",
        ["autoAssign"] = true,
        ["confirmDelete"] = true,
        // the desktop app's own: a double-click on the empty part of a rule's condition or action list adds one
        ["theme"] = "steam",
        // the desktop app's own: the file list comes back when the app starts again
        ["restoreSession"] = true,
        // the File Editor's scope lines and closing pairs, as in Sublime Text
        ["indentGuides"] = true,
        ["autoPairs"] = true,
        ["lineNumbers"] = true,
        ["wordWrap"] = false,
        ["reopenFile"] = true,
        // the descriptor tree shows only how files connect, unless this is on
        ["treeShowsAll"] = false,
        // exporting
        ["confirmExport"] = false,
        // the desktop app's own: each Run export asks where to write the files, rather than using the folders kept
        ["askExportFolder"] = true,
        ["armoryMarks"] = true,
        // the desktop app's own: under each value's name in the Armory's panes, the property it is in, in the game's files
        ["armoryFileNames"] = true,
        // the Armory shows the values as the mods that are on make them; off, as the source has them
        ["armoryModChanges"] = true,
        // the desktop app's own: the app redraws itself for each monitor's scaling, rather than Windows stretching it
        ["perMonitorDpi"] = true,
        ["backupOverwritten"] = false,
        ["openAfterExport"] = false,
        // the window: its size in percent, tooltips, and whether it opens where it was left
        ["uiScale"] = "100",
        ["panesScale"] = true,
        ["uiFont"] = "",
        ["codeFont"] = "",
        ["tooltips"] = true,
        ["rememberWindow"] = true,
        // how many changes Ctrl+Z can take back
        ["undoSteps"] = "60",
        // the language of the app's texts: "en", or the code of an imported language pack
        ["lang"] = "en",
    };

    /// <summary>The settings: what was saved, over the defaults (a setting added in a later version gets its default).</summary>
    public JsonObject LoadSettings() => WithDefaults(Read("settings.json"), DefaultSettings());

    /// <summary>Saves the settings.</summary>
    public void SaveSettings(JsonObject settings) => JsonFiles.Write(PathOf("settings.json"), settings);

    /// <summary>What the app remembered last time, or an empty object the first time.</summary>
    public JsonObject LoadState() => Read("state.json") ?? [];

    /// <summary>Saves what the app remembers between sessions.</summary>
    public void SaveState(JsonObject state) => JsonFiles.Write(PathOf("state.json"), state);

    /// <summary>The file list kept for the next start ("Reopen the last files"); null when none is.</summary>
    public JsonNode? LoadSession() => JsonFiles.TryRead(PathOf("session.json"));

    /// <summary>
    /// Keeps the file list for the next start, or forgets it (null). It has a file of its own, written as the app
    /// closes: kept in state.json, its thousands of rows were written again with every small thing the app remembers.
    /// </summary>
    public void SaveSession(JsonNode? session)
    {
        if (session is null) File.Delete(PathOf("session.json"));
        else JsonFiles.Write(PathOf("session.json"), session);
    }

    /// <summary>"Reset settings": the settings go back to the defaults. Mods and the rest are kept.</summary>
    public void ResetSettings() => SaveSettings(DefaultSettings());

    private JsonObject? Read(string fileName) => JsonFiles.TryRead(PathOf(fileName)) as JsonObject;

    private string PathOf(string fileName) => Path.Combine(settingsFolder, fileName);

    /// <summary>Like JavaScript's <c>Object.assign({}, defaults, saved)</c>: saved values win, one level deep.</summary>
    private static JsonObject WithDefaults(JsonObject? saved, JsonObject defaults)
    {
        if (saved is null) return defaults;
        foreach (var (key, value) in saved) defaults[key] = value?.DeepClone();
        return defaults;
    }
}
