using System.Text.Json.Nodes;
using Xunit;

namespace EasyNdf.App.Tests;

/// <summary>What's New shows by itself once after each update, and not on a first start.</summary>
public class WhatsNewTests
{
    [Fact]
    public void AFirstStartShowsTheWelcomeWindowOnly() => Assert.False(MainView.WhatsNewIsDue(new JsonObject(), "1.0.3"));

    [Fact]
    public void TheVersionWelcomedInIsNotAnUpdate() => Assert.False(MainView.WhatsNewIsDue(new JsonObject { ["welcomed"] = "1.0.3" }, "1.0.3"));

    [Fact]
    public void AnUpdateShowsIt() => Assert.True(MainView.WhatsNewIsDue(new JsonObject { ["welcomed"] = "1.0.2" }, "1.0.3"));

    [Fact]
    public void ItShowsOncePerVersion()
    {
        var state = new JsonObject { ["welcomed"] = "1.0.1", [MainView.WhatsNewKey] = "1.0.3" };
        Assert.False(MainView.WhatsNewIsDue(state, "1.0.3"));
        Assert.True(MainView.WhatsNewIsDue(state, "1.0.4"));
    }

    [Fact]
    public void AnOlderStatesWelcomeFlagThatIsNotAVersionStillCounts() =>
        Assert.True(MainView.WhatsNewIsDue(new JsonObject { ["welcomed"] = true }, "1.0.3"));
}
