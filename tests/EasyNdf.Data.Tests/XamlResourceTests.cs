using System.Text.RegularExpressions;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Checks the app's screens against its styles, without Windows: every style or resource a screen names with
/// {StaticResource …} is defined, in that screen or in the app's shared styles. A missing one stops the app as it starts.
/// </summary>
public partial class XamlResourceTests
{
    private static readonly string AppFolder = Path.Combine(TextNameTests.RepositoryFolder(), "src", "EasyNdf.App");

    [Fact]
    public void Every_resource_a_screen_names_is_defined()
    {
        string[] screens = [.. Directory.GetFiles(AppFolder, "*.xaml", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))];
        // the shared styles, and the app's own resources, are seen from every screen
        HashSet<string> shared = [.. screens
            .Where(file => file.Contains($"{Path.DirectorySeparatorChar}Styles{Path.DirectorySeparatorChar}") || Path.GetFileName(file) == "App.xaml")
            .SelectMany(file => Key().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))];
        var missing = new List<string>();
        foreach (string screen in screens)
        {
            string text = File.ReadAllText(screen);
            HashSet<string> own = [.. Key().Matches(text).Select(match => match.Groups[1].Value)];
            foreach (Match use in StaticResource().Matches(text))
            {
                string name = use.Groups[1].Value;
                if (!own.Contains(name) && !shared.Contains(name)) missing.Add(Path.GetFileName(screen) + ": " + name);
            }
        }
        Assert.True(missing.Count == 0, "Resources named but not defined:\n" + string.Join("\n", missing.Distinct()));
    }

    [GeneratedRegex(@"x:Key=""([^""{}]+)""")]
    private static partial Regex Key();

    // a name, not {x:Type …} or another markup extension
    [GeneratedRegex(@"\{StaticResource ([A-Za-z0-9_.-]+)\}")]
    private static partial Regex StaticResource();
}
