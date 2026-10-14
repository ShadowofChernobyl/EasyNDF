using System.IO;
using EasyNdf.Data;

namespace EasyNdf.App.Mods;

/// <summary>
/// The mods the app comes with: WARNO Tactical Overhaul, built into the app, and the empty "My mod", which a new user
/// starts on. They are added to the Mods folder the first time the app runs (see <see cref="AppData.Load"/>).
/// </summary>
public static class DefaultMods
{
    /// <summary>WARNO Tactical Overhaul as a mod file: the HTML app's <c>#defaultMod</c>.</summary>
    public static string Text { get; } = ReadText();

    /// <summary>A new empty "My mod" (the HTML app's <c>BLANK_MOD</c>).</summary>
    public static Mod BlankMod => new() { Name = ModStore.BlankModName };

    /// <summary>A new copy of WARNO Tactical Overhaul.</summary>
    public static Mod TacticalOverhaul => ModFiles.Read(Text, "default.mod.json")[0];

    private static string ReadText()
    {
        using Stream stream = typeof(DefaultMods).Assembly.GetManifestResourceStream("DefaultMod.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
