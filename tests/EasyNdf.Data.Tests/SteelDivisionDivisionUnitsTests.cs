using System.Text.Json.Nodes;
using EasyNdf.Data.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Steel Division 2's units in a division, put in, taken out and changed: written in the four places the game keeps them
/// (the division's entry of DivisionRules.ndf's one MAP, its PackList in Divisions.ndf, a pack in Packs.ndf and the
/// pack's number in DeckSerializer.ndf), the other divisions' entries left alone, and read back as the Armory shows
/// them. Two mods changing different units of one division both take. Files laid out as the game's, with made-up names.
/// </summary>
public class SteelDivisionDivisionUnitsTests
{
    private const string Armored = "Descriptor_Deck_Division_US_Test_Armored_multi", Other = "Descriptor_Deck_Division_US_Test_Other_multi";
    private const string Tank = "Descriptor_Unit_Test_Tank_US", Squad = "Descriptor_Unit_Test_Squad_US", Jeep = "Descriptor_Unit_Test_Jeep_US";

    private static string DivisionText(string descriptor, int number, string packs) =>
        "export " + descriptor + " is TDeckDivisionDescriptor\n(\n"
        + "    DescriptorId = GUID:{00000000-0000-0000-0000-00000000000" + number + "}\n"
        + "    DivisionName = 'TESTDIVNAM'\n"
        + "    DivisionDescription = 'DESTEST'\n"
        + "    AvailableForPlay = True\n"
        + "    DivisionNationalite = ENationalite/Allied\n"
        + "    DivisionTags = ['West', 'DEFAULT', 'US', 'Allied', 'armor']\n"
        + "    PackList = MAP\n    [\n" + packs + "    ]\n"
        + "    MaxActivationPoints = 50\n"
        + "    CountryId = \"US\"\n"
        + ")\n";

    private static readonly string Divisions = "// Ne pas éditer, ce fichier est auto-généré !\n\n\n"
        + DivisionText(Armored, 1, "        (~/Descriptor_Deck_Pack_TOE_US_Test_Armored_multi_Test_Tank_US, 2),\n        (~/Descriptor_Deck_Pack_TOE_US_Test_Armored_multi_Test_Squad_US, 3),\n")
        + DivisionText(Other, 2, "        (~/Descriptor_Deck_Pack_TOE_US_Test_Other_multi_Test_Tank_US, 2),\n");

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
                                MaxPackNumber = 2
                                NumberOfUnitInPackByPhase = [2, 4, 6]
                                NumberOfUnitInPackXPMultiplier = [0.0, 1.0, 0.5]
                            ),
                            TDeckUniteRule
                            (
                                UnitDescriptor = ~/Descriptor_Unit_Test_Squad_US
                                AvailableWithoutTransport = False
                                AvailableTransportList = [~/Descriptor_Unit_Test_Truck_US]
                                MaxPackNumber = 3
                                NumberOfUnitInPackByPhase = [6, 12, 0]
                                NumberOfUnitInPackXPMultiplier = [1.0, 0.0, 0.0]
                            ),
                        ]
                        TransportRuleList =
                        [
                            TDeckTransportRule
                            (
                                TransportDescriptor = ~/Descriptor_Unit_Test_Truck_US
                                MaxNumber = 32
                            ),
                        ]
                    )
                ),
                (
                    ~/Descriptor_Deck_Division_US_Test_Other_multi,
                    TDeckDivisionRule
                    (
                        UnitRuleList =
                        [
                            TDeckUniteRule
                            (
                                UnitDescriptor = ~/Descriptor_Unit_Test_Tank_US
                                AvailableWithoutTransport = True
                                MaxPackNumber = 2
                                NumberOfUnitInPackByPhase = [2, 4, 6]
                                NumberOfUnitInPackXPMultiplier = [0.0, 1.0, 0.5]
                            ),
                        ]
                        TransportRuleList =
                        [
                            TDeckTransportRule
                            (
                                TransportDescriptor = ~/Descriptor_Unit_Test_Halftrack_US
                                MaxNumber = 12
                            ),
                        ]
                    )
                ),
            ]
        )
        """;

    private const string Packs = """
        // Ne pas éditer, ce fichier est auto-généré !


        export Descriptor_Deck_Pack_TOE_US_Test_Armored_multi_Test_Tank_US is TDeckPackDescriptor
        (
            DescriptorId = GUID:{00000000-0000-0000-0000-0000000000a1}
            TransporterAndUnitsList =
            [
                TDeckTransporterAndUnitsDescriptor
                (
                    UnitDescriptorList =
                    [
                        ~/Descriptor_Unit_Test_Tank_US,
                    ]
                ),
            ]
        )
        """;

    private const string DeckSerializer = """
        // Ne pas éditer, ce fichier est auto-généré !


        DeckSerializer is TDeckSerializer
        (

            DivisionIds = MAP
            [
                (Descriptor_Deck_Division_US_Test_Armored_multi, 66),
                (Descriptor_Deck_Division_US_Test_Other_multi, 67),
            ]
            PackIds = MAP
            [
                (Descriptor_Deck_Pack_TOE_US_Test_Armored_multi_Test_Tank_US, 11891),
                (Descriptor_Deck_Pack_TOE_US_Test_Armored_multi_Test_Squad_US, 11892),
                (Descriptor_Deck_Pack_TOE_US_Test_Other_multi_Test_Tank_US, 11893),
            ]
            UnitIds = MAP
            [
                (Descriptor_Unit_Test_Tank_US, 0),
            ]
        )
        """;

    private static Dictionary<string, string> Files() => new()
    {
        ["Generated/Gameplay/Decks/Divisions.ndf"] = Divisions,
        ["Generated/Gameplay/Decks/DivisionRules.ndf"] = DivisionRules,
        ["Generated/Gameplay/Decks/Packs.ndf"] = Packs,
        ["Generated/Gameplay/Decks/DeckSerializer.ndf"] = DeckSerializer,
    };

    private static List<Division> Read(string zip, IReadOnlyList<ChangedFile>? changed = null)
    {
        using var files = new GameDataFiles(zip, null, changed);
        return DivisionReader.Read(files, GameTexts.FromPairs([]), []);
    }

    /// <summary>Runs the rules on each file, as the export does, on the files given; gives back every file, changed or not.</summary>
    private static Dictionary<string, string> RunOnEachFile(JsonArray rules, Dictionary<string, string> files)
    {
        var after = new Dictionary<string, string>(files);
        foreach ((string path, string text) in files)
        {
            JsonArray forFile = DivisionChangeRules.ForFile(rules, Path.GetFileName(path));
            if (forFile.Count == 0) continue;
            var engine = new NdfEngine();
            engine.Load(text);
            RunResult result = engine.Run(RuleLoading.LoadForEngine(forFile), new RunOptions());
            Assert.Equal("", result.ParseError);
            // a transport list is set where the unit has one and added where it has none: one of the two finds nothing
            Assert.All(result.Stats.Where(rule => !rule.Name.Contains("transports", StringComparison.Ordinal)), rule => Assert.Equal(0, rule.Skipped));
            Parser.Parse(result.Text!);
            after[path] = result.Text!;
        }
        return after;
    }

    private static List<ChangedFile> AsChangedFiles(Dictionary<string, string> files) =>
        [.. files.Select(file => new ChangedFile("C:/Mod/GameData/" + file.Key, file.Value))];

    private static string Describe(Division division) => string.Join("; ", division.Units.Select(unit =>
        unit.Unit + " " + unit.Cards + " [" + string.Join(",", unit.UnitsPerCardByPhase) + "] [" + string.Join(",", unit.VeterancyMultipliers) + "] "
        + unit.AvailableWithoutTransport + " [" + string.Join(",", unit.Transports) + "]"));

    [Fact]
    public void Units_put_in_taken_out_and_changed_land_in_all_four_files_and_read_back_as_shown()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        List<Division> game = Read(zip);
        Assert.Equal(32, game.Single(division => division.Descriptor == Armored).TransportCaps["Descriptor_Unit_Test_Truck_US"]);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.RemoveUnit(mod, game, Armored, Squad);
        DivisionChanges.AddUnits(mod, game, Armored, [Jeep]);
        DivisionUnit jeep = mod.Divisions.Single().Added.Single(unit => unit.Unit == Jeep) with
        {
            Cards = 3, UnitsPerCardByPhase = [1, 2, 3], AvailableWithoutTransport = false, Transports = ["Descriptor_Unit_Test_Halftrack_US"],
        };
        DivisionChanges.SetRule(mod, game, Armored, jeep);
        DivisionUnit tank = game.Single(division => division.Descriptor == Armored).Units.Single(unit => unit.Unit == Tank);
        DivisionChanges.SetRule(mod, game, Armored, tank with { Cards = 4, UnitsPerCardByPhase = [3, 6, 9] });

        Assert.True(DivisionChangeRules.Writes(GameTitle.SteelDivision2, DivisionDetail.Units));
        Assert.True(Units.UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Dictionary<string, string> after = RunOnEachFile(rules, Files());
        List<Division> exported = Read(zip, AsChangedFiles(after));

        // as the Armory shows the division, and the other division as the game has it
        Division shown = mod.Divisions.Single().Apply(game.Single(division => division.Descriptor == Armored));
        Assert.Equal(Describe(shown), Describe(exported.Single(division => division.Descriptor == Armored)));
        Assert.Equal(Describe(game.Single(division => division.Descriptor == Other)), Describe(exported.Single(division => division.Descriptor == Other)));
        Assert.Equal(["Descriptor_Unit_Test_Tank_US", "Descriptor_Unit_Test_Jeep_US"], exported.Single(division => division.Descriptor == Armored).Units.Select(unit => unit.Unit));
        // the jeep's transport is capped as the game caps it elsewhere
        Assert.Equal(12, exported.Single(division => division.Descriptor == Armored).TransportCaps["Descriptor_Unit_Test_Halftrack_US"]);

        // the packs: the squad's out of the list, the tank's cards as its rule's, the jeep's own pack in, numbered for deck codes
        string pack = DivisionChangeRules.OwnPackOf(Armored, Jeep);
        string divisions = after["Generated/Gameplay/Decks/Divisions.ndf"];
        Assert.DoesNotContain("Test_Squad_US", divisions, StringComparison.Ordinal);
        Assert.Contains("(~/Descriptor_Deck_Pack_TOE_US_Test_Armored_multi_Test_Tank_US, 4)", divisions, StringComparison.Ordinal);
        Assert.Contains("(~/Descriptor_Deck_Pack_TOE_US_Test_Other_multi_Test_Tank_US, 2)", divisions, StringComparison.Ordinal);
        Assert.Contains("(~/" + pack + ", 3)", divisions, StringComparison.Ordinal);
        Assert.Equal("Descriptor_Deck_Pack_EZ_US_Test_Armored_multi_Test_Jeep_US", pack);
        Declaration added = Parser.Parse(after["Generated/Gameplay/Decks/Packs.ndf"]).Declarations.Single(declaration => declaration.Name == pack);
        Assert.Contains("~/" + Jeep, Engine.Writing.CanonicalWriter.Write(added.Value), StringComparison.Ordinal);
        Assert.Contains("DescriptorId = GUID:{", after["Generated/Gameplay/Decks/Packs.ndf"].Split(pack)[1], StringComparison.Ordinal);
        string serializer = after["Generated/Gameplay/Decks/DeckSerializer.ndf"];
        System.Text.RegularExpressions.Match number = System.Text.RegularExpressions.Regex.Match(serializer, "\\(" + pack + ", ([0-9]+)\\)");
        Assert.True(number.Success);
        Assert.InRange(int.Parse(number.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 360000, 524287);
        // nothing written WARNO's way
        foreach (string text in after.Values) Assert.DoesNotContain("$/GFX/Unit/", text, StringComparison.Ordinal);
    }

    private const string DivisionList = """
        private DivisionList is TDeckDivisionList
        (
            DivisionList =
            [
                ~/Descriptor_Deck_Division_US_Test_Armored_multi,
                ~/Descriptor_Deck_Division_US_Test_Other_multi,
            ]
        )
        """;

    private const string CostMatrices = """
        // Ne pas éditer, ce fichier est auto-généré !


        MatrixCostName_US_Test_Armored_multi is MAP
        [
            (EDefaultFactories/infanterie, [1, 1, 2]),
            (EDefaultFactories/tank, [1, 1, 1, 2, 3]),
        ]
        """;

    [Fact]
    public void A_new_division_is_written_in_every_file_the_game_keeps_a_division_in_and_reads_back_as_shown()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        Dictionary<string, string> files = Files();
        files["Generated/Gameplay/Decks/DivisionList.ndf"] = DivisionList;
        files["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"] = CostMatrices;
        files["Generated/Gameplay/Decks/Divisions.ndf"] = files["Generated/Gameplay/Decks/Divisions.ndf"]
            .Replace("    MaxActivationPoints = 50\n    CountryId = \"US\"\n)\n", "    MaxActivationPoints = 50\n    CostMatrix = MatrixCostName_US_Test_Armored_multi\n    CountryId = \"US\"\n)\n");
        GameFileWriters.BaseZip(zip, files);
        List<Division> game = Read(zip);
        Division start = game.Single(division => division.Descriptor == Armored);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        string descriptor = DivisionChanges.AddNew(mod, game, "Task Force Test", start, [Tank, Jeep], GameTitle.SteelDivision2, ("US", "Allied"));
        ModDivision created = mod.Divisions.Single();

        Assert.True(DivisionChangeRules.Writes(GameTitle.SteelDivision2, DivisionDetail.New));
        Assert.True(DivisionChangeRules.Writes(GameTitle.SteelDivision2, DivisionDetail.Generated));
        Assert.True(Units.UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        Dictionary<string, string> after = RunOnEachFile(rules, files);
        List<Division> exported = Read(zip, AsChangedFiles(after));

        // the division, offered by the game, with its units, its own costs and its number for deck codes
        Division made = exported.Single(division => division.Descriptor == descriptor);
        Assert.True(made.IsForMultiplayer);
        Assert.Equal([Tank, Jeep], made.Units.Select(unit => unit.Unit));
        Assert.Equal(created.DeckNumber, made.DeckNumber);
        Assert.Equal("MatrixCostName_Task_Force_Test", made.CostMatrixName);
        Assert.Equal(start.Costs["Tanks"], made.Costs["Tanks"]);
        Assert.Equal(("US", "Allied", 50), (made.Country, made.Side, made.ActivationPoints ?? 0));
        // a copy of its start with GUIDs of its own, its cards its own units' packs only
        string divisions = after["Generated/Gameplay/Decks/Divisions.ndf"];
        string own = divisions[divisions.IndexOf("export " + descriptor, StringComparison.Ordinal)..];
        if (own.IndexOf("export ", 1, StringComparison.Ordinal) is int next and > 0) own = own[..next];
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000001", own, StringComparison.Ordinal);
        Assert.DoesNotContain("_TOE_", own, StringComparison.Ordinal);
        Assert.Contains("(~/" + DivisionChangeRules.OwnPackOf(descriptor, Jeep) + ", ", own, StringComparison.Ordinal);
        Assert.Contains("DivisionName = '" + DivisionChangeRules.TextToken(descriptor, "name") + "'", own, StringComparison.Ordinal);
        Assert.Contains("DivisionDescription = '" + DivisionChangeRules.TextToken(descriptor, "summary") + "'", own, StringComparison.Ordinal);
        Assert.Contains("PackList = MAP", own, StringComparison.Ordinal);
        // its name and description go into MODDING.csv
        Assert.Contains(DivisionExtras.ArmoryTextsOf(rules), text => text.Text == "Task Force Test");
        Assert.Contains(DivisionExtras.ArmoryTextsOf(rules), text => text.Token == DivisionChangeRules.TextToken(descriptor, "summary") && text.Text == "Task Force Test");
        Assert.Contains("~/" + descriptor, after["Generated/Gameplay/Decks/DivisionList.ndf"], StringComparison.Ordinal);
        Assert.Contains("(" + descriptor + ", " + created.DeckNumber + ")", after["Generated/Gameplay/Decks/DeckSerializer.ndf"], StringComparison.Ordinal);
        // the game's divisions stay as they are
        foreach (Division each in game) Assert.Equal(Describe(each), Describe(exported.Single(division => division.Descriptor == each.Descriptor)));
    }

    /// <summary>A DLC division (<see cref="SteelDivisionDivisions.IsInDlc"/>), the only one with the paratroopers.</summary>
    private const string DlcDivision = "Descriptor_Deck_Division_US_101st_Airborne_multi", Para = "Descriptor_Unit_Test_Para_US";

    private static Unit MadeUp(string descriptor, string factory, double cost) => new()
    {
        Descriptor = descriptor, Name = descriptor, Nation = "US", Side = "Allied", Factory = factory, HasDeckNumber = true, Cost = cost,
    };

    /// <summary>The test's US units: the tank and squad of the game's divisions, a jeep none has, and paratroopers only a DLC division has.</summary>
    private static List<Unit> UsUnits() =>
        [MadeUp(Tank, "Tanks", 100), MadeUp(Squad, "Infantry", 30), MadeUp(Jeep, "Recons", 20), MadeUp(Para, "Infantry", 40)];

    /// <summary>The game's divisions read from the files, and the DLC division with the paratroopers.</summary>
    private static List<Division> WithDlcDivision(List<Division> game) =>
    [
        .. game,
        new Division
        {
            Descriptor = DlcDivision, Name = "101st", Country = "US", Side = "Allied", IsForMultiplayer = true,
            Units = [new DivisionUnit { Unit = Para, Cards = 2, UnitsPerCardByPhase = [4, 8, 0], VeterancyMultipliers = [0.0, 1.0, 0.0], AvailableWithoutTransport = true }],
        },
    ];

    [Fact]
    public void A_generated_divisions_units_are_numbered_by_phase_and_leave_out_the_units_only_DLC_divisions_have()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        var rosters = new DivisionRosters(UsUnits(), WithDlcDivision(Read(zip)));
        var limited = new DivisionGeneration { ByPhase = true, Units = GeneratedUnits.Nation, Nation = "US", Side = "Allied", Numbers = GeneratedNumbers.Same };

        IReadOnlyList<DivisionUnit> same = rosters.UnitsFor(limited);
        IReadOnlyList<DivisionUnit> unlimited = rosters.UnitsFor(limited with { Numbers = GeneratedNumbers.Unlimited });
        IReadOnlyList<DivisionUnit> asTheGame = rosters.UnitsFor(limited with { Numbers = GeneratedNumbers.AsTheGame });

        // the paratroopers only a DLC division has are left out; WARNO's way of making divisions leaves no unit out
        Assert.Equal([Jeep, Squad, Tank], same.Select(unit => unit.Unit).Order(StringComparer.Ordinal));
        Assert.Contains(rosters.UnitsFor(limited with { ByPhase = false }), unit => unit.Unit == Para);
        Assert.All(same, unit => Assert.Equal("6,12,18 0,1,0.5 80 none", NumbersText(unit)));
        Assert.All(unlimited, unit => Assert.Equal([999, 999, 999], unit.UnitsPerCardByPhase));
        // each unit's own numbers, by phase, from the game's divisions
        Assert.Equal("2,4,6 0,1,0.5 80 none", NumbersText(asTheGame.Single(unit => unit.Unit == Tank)));
        Assert.Equal("6,12,0 1,0,0 80 none", NumbersText(asTheGame.Single(unit => unit.Unit == Squad)));
        Assert.Equal(3, asTheGame.Single(unit => unit.Unit == Jeep).UnitsPerCardByPhase.Count);
    }

    /// <summary>A unit's numbers as text: "6,12,18 0,1,0.5 80 none", its units by phase, its shares, its cards and its units per card.</summary>
    private static string NumbersText(DivisionUnit unit) =>
        string.Join(",", unit.UnitsPerCardByPhase) + " " + string.Join(",", unit.VeterancyMultipliers.Select(share => share.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        + " " + unit.Cards + " " + (unit.UnitsPerCard?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none");

    [Fact]
    public void A_generated_division_is_written_as_a_new_one_its_units_filled_in_at_export_in_every_file_and_read_back()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        Dictionary<string, string> files = Files();
        files["Generated/Gameplay/Decks/DivisionList.ndf"] = DivisionList;
        files["Generated/Gameplay/Decks/DivisionCostMatrix.ndf"] = CostMatrices;
        const string phases = "    DivisionEconomyList = DivisionEconomy_Russian\n    PhaseList =\n    [\n"
            + "        TPhaseDescriptor\n        (\n            PhaseName = 'PHASE_A'\n            PhaseStartTimeInSeconds = 0\n            PhaseBonus = 0\n            PhaseIncome = 100\n        ),\n"
            + "    ]\n";
        files["Generated/Gameplay/Decks/Divisions.ndf"] = files["Generated/Gameplay/Decks/Divisions.ndf"]
            .Replace("    MaxActivationPoints = 50\n    CountryId = \"US\"\n)\n", phases + "    MaxActivationPoints = 50\n    CostMatrix = MatrixCostName_US_Test_Armored_multi\n    CountryId = \"US\"\n)\n");
        GameFileWriters.BaseZip(zip, files);
        List<Division> game = Read(zip);
        var rosters = new DivisionRosters(UsUnits(), WithDlcDivision(game));
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        var generation = new DivisionGeneration
        {
            ByPhase = true, Units = GeneratedUnits.Nation, Nation = "US", Side = "Allied", Numbers = GeneratedNumbers.Same, Slots = 5,
            Economy = "DivisionEconomy_Russian", AllTransports = false,
        };
        string descriptor = DivisionChanges.AddGenerated(mod, game, "Generated Test", game.Single(division => division.Descriptor == Armored), generation, ("US", "Allied"), GameTitle.SteelDivision2);
        ModDivision created = mod.Divisions.Single();

        // the income choices and their phases, as the game's divisions with them have them; no battlegroup, which the game has none of
        Assert.Equal(("DivisionEconomy_Russian", 1, false), (created.Economy, created.Phases?.Count ?? 0, created.BattlegroupGeneration is not null));
        Assert.Equal(45, created.ActivationPoints);
        Assert.True(Units.UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit);
        // as the export does: the units made from the files, filled into the rules that need them
        Assert.True(GeneratedUnitLists.AnyIn(rules));
        GeneratedUnitLists.Fill(rules, GeneratedUnitLists.UnitsOf(rules, rosters));
        Dictionary<string, string> after = RunOnEachFile(rules, files);
        List<Division> exported = Read(zip, AsChangedFiles(after));

        // its units in its entry, by phase, the DLC-only paratroopers left out; its cards its units' own packs
        Division made = exported.Single(division => division.Descriptor == descriptor);
        Assert.Equal([Jeep, Squad, Tank], made.Units.Select(unit => unit.Unit).Order(StringComparer.Ordinal));
        Assert.All(made.Units, unit => Assert.Equal("6,12,18 0,1,0.5 5 none", NumbersText(unit)));
        Assert.True(made.IsForMultiplayer);
        string divisions = after["Generated/Gameplay/Decks/Divisions.ndf"];
        string own = divisions[divisions.IndexOf("export " + descriptor, StringComparison.Ordinal)..];
        if (own.IndexOf("export ", 1, StringComparison.Ordinal) is int next and > 0) own = own[..next];
        Assert.DoesNotContain("_TOE_", own, StringComparison.Ordinal);
        foreach (string unit in new[] { Tank, Squad, Jeep })
        {
            string pack = DivisionChangeRules.OwnPackOf(descriptor, unit);
            Assert.Contains("(~/" + pack + ", 5)", own, StringComparison.Ordinal);
            Assert.Contains(Parser.Parse(after["Generated/Gameplay/Decks/Packs.ndf"]).Declarations, declaration => declaration.Name == pack);
            Assert.Matches("\\(" + pack + ", 3[6-9][0-9]{4}|\\(" + pack + ", [4-5][0-9]{5}", after["Generated/Gameplay/Decks/DeckSerializer.ndf"]);
        }
        Assert.DoesNotContain(Para, after["Generated/Gameplay/Decks/Packs.ndf"], StringComparison.Ordinal);
        Assert.Contains("~/" + descriptor, after["Generated/Gameplay/Decks/DivisionList.ndf"], StringComparison.Ordinal);
        // the game's divisions stay as they are
        foreach (Division each in game) Assert.Equal(Describe(each), Describe(exported.Single(division => division.Descriptor == each.Descriptor)));
    }

    [Fact]
    public void A_generation_by_phase_is_kept_in_the_mod_file_and_one_of_WARNOs_is_kept_as_it_was()
    {
        var generation = new DivisionGeneration
        {
            ByPhase = true, Units = GeneratedUnits.Side, Side = "Axis", Numbers = GeneratedNumbers.Same,
            PhaseCounts = [3, 5, 7], VeterancyShares = [0.25, 1.0, 0.0], Economy = "DivisionEconomy_German",
        };
        DivisionGeneration read = DivisionGeneration.FromJson(JsonNode.Parse(generation.ToJson().ToJsonString())!.AsObject());

        Assert.True(read.SameAs(generation));
        Assert.Equal([3, 5, 7], read.PhaseCounts);
        Assert.Equal([0.25, 1.0, 0.0], read.VeterancyShares);
        Assert.Equal(("DivisionEconomy_German", true), (read.Economy, read.ByPhase));
        // WARNO's: none of it written, and read back as it was
        JsonObject warno = new DivisionGeneration { Units = GeneratedUnits.Nation, Nation = "US", Side = "NATO" }.ToJson();
        Assert.False(warno.ContainsKey("ByPhase") || warno.ContainsKey("PhaseCounts") || warno.ContainsKey("Economy"));
        Assert.False(DivisionGeneration.FromJson(warno).ByPhase);
    }

    [Fact]
    public void A_divisions_phases_and_income_choices_are_written_in_the_games_form_and_read_back()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        Dictionary<string, string> files = Files();
        const string phases = "    DivisionEconomyList = DivisionEconomy_Russian\n    PhaseList =\n    [\n"
            + "        TPhaseDescriptor\n        (\n            PhaseName = 'PHASE_A'\n            PhaseStartTimeInSeconds = 0\n            PhaseBonus = 0\n            PhaseIncome = 100\n        ),\n"
            + "        TPhaseDescriptor\n        (\n            PhaseName = 'PHASE_B'\n            PhaseStartTimeInSeconds = 600\n            PhaseBonus = 0\n            PhaseIncome = 140\n        ),\n"
            + "    ]\n";
        files["Generated/Gameplay/Decks/Divisions.ndf"] = files["Generated/Gameplay/Decks/Divisions.ndf"].Replace("    MaxActivationPoints = 50\n", phases + "    MaxActivationPoints = 50\n");
        GameFileWriters.BaseZip(zip, files);
        List<Division> game = Read(zip);
        Division armored = game.Single(division => division.Descriptor == Armored);
        Assert.Equal([new DivisionPhase(0, 100, 0), new DivisionPhase(600, 140, 0)], armored.Phases);
        Assert.Equal("DivisionEconomy_Russian", armored.Economy);
        Assert.True(DivisionChangeRules.Has(GameTitle.SteelDivision2, DivisionDetail.Phases));
        Assert.False(DivisionChangeRules.Has(GameTitle.Warno, DivisionDetail.Phases));

        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.ChangeDetails(mod, game, Armored, change =>
        {
            change.Phases = [new DivisionPhase(0, 120, 0), new DivisionPhase(480, 150, 25)];
            change.Economy = "DivisionEconomy_German";
        });
        Assert.True(Units.UnitChangeRules.WritesEverythingOf(mod));
        Dictionary<string, string> after = RunOnEachFile(DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit), files);
        Division exported = Read(zip, AsChangedFiles(after)).Single(division => division.Descriptor == Armored);

        Assert.Equal([new DivisionPhase(0, 120, 0), new DivisionPhase(480, 150, 25)], exported.Phases);
        Assert.Equal("DivisionEconomy_German", exported.Economy);
        Assert.Contains("PhaseName = 'PHASE_B'", after["Generated/Gameplay/Decks/Divisions.ndf"], StringComparison.Ordinal);
        // the other division as it was
        Assert.Equal(game.Single(division => division.Descriptor == Other).Phases, Read(zip, AsChangedFiles(after)).Single(division => division.Descriptor == Other).Phases);
        // kept in the mod file and read back
        Mod read = ModFiles.Read(ModFiles.StoredText(mod), "My mod.mod.json")[0];
        Assert.Equal(mod.Divisions.Single().Phases, read.Divisions.Single().Phases);
        Assert.Equal("DivisionEconomy_German", read.Divisions.Single().Economy);
    }

    [Fact]
    public void Two_mods_changing_different_units_of_one_division_both_take()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        List<Division> game = Read(zip);
        var first = new Mod { Name = "First", Game = GameTitle.SteelDivision2 };
        DivisionUnit tank = game.Single(division => division.Descriptor == Armored).Units.Single(unit => unit.Unit == Tank);
        DivisionChanges.SetRule(first, game, Armored, tank with { Cards = 5 });
        var second = new Mod { Name = "Second", Game = GameTitle.SteelDivision2 };
        DivisionChanges.RemoveUnit(second, game, Armored, Squad);

        // as the export runs mods: the second on what the first gave
        Dictionary<string, string> afterFirst = RunOnEachFile(DivisionChangeRules.For(first, game, new TextCatalog(), unit => unit), Files());
        Dictionary<string, string> afterSecond = RunOnEachFile(DivisionChangeRules.For(second, game, new TextCatalog(), unit => unit), afterFirst);
        Division exported = Read(zip, AsChangedFiles(afterSecond)).Single(division => division.Descriptor == Armored);

        Assert.Equal(Tank + " 5", string.Join(" ", exported.Units.Select(unit => unit.Unit + " " + unit.Cards)));
    }

    [Fact]
    public void A_unit_line_is_written_in_the_games_three_phases_and_levels()
    {
        // a unit given WARNO's single number and four levels, as a unit the game's divisions do not have starts
        var unit = new DivisionUnit { Unit = Jeep, Cards = 1, UnitsPerCard = 2, VeterancyMultipliers = [1.0, 0, 0, 0], AvailableWithoutTransport = true };

        Assert.Equal("TDeckUniteRule ( UnitDescriptor = ~/Descriptor_Unit_Test_Jeep_US AvailableWithoutTransport = True MaxPackNumber = 1 "
            + "NumberOfUnitInPackByPhase = [2, 2, 2] NumberOfUnitInPackXPMultiplier = [1.0, 0.0, 0.0] )", DivisionChangeRules.SteelDivisionUnitLine(unit));
    }

    // ---- the game's own files, where it is installed ----

    /// <summary>The Steel Division 2 mod made with the game's CreateNewMod on the test computer; the test passes without looking where it is not.</summary>
    private const string InstalledDecks = @"E:\Steel Division 2\Mods\Test Mod\GameData\Generated\Gameplay\Decks";

    [Fact]
    public void A_real_divisions_units_change_in_the_games_files_and_the_other_divisions_stay_as_they_are()
    {
        if (!File.Exists(Path.Combine(InstalledDecks, "DivisionRules.ndf"))) return;
        // the game's own files, but for Packs.ndf (77 MB), of which one pack stands in
        var files = new Dictionary<string, string>
        {
            ["Generated/Gameplay/Decks/Divisions.ndf"] = File.ReadAllText(Path.Combine(InstalledDecks, "Divisions.ndf")),
            ["Generated/Gameplay/Decks/DivisionRules.ndf"] = File.ReadAllText(Path.Combine(InstalledDecks, "DivisionRules.ndf")),
            ["Generated/Gameplay/Decks/DeckSerializer.ndf"] = File.ReadAllText(Path.Combine(InstalledDecks, "DeckSerializer.ndf")),
            ["Generated/Gameplay/Decks/Packs.ndf"] = Packs,
        };
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, files);
        List<Division> game = Read(zip);
        // a division of the base game: no unit is added to a DLC one (SteelDivisionDivisions.IsInDlc)
        const string changed = "Descriptor_Deck_Division_SOV_2CCharGd_multi";
        Division before = game.Single(division => division.Descriptor == changed);
        DivisionUnit first = before.Units[0], second = before.Units[1];
        // a unit of another nation's division, which it does not have
        string newcomer = game.SelectMany(division => division.Units).Select(unit => unit.Unit)
            .First(unit => unit.StartsWith("Descriptor_Unit_", StringComparison.Ordinal) && before.Units.All(each => each.Unit != unit));
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        DivisionChanges.RemoveUnit(mod, game, changed, first.Unit);
        DivisionChanges.SetRule(mod, game, changed, second with { Cards = second.Cards + 1 });
        DivisionChanges.AddUnits(mod, game, changed, [newcomer]);

        Dictionary<string, string> after = RunOnEachFile(DivisionChangeRules.For(mod, game, new TextCatalog(), unit => unit), files);
        List<Division> exported = Read(zip, AsChangedFiles(after));

        Division shown = mod.Divisions.Single().Apply(before);
        Assert.Equal(Describe(shown), Describe(exported.Single(division => division.Descriptor == changed)));
        // every other division as the game has it
        Assert.All(game.Where(division => division.Descriptor != changed), division =>
            Assert.Equal(Describe(division), Describe(exported.Single(each => each.Descriptor == division.Descriptor))));
        Assert.DoesNotContain(DivisionChangeRules.GamePackOf(changed, first.Unit) + ",", after["Generated/Gameplay/Decks/Divisions.ndf"], StringComparison.Ordinal);
        Assert.Contains("(~/" + DivisionChangeRules.GamePackOf(changed, second.Unit) + ", " + (second.Cards + 1) + ")", after["Generated/Gameplay/Decks/Divisions.ndf"], StringComparison.Ordinal);
        Assert.Contains(DivisionChangeRules.OwnPackOf(changed, newcomer), after["Generated/Gameplay/Decks/DeckSerializer.ndf"], StringComparison.Ordinal);
    }
    [Fact]
    public void A_defences_pack_is_named_as_the_game_names_it_without_its_Production_prefix()
    {
        const string division = "Descriptor_Deck_Division_BUL_Sofia_Brigade_multi";
        Assert.Equal("Descriptor_Deck_Pack_TOE_BUL_Sofia_Brigade_multi_Barbed_Wire", DivisionChangeRules.GamePackOf(division, "Descriptor_Production_Barbed_Wire"));
        Assert.Equal("Descriptor_Deck_Pack_TOE_BUL_Sofia_Brigade_multi_Panzer_IV_H_BUL", DivisionChangeRules.GamePackOf(division, "Descriptor_Unit_Panzer_IV_H_BUL"));
        Assert.Equal("Descriptor_Deck_Pack_EZ_BUL_Sofia_Brigade_multi_Bunker", DivisionChangeRules.OwnPackOf(division, "Descriptor_Production_Bunker"));
    }
    [Fact]
    public void A_units_number_for_deck_codes_is_written_in_the_games_DeckSerializer_as_it_names_units()
    {
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        mod.UnitDeckNumbers["Descriptor_Unit_Clone_Tank_US"] = 4492;
        ArmoryPresets.AddDeckNumberRules(mod, new TextCatalog(), _ => "CLONE", "Units");

        Dictionary<string, string> after = RunOnEachFile(new JsonArray([.. ArmoryPresets.RulesOf(mod).Select(rule => rule?.DeepClone())]), Files());

        string serializer = after["Generated/Gameplay/Decks/DeckSerializer.ndf"];
        Assert.Contains("(Descriptor_Unit_Clone_Tank_US, 4492)", serializer, StringComparison.Ordinal);
        Assert.DoesNotContain("$/GFX/Unit/", serializer, StringComparison.Ordinal);
        // in the units' list, after the game's own
        Assert.True(serializer.IndexOf("(Descriptor_Unit_Clone_Tank_US", StringComparison.Ordinal) > serializer.IndexOf("(Descriptor_Unit_Test_Tank_US, 0)", StringComparison.Ordinal));
    }
}
