using System.IO;
using EasyNdf.App.Files;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Tests;

/// <summary>
/// The files an export makes generated divisions from are those the Armory reads: of two files of one name, the one in
/// the game's Generated folder. Steel Division 2 has a 60-byte GameData/Gameplay/Gfx/UniteDescriptor.ndf beside its
/// Generated one; taken instead, a generated division got its defences alone where the Armory showed 457 units.
/// </summary>
[Collection(AppModsTests.Name)]
public sealed class GeneratedListsInputsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "EasyNdfInputs" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private const string Units = """
        export Descriptor_Unit_Test_Tank_GER is TEntityDescriptor
        (
            ModulesDescriptors =
            [
                TUnitUIModuleDescriptor ( MotherCountry = 'GER' Nationalite = ENationalite/Axis ),
                TProductionModuleDescriptor ( Factory = EDefaultFactories/Tanks ),
            ]
        )
        export Descriptor_Unit_Test_Squad_GER is TEntityDescriptor
        (
            ModulesDescriptors =
            [
                TUnitUIModuleDescriptor ( MotherCountry = 'GER' Nationalite = ENationalite/Axis ),
                TProductionModuleDescriptor ( Factory = EDefaultFactories/Infantry ),
            ]
        )
        """;

    /// <summary>A file on the Files tab of Steel Division 2, written under the made-up game folder.</summary>
    private FileEntry Listed(string place, string text)
    {
        string path = Path.Combine(folder, "GameData", place, "UniteDescriptor.ndf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return new FileEntry { Name = "UniteDescriptor.ndf", Folder = "GameData/" + place.Replace('\\', '/'), Source = path, Game = GameTitle.SteelDivision2 };
    }

    [Fact]
    public void The_units_an_export_reads_are_those_the_Armory_reads_from_the_Generated_file()
    {
        // the game's stub listed first, and on
        FileEntry stub = Listed(Path.Combine("Gameplay", "Gfx"), "// a stub\n");
        FileEntry generated = Listed(Path.Combine("Generated", "Gameplay", "Gfx"), Units);

        FileEntry read = GeneratedListsMaker.InputFile([stub, generated], GeneratedListsMaker.UnitsFile, GameTitle.SteelDivision2)!;
        DivisionRosters export = DivisionRosters.FromFiles([new ChangedFile(read.Source!, File.ReadAllText(read.Source!))], GameTitle.SteelDivision2);
        DivisionRosters armory = DivisionRosters.FromFiles([new ChangedFile(generated.Source!, Units)], GameTitle.SteelDivision2);

        Assert.Same(generated, read);
        Assert.NotEmpty(armory.AllUnits);
        Assert.Equal(armory.AllUnits.Select(unit => unit.Descriptor), export.AllUnits.Select(unit => unit.Descriptor));
        // and so with the Generated file off: the stub is never read for it
        generated.On = false;
        Assert.Same(generated, GeneratedListsMaker.InputFile([stub, generated], GeneratedListsMaker.UnitsFile, GameTitle.SteelDivision2));
    }
}
