using System.IO;
using System.Text.RegularExpressions;

namespace EasyNdf.App.Tests;

/// <summary>
/// The installed app looks for updates only when it starts through Startup.cs, whose first step is VelopackApp's.
/// 1.0.2 and 1.1.0 were built with another start-up object and never offered an update. These keep it from coming back.
/// </summary>
public class ReleaseStartTests
{
    private static string Source(string relative)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "EasyNDF.slnx"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, relative));
    }

    [Fact]
    public void The_app_starts_from_Startup_and_a_release_build_with_any_other_start_is_stopped()
    {
        string project = Source(@"src\EasyNdf.App\EasyNdf.App.csproj");
        Assert.Contains(">EasyNdf.App.Startup</StartupObject>", project);
        Assert.Contains("Name=\"RequireInstallerStart\"", project);
        Assert.Contains("'$(Configuration)' == 'Release' And '$(StartupObject)' != 'EasyNdf.App.Startup'", project);
    }

    [Fact]
    public void Startup_runs_the_installers_steps_before_anything_else()
    {
        string startup = Source(@"src\EasyNdf.App\Startup.cs");
        Match main = Regex.Match(startup, @"static void Main\(\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(main.Success);
        string body = main.Groups["body"].Value;
        int velopack = body.IndexOf("VelopackApp.Build().Run();", StringComparison.Ordinal);
        Assert.True(velopack >= 0, "Startup.Main must call VelopackApp.Build().Run()");
        Assert.True(velopack < body.IndexOf("ShowSplash", StringComparison.Ordinal) && velopack < body.IndexOf("new App()", StringComparison.Ordinal),
            "VelopackApp.Build().Run() must come before the splash and the app");
    }
}
