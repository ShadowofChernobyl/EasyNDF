using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Steel Division 2's divisions changed in the Division pane: the details the game has (activation points, nation and
/// side, tags, emblem, costs) written as rules in the game's own names and forms, never WARNO's, and read back as the
/// Armory shows them. What the game's divisions lack, what the Armory does not write for them yet (names and texts,
/// units, new divisions) and costs other divisions share are not written. Divisions laid out as the game's are, with
/// made-up names and values; and, where the game is installed, some of its real divisions.
/// </summary>
public class SteelDivisionDivisionChangesTests
{
    private const string Armored = "Descriptor_Deck_Division_US_Test_Armored_multi";
    private const string Battalion = "Descriptor_Deck_Division_GER_Test_Battalion_HB";
    private const string Regiment = "Descriptor_Deck_Division_SOV_Test_Regiment_HB";

    /// <summary>A flag as the game writes it: a texture a line, one per frame.</summary>
    private static string Flag(string country) =>
        "    FlagAnimationTextures = [\n" + string.Concat(Enumerable.Range(0, 4).Select(frame => "        \"Texture_Division_Flag_" + country + "_" + frame + "\",\n")) + "    ]\n";

    /// <summary>A division laid out as the game's Divisions.ndf has it.</summary>
    private static string DivisionText(string descriptor, int number, string side, string tags, string matrix, string emblem, string country) =>
        "export " + descriptor + " is TDeckDivisionDescriptor\n(\n"
        + "    DescriptorId = GUID:{00000000-0000-0000-0000-00000000000" + number + "}\n"
        + "    DivisionName = 'TESTDIVNAM'\n"
        + "    DivisionNickname = ''\n"
        + "    DivisionDescription = 'TESTDIVDES'\n"
        + "    DivisionPowerClassification = 'DC_PWR2'\n"
        + "    DivisionNationalite = ENationalite/" + side + "\n"
        + "    DivisionTags = [" + tags + "]\n"
        + "    DivisionEconomyList = DivisionEconomy_Test\n"
        + "    AvailableForPlay = True\n"
        + "    PhaseList =\n    [\n        TPhaseDescriptor\n        (\n            PhaseName = 'PHASE_A'\n            PhaseStartTimeInSeconds = 0\n            PhaseBonus = 0\n            PhaseIncome = 100\n        ),\n    ]\n"
        + "    PackList = MAP\n    [\n        (~/Descriptor_Deck_Pack_TOE_Test_Tank, 2),\n    ]\n"
        + "    MaxActivationPoints = 50\n"
        + "    CostMatrix = " + matrix + "\n"
        + "    EmblemTexture     = \"" + emblem + "\"\n"
        + "    PortraitTexture   = \"Texture_Division_Portrait_Test\"\n"
        + "    TypeTexture       = \"Texture_Division_Type_armor\"\n"
        + "    CountryId = \"" + country + "\"\n"
        + Flag(country)
        + ")\n";

    private static readonly string Divisions = "// Ne pas éditer, ce fichier est auto-généré !\n\n\n"
        + DivisionText(Armored, 1, "Allied", "'West', 'DEFAULT', 'US', 'Allied', 'armor', 'DC_PWR2'", "MatrixCostName_US_Test_Armored_multi", "Texture_Division_Emblem_Test_Armored", "US")
        // two of the historical battles' divisions: no DEFAULT tag, and costs they share
        + DivisionText(Battalion, 2, "Axis", "'East', 'SS', 'Axis', 'infantry'", "MatrixCostName_HB_RemoveConstraints", "Texture_Division_Emblem_Test_Battalion", "SS")
        + DivisionText(Regiment, 3, "Allied", "'East', 'SOV', 'Allied', 'infantry'", "MatrixCostName_HB_RemoveConstraints", "Texture_Division_Emblem_Test_Regiment", "SOV");

    private const string DivisionList = """
        private DivisionList is TDeckDivisionList
        (
            DivisionList =
            [
                ~/Descriptor_Deck_Division_US_Test_Armored_multi,
                ~/Descriptor_Deck_Division_GER_Test_Battalion_HB,
                ~/Descriptor_Deck_Division_SOV_Test_Regiment_HB,
            ]
        )
        """;

    private const string DivisionRules = """
        // Ne pas éditer, ce fichier est auto-généré !

        private DivisionRules is TDeckDivisionRules
        (
            DivisionRules = MAP
            [
                (
                    ~/Descriptor_Deck_Division_US_Test_Armored_multi,
                    TDeckDivisionRule
                    (
                        UnitRuleList =
                        [
                            TDeckUniteRule
                            (
                                UnitDescriptor = ~/Descriptor_Unit_Test_Tank_US
                                AvailableWithoutTransport = True
                                AvailableTransportList = []
                                MaxPackNumber = 2
                                NumberOfUnitInPackByPhase = [2, 4, 6]
                                NumberOfUnitInPackXPMultiplier = [0.0, 1.0, 0.5]
                            ),
                        ]
                        TransportRuleList =
                        [
                        ]
                    )
                ),
            ]
        )
        """;

    private const string Costs = """
        // Ne pas éditer, ce fichier est auto-généré !


        MatrixCostName_US_Test_Armored_multi is MAP
        [
            (EDefaultFactories/air, [1, 1, 2]),
            (EDefaultFactories/infanterie, [1, 1, 1, 2]),
            (EDefaultFactories/reco, [1, 2]),
            (EDefaultFactories/tank, [1, 1, 2, 3]),
        ]

        MatrixCostName_Other_multi is MAP
        [
            (EDefaultFactories/tank, [1, 1, 2, 3]),
        ]
        """;

    // the one matrix of the game's own Gameplay/Decks/DivisionCostMatrix.ndf, which its historical battles share
    private const string SharedCosts = """
        MatrixCostName_HB_RemoveConstraints is MAP
        [
            (EDefaultFactories/infanterie, [0, 0, 0]),
            (EDefaultFactories/tank, [0, 0, 0]),
        ]
        """;

    private const string Emblems = """
        export Texture_Division_Emblem_Test_Armored is TUIResourceTexture_Common
        (
            FileName = "GameData:/Assets/2D/Interface/UseOutGame/Division/Emblem/Test_Armored.png"
        )
        export Texture_Division_Emblem_Test_Battalion is TUIResourceTexture_Common
        (
            FileName = "GameData:/Assets/2D/Interface/UseOutGame/Division/Emblem/Test_Battalion.png"
        )
        DivisionAdditionalTextureBank is TBUCKToolAdditionalTextureBank
        (
            Textures = MAP
            [
                ("Texture_Division_Emblem_Test_Armored", MAP [(~/ComponentState/Normal, ~/Texture_Division_Emblem_Test_Armored)]),
                ("Texture_Division_Emblem_Test_Battalion", MAP [(~/ComponentState/Normal, ~/Texture_Division_Emblem_Test_Battalion)]),
            ]
        )
        """;

    private static Dictionary<string, string> Files() => new()
    {
        ["Generated/Gameplay/Decks/Divisions.ndf"] = Divisions,
        ["Generated/Gameplay/Decks/DivisionList.ndf"] = DivisionList,
        ["Generated/Gameplay/Decks/DivisionRules.ndf"] = DivisionRules,
        ["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"] = Costs,
        ["GameData/Gameplay/Decks/DivisionCostMatrix.ndf"] = SharedCosts,
        ["Generated/Gameplay/UI/Resources/Textures/DivisionTextures.ndf"] = Emblems,
    };

    private static List<Division> Read(string zip, IReadOnlyList<ChangedFile>? changed = null)
    {
        using var files = new GameDataFiles(zip, null, changed);
        return DivisionReader.Read(files, GameTexts.FromPairs([]), []);
    }

    private static (string Zip, List<Division> Game) GameFiles(TemporaryFolder temp, Dictionary<string, string> files)
    {
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, files);
        return (zip, Read(zip));
    }

    /// <summary>
    /// Runs the rules on each file, as the export does: the output must parse, every rule for the file match and change
    /// something, and none skip anything. The files changed are given back, by path.
    /// </summary>
    private static Dictionary<string, string> RunOnEachFile(JsonArray rules, Dictionary<string, string> files)
    {
        var changed = new Dictionary<string, string>();
        foreach ((string path, string text) in files)
        {
            JsonArray forFile = DivisionChangeRules.ForFile(rules, Path.GetFileName(path));
            if (forFile.Count == 0) continue;
            var engine = new NdfEngine();
            engine.Load(text);
            RunResult result = engine.Run(RuleLoading.LoadForEngine(forFile), new RunOptions());
            Assert.Equal("", result.ParseError);
            Assert.All(result.Stats, rule => Assert.Equal(0, rule.Skipped));
            Parser.Parse(result.Text!);
            if (result.Text != text) changed[path] = result.Text!;
        }
        return changed;
    }

    /// <summary>The files changed, as the export writes them into the mod.</summary>
    private static List<ChangedFile> AsChangedFiles(Dictionary<string, string> changed) =>
        [.. changed.Select(file => new ChangedFile("C:/Mod/GameData/" + file.Key, file.Value))];

    /// <summary>The lines the rules changed, as the game wrote them: the files keep their lines, one for one.</summary>
    private static List<string> ChangedLines(string before, string after)
    {
        Assert.Equal(before.Split('\n').Length, after.Split('\n').Length);
        return [.. before.Split('\n').Zip(after.Split('\n')).Where(pair => pair.First != pair.Second).Select(pair => pair.First.Trim())];
    }

    /// <summary>WARNO's names and forms for a division's details, which Steel Division 2's files must never get.</summary>
    private static readonly string[] WarnoOnly =
        ["DivisionCoalition", "TWargameCoalition", "Factory/", "InterfaceOrder", "TypeToken", "StandoutUnits", "SummaryTextToken", "HistoryTextToken", "$/GFX/", "DivisionRule "];

    /// <summary>A division as the Armory and the tests compare it: its details, in the forms read.</summary>
    private static string Describe(Division division) =>
        division.Descriptor + " " + division.ActivationPoints + " " + division.Country + " " + division.Side + " [" + string.Join(" ", division.Tags) + "] "
        + division.EmblemTexture + " (" + string.Join(" ", division.FlagTextures) + ") "
        + string.Join("; ", division.Costs.OrderBy(cost => cost.Key, StringComparer.Ordinal).Select(cost => cost.Key + "=" + string.Join(",", cost.Value)));

    [Fact]
    public void Its_divisions_are_read_with_their_cost_keys_flags_shared_costs_and_emblems()
    {
        using var temp = new TemporaryFolder();
        (_, List<Division> game) = GameFiles(temp, Files());

        Division armored = game.Single(division => division.Descriptor == Armored);
        Assert.Equal("EDefaultFactories / tank", armored.CostKeys["Tanks"]);
        Assert.Equal("EDefaultFactories / infanterie", armored.CostKeys["Infantry"]);
        Assert.Equal(["Texture_Division_Flag_US_0", "Texture_Division_Flag_US_1", "Texture_Division_Flag_US_2", "Texture_Division_Flag_US_3"], armored.FlagTextures);
        Assert.Equal(0, armored.CostsSharedWith);
        Assert.Equal("GameData:/Assets/2D/Interface/UseOutGame/Division/Emblem/Test_Armored.png", armored.EmblemSource);
        // the historical battles' divisions share one matrix
        Division battalion = game.Single(division => division.Descriptor == Battalion);
        Assert.Equal(1, battalion.CostsSharedWith);
        Assert.Equal([0, 0, 0], battalion.Costs["Tanks"]);
    }

    [Fact]
    public void Every_detail_the_pane_changes_lands_in_its_own_property_in_the_games_form_and_reads_back_as_shown()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = Files();
        (string zip, List<Division> game) = GameFiles(temp, files);
        Division armored = game.Single(division => division.Descriptor == Armored);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.ChangeDetails(mod, game, Armored, change =>
        {
            change.ActivationPoints = 60;
            (change.Country, change.Side) = ("SOV", "Allied");
            (change.EmblemTexture, change.EmblemSource) = ("Texture_Division_Emblem_Test_Battalion", "GameData:/Assets/2D/Interface/UseOutGame/Division/Emblem/Test_Battalion.png");
            change.Costs = armored.Costs.ToDictionary(cost => cost.Key, cost => cost.Value.ToList());
            change.Costs["Tanks"] = [1, 2, 2, 3, 4];
            change.Costs["Recons"] = [2];
        });
        Assert.True(UnitChangeRules.WritesEverythingOf(mod));

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Dictionary<string, string> changed = RunOnEachFile(rules, files);

        Assert.Equal(["Generated/Gameplay/Decks/DivisionCostMatrix.ndf", "Generated/Gameplay/Decks/Divisions.ndf"], changed.Keys.Order(StringComparer.Ordinal));
        string divisions = changed["Generated/Gameplay/Decks/Divisions.ndf"];
        string costs = changed["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"];
        // each in the game's own form, in the armoured division only: the others are as they were
        Assert.Equal(
            [
                "DivisionTags = ['West', 'DEFAULT', 'US', 'Allied', 'armor', 'DC_PWR2']", "MaxActivationPoints = 50",
                "EmblemTexture     = \"Texture_Division_Emblem_Test_Armored\"", "CountryId = \"US\"",
                "\"Texture_Division_Flag_US_0\",", "\"Texture_Division_Flag_US_1\",", "\"Texture_Division_Flag_US_2\",", "\"Texture_Division_Flag_US_3\",",
            ],
            ChangedLines(Divisions, divisions));
        Assert.Contains("    MaxActivationPoints = 60\n", divisions, StringComparison.Ordinal);
        Assert.Contains("    CountryId = \"SOV\"\n", divisions, StringComparison.Ordinal);
        // the Soviet divisions are all on the East front: the division goes with them
        Assert.Contains("    DivisionTags = ['East', 'DEFAULT', 'SOV', 'Allied', 'armor', 'DC_PWR2']\n", divisions, StringComparison.Ordinal);
        Assert.Contains("    EmblemTexture     = \"Texture_Division_Emblem_Test_Battalion\"\n", divisions, StringComparison.Ordinal);
        Assert.Contains("        \"Texture_Division_Flag_SOV_3\",\n", divisions, StringComparison.Ordinal);
        Assert.Equal(Divisions[Divisions.IndexOf("export " + Battalion, StringComparison.Ordinal)..], divisions[divisions.IndexOf("export " + Battalion, StringComparison.Ordinal)..]);
        Assert.Equal(["(EDefaultFactories/reco, [1, 2]),", "(EDefaultFactories/tank, [1, 1, 2, 3]),"], ChangedLines(Costs, costs));
        Assert.Contains("    (EDefaultFactories/tank, [1, 2, 2, 3, 4]),\n", costs, StringComparison.Ordinal);
        Assert.Contains("    (EDefaultFactories/reco, [2]),\n", costs, StringComparison.Ordinal);
        // nothing of WARNO's: neither its names in the rules nor in the files
        IEnumerable<string> targets = rules.SelectMany(rule => rule?["Actions"]?.AsArray() ?? []).Select(action => (string?)action?["Target"] ?? "");
        Assert.All(targets, target => Assert.DoesNotContain(WarnoOnly, warno => target.Contains(warno, StringComparison.Ordinal)));
        Assert.All(WarnoOnly, warno => Assert.DoesNotContain(warno, divisions + costs, StringComparison.Ordinal));
        // read back as the Armory shows it
        Division exported = Read(zip, AsChangedFiles(changed)).Single(division => division.Descriptor == Armored);
        Division shown = DivisionChanges.Apply(game, mod).Single(division => division.Descriptor == Armored);
        Assert.Equal(Describe(shown with { FlagTextures = exported.FlagTextures }), Describe(exported));
        Assert.Equal(["Texture_Division_Flag_SOV_0", "Texture_Division_Flag_SOV_1", "Texture_Division_Flag_SOV_2", "Texture_Division_Flag_SOV_3"], exported.FlagTextures);
    }

    [Fact]
    public void A_division_taken_out_leaves_the_games_list_of_divisions_and_the_others_stay()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = Files();
        (string zip, List<Division> game) = GameFiles(temp, files);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.Delete(mod, Armored);

        Assert.True(UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Dictionary<string, string> changed = RunOnEachFile(rules, files);

        Assert.Equal(["Generated/Gameplay/Decks/DivisionList.ndf"], changed.Keys);
        string list = changed["Generated/Gameplay/Decks/DivisionList.ndf"];
        Assert.DoesNotContain("Descriptor_Deck_Division_US_Test_Armored_multi", list, StringComparison.Ordinal);
        Assert.Contains("~/Descriptor_Deck_Division_GER_Test_Battalion_HB,", list, StringComparison.Ordinal);
        List<Division> exported = Read(zip, AsChangedFiles(changed));
        Assert.False(exported.Single(division => division.Descriptor == Armored).IsForMultiplayer);
        Assert.True(exported.Single(division => division.Descriptor == Battalion).IsForMultiplayer);
        // its DEFAULT tag is left as it is: Steel Division 2 goes by its list
        Assert.DoesNotContain("Divisions.ndf", string.Join(" ", changed.Keys), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_nation_takes_its_front_only_where_the_games_divisions_of_it_are_all_on_one()
    {
        Division Of(string country, params string[] tags) => new() { Descriptor = "Descriptor_Deck_Division_" + country, Name = country, Country = country, Side = "Axis", Tags = tags };
        // the Soviets fight in the East only; the SS in both
        List<Division> game = [Of("SOV", "East", "SOV"), Of("SS", "East", "SS"), Of("SS", "West", "SS"), Of("US", "West", "US")];
        Dictionary<string, string> fronts = SteelDivisionDivisions.FrontsOf(game);
        Division before = Of("US", "West", "DEFAULT", "US", "Axis");

        Assert.Equal(["East", "DEFAULT", "SOV", "Allied"], ModDivision.TagsWith(before, "SOV", "Allied", "", fronts));
        Assert.Equal(["West", "DEFAULT", "SS", "Axis"], ModDivision.TagsWith(before, "SS", "Axis", "", fronts));
        // WARNO's divisions have no front: nothing more changes than the nation and side
        Assert.Empty(SteelDivisionDivisions.FrontsOf([Of("US", "DEFAULT", "US", "NATO")]));
        Assert.Equal(["DEFAULT", "SOV", "PACT"], ModDivision.TagsWith(Of("US", "DEFAULT", "US", "NATO") with { Side = "NATO" }, "SOV", "PACT", "", null));
    }

    [Fact]
    public void A_new_side_alone_keeps_the_flag_and_a_division_without_DEFAULT_is_given_none()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = Files();
        (string zip, List<Division> game) = GameFiles(temp, files);
        var sideOnly = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.ChangeDetails(sideOnly, game, Battalion, change => (change.Country, change.Side) = ("SS", "Allied"));
        var newNation = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.ChangeDetails(newNation, game, Battalion, change => (change.Country, change.Side) = ("SOV", "Allied"));

        string sideChanged = RunOnEachFile(DivisionChangeRules.For(sideOnly, game, new TextCatalog(), unit => unit), files)["Generated/Gameplay/Decks/Divisions.ndf"];
        string nationChanged = RunOnEachFile(DivisionChangeRules.For(newNation, game, new TextCatalog(), unit => unit), files)["Generated/Gameplay/Decks/Divisions.ndf"];

        Assert.Equal(["DivisionNationalite = ENationalite/Axis", "DivisionTags = ['East', 'SS', 'Axis', 'infantry']"], ChangedLines(Divisions, sideChanged));
        Assert.Contains("    DivisionTags = ['East', 'SS', 'Allied', 'infantry']\n", sideChanged, StringComparison.Ordinal);
        // the new nation's flag is the one its own divisions wave
        Division exported = Read(zip, AsChangedFiles(new() { ["Generated/Gameplay/Decks/Divisions.ndf"] = nationChanged })).Single(division => division.Descriptor == Battalion);
        Assert.Equal(["East", "SOV", "Allied", "infantry"], exported.Tags);
        Assert.Equal(game.Single(division => division.Descriptor == Regiment).FlagTextures, exported.FlagTextures);
    }

    [Fact]
    public void An_emblem_picture_of_ones_own_is_a_modded_texture_in_AssetsModding_with_its_bank_entry()
    {
        using var temp = new TemporaryFolder();
        Dictionary<string, string> files = Files();
        (string zip, List<Division> game) = GameFiles(temp, files);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.ChangeDetails(mod, game, Armored, change => change.EmblemFile = "C:/Pictures/Brigade.png");
        Assert.True(UnitChangeRules.WritesEverythingOf(mod));

        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Dictionary<string, string> changed = RunOnEachFile(rules, files);

        string textures = changed["Generated/Gameplay/UI/Resources/Textures/DivisionTextures.ndf"];
        Assert.Contains("export Texture_Division_Emblem_EZ_US_Test_Armored is TUIResourceTexture_CommonModded\n(\n    FileName = 'Division\\Emblem\\EZ_US_Test_Armored.png'\n)", textures.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("(\"Texture_Division_Emblem_EZ_US_Test_Armored\", MAP [(~/ComponentState/Normal, ~/Texture_Division_Emblem_EZ_US_Test_Armored)])", textures, StringComparison.Ordinal);
        Assert.Contains("EmblemTexture     = \"Texture_Division_Emblem_EZ_US_Test_Armored\"", changed["Generated/Gameplay/Decks/Divisions.ndf"], StringComparison.Ordinal);
        // the picture is copied into AssetsModding, beside the mod's GameData folder
        JsonObject picture = rules.OfType<JsonObject>().Single(rule => rule[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.PictureFileKey] is not null);
        Assert.Equal("AssetsModding/Division/Emblem/EZ_US_Test_Armored.png", (string?)picture[DivisionChangeRules.ArmoryKey]![DivisionChangeRules.PicturePathKey]);
        Assert.Equal("Texture_Division_Emblem_EZ_US_Test_Armored", Read(zip, AsChangedFiles(changed)).Single(division => division.Descriptor == Armored).EmblemTexture);
    }

    [Fact]
    public void What_its_divisions_lack_or_the_Armory_does_not_write_for_them_and_shared_costs_are_never_written()
    {
        using var temp = new TemporaryFolder();
        (_, List<Division> game) = GameFiles(temp, Files());
        string[] notWritten =
        [
            DivisionDetail.Type, DivisionDetail.ListOrder, DivisionDetail.History, DivisionDetail.Standout,
            DivisionDetail.Battlegroup,
        ];
        Assert.All(notWritten, detail => Assert.False(DivisionChangeRules.Writes(GameTitle.SteelDivision2, detail), detail));
        Assert.All(SteelDivisionDivisions.Written, detail => Assert.True(DivisionChangeRules.Writes(GameTitle.SteelDivision2, detail), detail));
        // the game has a name and a description, but no type key, place in the list, history or standout units
        Assert.True(DivisionChangeRules.Has(GameTitle.SteelDivision2, DivisionDetail.Name));
        Assert.All(SteelDivisionDivisions.Lacks, detail => Assert.False(DivisionChangeRules.Has(GameTitle.SteelDivision2, detail), detail));
        // WARNO writes them all, as before
        Assert.All(notWritten.Concat(SteelDivisionDivisions.Written), detail => Assert.True(DivisionChangeRules.Writes(GameTitle.Warno, detail), detail));

        // each change the Armory does not write is refused, and none of it reaches the rules
        Action<Mod>[] refused =
        [
            mod => DivisionChanges.ChangeDetails(mod, game, Armored, change => change.ListOrder = 3),
            mod => DivisionChanges.ChangeDetails(mod, game, Armored, change => change.TypeToken = "ARMORED"),
        ];
        foreach (Action<Mod> change in refused)
        {
            var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
            change(mod);
            Assert.False(UnitChangeRules.WritesEverythingOf(mod));
            Assert.Empty(DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit));
        }

        // costs the historical battles' divisions share would change the other's too
        Division battalion = game.Single(division => division.Descriptor == Battalion);
        Assert.False(DivisionChangeRules.WritesCosts(GameTitle.SteelDivision2, battalion));
        Assert.True(DivisionChangeRules.WritesCosts(GameTitle.SteelDivision2, game.Single(division => division.Descriptor == Armored)));
        Assert.True(DivisionChangeRules.WritesCosts(GameTitle.Warno, battalion));
        var shared = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.ChangeDetails(shared, game, Battalion, change => change.Costs = new() { ["Tanks"] = [1, 1, 1], ["Infantry"] = [0, 0, 0] });
        Assert.Empty(DivisionChangeRules.For(shared, game, new TextCatalog(), unit => unit));
    }

    // ---- the game's own files, where it is installed ----

    /// <summary>The Steel Division 2 mod made with the game's CreateNewMod on the test computer; the tests pass without looking where it is not.</summary>
    private const string InstalledGameData = @"E:\Steel Division 2\Mods\Test Mod\GameData";

    private static string InstalledDecks(string file) => Path.Combine(InstalledGameData, "Generated", "Gameplay", "Decks", file);

    private static string InstalledTextures => Path.Combine(InstalledGameData, "Generated", "Gameplay", "UI", "Resources", "Textures", "DivisionTextures.ndf");

    /// <summary>Each top-level declaration of a real file of the game, with its text as written.</summary>
    private static List<(string Name, string Text)> DeclarationsIn(string file)
    {
        using var reader = new StreamReader(file);
        return [.. UnitReader.DeclarationTexts(reader)];
    }

    /// <summary>The properties each rule writes into a division of Divisions.ndf, by name.</summary>
    private static readonly string[] WrittenProperties =
        ["MaxActivationPoints", "CountryId", SteelDivisionDivisions.SideProperty, "DivisionTags", SteelDivisionDivisions.FlagProperty, "EmblemTexture"];

    [Fact]
    public void Each_property_written_is_found_once_in_every_real_division_and_each_cost_key_once_in_every_matrix()
    {
        if (!File.Exists(InstalledDecks("Divisions.ndf"))) return;
        int divisions = 0;
        var flagsOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach ((string name, string text) in DeclarationsIn(InstalledDecks("Divisions.ndf")))
        {
            NdfValue division = Parser.Parse(text).Declarations.Single().Value;
            List<Property> properties = [.. NdfValues.AllProperties(division)];
            foreach (string property in WrittenProperties)
                Assert.True(properties.Count(each => each.Name == property) == 1, name + " has " + property + " other than once");
            string country = NdfValues.Text(NdfValues.Find(division, "CountryId")) ?? "";
            flagsOf.TryAdd(country, [.. NdfValues.ItemsOf(NdfValues.Find(division, SteelDivisionDivisions.FlagProperty)).Select(NdfValues.Text).OfType<string>()]);
            divisions++;
        }
        Assert.True(divisions > 700);

        // each nation's flag is made of textures the game declares, named after the nation
        HashSet<string> textures = [.. DeclarationsIn(InstalledTextures).Select(declaration => declaration.Name)];
        foreach ((string country, List<string> flag) in flagsOf)
        {
            Assert.NotEmpty(flag);
            Assert.All(flag, texture => Assert.StartsWith(SteelDivisionDivisions.FlagPrefix + country + "_", texture, StringComparison.Ordinal));
            Assert.All(flag, texture => Assert.Contains(texture, textures));
        }

        // each cost key once in each matrix, written as the rules name it
        int matrices = 0;
        foreach ((string name, string text) in DeclarationsIn(InstalledDecks("DivisionCostMatrix.ndf")))
        {
            List<string> keys = [.. NdfValues.PairsOf(Parser.Parse(text).Declarations.Single().Value).Select(pair => Engine.Writing.CanonicalWriter.PrimitiveText(pair.Key))];
            Assert.All(keys, key => Assert.StartsWith(SteelDivisionDivisions.CostKeyPrefix, key, StringComparison.Ordinal));
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
            matrices++;
        }
        Assert.True(matrices > 100);
    }

    [Fact]
    public void Real_divisions_of_the_game_take_the_changes_and_only_the_intended_lines_change()
    {
        if (!File.Exists(InstalledDecks("Divisions.ndf"))) return;
        const string sofia = "Descriptor_Deck_Division_BUL_Sofia_Brigade_multi";
        List<(string Name, string Text)> all = DeclarationsIn(InstalledDecks("Divisions.ndf"));
        string listText = File.ReadAllText(InstalledDecks("DivisionList.ndf"));
        // the Bulgarian brigade, and a Soviet division the game offers, for the flag of its nation
        (string Name, string Text) soviet = all.First(declaration => declaration.Text.Contains("CountryId = \"SOV\"", StringComparison.Ordinal)
            && listText.Contains("~/" + declaration.Name + ",", StringComparison.Ordinal));
        string divisionsText = all.Single(declaration => declaration.Name == sofia).Text + soviet.Text;
        string costsText = DeclarationsIn(InstalledDecks("DivisionCostMatrix.ndf")).Single(declaration => declaration.Name == "MatrixCostName_BUL_Sofia_Brigade_multi").Text;
        var files = new Dictionary<string, string>
        {
            ["Generated/Gameplay/Decks/Divisions.ndf"] = divisionsText,
            ["Generated/Gameplay/Decks/DivisionList.ndf"] = listText,
            ["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"] = costsText,
        };
        using var temp = new TemporaryFolder();
        (string zip, List<Division> game) = GameFiles(temp, files);
        Division before = game.Single(division => division.Descriptor == sofia);
        Division other = game.Single(division => division.Descriptor == soviet.Name);
        Assert.Equal((50, "BUL", "Allied", 58), (before.ActivationPoints, before.Country, before.Side, before.FlagTextures.Count));
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.ChangeDetails(mod, game, sofia, change =>
        {
            change.ActivationPoints = 55;
            (change.Country, change.Side) = ("SOV", "Allied");
            change.EmblemTexture = other.EmblemTexture;
            change.Costs = before.Costs.ToDictionary(cost => cost.Key, cost => cost.Value.ToList());
            change.Costs["Tanks"] = [.. before.Costs["Tanks"], 4];
        });

        Dictionary<string, string> changed = RunOnEachFile(DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit), files);

        string divisions = changed["Generated/Gameplay/Decks/Divisions.ndf"];
        string costs = changed["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"];
        Assert.Equal(2, Parser.Parse(divisions).Declarations.Count);
        // the points, nation, tags, emblem and the 58 frames of the flag; nothing else
        List<string> lines = ChangedLines(divisionsText, divisions);
        Assert.Equal(4 + 58, lines.Count);
        Assert.All(lines, line => Assert.Matches("^(MaxActivationPoints |CountryId |DivisionTags |EmblemTexture |\"Texture_Division_Flag_BUL_[0-9]+\",)", line));
        Assert.Equal(["(EDefaultFactories/tank, [1, 1, 1, 1, 1, 2, 2, 3, 3, 4]),"], ChangedLines(costsText, costs));
        Assert.Contains("(EDefaultFactories/tank, [1, 1, 1, 1, 1, 2, 2, 3, 3, 4, 4]),", costs, StringComparison.Ordinal);
        Assert.All(WarnoOnly, warno => Assert.DoesNotContain(warno, divisions + costs, StringComparison.Ordinal));
        // the Soviet division is as it was
        Assert.EndsWith(soviet.Text, divisions, StringComparison.Ordinal);
        Division exported = Read(zip, AsChangedFiles(changed)).Single(division => division.Descriptor == sofia);
        Division shown = DivisionChanges.Apply(game, mod).Single(division => division.Descriptor == sofia);
        Assert.Equal(Describe(shown with { FlagTextures = other.FlagTextures }), Describe(exported));
        Assert.Equal(["East", "DEFAULT", "SOV", "Allied", "armor", "DC_PWR2"], exported.Tags);
    }
}
