namespace EasyNdf.App.Tests;

/// <summary>
/// The tests that add mods to the app's own list (AppData.Current) run one after the other: a mod one adds would
/// otherwise show among the mods another finds on its files.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AppModsTests
{
    public const string Name = "The app's mods";
}
