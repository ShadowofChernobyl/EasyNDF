using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.App.Files;
using EasyNdf.App.Mods;
using EasyNdf.App.Units;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Game;

namespace EasyNdf.App.Tests;

/// <summary>
/// What the Armory's mod values are read from (<see cref="OpenModUnits.InputsOf"/>) is found with less work: the open
/// mod's locked Armory rules are no longer copied only to be left out, each file's rules are found once, and they are
/// written one after the other. The text must stay what it was: each file with the rules run on it, as a JSON list,
/// the open mod's Armory rules kept by hand after its rule sets' (with their headings), its locked ones left out.
/// </summary>
public sealed class OpenModUnitsInputsTests : IDisposable
{
    private const string GameDataFolder = "GameData/Generated/Gameplay/Gfx";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "EasyNdf.App.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<Mod> modsMade = [];
    private readonly List<Mod> switchedOff = [];

    public OpenModUnitsInputsTests()
    {
        Directory.CreateDirectory(folder);
        // only the mods made here run, so the text is known
        foreach (Mod mod in AppData.Current.Mods.ToList().Where(ModStack.IsOn))
        {
            ModStack.SetOn(mod, false);
            switchedOff.Add(mod);
        }
    }

    public void Dispose()
    {
        foreach (Mod mod in modsMade)
        {
            ModStack.SetOn(mod, false);
            AppData.Current.ModStore.Delete(mod);
        }
        foreach (Mod mod in switchedOff) ModStack.SetOn(mod, true);
        Directory.Delete(folder, recursive: true);
    }

    private static JsonObject ArmoryRule(string name, string file, bool locked) => new()
    {
        ["Name"] = name, ["Kind"] = "Edit", ["Enabled"] = true,
        ["Armory"] = locked ? new JsonObject { ["File"] = file } : new JsonObject { ["File"] = file, ["Locked"] = false },
    };

    private static JsonObject Heading(string name) => new() { ["Name"] = name, ["Kind"] = "Group", ["Enabled"] = true };

    /// <summary>A mod, switched on, with a rule set for Ammunition.ndf and an Armory preset with these rules.</summary>
    private Mod MakeMod(string name, params JsonNode?[] armoryRules)
    {
        var mod = new Mod
        {
            Name = name,
            Presets =
            [
                new ModPreset { Name = name + " (Ammunition.ndf)", Rules = [new JsonObject { ["Name"] = "Damage", ["Kind"] = "Edit", ["Enabled"] = true }] },
                new ModPreset { Name = "Divisions", ArmoryPart = ArmoryPart.Divisions, Rules = [.. armoryRules] },
            ],
        };
        AppData.Current.ModStore.Add(mod);
        modsMade.Add(mod);
        ModStack.SetOn(mod, true);
        return mod;
    }

    private FileEntry Entry(string name)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, "A is B()");
        return new FileEntry { Name = name, Folder = GameDataFolder, Source = path, Game = GameTitle.Warno, On = true };
    }

    private static string Start(Mod mod, RunSettings settings) => mod.Name + "\n" + GameTitle.Warno.Id + "\n" + settings + "\n";

    private static string Line(FileEntry entry, Mod mod, params JsonNode[] rules) =>
        entry.Source + "|" + "|" + mod.Name + "|" + new JsonArray([.. rules.Select(rule => rule.DeepClone())]).ToJsonString() + "\n";

    [Fact]
    public void Locked_Armory_rules_are_left_out_and_those_kept_by_hand_follow_the_rule_sets()
    {
        JsonObject heading = Heading("A division"), locked = ArmoryRule("Points", "Divisions.ndf", locked: true), byHand = ArmoryRule("Cards", "Divisions.ndf", locked: false);
        Mod mod = MakeMod("Inputs test, kept by hand", heading, locked, byHand, ArmoryRule("Other", "DivisionRules.ndf", locked: true));
        FileEntry ammunition = Entry("Ammunition.ndf"), divisions = Entry("Divisions.ndf"), rules = Entry("DivisionRules.ndf");
        var settings = RunSettings.From(AppData.Current.Settings);

        string inputs = OpenModUnits.InputsOf([ammunition, divisions, rules], mod, GameTitle.Warno, settings);

        JsonObject ruleSetRule = (JsonObject)mod.Presets[0].Rules[0]!;
        Assert.Equal(Start(mod, settings) + Line(ammunition, mod, ruleSetRule) + Line(divisions, mod, heading, byHand), inputs);
    }

    [Fact]
    public void A_file_with_only_locked_Armory_rules_is_not_listed()
    {
        Mod mod = MakeMod("Inputs test, all locked", Heading("A division"), ArmoryRule("Points", "Divisions.ndf", locked: true));
        FileEntry ammunition = Entry("Ammunition.ndf"), divisions = Entry("Divisions.ndf");
        var settings = RunSettings.From(AppData.Current.Settings);

        string inputs = OpenModUnits.InputsOf([ammunition, divisions], mod, GameTitle.Warno, settings);

        Assert.Equal(Start(mod, settings) + Line(ammunition, mod, (JsonObject)mod.Presets[0].Rules[0]!), inputs);
    }

    [Fact]
    public void A_rule_kept_by_hand_first_gets_a_heading_of_its_own()
    {
        JsonObject byHand = ArmoryRule("Cards", "Divisions.ndf", locked: false);
        Mod mod = MakeMod("Inputs test, no heading", ArmoryRule("Points", "Divisions.ndf", locked: true), byHand);
        FileEntry divisions = Entry("Divisions.ndf");
        var settings = RunSettings.From(AppData.Current.Settings);

        string inputs = OpenModUnits.InputsOf([divisions], mod, GameTitle.Warno, settings);

        var divisionsHeading = new JsonObject
        {
            ["Name"] = "Divisions", ["Kind"] = "Group", ["Enabled"] = true, ["LogicGate"] = "All",
            ["Conditions"] = new JsonArray(), ["Actions"] = new JsonArray(), ["Params"] = new JsonObject(),
        };
        // Ammunition.ndf is not listed: the mod's rule set for it runs on the source's own file, after the listed files
        Assert.Equal(Start(mod, settings) + Line(divisions, mod, divisionsHeading, byHand) + Line(StandIn("Ammunition.ndf"), mod, (JsonObject)mod.Presets[0].Rules[0]!), inputs);
    }

    /// <summary>A file the Files tab does not list, as the Armory stands in for it: no place on the disk, the source's own text.</summary>
    private static FileEntry StandIn(string name) => new() { Name = name, Source = "(the source's own file)", Game = GameTitle.Warno };

    [Fact]
    public void A_mod_that_is_on_runs_on_a_unit_file_the_Files_tab_does_not_list()
    {
        JsonObject added = ArmoryRule("New unit", "UniteDescriptor.ndf", locked: true);
        Mod edited = MakeMod("Inputs test, edited"), other = MakeMod("Inputs test, other", added);
        var settings = RunSettings.From(AppData.Current.Settings);

        string inputs = OpenModUnits.InputsOf([], edited, GameTitle.Warno, settings);

        // the other mod's Armory rules, locked ones too, as the Armory makes only the edited mod's changes itself
        Assert.Contains("(the source's own file)||" + other.Name + "|", inputs);
        Assert.Contains("\"Name\":\"New unit\"", inputs);
    }

    [Fact]
    public void A_listed_file_switched_off_gets_no_stand_in()
    {
        Mod mod = MakeMod("Inputs test, off");
        FileEntry ammunition = Entry("Ammunition.ndf");
        ammunition.On = false;
        var settings = RunSettings.From(AppData.Current.Settings);

        Assert.Equal(Start(mod, settings), OpenModUnits.InputsOf([ammunition], mod, GameTitle.Warno, settings));
    }
}
