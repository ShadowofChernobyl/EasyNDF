using EasyNdf.App.Rules;
using EasyNdf.Data;
using Xunit;

namespace EasyNdf.App.Tests;

/// <summary>The scans a Lookup rule chooses its other file among: the files on the Files tab, not every file ever scanned.</summary>
public class PropertyScansTests
{
    private const string FromSource = "GameData/Generated/Gameplay/Gfx/Ammunition.ndf";

    private static Dictionary<string, ScanInfo> Kept(params string[] files) => files.ToDictionary(file => file, _ => ScanInfo.Empty);

    [Fact]
    public void AFileOnceAddedOnItsOwnIsNotOfferedBesideTheSourcesFile()
    {
        var all = new PropertyScans(Kept("Ammunition.ndf", FromSource, "GameData/Gameplay/Decks.ndf"), [FromSource, "GameData/Gameplay/Decks.ndf"]);
        Assert.Equal(3, all.Keys.Count);
        PropertyScans onTab = all.OnFilesTab("");
        Assert.Equal(["GameData/Generated/Gameplay/Gfx/Ammunition.ndf", "GameData/Gameplay/Decks.ndf"], onTab.Keys);
        // alone of its name now, it is named without a folder
        Assert.Equal("", onTab.Folder(FromSource));
        Assert.Equal("Ammunition.ndf", onTab.ShortName(FromSource));
    }

    [Fact]
    public void TheFileTheRuleNamesIsFoundOnTheTab()
    {
        var all = new PropertyScans(Kept("Ammunition.ndf", FromSource), [FromSource]);
        PropertyScans onTab = all.OnFilesTab("Ammunition.ndf");
        Assert.Equal([FromSource], onTab.Keys);
        Assert.Equal(FromSource, onTab.Find("Ammunition.ndf"));
    }

    [Fact]
    public void AFileTheRuleNamesThatLeftTheTabKeepsItsScan()
    {
        var all = new PropertyScans(Kept("Ammunition.ndf", "GameData/Gameplay/Decks.ndf"), ["GameData/Gameplay/Decks.ndf"]);
        PropertyScans onTab = all.OnFilesTab("Ammunition.ndf");
        Assert.Equal("Ammunition.ndf", onTab.Find("Ammunition.ndf"));
        Assert.Equal(2, onTab.Keys.Count);
    }

    [Fact]
    public void AFileOnTheTabNotReadYetIsNotStoodInForByAnOldScan()
    {
        var all = new PropertyScans(Kept("Ammunition.ndf"), [FromSource]);
        PropertyScans onTab = all.OnFilesTab("Ammunition.ndf");
        Assert.Empty(onTab.Keys);
        Assert.Equal([FromSource], onTab.Unread);
    }
}
