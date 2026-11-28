using System.Text.RegularExpressions;
using EasyNdf.Data.Themes;

namespace EasyNdf.Data.Tests;

public class DefaultThemeTests
{
    /// <summary>
    /// The app's starting colours (src/EasyNdf.App/Styles/DefaultTheme.xaml, which the Visual Studio designer also shows)
    /// must be Olivine's, as the app works them out at start-up.
    /// </summary>
    [Fact]
    public void The_app_starts_with_olivines_colours()
    {
        string xaml = File.ReadAllText(Path.Combine(RepositoryFolder(), "src", "EasyNdf.App", "Styles", "DefaultTheme.xaml"));
        var inXaml = Regex.Matches(xaml, "x:Key=\"([a-z-]+)\" Color=\"(#[0-9a-f]{6})\"")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        Dictionary<string, string> olivine = ThemeColors.For(BuiltInThemes.ById[BuiltInThemes.DefaultId], bevelSetting: null);
        ThemeColors.AddWorkedOut(olivine); // the app adds them too (see ThemeManager)
        Assert.Equal(olivine, inXaml);
    }

    private static string RepositoryFolder()
    {
        string folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "EasyNDF.slnx"))) folder = Path.GetDirectoryName(folder)!;
        return folder;
    }
}
