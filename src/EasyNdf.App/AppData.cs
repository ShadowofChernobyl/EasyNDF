using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.App.Mods;
using EasyNdf.Data;

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

    /// <summary>Raised when a file has been scanned.</summary>
    public event Action? ScansChanged;

    /// <summary>Waits a moment before saving the scans, so a folder of files read one after another is saved once.</summary>
    private System.Windows.Threading.DispatcherTimer? scanSaving;

    /// <summary>Keeps what a file holds (the HTML app's <c>setScan</c>), and saves the scans soon after.</summary>
    public void SetScan(string file, EasyNdf.Engine.LoadSummary summary)
    {
        Scans.Scans[file] = new ScanInfo(summary.Names, summary.Titles, summary.Types);
        if (scanSaving is null)
        {
            scanSaving = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            scanSaving.Tick += (_, _) => { scanSaving.Stop(); Saving(Scans.Save); };
        }
        scanSaving.Stop();
        scanSaving.Start();
        ScansChanged?.Invoke();
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
        if (data.State["defaultModsAdded"] is null)
        {
            modStore.AddDefaultMods(DefaultMods.Text);
            data.State["defaultModsAdded"] = true;
            data.SaveState();
        }
        data.Folders = folders;
        data.Themes = new ThemeStore(folders.Themes);
        Current = data;
    }

    /// <summary>The user's own themes; in memory only for data in memory only.</summary>
    public ThemeStore Themes { get; private set; } = new();

    /// <summary>Whether Clear all data has deleted everything; nothing is saved after it.</summary>
    private bool isCleared;

    /// <summary>The folders the data is kept in; null for data in memory only.</summary>
    public AppFolders? Folders { get; private set; }

    /// <summary>"Reset settings": every setting goes back to how it was at first. Mods, scans and what the app remembers are kept.</summary>
    public void ResetSettings()
    {
        Settings.Clear();
        foreach ((string key, System.Text.Json.Nodes.JsonNode? value) in SettingsStore.DefaultSettings()) Settings[key] = value?.DeepClone();
        SaveSettings();
    }

    /// <summary>
    /// "Clear all data": deletes everything the app keeps (mods, themes, filters, language packs, settings, scans). The
    /// app starts again afterwards, with the mods it comes with. Files elsewhere on the disk are not touched.
    /// </summary>
    public void ClearAll()
    {
        isCleared = true;
        if (Folders is null) return;
        foreach (string folder in Folders.All)
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
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
    }

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
    public void SaveSettings() => Saving(() => settingsStore?.SaveSettings(Settings));

    /// <summary>Saves what the app remembers after a change.</summary>
    public void SaveState() => Saving(() => settingsStore?.SaveState(State));

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
