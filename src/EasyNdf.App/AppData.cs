using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.App.Mods;
using EasyNdf.Data;
using EasyNdf.Data.Languages;

namespace EasyNdf.App;

/// <summary>
/// What the app keeps between sessions, loaded once at start-up: the settings, what it remembers (sizes, the open mod,
/// export folders), and the mods. Changes are saved straight away, in the folders <see cref="AppFolders"/> names.
/// A copy that lives in memory only can be used instead, so the user's data is never touched.
/// </summary>
public sealed class AppData
{
    private readonly SettingsStore? settingsStore;

    /// <summary>The app's data, for every part of the app to use.</summary>
    public static AppData Current { get; private set; } = InMemory();

    /// <summary>The settings, with the HTML app's keys (see <see cref="SettingsStore.DefaultSettings"/>).</summary>
    public JsonObject Settings { get; }

    /// <summary>What the app remembers between sessions: list sizes, column widths, the open mod, export folders.</summary>
    public JsonObject State { get; }

    /// <summary>The mods and their files: add, save and delete mods through it, then call <see cref="ModsChanged"/>.</summary>
    public ModStore ModStore { get; }

    /// <summary>The property scans: what each scanned file holds, for the rule window's dropdowns.</summary>
    public ScanStore Scans { get; }

    /// <summary>Raised when a file has been scanned, with the file (by folder and name, as the scans name it).</summary>
    public event Action<string>? ScansChanged;

    /// <summary>Waits a moment before saving the scans, so a folder of files read one after another is saved once.</summary>
    private System.Windows.Threading.DispatcherTimer? scanSaving;

    /// <summary>Keeps what a file holds (the HTML app's <c>setScan</c>), and saves the scans soon after.</summary>
    public void SetScan(string file, EasyNdf.Engine.LoadSummary summary)
    {
        Scans.Scans[file] = new ScanInfo(summary.Names, summary.Titles, summary.Types);
        if (scanSaving is null)
        {
            scanSaving = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            scanSaving.Tick += (_, _) => { scanSaving.Stop(); SaveScansInBackground(); };
        }
        scanSaving.Stop();
        scanSaving.Start();
        ScansChanged?.Invoke(file);
    }

    /// <summary>The scans' saves, one after another.</summary>
    private Task scansSaved = Task.CompletedTask;

    /// <summary>
    /// Saves the scans away from the screen's thread: a whole game's scans take a moment to write, and a long read of
    /// files saves them after every pause. They are copied here first, so the scans can go on changing meanwhile.
    /// </summary>
    private void SaveScansInBackground()
    {
        KeyValuePair<string, ScanInfo>[] scans = Scans.Copy();
        scansSaved = scansSaved.ContinueWith(_ => Saving(() => Scans.Write(scans)), TaskScheduler.Default);
    }

    /// <summary>The scanned file the user picked for a preset in the rule window's Property scan, or null.</summary>
    public string? ScanPickOf(Mod mod, ModPreset preset) => (string?)State["scanPick"]?[mod.Name]?[preset.Name];

    /// <summary>Remembers the scanned file picked for a preset.</summary>
    public void RememberScanPick(Mod mod, ModPreset preset, string file)
    {
        if (State["scanPick"] is not JsonObject picks) State["scanPick"] = picks = [];
        if (picks[mod.Name] is not JsonObject ofMod) picks[mod.Name] = ofMod = [];
        ofMod[preset.Name] = file;
        SaveState();
    }

    /// <summary>The mods, sorted by name.</summary>
    public IReadOnlyList<Mod> Mods => ModStore.Mods;

    /// <summary>
    /// Raised when a mod or a preset has been added, renamed, changed or deleted, or another mod opened, so every tab can
    /// show it (the Files tab's preset dropdowns, for one).
    /// </summary>
    public event Action<ModsChange>? ModsChanged;

    /// <summary>Tells every tab that the mods have changed.</summary>
    public void TellModsChanged(ModsChange? change = null) => ModsChanged?.Invoke(change ?? new ModsChange());

    /// <summary>The mod the user has open; its presets are the ones files get.</summary>
    public Mod OpenMod => Mods.FirstOrDefault(m => m.Name == (string?)State["openMod"]) ?? Mods.FirstOrDefault(m => m.Name == ModStore.BlankModName) ?? Mods[0];

    private AppData(SettingsStore? settingsStore, ModStore modStore, ScanStore scans, JsonObject settings, JsonObject state)
    {
        this.settingsStore = settingsStore;
        ModStore = modStore;
        Scans = scans;
        Settings = settings;
        State = state;
    }

    /// <summary>
    /// Loads the user's data from its folders. The first time the app runs, the mods it comes with are added: WARNO
    /// Tactical Overhaul and the empty "My mod", which a new user starts on.
    /// </summary>
    public static void Load()
    {
        AppFolders folders = AppFolders.ForThisApp();
        folders.CreateAll();
        var settingsStore = new SettingsStore(folders.Settings);
        var modStore = new ModStore(folders.Mods);
        var scans = new ScanStore(Path.Combine(folders.Settings, "scans.json"));
        var data = new AppData(settingsStore, modStore, scans, settingsStore.LoadSettings(), settingsStore.LoadState());
        data.Session = settingsStore.LoadSession() ?? TakeOldSession(data.State);
        if (data.State["defaultModsAdded"] is null)
        {
            modStore.AddDefaultMods(DefaultMods.Text);
            data.State["defaultModsAdded"] = true;
            data.SaveState();
        }
        data.Folders = folders;
        data.Themes = new ThemeStore(folders.Themes);
        data.Languages = new LanguageStore(folders.Languages);
        Current = data;
    }

    /// <summary>The user's own themes; in memory only for data in memory only.</summary>
    public ThemeStore Themes { get; private set; } = new();

    /// <summary>The language packs the user has imported; in memory only for data in memory only.</summary>
    public LanguageStore Languages { get; private set; } = new();

    /// <summary>Whether Clear all data has deleted everything; nothing is saved after it.</summary>
    private bool isCleared;

    /// <summary>
    /// Whether Reset look and layout has run: the settings and what the app remembers are not saved again before the
    /// app starts again, or the window, panes and columns would write back what was just reset as the app closes.
    /// </summary>
    private bool isLookReset;

    /// <summary>
    /// What the app remembers of how it is laid out (<see cref="State"/>): pane sizes and folds, column widths, the
    /// window, the view each list shows, the Armory's sub-tab and division, the File Editor's filter in use, the Help
    /// page, the colour picker's colours. Not the mods on and their order, the open mod, export and game folders, the
    /// file open in the File Editor or the saved filters: those are the user's work.
    /// </summary>
    private static readonly string[] LookStateKeys =
    [
        "sizes", "folds", "window", "cols", "unitColumns", "modsFolded", "helpPage", "armoryPart", "division",
        "weaponView", "unitView", "newUnitView", "addUnitsView", "addCardView", "dvFilter", "customColours",
    ];

    /// <summary>
    /// The settings that are the app's look and layout: the theme and its bevels and scroll bars, the app's size and
    /// fonts, the File Editor's text size, the Files tab's columns, sort, grouping and Show, the Armory's zoom, and where
    /// the panes sit. The settings that decide what the app does (exporting, the editor, the language, Undo) are kept.
    /// </summary>
    private static readonly string[] LookSettingKeys =
    [
        "theme", "bevel", "treadScrollbars", "uiScale", "panesScale", "uiFont", "codeFont", "edZoom",
        "cols", "sort", "autoFit", "groupFolders", "showOnly",
        "unitsZoomCards", "unitsZoomTable", "weaponsZoomCards", "weaponsZoomTable", "panesAuto", "panesSide",
    ];

    /// <summary>
    /// "Reset look and layout": the theme, pane sizes and folds, columns, the window, zoom and the views each list
    /// remembers go back to how they were at first (<see cref="LookStateKeys"/>, <see cref="LookSettingKeys"/>); the
    /// mods, the file list and the other settings are kept. The file list is saved first, as it is kept; then nothing
    /// else is saved, as the app starts again.
    /// </summary>
    /// <param name="deleteOwnThemes">Whether the themes the user made are deleted too.</param>
    public void ResetLook(bool deleteOwnThemes)
    {
        foreach (string key in LookStateKeys) State.Remove(key);
        JsonObject defaults = SettingsStore.DefaultSettings();
        foreach (string key in LookSettingKeys)
        {
            // a setting the defaults do not name has its default when it is not there
            if (defaults[key] is JsonNode value) Settings[key] = value.DeepClone();
            else Settings.Remove(key);
        }
        if (deleteOwnThemes)
            foreach (Data.CustomTheme theme in Themes.Themes.ToList()) Themes.Delete(theme);
        SaveSession();
        SaveSettings();
        SaveState();
        isLookReset = true;
    }

    /// <summary>The folders the data is kept in; null for data in memory only.</summary>
    public AppFolders? Folders { get; private set; }

    /// <summary>
    /// Where the files of File rules are kept: in the data folder, or a temporary folder when the app has none, as in
    /// the designer.
    /// </summary>
    public ModFileStore ModFileStore => new(Folders?.ModFiles ?? Path.Combine(Path.GetTempPath(), "EasyNDF", "Mod Files"));

    /// <summary>"Reset settings": every setting goes back to how it was at first. Mods, scans and what the app remembers are kept.</summary>
    public void ResetSettings()
    {
        Settings.Clear();
        foreach ((string key, System.Text.Json.Nodes.JsonNode? value) in SettingsStore.DefaultSettings()) Settings[key] = value?.DeepClone();
        SaveSettings();
    }

    /// <summary>
    /// "Clear all data": deletes everything the app keeps (mods, themes, filters, language packs, settings, scans, the
    /// Units tab's saved pictures). The app starts again afterwards, with the mods it comes with. Files elsewhere on
    /// the disk are not touched.
    /// </summary>
    public void ClearAll()
    {
        isCleared = true;
        // what the app remembers (pane sizes and folds, column widths, the window's place) goes from memory too, so
        // nothing written before the app starts again can bring it back
        State.Clear();
        if (Folders is null) return;
        foreach (string folder in Folders.All)
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        // the Units tab's saved pictures can be read out of the games again; one still on screen may be in use
        try
        {
            if (Directory.Exists(Folders.Cache)) Directory.Delete(Folders.Cache, recursive: true);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Data that lives in memory only, with the default settings and mods: for the designer, and for drawing the app without touching saved data.</summary>
    public static AppData InMemory()
    {
        var modStore = new ModStore();
        modStore.AddDefaultMods(DefaultMods.Text);
        return new AppData(null, modStore, new ScanStore(), SettingsStore.DefaultSettings(), []);
    }

    /// <summary>The mod a preset belongs to.</summary>
    public Mod? ModOf(ModPreset preset) => Mods.FirstOrDefault(mod => mod.Presets.Contains(preset));

    /// <summary>The folder a mod last exported to, which its new files start with; null before its first export.</summary>
    public string? ExportFolderOf(Mod mod) => (string?)State["exportFolders"]?[mod.Name];

    /// <summary>Remembers the folder a mod's files were last given.</summary>
    public void RememberExportFolder(Mod mod, string folder)
    {
        if (State["exportFolders"] is not JsonObject folders) State["exportFolders"] = folders = [];
        folders[mod.Name] = folder;
        SaveState();
        ModFoldersChanged?.Invoke();
    }

    /// <summary>Raised when a mod's folders change: its export folder, or the WARNO mod folder its UNITS.csv goes into.</summary>
    public event Action? ModFoldersChanged;

    /// <summary>Tells that a mod's folders changed (see <see cref="ModFoldersChanged"/>), for Undo to file the change.</summary>
    public void TellModFoldersChanged() => ModFoldersChanged?.Invoke();

    /// <summary>Opens another mod, and remembers it for the next start.</summary>
    public void Open(Mod mod)
    {
        State["openMod"] = mod.Name;
        SaveState();
        TellModsChanged(new ModsChange(OpenModChanged: true));
    }

    /// <summary>Saves a mod after a change, and tells every tab.</summary>
    public void Save(Mod mod)
    {
        Saving(() => ModStore.Save(mod));
        TellModsChanged();
    }

    /// <summary>Saves the settings after a change.</summary>
    public void SaveSettings()
    {
        if (!isLookReset) Saving(() => settingsStore?.SaveSettings(Settings));
    }

    /// <summary>Saves what the app remembers after a change.</summary>
    public void SaveState()
    {
        if (!isLookReset) Saving(() => settingsStore?.SaveState(State));
    }

    /// <summary>The file list to reopen at the start ("Reopen the last files"), kept as the app last closed; null for none.</summary>
    public JsonNode? Session { get; set; }

    /// <summary>Saves <see cref="Session"/>, as the app closes.</summary>
    public void SaveSession() => Saving(() => settingsStore?.SaveSession(Session));

    /// <summary>
    /// The file list an earlier version kept in the state, taken out of it: it now has a file of its own, and in the
    /// state it was written again with every small change. Null when there is none.
    /// </summary>
    private static JsonNode? TakeOldSession(JsonObject state)
    {
        JsonNode? session = state["session"];
        state.Remove("session");
        return session;
    }

    /// <summary>
    /// A save that fails (the disk is full, the folder is read-only) does not stop the app; the next change tries again.
    /// </summary>
    private void Saving(Action save)
    {
        // after Clear all data, nothing more is written: the app is about to start again with nothing saved
        if (isCleared) return;
        try { save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>What changed in the mods, for the tabs that follow them.</summary>
/// <param name="OpenModChanged">Another mod was opened: files get its presets.</param>
/// <param name="PresetsAdded">Presets were pasted or imported: files with none may get one now.</param>
public sealed record ModsChange(bool OpenModChanged = false, bool PresetsAdded = false);
