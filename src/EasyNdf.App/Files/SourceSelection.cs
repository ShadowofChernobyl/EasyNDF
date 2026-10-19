using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Files;

/// <summary>
/// The game or mod the app works with (the Source bar on the Files tab): the games found on this computer and their
/// mods, and the one chosen. The Files tab adds its files; the Units tab reads its units. Nothing is chosen when the
/// app starts, so nothing slow happens before the user asks for it.
/// </summary>
public sealed class SourceSelection
{
    /// <summary>The games found, each followed by its mods.</summary>
    public IReadOnlyList<UnitSource> Sources { get; private set; } = [];

    /// <summary>The source chosen; null until one is chosen, each time the app starts.</summary>
    public UnitSource? Chosen { get; private set; }

    /// <summary>Whether the games have been looked for yet.</summary>
    public bool HasLooked { get; private set; }

    /// <summary>Raised when the sources found or the one chosen change.</summary>
    public event Action? Changed;

    /// <summary>Looks for the games and their mods, in the background (Steam's libraries and the folders chosen before).</summary>
    public async Task LookAsync()
    {
        Sources = await Task.Run(() => UnitSource.FindAll(ChosenFolders()));
        HasLooked = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Looks again at once, for mods added to or taken out of the games' Mods folders since; tells only when something
    /// changed.
    /// </summary>
    public void LookAgain()
    {
        List<UnitSource> found = UnitSource.FindAll(ChosenFolders());
        if (found.Select(source => source.Key + source.Name).SequenceEqual(Sources.Select(source => source.Key + source.Name))) return;
        Sources = found;
        Changed?.Invoke();
    }

    /// <summary>Chooses a source.</summary>
    public void Choose(UnitSource source)
    {
        if (Chosen?.Key == source.Key) return;
        Chosen = source;
        Changed?.Invoke();
    }

    /// <summary>
    /// A folder chosen with "Choose a Game Folder…": the game's folder, or a mod's in its Mods folder. The game is kept
    /// for next time, its mods listed, and the folder chosen becomes the source. False when the folder is neither.
    /// </summary>
    public async Task<bool> AddFolder(string folder)
    {
        string? gameFolder = GameFolderOf(folder);
        if (gameFolder is null) return false;
        if (AppData.Current.State["unitGameFolders"] is not JsonArray folders) AppData.Current.State["unitGameFolders"] = folders = [];
        if (!folders.Any(kept => string.Equals((string?)kept, gameFolder, StringComparison.OrdinalIgnoreCase))) folders.Add(gameFolder);
        AppData.Current.SaveState();
        await LookAsync();
        UnitSource? picked = Sources.FirstOrDefault(source => string.Equals(source.Folder, folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            ?? Sources.FirstOrDefault(source => source.Mod is null && string.Equals(source.GameFolder, gameFolder, StringComparison.OrdinalIgnoreCase));
        if (picked is not null) Choose(picked);
        return true;
    }

    /// <summary>Sets the sources and the choice at once, for the workbench's screenshots and checks.</summary>
    internal void UseForChecks(IReadOnlyList<UnitSource> sources, UnitSource chosen)
    {
        Sources = sources;
        HasLooked = true;
        Chosen = chosen;
        Changed?.Invoke();
    }

    /// <summary>The game folders the user chose with "Choose a Game Folder…", kept between sessions.</summary>
    private static List<string> ChosenFolders() =>
        AppData.Current.State["unitGameFolders"] is JsonArray folders ? [.. folders.Select(folder => (string?)folder).OfType<string>()] : [];

    /// <summary>The game folder a chosen folder is or is in (a mod's folder is two below it, in Mods); null for neither.</summary>
    private static string? GameFolderOf(string folder)
    {
        string trimmed = folder.TrimEnd('\\', '/');
        if (GameInstall.IsGameFolder(trimmed)) return trimmed;
        string? aboveMods = Path.GetDirectoryName(Path.GetDirectoryName(trimmed));
        return aboveMods is not null && GameInstall.IsGameFolder(aboveMods) ? aboveMods : null;
    }
}
