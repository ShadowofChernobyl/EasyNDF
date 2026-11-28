using System.Text.Json.Nodes;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Rules;
using EasyNdf.Data.Divisions;

namespace EasyNdf.Data.Tests;

/// <summary>
/// WARNO's planes keep 0 under EOpticalStrength/Standard and their optics under LowAltitude and HighAltitude: the
/// Armory shows a plane's LowAltitude, and a change goes to both altitudes. Helicopters, which fly too but are no
/// planes, keep Standard.
/// </summary>
public class PlaneOpticsTests
{
    private const string Plane = "Descriptor_Unit_Jet_US", Helicopter = "Descriptor_Unit_Heli_US";

    private const string Units = """
        export Descriptor_Unit_Jet_US is TEntityDescriptor
        (
            ModulesDescriptors = [
                TTypeUnitModuleDescriptor( Coalition = TWargameCoalition/NATO MotherCountry = 'US' ),
                TAirplaneModuleDescriptor( EvacuationTime = 5 ),
                TScannerConfigurationDescriptor
                (
                    OpticalStrengths = MAP [
                        ( EOpticalStrength/Standard, 0.0 ),
                        ( EOpticalStrength/LowAltitude, 5250.0 ),
                        ( EOpticalStrength/HighAltitude, 5250.0 ),
                    ]
                ),
                TProductionModuleDescriptor( FactoryType = Factory/Planes ),
            ]
        )
        export Descriptor_Unit_Heli_US is TEntityDescriptor
        (
            ModulesDescriptors = [
                TTypeUnitModuleDescriptor( Coalition = TWargameCoalition/NATO MotherCountry = 'US' ),
                TScannerConfigurationDescriptor
                (
                    OpticalStrengths = MAP [
                        ( EOpticalStrength/Standard, 3180.0 ),
                        ( EOpticalStrength/LowAltitude, 3180.0 ),
                        ( EOpticalStrength/HighAltitude, 1413.0 ),
                    ]
                ),
                TProductionModuleDescriptor( FactoryType = Factory/Helis ),
            ]
        )
        """;

    private static Dictionary<string, string> Files()
    {
        Dictionary<string, string> files = UnitReaderTests.WarnoFiles();
        files["Generated/Gameplay/Gfx/UniteDescriptor.ndf"] = Units;
        return files;
    }

    private static UnitCatalog Read(string zip, IReadOnlyList<ChangedFile>? changed = null)
    {
        using var files = new GameDataFiles(zip, null, changed);
        return UnitReader.Read(files, GameTitle.Warno, UnitReaderTests.WarnoTexts);
    }

    [Fact]
    public void A_planes_optics_are_its_low_altitude_ones_and_a_helicopter_keeps_standard()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);

        Unit plane = game.Units.Single(unit => unit.Descriptor == Plane), helicopter = game.Units.Single(unit => unit.Descriptor == Helicopter);
        Assert.Equal((5250.0, true), (plane.Optics, plane.OpticsInFlight));
        Assert.Equal((3180.0, false), (helicopter.Optics, helicopter.OpticsInFlight));
    }

    [Fact]
    public void A_change_to_a_planes_optics_goes_to_both_altitudes_and_standard_stays_0()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, Files());
        UnitCatalog game = Read(zip);
        var mod = new Mod { Name = "My mod" };
        mod.UnitEdits.Add(new UnitEdit { Descriptor = Plane, Optics = 6000 });
        mod.UnitEdits.Add(new UnitEdit { Descriptor = Helicopter, Optics = 4000 });

        JsonArray rules = UnitChangeRules.For(mod, game.Units, new TextCatalog());
        var engine = new NdfEngine();
        engine.Load(Units);
        RunResult result = engine.Run(RuleLoading.LoadForEngine(DivisionChangeRules.ForFile(rules, "UniteDescriptor.ndf")), new RunOptions { WantText = true });
        string text = result.Text!;
        string jet = text[..text.IndexOf("export " + Helicopter, StringComparison.Ordinal)], heli = text[text.IndexOf("export " + Helicopter, StringComparison.Ordinal)..];

        Assert.Contains("( EOpticalStrength/Standard, 0.0 )", jet);
        Assert.Contains("( EOpticalStrength/LowAltitude, 6000.0 )", jet);
        Assert.Contains("( EOpticalStrength/HighAltitude, 6000.0 )", jet);
        Assert.Contains("( EOpticalStrength/Standard, 4000.0 )", heli);
        Assert.Contains("( EOpticalStrength/HighAltitude, 1413.0 )", heli);
    }
}
