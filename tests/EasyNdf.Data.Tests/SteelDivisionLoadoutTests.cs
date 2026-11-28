using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Steel Division 2's loadouts changed in the Unit pane (swap, take off, add, a box's salvos): read with its own names
/// (SalvoStockIndex, its weapons numbered by their effect tags), written as rules in its own forms (ammunition named
/// "~/Ammo_X", a weapon added given the first box its list of 29 leaves unused, with an entry of SalvoIsMainSalvo, the
/// unit pointed at its own loadout in "$/GFX/Everything/"), and read back as the Armory shows them. Its loadouts have no
/// number of a weapon at one place, so none is written. Loadouts laid out as the game's, with made-up names and values;
/// and, where the game is installed, one of its real units.
/// </summary>
public class SteelDivisionLoadoutTests
{
    private const string Tank = "Descriptor_Unit_Test_Tank_US", Loadout = "WeaponDescriptor_Test_Tank_US", OwnLoadout = "WeaponDescriptor_EZ_Test_Tank_US";

    // laid out as the game's WeaponDescriptor.ndf: Salves padded with -1, SalvoIsMainSalvo one longer than the boxes in
    // use, mounts naming their ammunition with ~/, the machine gun's box hidden in the interface (-1)
    private const string Loadouts = """
        export WeaponDescriptor_Test_Tank_US is TWeaponManagerModuleDescriptor
        (
            Salves               = [ 40, 20, 600, -1, -1 ]
            HasMainSalvo = False
            SalvoIsMainSalvo = [ False, False, False, ]
            TurretDescriptorList = [
                TTurretTwoAxisDescriptor
                (
                    MountedWeaponDescriptorList = [
                        TMountedWeaponDescriptor
                        (
                            Ammunition                   = ~/Ammo_Test_Canon_75mm_AP
                            EffectTag                    = 'weapon_effet_tag1'
                            SalvoStockIndex              = 0
                            SalvoStockIndex_ForInterface = 0
                            TirEnMouvement               = True
                            WeaponNumber_ForInterface    = 1
                        ),
                        TMountedWeaponDescriptor
                        (
                            Ammunition                   = ~/Ammo_Test_Canon_75mm_HE
                            EffectTag                    = 'weapon_effet_tag2'
                            SalvoStockIndex              = 1
                            SalvoStockIndex_ForInterface = 1
                            TirEnMouvement               = True
                            WeaponNumber_ForInterface    = 1
                        ),
                    ]
                    NbFX = 1
                    Tag = 'tourelle1'
                ),
                TTurretTwoAxisDescriptor
                (
                    MountedWeaponDescriptorList = [
                        TMountedWeaponDescriptor
                        (
                            Ammunition                   = ~/Ammo_Test_MG_30cal
                            EffectTag                    = 'weapon_effet_tag5'
                            SalvoStockIndex              = 2
                            SalvoStockIndex_ForInterface = -1
                            TirEnMouvement               = False
                            WeaponNumber_ForInterface    = 1
                        ),
                    ]
                    NbFX = 1
                    Tag = 'tourelle2'
                ),
            ]
        )
        """;

    private const string Ammunition = """
        export Ammo_Test_Canon_75mm_AP is TAmmunitionDescriptor
        (
            Name = 'TEST75AP'
            Arme = EArmeType/AP_20
            PorteeMaximale = ((8000) * Metre)
            NbTirParSalves = 1
        )
        export Ammo_Test_Canon_75mm_HE is TAmmunitionDescriptor
        (
            Name = 'TEST75HE'
            Arme = EArmeType/HE
            PorteeMaximale = ((8000) * Metre)
            NbTirParSalves = 1
        )
        export Ammo_Test_MG_30cal is TAmmunitionDescriptor
        (
            Name = 'TESTMG'
            Arme = EArmeType/Balle
            PorteeMaximale = ((4000) * Metre)
            NbTirParSalves = 5
        )
        """;

    // the unit names its loadout from its named WeaponManager module, in the "Everything" namespace
    private const string Units = """
        export Descriptor_Unit_Test_Tank_US is TEntityDescriptor
        (
            DescriptorId      = GUID:{00000000-0000-0000-0000-000000000001}
            Modules           = [
                    TModuleSelector
                    (
                        Default            = TTypeUnitModuleDescriptor
                        (
                            Nationalite                      = ENationalite/Allied
                            MotherCountry                    = 'US'
                        )
                    ),
                WeaponManager is
                    TModuleSelector
                    (
                        Default        = $/GFX/Everything/WeaponDescriptor_Test_Tank_US
                        Selection      = [~/NilDescriptorIfGhostOrCadavre]
                    ),
            ]
        )
        """;

    private static Dictionary<string, string> Files() => new()
    {
        ["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = Units,
        ["Generated/Gameplay/Gfx/WeaponDescriptor.ndf"] = Loadouts,
        ["Generated/Gameplay/Gfx/Ammunition.ndf"] = Ammunition,
    };

    private static UnitCatalog Read(string zip, IReadOnlyList<ChangedFile>? changed = null)
    {
        using var files = new GameDataFiles(zip, null, changed);
        return UnitReader.Read(files, GameTitle.SteelDivision2, GameTexts.FromPairs([]));
    }

    /// <summary>Runs the rules on each file, as the export does, and gives back the files they changed.</summary>
    private static List<ChangedFile> RunOnEachFile(JsonArray rules, Dictionary<string, string> files)
    {
        var changed = new List<ChangedFile>();
        foreach ((string path, string text) in files)
        {
            JsonArray forFile = DivisionChangeRules.ForFile(rules, Path.GetFileName(path));
            if (forFile.Count == 0) continue;
            var engine = new NdfEngine();
            engine.Load(text);
            RunResult result = engine.Run(RuleLoading.LoadForEngine(forFile), new RunOptions());
            Assert.Equal("", result.ParseError);
            Assert.True(result.Stats.All(rule => rule.Skipped == 0), string.Join(" | ", result.Warnings.Select(warning => warning.Message)));
            Assert.All(result.Stats.Where(rule => rule.Kind != "Group"), rule => Assert.True(rule.Edits > 0 || rule.Kind == "Clone", rule.Name + " changed nothing"));
            changed.Add(new ChangedFile("C:/Mod/GameData/" + path, result.Text!));
        }
        return changed;
    }

    private static string WeaponsOf(UnitCatalog catalog, string unit) =>
        string.Join(", ", catalog.Units.Single(each => each.Descriptor == unit).Weapons.Select(weapon => weapon.Ammunition));

    [Fact]
    public void Its_loadouts_are_read_with_their_boxes_and_weapons_numbered_by_their_effect_tags()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());

        Loadout loadout = Read(zip).Loadouts![Loadout];

        Assert.Equal([40, 20, 600, -1, -1], loadout.AmmoBoxes);
        Assert.Equal(3, loadout.MainSalvoCount);
        Assert.Equal([0, 1, 2], loadout.Mounts.Select(mount => mount.AmmoBox));
        Assert.Equal([1, 2, 5], loadout.Mounts.Select(mount => mount.WeaponNumber));
        Assert.Equal(["tourelle1", "tourelle2"], loadout.Turrets.Select(turret => turret.Tag));
        // the weapons added get the places the list leaves unused first
        Assert.Equal([3, 4, 5], LoadoutRules.FreeBoxes(loadout).Take(3));
    }

    [Fact]
    public void A_units_weapon_changes_go_into_a_loadout_of_its_own_in_the_games_own_forms_as_the_Armory_shows_them()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        var edit = new UnitEdit { Descriptor = Tank };
        // the HE round swapped for the machine gun, the AP box given 30, the machine gun taken off its turret, and a
        // machine gun added to the main turret, mounted as the game mounts it elsewhere
        string? gameMount = LoadoutRules.MountTextOf(new StringReader(Loadouts), "Ammo_Test_MG_30cal");
        Assert.NotNull(gameMount);
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Swap, 0, 1, "Ammo_Test_MG_30cal"));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Salvos, 0, 0, Number: 30));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Remove, 1, 0, MountText: LoadoutRules.MountText(new StringReader(Loadouts), Loadout, 1, 0)!));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Add, 0, 2, "Ammo_Test_MG_30cal",
            LoadoutRules.SteelDivisionMountText(gameMount, "Ammo_Test_MG_30cal", "weapon_effet_tag1"), Number: 500));
        mod.UnitEdits.Add(edit);

        Assert.True(UnitChangeRules.Writes(GameTitle.SteelDivision2, UnitField.Weapons));
        Assert.True(UnitChangeRules.WritesEverythingOf(mod));
        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog(), game.Loadouts);
        List<ChangedFile> changed = RunOnEachFile(rules, Files());
        UnitCatalog exported = Read(zip, changed);

        string units = changed.Single(file => file.Path.EndsWith("UniteDescriptor.ndf", StringComparison.Ordinal)).Text;
        string loadouts = changed.Single(file => file.Path.EndsWith("WeaponDescriptor.ndf", StringComparison.Ordinal)).Text;
        Assert.Contains("Default = $/GFX/Everything/" + OwnLoadout, units.Replace("        =", " =", StringComparison.Ordinal), StringComparison.Ordinal);
        // nothing written WARNO's way
        Assert.DoesNotContain("$/GFX/Weapon/", loadouts, StringComparison.Ordinal);
        Assert.DoesNotContain("NbWeapons", loadouts, StringComparison.Ordinal);
        Assert.DoesNotContain("AmmoBoxIndex", loadouts, StringComparison.Ordinal);
        Assert.Equal(2, Parser.Parse(loadouts).Declarations.Count);

        Loadout own = exported.Loadouts![OwnLoadout];
        // the added gun fills the first unused place of the list, with an entry of SalvoIsMainSalvo for it
        Assert.Equal([30, 20, 600, 500, -1], own.AmmoBoxes);
        Assert.Equal(4, own.MainSalvoCount);
        LoadoutMount added = own.Turrets[0].Mounts[2];
        Assert.Equal(("Ammo_Test_MG_30cal", 3, "weapon_effet_tag1"), (added.Ammunition, added.AmmoBox, added.EffectTag));
        Assert.Empty(own.Turrets[1].Mounts);
        // as the game mounts the gun elsewhere: it fires only when stopped, and its box stays hidden in the interface
        Assert.Contains("SalvoStockIndex_ForInterface = -1", loadouts, StringComparison.Ordinal);
        Assert.Matches(@"SalvoStockIndex[ ]*=[ ]*3", loadouts);

        // as the Armory shows it, and the game's loadout as it was
        Unit tank = game.Units.Single(unit => unit.Descriptor == Tank);
        Unit shown = edit.WithLoadoutChanges(edit.Apply(tank), Tank, game.Loadouts, game.Weapons!.ToDictionary(weapon => weapon.Descriptor));
        Assert.Equal(OwnLoadout, exported.Units.Single(unit => unit.Descriptor == Tank).WeaponSet);
        Assert.Equal(string.Join(", ", shown.Weapons.Select(weapon => weapon.Ammunition)), WeaponsOf(exported, Tank));
        Assert.Equal("Ammo_Test_Canon_75mm_AP, Ammo_Test_MG_30cal, Ammo_Test_MG_30cal", WeaponsOf(exported, Tank));
        Assert.Equal(LoadoutRules.Changed(game.Loadouts![Loadout], edit.LoadoutChanges, OwnLoadout).AmmoBoxes, own.AmmoBoxes);
        Assert.Equal(game.Loadouts[Loadout].AmmoBoxes, exported.Loadouts[Loadout].AmmoBoxes);
    }

    [Fact]
    public void A_number_of_a_weapon_at_one_place_is_not_written_as_the_game_has_none()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        var edit = new UnitEdit { Descriptor = Tank };
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Count, 0, 0, Number: 2));
        mod.UnitEdits.Add(edit);

        Assert.False(LoadoutRules.Writes(GameTitle.SteelDivision2, LoadoutChange.Count));
        Assert.True(LoadoutRules.Writes(GameTitle.Warno, LoadoutChange.Count));
        Assert.False(UnitChangeRules.WritesEverythingOf(mod));
        Assert.Empty(LoadoutRules.ActionsFor(game.Loadouts![Loadout], edit.LoadoutChanges, GameTitle.SteelDivision2));
        Assert.Single(LoadoutRules.ActionsFor(game.Loadouts[Loadout], edit.LoadoutChanges));
    }

    [Fact]
    public void A_weapon_no_loadout_has_is_mounted_with_the_games_plain_values()
    {
        string text = LoadoutRules.SteelDivisionMountText(null, "Ammo_New", "weapon_effet_tag3");

        Assert.Contains("Ammunition = ~/Ammo_New", text, StringComparison.Ordinal);
        Assert.Contains("EffectTag = 'weapon_effet_tag3'", text, StringComparison.Ordinal);
        Assert.Contains("SalvoStockIndex = " + LoadoutRules.AmmoBoxBlank, text, StringComparison.Ordinal);
        Assert.Contains("SalvoStockIndex_ForInterface = " + LoadoutRules.AmmoBoxBlank, text, StringComparison.Ordinal);
        Assert.DoesNotContain("NbWeapons", text, StringComparison.Ordinal);
    }

    // ---- the game's own files, where it is installed ----

    /// <summary>The Steel Division 2 mod made with the game's CreateNewMod on the test computer; the test passes without looking where it is not.</summary>
    private const string InstalledGameData = @"E:\Steel Division 2\Mods\Test Mod\GameData";

    [Fact]
    public void A_real_units_loadout_takes_a_swap_and_an_added_weapon_and_still_reads()
    {
        string gfx = Path.Combine(InstalledGameData, "Generated", "Gameplay", "Gfx");
        if (!File.Exists(Path.Combine(gfx, "WeaponDescriptor.ndf"))) return;
        const string sherman = "Descriptor_Unit_M4A1_76_US", shermanLoadout = "WeaponDescriptor_M4A1_76_US";
        string Declaration(string file, string name)
        {
            using var reader = new StreamReader(Path.Combine(gfx, file));
            return UnitReader.DeclarationTexts(reader).First(declaration => declaration.Name == name).Text;
        }
        string unitText = Declaration("UniteDescriptor.ndf", sherman);
        string loadoutText = Declaration("WeaponDescriptor.ndf", shermanLoadout);
        string? gameMount;
        using (var reader = new StreamReader(Path.Combine(gfx, "WeaponDescriptor.ndf")))
            gameMount = LoadoutRules.MountTextOf(reader, "Ammo_M1919_7_62mm_vehicule");
        Assert.NotNull(gameMount);
        // colours on one line, as the game writes them
        string added = LoadoutRules.SteelDivisionMountText(gameMount, "Ammo_M1919_7_62mm_vehicule", "weapon_effet_tag1");
        Assert.Contains("D3DRGBA[0,0,0,0]", added, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"D3DRGBA\[[ \t]*[\r\n]", added);
        var files = new Dictionary<string, string>
        {
            ["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = unitText,
            ["Generated/Gameplay/Gfx/WeaponDescriptor.ndf"] = loadoutText,
            ["Generated/Gameplay/Gfx/Ammunition.ndf"] = File.ReadAllText(Path.Combine(gfx, "Ammunition.ndf")),
        };
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, files);
        UnitCatalog game = Read(zip);
        Loadout before = game.Loadouts![shermanLoadout];
        var mod = new Mod { Name = "My mod", Game = GameTitle.SteelDivision2 };
        var edit = new UnitEdit { Descriptor = sherman };
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Swap, 0, 1, "Ammo_Canon_M1A1_76mmL_AP"));
        edit.LoadoutChanges.Add(new LoadoutChange(LoadoutChange.Add, 0, before.Turrets[0].Mounts.Count, "Ammo_M1919_7_62mm_vehicule",
            LoadoutRules.SteelDivisionMountText(gameMount, "Ammo_M1919_7_62mm_vehicule", before.Turrets[0].Mounts[0].EffectTag), Number: 900));
        mod.UnitEdits.Add(edit);

        UnitCatalog exported = Read(zip, RunOnEachFile(UnitChangeRules.For(mod, game.Units, new TextCatalog(), game.Loadouts), files));

        Loadout own = exported.Loadouts!["WeaponDescriptor_EZ_M4A1_76_US"];
        Loadout expected = LoadoutRules.Changed(before, edit.LoadoutChanges, own.Descriptor);
        Assert.Equal(expected.AmmoBoxes, own.AmmoBoxes);
        Assert.Equal(expected.Mounts.Select(mount => (mount.Ammunition, mount.AmmoBox)), own.Mounts.Select(mount => (mount.Ammunition, mount.AmmoBox)));
        // the list keeps its 29 places, and SalvoIsMainSalvo has an entry for the new box
        Assert.Equal(before.AmmoBoxes.Count, own.AmmoBoxes.Count);
        Assert.True(own.MainSalvoCount > own.Mounts.Max(mount => mount.AmmoBox));
        Assert.Equal("WeaponDescriptor_EZ_M4A1_76_US", exported.Units.Single().WeaponSet);
    }
}
