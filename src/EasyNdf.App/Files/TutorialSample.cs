using System.IO;

namespace EasyNdf.App.Files;

/// <summary>
/// The tutorial's sample Ammunition.ndf (Assets/Tutorial/Ammunition.ndf, built into the app): 29 ammunition objects in
/// six families, enough for one rule to change several of them. The tutorial lists it on the Files tab when no
/// Ammunition.ndf is there, so it needs neither the game nor the user's own files.
/// </summary>
public static class TutorialSample
{
    /// <summary>The sample's file name, as the Files tab lists it.</summary>
    public const string Name = "Ammunition.ndf";

    /// <summary>
    /// Whether a row is the sample. Only the open mod runs on it (the mod the tutorial makes), so what the tutorial shows
    /// is that mod's rule alone, not the changes of every mod that is on.
    /// </summary>
    public static bool Is(Data.FileList.FileEntry entry) =>
        string.Equals(entry.Name, Name, StringComparison.OrdinalIgnoreCase) && entry.Source is string source
        && string.Equals(Path.GetFullPath(source), Path.GetFullPath(Path.Combine(AppData.Current.Folders?.Cache ?? Path.GetTempPath(), "Tutorial", Name)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies the sample into the app's cache folder, once or when the app's copy differs, and gives back its path.</summary>
    public static string FilePath()
    {
        string folder = Path.Combine(AppData.Current.Folders?.Cache ?? Path.GetTempPath(), "Tutorial");
        string path = Path.Combine(folder, Name);
        using Stream stream = typeof(TutorialSample).Assembly.GetManifestResourceStream("TutorialAmmunition.ndf")!;
        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd();
        if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, text);
        }
        return path;
    }
}
