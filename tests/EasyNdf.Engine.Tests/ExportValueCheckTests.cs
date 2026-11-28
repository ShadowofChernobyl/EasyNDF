using System.Diagnostics;
using System.Text;
using EasyNdf.Engine.Editing;
using Xunit.Abstractions;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// Export's value check: the File Editor's check on what the rules wrote, read only in the declarations that differ from
/// the source, with errors only, their declaration and line.
/// </summary>
public class ExportValueCheckTests(ITestOutputHelper output)
{
    private static readonly ValueGuide Guide = ValueGuide.Read(new MemoryStream(Encoding.UTF8.GetBytes("""
        {"types": {"TShot": {
          "Count": {"valueType": "int", "typeCertain": true, "rules": ["Must be at least 1."], "limits": [{"side": "min", "value": 0, "open": true, "confirmed": true}]},
          "Spread": {"valueType": "float", "typeCertain": true, "rules": ["In radians. Must be from 0 to 0.785."], "limits": [{"side": "min", "value": 0, "open": false, "confirmed": true}, {"side": "max", "value": 0.7853985, "open": false, "confirmed": true}]},
          "Lines": {"valueType": "int (0 or more)", "typeCertain": true}
        }}}
        """)));

    private const string Source = "Shot_A is TShot\n(\n    Count = 2\n    Spread = 0.5\n)\nShot_B is TShot\n(\n    Count = 0\n)\n";

    [Fact]
    public void A_wrong_value_the_rules_wrote_is_found_with_its_declaration_and_line()
    {
        string exported = Source.Replace("Spread = 0.5", "Spread = 0.9", StringComparison.Ordinal);

        ExportValueError error = Assert.Single(ExportValueCheck.Find(Source, exported, Guide));

        Assert.Equal("Shot_A", error.Descriptor);
        Assert.Equal(4, error.Line);
        Assert.Equal(("Spread", "0.9", ValueProblemKind.OutsideLimit), (error.Problem.Name, error.Problem.Value, error.Problem.Kind));
    }

    [Fact]
    public void A_declaration_the_rules_left_alone_is_not_read()
    {
        // Shot_B's Count = 0 is wrong in the source already; the rules did not write it
        string exported = Source.Replace("Count = 2", "Count = 3", StringComparison.Ordinal);

        Assert.Empty(ExportValueCheck.Find(Source, exported, Guide));
    }

    [Fact]
    public void A_declaration_the_rules_added_is_read()
    {
        string exported = Source + "Shot_C is TShot\n(\n    Count = 1.5\n)\n";

        ExportValueError error = Assert.Single(ExportValueCheck.Find(Source, exported, Guide));

        Assert.Equal(("Shot_C", 12, ValueProblemKind.WrongKind), (error.Descriptor, error.Line, error.Problem.Kind));
    }

    [Fact]
    public void Warnings_are_left_out_as_mod_generation_works_with_them()
    {
        string exported = Source.Replace("    Spread = 0.5\n", "    Spread = 0.5\n    Lines = -1\n", StringComparison.Ordinal);

        Assert.Empty(ExportValueCheck.Find(Source, exported, Guide));
    }

    [Fact]
    public void A_name_set_twice_gives_the_line_of_its_first_value()
    {
        string exported = Source.Replace("    Spread = 0.5\n", "    Spread = 0.5\n    Count = 4\n", StringComparison.Ordinal);

        ExportValueError error = Assert.Single(ExportValueCheck.Find(Source, exported, Guide));

        Assert.Equal((ValueProblemKind.WrittenTwice, 5, 3), (error.Problem.Kind, error.Line, error.FirstLine));
    }

    [Fact]
    public void Unnamed_declarations_are_matched_in_order()
    {
        const string source = "unnamed TShot\n(\n    Count = 2\n)\nunnamed TShot\n(\n    Count = 3\n)\n";
        string exported = source.Replace("Count = 3", "Count = 0", StringComparison.Ordinal);

        ExportValueError error = Assert.Single(ExportValueCheck.Find(source, exported, Guide));

        Assert.Equal(("", 7), (error.Descriptor, error.Line));
    }

    /// <summary>
    /// The game's largest file with every declaration changed, the most an export can ask of the check: nothing is
    /// marked, as mod generation takes the game's values. Runs only where the game's files and the guide data are (see
    /// <see cref="ValueCheckerRealDataTests"/>).
    /// </summary>
    [Fact]
    public void Every_declaration_of_the_largest_game_file_changed_is_checked_without_a_mark()
    {
        string? gameData = Environment.GetEnvironmentVariable("EASYNDF_GAMEDATA"), guidePath = Environment.GetEnvironmentVariable("EASYNDF_GUIDE");
        if (gameData is null || guidePath is null || !Directory.Exists(gameData) || !File.Exists(guidePath)) return;
        ValueGuide guide;
        using (FileStream json = File.OpenRead(guidePath)) guide = ValueGuide.Read(json);
        string path = Directory.EnumerateFiles(gameData, "Ammunition.ndf", SearchOption.AllDirectories).First();
        string source = File.ReadAllText(path);
        // a space before each closing bracket at the start of a line changes every declaration and no value
        string exported = source.Replace("\n)", "\n )", StringComparison.Ordinal);

        var clock = Stopwatch.StartNew();
        List<ExportValueError> errors = ExportValueCheck.Find(source, exported, guide);
        output.WriteLine($"{Path.GetFileName(path)}, {source.Length / 1024} KB, every declaration changed: {clock.ElapsedMilliseconds} ms");

        Assert.Empty(errors);
    }

    [Fact]
    public void A_declaration_that_does_not_read_is_left_to_the_read_back_check()
    {
        string exported = Source.Replace("Spread = 0.5\n)", "Spread = (0.5\n)", StringComparison.Ordinal);

        Assert.Empty(ExportValueCheck.Find(Source, exported, Guide));
    }

    [Fact]
    public void A_weapon_a_rule_adds_the_older_way_is_held_where_the_file_names_its_mounted_weapons()
    {
        // WARNO v210942's files give each mounted weapon its Name; a rule's text from before leaves it out, and the
        // game refuses it ("Property 'Name' must be defined")
        const string weapons = "W_A is TWeaponManagerModuleDescriptor\n(\n    Turrets = [ TMountedWeaponDescriptor\n    (\n        Name = \"AAAA\"\n    ) ]\n)\n";
        string exported = weapons + "W_B is TWeaponManagerModuleDescriptor\n(\n    Turrets = [ TMountedWeaponDescriptor\n    (\n        NbWeapons = 1\n    ) ]\n)\n";

        ExportValueError error = Assert.Single(ExportValueCheck.Find(weapons, exported, Guide));

        Assert.Equal(("W_B", ValueProblemKind.MountWithoutName), (error.Descriptor, error.Problem.Kind));
        // an older file, whose mounted weapons are all without names, is right as it is
        Assert.Empty(ExportValueCheck.Find(exported.Replace("Name = \"AAAA\"", "NbWeapons = 2", StringComparison.Ordinal),
            exported.Replace("Name = \"AAAA\"", "NbWeapons = 2", StringComparison.Ordinal) + "W_C is TWeaponManagerModuleDescriptor\n(\n    Turrets = [ TMountedWeaponDescriptor\n    (\n    ) ]\n)\n", Guide));
    }

    [Fact]
    public void An_impact_the_game_does_not_have_is_an_error_in_a_declaration_the_rules_changed()
    {
        const string source = "Ammo_A is TAmmunitionDescriptor\n(\n    ImpactHappening = 'MIssileAATOR'\n    Range = 1\n)\n";
        string exported = source.Replace("Range = 1", "Range = 2", StringComparison.Ordinal);

        ExportValueError error = Assert.Single(ExportValueCheck.Find(source, exported, Guide, new HashSet<string> { "MissileAATOR" }));

        Assert.Equal(("Ammo_A", 3, ValueProblemKind.NoSuchImpact), (error.Descriptor, error.Line, error.Problem.Kind));
        Assert.Empty(ExportValueCheck.Find(source, exported, Guide));
    }
}
