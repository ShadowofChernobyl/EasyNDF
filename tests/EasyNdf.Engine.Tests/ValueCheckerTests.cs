using System.Text;
using EasyNdf.Engine.Editing;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The File Editor's value check and the place finder behind its tips and suggestions, on a made-up guide: values of the
/// wrong kind, choices a complete list doesn't have, numbers outside confirmed limits, names set twice, and negative
/// numbers in a whole number of 0 or more (a warning).
/// </summary>
public class ValueCheckerTests
{
    private static readonly ValueGuide Guide = ValueGuide.Read(new MemoryStream(Encoding.UTF8.GetBytes("""
        {"types": {"TShot": {
          "Count": {"valueType": "int", "typeCertain": true, "rules": ["Must be at least 1."], "limits": [{"side": "min", "value": 0, "open": true, "confirmed": true}]},
          "Spread": {"valueType": "float", "typeCertain": true, "rules": ["In radians. Must be from 0 to 0.785."], "limits": [{"side": "min", "value": 0, "open": false, "confirmed": true}, {"side": "max", "value": 0.7853985, "open": false, "confirmed": true}]},
          "Guess": {"valueType": "float", "typeCertain": true, "limits": [{"side": "min", "value": 0, "open": false, "confirmed": false}]},
          "Indirect": {"valueType": "boolean", "typeCertain": true, "choices": ["True", "False"], "choicesComplete": true},
          "Kind": {"valueType": "enum", "typeCertain": true, "choices": ["EKind/Shell", "EKind/Bomb"], "choicesComplete": true},
          "Cursor": {"valueType": "enum", "typeCertain": true, "choices": ["Cursor_Gun"]},
          "Lines": {"valueType": "int (0 or more)", "typeCertain": true},
          "Unsure": {"valueType": "int", "typeCertain": false},
          "MinRange": {"valueType": "float", "typeCertain": true, "comparisons": [{"op": "<", "other": "MaxRange", "unlessOtherIs": 0}]},
          "MaxRange": {"valueType": "float", "typeCertain": true},
          "MinPitch": {"valueType": "float", "typeCertain": true, "comparisons": [{"op": "<", "other": "MaxPitch", "unlessSet": "Master"}]},
          "MaxPitch": {"valueType": "float", "typeCertain": true}
        },
        "TLobby": {
          "JoinSound": {"valueType": "reference to TSoundDescriptor", "typeCertain": true, "rules": ["Must be set."], "required": true},
          "LeaveSound": {"valueType": "reference to TSoundDescriptor", "typeCertain": true, "rules": ["Must be set."]}
        },
        "TIcon": {
          "HasBackground": {"valueType": "boolean", "typeCertain": true, "choices": ["True", "False"], "choicesComplete": true},
          "UniformDrawer": {"valueType": "reference to TUIUniformDrawer", "typeCertain": true, "requiredWhen": "HasBackground"}
        }}}
        """)));

    private static List<ValueProblem> Check(string body) => ValueChecker.Check("Shot_A is TShot\n(\n" + body + ")\n", Guide);

    [Fact]
    public void A_bare_word_no_file_declares_is_a_warning_in_a_number_and_a_boolean()
    {
        string text = "Known is 5\nShot_A is TShot\n(\n    Count = abc\n    Spread = Known\n    Indirect = maybe\n    Guess = ~/Other\n)\n";
        List<ValueProblem> problems = ValueChecker.Check(text, Guide, isDeclared: word => word == "Known");
        Assert.Equal(["abc", "maybe"], problems.Select(problem => problem.Value));
        Assert.All(problems, problem => Assert.True(problem.IsWarning && problem.Kind == ValueProblemKind.UnknownName));
        // without names to go by, no word is marked
        Assert.Empty(ValueChecker.Check(text, Guide));
    }

    private const string Serializer = """
        unnamed TDeckSerializerEntries
        (
            DivisionIds = MAP
            [
                (Descriptor_Deck_Division_A_multi, 390),
                (Descriptor_Deck_Division_B_multi, 391),
                (Descriptor_Deck_Division_New_multi, 391),
            ]
            UnitIds = MAP
            [
                (Descriptor_Unit_A, 391),
                (Descriptor_Unit_B, 7),
            ]
        )

        """;

    [Fact]
    public void A_deck_number_given_to_two_divisions_is_marked_with_both_names()
    {
        ValueProblem problem = Assert.Single(ValueChecker.Check(Serializer, Guide));

        Assert.Equal(ValueProblemKind.NumberGivenTwice, problem.Kind);
        Assert.Equal(("DivisionIds", "391", "Descriptor_Deck_Division_B_multi", "Descriptor_Deck_Division_New_multi"),
            (problem.Name, problem.Value, problem.OtherValue, problem.Key));
        Assert.False(problem.IsWarning);
        // marked on the second 391 of the divisions; the units' 391 is another MAP's, and no conflict
        Assert.Equal(Serializer.IndexOf("New_multi, 391", StringComparison.Ordinal) + "New_multi, ".Length, problem.Position);
    }

    [Fact]
    public void A_deck_number_given_to_two_units_is_marked_too_and_other_types_are_left_alone()
    {
        string units = Serializer.Replace("(Descriptor_Unit_B, 7)", "(Descriptor_Unit_B, 7), (Descriptor_Unit_C, 7)").Replace("New_multi, 391", "New_multi, 392");
        ValueProblem problem = Assert.Single(ValueChecker.Check(units, Guide));
        Assert.Equal(("UnitIds", "7", "Descriptor_Unit_B", "Descriptor_Unit_C"), (problem.Name, problem.Value, problem.OtherValue, problem.Key));

        // the same numbers in another type's MAP are no deck numbers
        Assert.Empty(ValueChecker.Check(Serializer.Replace("TDeckSerializerEntries", "TOtherTable"), Guide));
    }

    [Fact]
    public void Right_values_and_unknown_ones_are_not_marked()
    {
        List<ValueProblem> problems = Check("    Count = 2\n    Spread = 0.5\n    Indirect = True\n    Kind = ~/EKind/Bomb\n    Cursor = Cursor_Other\n"
            + "    Guess = -3\n    Unsure = 1.5\n    Count2 = 'x'\n    Spread2 = ~/Constant\n");

        Assert.Empty(problems);
    }

    [Fact]
    public void Values_of_the_wrong_kind_are_errors()
    {
        List<ValueProblem> problems = Check("    Count = 1.5\n    Spread = 'twelve'\n    Indirect = 1\n");

        Assert.Equal(3, problems.Count);
        Assert.All(problems, problem => Assert.Equal(ValueProblemKind.WrongKind, problem.Kind));
        Assert.Equal(("int", "float"), (problems[0].Expected, problems[0].Found));
        Assert.Equal(("float", "string"), (problems[1].Expected, problems[1].Found));
        Assert.Equal(("boolean", "int"), (problems[2].Expected, problems[2].Found));
        Assert.False(problems[0].IsWarning);
    }

    [Fact]
    public void An_int_in_a_float_is_right()
    {
        Assert.Empty(Check("    Spread = 0\n"));
    }

    [Fact]
    public void A_choice_outside_a_complete_list_is_an_error_however_it_is_written()
    {
        List<ValueProblem> problems = Check("    Kind = EKind/NotAKind\n");

        ValueProblem problem = Assert.Single(problems);
        Assert.Equal(ValueProblemKind.NoSuchChoice, problem.Kind);
        Assert.Equal("EKind/NotAKind", problem.Value);
        Assert.Empty(Check("    Kind = $/Folder/EKind/Shell\n"));
    }

    [Fact]
    public void Only_confirmed_limits_mark_a_number_and_rounded_limits_keep_their_exact_value_unmarked()
    {
        Assert.Equal(ValueProblemKind.OutsideLimit, Assert.Single(Check("    Spread = 1.2\n")).Kind);
        Assert.Equal(ValueProblemKind.OutsideLimit, Assert.Single(Check("    Count = 0\n")).Kind);
        Assert.Empty(Check("    Spread = 0.78539816\n"));
        Assert.Equal(["In radians. Must be from 0 to 0.785."], Assert.Single(Check("    Spread = -1\n")).Rules);
    }

    [Fact]
    public void A_name_set_twice_marks_the_second_value_in_any_object()
    {
        string body = "    Count = 2\n    Lines = 1\n    Count = 3\n    Inner = TOther\n    (\n        X = 1\n        X = 2\n    )\n";
        string text = "Shot_A is TShot\n(\n" + body + ")\n";

        List<ValueProblem> problems = ValueChecker.Check(text, Guide);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.Equal(ValueProblemKind.WrittenTwice, problem.Kind));
        Assert.Equal(text.IndexOf("3\n", StringComparison.Ordinal), problems[0].Position);
        Assert.Equal(text.IndexOf("2\n    Lines", StringComparison.Ordinal), problems[0].FirstPosition);
        Assert.Equal("X", problems[1].Name);
    }

    [Fact]
    public void A_negative_whole_number_of_0_or_more_is_a_warning()
    {
        ValueProblem problem = Assert.Single(Check("    Lines = -1\n"));

        Assert.Equal(ValueProblemKind.NegativeInZeroOrMore, problem.Kind);
        Assert.True(problem.IsWarning);
        Assert.Empty(Check("    Lines = 4\n"));
    }

    [Fact]
    public void A_number_on_the_wrong_side_of_another_value_of_its_object_is_an_error()
    {
        ValueProblem problem = Assert.Single(Check("    MinRange = 3000\n    MaxRange = 1925\n"));

        Assert.Equal(ValueProblemKind.BrokenComparison, problem.Kind);
        Assert.Equal(("MinRange", "3000", "MaxRange", "1925"), (problem.Name, problem.Value, problem.Comparison!.Other, problem.OtherValue));
        Assert.Empty(Check("    MinRange = 35\n    MaxRange = 1925\n"));
        Assert.Empty(Check("    MinRange = 3000\n    MaxRange = ~/Constant\n"));
        Assert.Empty(Check("    MinRange = 3000\n"));
    }

    [Fact]
    public void A_comparison_that_holds_only_in_some_cases_is_not_marked_where_it_does_not_apply()
    {
        Assert.Empty(Check("    MinRange = 0\n    MaxRange = 0\n"));
        Assert.Single(Check("    MinPitch = 1\n    MaxPitch = 0\n"));
        Assert.Empty(Check("    MinPitch = 1\n    MaxPitch = 0\n    Master = 1\n"));
    }

    [Fact]
    public void A_declaration_that_does_not_read_is_left_to_the_syntax_check_and_the_others_are_still_checked()
    {
        const string text = "Shot_A is TShot\n(\n    Count = = 2\n)\nShot_B is TShot\n(\n    Count = 1.5\n)\n";

        ValueProblem problem = Assert.Single(ValueChecker.Check(text, Guide));

        Assert.Equal(text.IndexOf("1.5", StringComparison.Ordinal), problem.Position);
    }

    [Fact]
    public void One_value_is_judged_as_the_check_would_judge_it()
    {
        Assert.Null(ValueChecker.ProblemWith("TShot", "Kind", "EKind/Shell", Guide));
        Assert.NotNull(ValueChecker.ProblemWith("TShot", "Kind", "EKind/Nope", Guide));
        Assert.NotNull(ValueChecker.ProblemWith("TShot", "Indirect", "1", Guide));
        Assert.Null(ValueChecker.ProblemWith("TOther", "Indirect", "1", Guide));
    }

    // ---- the place finder ----

    private const string Entry = "Shot_A is TShot\n(\n    Count = 2\n    Kind = EKind/Shell // a comment\n    Damage = TDamage(Family=DamageFamily_ap Index=24)\n    Sp\n    Spread = \n)\n";

    [Fact]
    public void A_name_is_found_with_its_object_type_and_its_value_on_the_line()
    {
        NdfPlace place = NdfPlace.At(Entry, Entry.IndexOf("Kind", StringComparison.Ordinal) + 1)!;

        Assert.Equal("TShot", place.ObjectType);
        Assert.Equal("Kind", place.Name);
        Assert.False(place.IsInValue);
        Assert.Equal("EKind/Shell", place.ValueText);
        Assert.Equal(["Count", "Kind", "Damage", "Spread"], place.NamesSet.Keys);
    }

    [Fact]
    public void A_value_is_found_with_the_name_it_is_given_to()
    {
        NdfPlace place = NdfPlace.At(Entry, Entry.IndexOf("Shell", StringComparison.Ordinal))!;

        Assert.True(place.IsInValue);
        Assert.Equal("Kind", place.Name);
        Assert.Equal(Entry.IndexOf("EKind/Shell", StringComparison.Ordinal), place.WordStart);
    }

    [Fact]
    public void A_name_inside_an_object_written_on_one_line_belongs_to_that_object()
    {
        NdfPlace place = NdfPlace.At(Entry, Entry.IndexOf("Index", StringComparison.Ordinal) + 2)!;

        Assert.Equal("TDamage", place.ObjectType);
        Assert.Equal("Index", place.Name);
        Assert.Equal("24", place.ValueText);
        Assert.Equal(["Family", "Index"], place.NamesSet.Keys);
    }

    [Fact]
    public void A_word_being_typed_where_a_name_goes_and_a_value_about_to_be_typed_are_told_apart()
    {
        int afterSp = Entry.IndexOf("Sp\n", StringComparison.Ordinal) + 2;
        NdfPlace typing = NdfPlace.At(Entry, afterSp)!;
        Assert.False(typing.IsInValue);
        Assert.Equal("Sp", typing.Name);
        Assert.Equal(afterSp - 2, typing.WordStart);

        int afterEquals = Entry.IndexOf("Spread = ", StringComparison.Ordinal) + "Spread = ".Length;
        NdfPlace value = NdfPlace.At(Entry, afterEquals)!;
        Assert.True(value.IsInValue);
        Assert.Equal("Spread", value.Name);
        Assert.Equal(afterEquals, value.WordStart);
    }

    [Fact]
    public void A_place_outside_any_object_or_in_a_list_has_none()
    {
        const string text = "Shot_A is TShot\n(\n    Items = [ (EKind/Shell, 0) ]\n)\n";

        Assert.Null(NdfPlace.At(text, 2));
        Assert.Null(NdfPlace.At(text, text.IndexOf("0)", StringComparison.Ordinal)));
    }

    // ---- WARNO v210942's checks of arrays ----

    private static readonly ValueGuide ArrayGuide = ValueGuide.Read(new MemoryStream(Encoding.UTF8.GetBytes("""
        {"types": {
          "TWeaponManagerModuleDescriptor": {"Salves": {"valueType": "array", "typeCertain": true}},
          "TDeckUniteRule": {"NumberOfUnitInPackXPMultiplier": {"valueType": "array", "typeCertain": true}, "NumberOfUnitInPack": {"valueType": "int", "typeCertain": true}}
        }}
        """)));

    [Fact]
    public void A_weapon_set_needs_an_ammo_box_and_each_box_salvos_above_0()
    {
        string text = "W_A is TWeaponManagerModuleDescriptor\n(\n    Salves = [4, 0, -1]\n)\nW_B is TWeaponManagerModuleDescriptor\n(\n    Salves = []\n)\n"
            + "W_C is TWeaponManagerModuleDescriptor\n(\n    Salves = [1, 2]\n)\n";

        List<ValueProblem> problems = ValueChecker.Check(text, ArrayGuide);

        Assert.Equal([ValueProblemKind.EmptyAmmoBox, ValueProblemKind.EmptyAmmoBox, ValueProblemKind.NoAmmoBox], problems.Select(problem => problem.Kind));
        Assert.Equal([1, 2], problems.Take(2).Select(problem => problem.Index));
        Assert.Equal("0", text.Substring(problems[0].Position, problems[0].Length));
        Assert.All(problems, problem => Assert.False(problem.IsWarning));
    }

    [Fact]
    public void A_veterancy_level_offered_must_give_the_card_a_unit()
    {
        // 2 x 0.2 = 0.4 rounds to 0; 2 x 0.25 = 0.5 rounds to 1 (halves away from 0); 0 leaves the level out
        string text = "R is TDeckUniteRule\n(\n    NumberOfUnitInPack = 2\n    NumberOfUnitInPackXPMultiplier = [1, 0.2, 0.25, 0]\n)\n";

        ValueProblem problem = Assert.Single(ValueChecker.Check(text, ArrayGuide));

        Assert.Equal(ValueProblemKind.EmptyPack, problem.Kind);
        Assert.Equal((1, "0.2", "2"), (problem.Index, problem.Value, problem.OtherValue));
        Assert.Equal("0.2", text.Substring(problem.Position, problem.Length));
    }

    [Fact]
    public void The_array_checks_are_left_out_for_types_the_guide_does_not_know()
    {
        Assert.Empty(ValueChecker.Check("W is TOtherManager\n(\n    Salves = [0]\n)\n", ArrayGuide));
        Assert.Empty(ValueChecker.Check("W is TWeaponManagerModuleDescriptor\n(\n    Salves = [0]\n)\n", Guide));
    }

    // ---- WARNO v210942's names on mounted weapons ----

    private const string NamedMount = "W_A is TWeaponManagerModuleDescriptor\n(\n    Turrets = [ TMountedWeaponDescriptor\n    (\n        Ammunition = $/GFX/Weapon/Ammo_A\n        Name = \"AAAA\"\n    ) ]\n)\n";

    private const string OlderMount = "W_B is TWeaponManagerModuleDescriptor\n(\n    Turrets = [ TMountedWeaponDescriptor\n    (\n        Ammunition = $/GFX/Weapon/Ammo_B\n        NbWeapons = 1\n    ) ]\n)\n";

    [Fact]
    public void A_mounted_weapon_without_a_name_is_marked_where_the_file_names_its_mounted_weapons()
    {
        string text = NamedMount + OlderMount;

        ValueProblem problem = Assert.Single(ValueChecker.Check(text, Guide));

        Assert.Equal(ValueProblemKind.MountWithoutName, problem.Kind);
        Assert.Equal("TMountedWeaponDescriptor", text.Substring(problem.Position, problem.Length));
        Assert.True(problem.Position > text.IndexOf("W_B", StringComparison.Ordinal));
        Assert.False(problem.IsWarning);
    }

    [Fact]
    public void An_older_file_whose_mounted_weapons_have_no_names_is_not_marked()
    {
        Assert.False(ValueChecker.NamesMountedWeapons(OlderMount + OlderMount.Replace("W_B", "W_C", StringComparison.Ordinal)));
        Assert.Empty(ValueChecker.Check(OlderMount + OlderMount.Replace("W_B", "W_C", StringComparison.Ordinal), Guide));
        Assert.True(ValueChecker.NamesMountedWeapons(NamedMount));
    }

    private const string Ammo = "Ammo_A is TAmmunitionDescriptor\n(\n    ImpactHappening = 'MIssileAASIDEWINDER'\n)\n"
        + "Ammo_B is TAmmunitionDescriptor\n(\n    ImpactHappening = 'MissileAASIDEWINDER'\n)\n";

    private static readonly HashSet<string> Impacts = ["MissileAASIDEWINDER", "Shell"];

    [Fact]
    public void An_impact_the_mapping_does_not_have_is_marked_letter_case_and_all()
    {
        ValueProblem problem = Assert.Single(ValueChecker.Check(Ammo, Guide, impacts: Impacts));

        Assert.Equal(ValueProblemKind.NoSuchImpact, problem.Kind);
        Assert.Equal("'MIssileAASIDEWINDER'", Ammo.Substring(problem.Position, problem.Length));
        Assert.True(problem.CrashesGame);
        Assert.False(problem.IsWarning);
    }

    [Fact]
    public void Impacts_are_checked_only_with_the_mapping_and_only_in_ammunition()
    {
        Assert.Empty(ValueChecker.Check(Ammo, Guide));
        Assert.Empty(ValueChecker.Check(Ammo.Replace("TAmmunitionDescriptor", "TOther", StringComparison.Ordinal), Guide, impacts: Impacts));
        Assert.Empty(ValueChecker.Check(Ammo.Replace("'MIssileAASIDEWINDER'", "~/Impact", StringComparison.Ordinal), Guide, impacts: Impacts));
    }

    [Fact]
    public void A_required_value_left_out_is_marked_on_the_type_name_and_one_set_to_nil_on_the_nil()
    {
        const string left = "Lobby is TLobby\n(\n    LeaveSound = ~/Leave\n)\n";
        const string nil = "Lobby is TLobby\n(\n    JoinSound = nil\n)\n";

        ValueProblem leftOut = Assert.Single(ValueChecker.Check(left, Guide));
        ValueProblem setToNil = Assert.Single(ValueChecker.Check(nil, Guide));

        Assert.Equal((ValueProblemKind.MissingValue, "JoinSound", "TLobby"), (leftOut.Kind, leftOut.Name, left.Substring(leftOut.Position, leftOut.Length)));
        Assert.Equal((ValueProblemKind.MissingValue, "nil"), (setToNil.Kind, nil.Substring(setToNil.Position, setToNil.Length)));
        Assert.False(leftOut.IsWarning);
        // a check no test confirmed ("Must be set." in words only) marks nothing
        Assert.Empty(ValueChecker.Check("Lobby is TLobby\n(\n    JoinSound = ~/Join\n)\n", Guide));
    }

    [Fact]
    public void A_value_required_while_another_is_true_is_checked_only_then()
    {
        ValueProblem problem = Assert.Single(ValueChecker.Check("Icon is TIcon\n(\n    HasBackground = True\n)\n", Guide));

        Assert.Equal((ValueProblemKind.MissingValue, "UniformDrawer"), (problem.Kind, problem.Name));
        Assert.Empty(ValueChecker.Check("Icon is TIcon\n(\n    HasBackground = False\n)\n", Guide));
        Assert.Empty(ValueChecker.Check("Icon is TIcon\n(\n)\n", Guide));
        Assert.Empty(ValueChecker.Check("Icon is TIcon\n(\n    HasBackground = True\n    UniformDrawer = $/UserInterface/UIUniformDrawer\n)\n", Guide));
    }
}
