namespace EasyNdf.Data;

/// <summary>
/// Where EasyNDF keeps its data, and the folders inside it: Mods, Themes, Filters, Languages and Settings.
/// </summary>
/// <remarks>
/// Normally the data is in <c>%LOCALAPPDATA%\EasyNDF</c>, where the WinForms app kept its data too. In portable mode it is in a
/// <c>Data</c> folder beside EasyNDF.exe instead. Portable mode is on while a marker file (<see cref="PortableMarkerName"/>) sits
/// beside the exe: the app looks for it at start-up, so the choice is not stored inside the folder it chooses.
/// The WinForms app's <c>RulePresets</c> folder is never read or changed.
/// </remarks>
public sealed class AppFolders
{
    /// <summary>The marker file that turns portable mode on, beside EasyNDF.exe.</summary>
    public const string PortableMarkerName = "EasyNDF portable mode.txt";

    /// <summary>The folder of the data in portable mode, beside EasyNDF.exe.</summary>
    public const string PortableDataFolderName = "Data";

    /// <summary>Creates the folders for a data folder. Nothing is created on disk until <see cref="CreateAll"/>.</summary>
    public AppFolders(string root, bool isPortable = false)
    {
        Root = root;
        IsPortable = isPortable;
    }

    /// <summary>The data folder itself.</summary>
    public string Root { get; }

    /// <summary>Whether this is the portable data folder beside the exe.</summary>
    public bool IsPortable { get; }

    /// <summary>One file per mod, in the same format as "Export mod".</summary>
    public string Mods => Path.Combine(Root, "Mods");

    /// <summary>One file per custom theme, in the same format as "Export theme".</summary>
    public string Themes => Path.Combine(Root, "Themes");

    /// <summary>One file per saved descriptor filter, in the same format as the filter window's "Export".</summary>
    public string Filters => Path.Combine(Root, "Filters");

    /// <summary>One file per imported language pack.</summary>
    public string Languages => Path.Combine(Root, "Languages");

    /// <summary>The settings, and what the app remembers between sessions (column widths, the open mod...).</summary>
    public string Settings => Path.Combine(Root, "Settings");

    /// <summary>
    /// What the app saves to save time, and can make again: the Units tab's pictures, read out of the games. It is not
    /// one of the folders in <see cref="All"/>, so it is not copied when portable mode is turned on or off.
    /// </summary>
    public string Cache => Path.Combine(Root, "Cache");

    /// <summary>All five folders, in the order above.</summary>
    public IReadOnlyList<string> All => [Mods, Themes, Filters, Languages, Settings];

    /// <summary>Makes sure all five folders exist.</summary>
    public void CreateAll()
    {
        foreach (string folder in All) Directory.CreateDirectory(folder);
    }

    /// <summary>
    /// The folders to use: the portable ones when the marker file is beside the exe, otherwise the ones in Local AppData.
    /// </summary>
    /// <param name="appFolder">The folder EasyNDF.exe is in.</param>
    /// <param name="localAppData">The user's Local AppData folder. Tests pass a folder of their own.</param>
    public static AppFolders Find(string appFolder, string localAppData)
    {
        if (File.Exists(Path.Combine(appFolder, PortableMarkerName)))
            return new AppFolders(Path.Combine(appFolder, PortableDataFolderName), isPortable: true);
        return new AppFolders(Path.Combine(localAppData, "EasyNDF"));
    }

    /// <summary>The folders for this user, found from where the running exe is.</summary>
    public static AppFolders ForThisApp() =>
        Find(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// Whether portable mode can be turned on: the exe's folder must be writable. An app installed in Program Files is not,
    /// so there the Settings checkbox is greyed out.
    /// </summary>
    public static bool CanBePortable(string appFolder)
    {
        string probe = Path.Combine(appFolder, ".easyndf-write-test");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Turns portable mode on or off: writes or deletes the marker file and, when asked, copies the data across first.
    /// Returns the folders to use from now on. The old data is left where it was, so nothing is lost if the copy is not wanted.
    /// </summary>
    public static AppFolders SetPortable(bool portable, string appFolder, string localAppData, bool copyData)
    {
        AppFolders from = Find(appFolder, localAppData);
        AppFolders to = portable
            ? new AppFolders(Path.Combine(appFolder, PortableDataFolderName), isPortable: true)
            : new AppFolders(Path.Combine(localAppData, "EasyNDF"));
        if (from.Root == to.Root) return to;

        if (copyData) CopyFolders(from, to);
        string marker = Path.Combine(appFolder, PortableMarkerName);
        if (portable) File.WriteAllText(marker, "While this file is here, EasyNDF keeps its data in the Data folder beside it.\r\n");
        else File.Delete(marker);
        to.CreateAll();
        return to;
    }

    /// <summary>Copies the five folders' files, replacing files of the same name.</summary>
    private static void CopyFolders(AppFolders from, AppFolders to)
    {
        for (int i = 0; i < from.All.Count; i++)
        {
            if (!Directory.Exists(from.All[i])) continue;
            Directory.CreateDirectory(to.All[i]);
            foreach (string file in Directory.GetFiles(from.All[i]))
                File.Copy(file, Path.Combine(to.All[i], Path.GetFileName(file)), overwrite: true);
        }
    }
}
