using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Engine.Mods;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Mods as packages (.mod.zip) and mod files carrying the pictures their Armory changes name: an export carries the
/// mod whole, an import reads it back, a package can put nothing anywhere else, and a mod file of an earlier version
/// reads as it always did.
/// </summary>
public class ModPackageTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly byte[] OtherPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9];
    private const string Emblem = @"C:\Users\Someone\AppData\Local\EasyNDF\Pictures\WA - Emblem.png";
    private const string Card = @"C:\Users\Someone\AppData\Local\EasyNDF\Pictures\WA - Tank.png";

    private static JsonObject FileRule(string stored)
    {
        JsonObject rule = RuleLoading.NewRule("File");
        rule["Params"]!["Destination"] = "GameData/Logo.png";
        rule["Params"]!["Stored"] = stored;
        rule["Params"]!["ChosenName"] = "Logo.png";
        return rule;
    }

    /// <summary>A mod with a File rule, a division with its own emblem and a unit with its own card picture.</summary>
    private static (Mod Mod, ModFileStore Store, string Stored) Sample(TemporaryFolder folder)
    {
        var store = new ModFileStore(Path.Combine(folder.Path, "store"));
        string stored = store.Keep(Encoding.UTF8.GetBytes("logo"), "Logo.png");
        var mod = new Mod { Name = "WA" };
        mod.Presets.Add(new ModPreset { Name = "WA Files", Rules = [FileRule(stored)] });
        mod.Divisions.Add(new ModDivision { Descriptor = "Descriptor_Deck_Division_US_Test_multi", EmblemFile = Emblem });
        mod.UnitEdits.Add(new UnitEdit { Descriptor = "Descriptor_Unit_Tank_US", PictureFile = Card });
        return (mod, store, stored);
    }

    private static byte[]? Pictures(string path) =>
        path == Emblem ? Png : path == Card ? OtherPng : null;

    [Fact]
    public void A_package_carries_the_mod_its_files_and_its_pictures()
    {
        using var folder = new TemporaryFolder();
        (Mod mod, ModFileStore store, string stored) = Sample(folder);
        using var package = new MemoryStream();
        ModPackage.Write(package, mod, isOn: false, store, Pictures);
        package.Position = 0;

        Mod read = Assert.Single(ModPackage.Read(package, "WA.mod.zip"));
        Assert.Equal("WA", read.Name);
        Assert.False(read.WasOn);
        Assert.Equal(Encoding.UTF8.GetBytes("logo"), read.CarriedFiles[stored]);
        Assert.Equal(Png, read.CarriedPictures["WA - Emblem.png"]);
        Assert.Equal(OtherPng, read.CarriedPictures["WA - Tank.png"]);
        Assert.Equal(Emblem, read.Divisions[0].EmblemFile);
    }

    [Fact]
    public void A_mod_file_carries_its_pictures_too_and_reads_them_back()
    {
        using var folder = new TemporaryFolder();
        (Mod mod, ModFileStore store, _) = Sample(folder);
        Mod read = ModFiles.Read(ModFiles.ExportText(mod, isOn: true, store, Pictures), "WA.mod.json")[0];
        Assert.Equal(2, read.CarriedPictures.Count);
        Assert.Equal(Png, read.CarriedPictures["WA - Emblem.png"]);
    }

    [Fact]
    public void A_backup_carries_every_mods_pictures()
    {
        using var folder = new TemporaryFolder();
        (Mod mod, ModFileStore store, _) = Sample(folder);
        List<Mod> read = ModFiles.Read(ModFiles.BackupText([mod], store, Pictures), "backup.json");
        Assert.Equal(OtherPng, Assert.Single(read).CarriedPictures["WA - Tank.png"]);
    }

    [Fact]
    public void A_mod_file_without_pictures_reads_as_before_and_an_export_without_a_finder_leaves_them_out()
    {
        using var folder = new TemporaryFolder();
        (Mod mod, ModFileStore store, _) = Sample(folder);
        string text = ModFiles.ExportText(mod, isOn: true, store);
        Assert.Null(JsonNode.Parse(text)![ModFiles.PicturesKey]);
        Mod read = ModFiles.Read(text, "WA.mod.json")[0];
        Assert.Empty(read.CarriedPictures);
        Assert.Equal(Card, read.UnitEdits[0].PictureFile);
    }

    [Fact]
    public void A_package_never_takes_an_entry_outside_its_three_kinds_nor_a_picture_that_is_not_a_png()
    {
        using var package = new MemoryStream();
        using (var zip = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] bytes) { using Stream entry = zip.CreateEntry(name).Open(); entry.Write(bytes); }
            Add("mod.json", Encoding.UTF8.GetBytes("""{ "ModName": "Evil", "FormatVersion": 1, "Game": "WARNO", "Presets": [] }"""));
            Add("../../outside.png", Png);
            Add("pictures/../escape.png", Png);
            Add("pictures/sub/deep.png", Png);
            Add("pictures/notes.txt", Encoding.UTF8.GetBytes("text"));
            Add("pictures/fake.png", Encoding.UTF8.GetBytes("not a picture"));
            Add("pictures/good.png", Png);
            Add("files/not-a-stored-name.png", Png);
        }
        package.Position = 0;
        Mod read = Assert.Single(ModPackage.Read(package, "Evil.mod.zip"));
        Assert.Equal(["good.png"], read.CarriedPictures.Keys);
        Assert.Empty(read.CarriedFiles);
    }

    [Fact]
    public void A_file_that_is_not_a_package_is_refused_with_a_reason()
    {
        using var notZip = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        Assert.Throws<ModFileException>(() => ModPackage.Read(notZip, "x.mod.zip"));
        using var noMod = new MemoryStream();
        using (var zip = new ZipArchive(noMod, ZipArchiveMode.Create, leaveOpen: true)) zip.CreateEntry("readme.txt");
        noMod.Position = 0;
        Assert.Throws<ModFileException>(() => ModPackage.Read(noMod, "x.mod.zip"));
    }

    [Fact]
    public void Repointing_moves_every_place_a_picture_is_named()
    {
        using var folder = new TemporaryFolder();
        (Mod mod, _, _) = Sample(folder);
        mod.Presets.Add(new ModPreset { Name = "Divisions", Rules = [new JsonObject { ["Kind"] = "Edit", ["Armory"] = new JsonObject { ["PictureFile"] = Emblem } }] });
        ModFiles.RepointPictures(mod, new Dictionary<string, string> { ["WA - Emblem.png"] = @"D:\Here\WA - Emblem (2).png" });
        Assert.Equal(@"D:\Here\WA - Emblem (2).png", mod.Divisions[0].EmblemFile);
        Assert.Equal(@"D:\Here\WA - Emblem (2).png", (string?)mod.Presets[1].Rules[0]!["Armory"]!["PictureFile"]);
        Assert.Equal(Card, mod.UnitEdits[0].PictureFile);
    }
}
