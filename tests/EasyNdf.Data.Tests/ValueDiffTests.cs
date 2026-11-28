using EasyNdf.Data.FileList;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

public class ValueDiffTests
{
    private static string Text(IEnumerable<ValuePiece> pieces) => string.Concat(pieces.Select(piece => piece.Text));

    private static string[] Changed(IEnumerable<ValuePiece> pieces) =>
        [.. pieces.Where(piece => piece.Kind == ValuePieceKind.Changed).Select(piece => piece.Text.Trim())];

    [Fact]
    public void A_short_value_shows_whole_with_its_changed_word_marked()
    {
        ValueChange change = ValueDiff.Of("'Unit_AIFV_B_C25_NL'", "'Unit_YC_Illegal'");

        Assert.Equal(ValueChangeShape.Changed, change.Shape);
        Assert.Equal(["Unit_AIFV_B_C25_NL"], Changed(change.Before));
        Assert.Equal(["Unit_YC_Illegal"], Changed(change.After));
        Assert.Equal("'Unit_AIFV_B_C25_NL'", Text(change.Before));
    }

    [Fact]
    public void A_number_changes_as_a_whole_not_digit_by_digit()
    {
        ValueChange change = ValueDiff.Of("2100.5", "2200.5");

        Assert.Equal(["2100.5"], Changed(change.Before));
        Assert.Equal(["2200.5"], Changed(change.After));
    }

    [Fact]
    public void Spacing_is_not_a_change()
    {
        ValueChange change = ValueDiff.Of("TResistanceTypeRTTI\n(\n    Family = ResistanceFamily_infanterie\n)", "TResistanceTypeRTTI(Family=ResistanceFamily_infanterie)");

        Assert.Equal(ValueChangeShape.SpacingOnly, change.Shape);
        Assert.Empty(Changed(change.After));
    }

    [Fact]
    public void Only_the_numbers_of_a_list_written_differently_change()
    {
        ValueChange change = ValueDiff.Of("MAP[\n    (EVisionRange/Standard, 3500.0),\n    (EVisionRange/LowAltitude, 4947.0),\n]",
            "MAP [\n    (EVisionRange/Standard, 999999.0),\n    (EVisionRange/LowAltitude, 999999.0),\n]");

        Assert.Equal(["3500.0", "4947.0"], Changed(change.Before));
        Assert.Equal(["999999.0", "999999.0"], Changed(change.After));
    }

    [Fact]
    public void An_entry_added_to_a_list_reads_from_its_first_word_to_its_comma()
    {
        ValueChange change = ValueDiff.Of("[TAlpha, TBeta, TGamma]", "[TAlpha, TRout(MoralLevel=9), TBeta, TGamma]");

        Assert.Equal(ValueChangeShape.Added, change.Shape);
        Assert.Empty(change.Before);
        Assert.Equal(["TRout(MoralLevel=9),"], Changed(change.After));
    }

    [Fact]
    public void An_entry_taken_from_a_list_is_a_removal()
    {
        ValueChange change = ValueDiff.Of("[TAlpha, TBeta, TGamma]", "[TAlpha, TGamma]");

        Assert.Equal(ValueChangeShape.Removed, change.Shape);
        Assert.Empty(change.After);
        Assert.Equal(["TBeta,"], Changed(change.Before));
    }

    [Fact]
    public void A_value_where_there_was_none_is_all_added()
    {
        ValueChange change = ValueDiff.Of("", "True");

        Assert.Equal(ValueChangeShape.Added, change.Shape);
        Assert.Equal(["True"], Changed(change.After));
    }

    [Fact]
    public void A_long_value_leaves_out_what_stayed_the_same_far_from_the_change()
    {
        string start = string.Concat(Enumerable.Range(0, 200).Select(i => "TModule" + i + ","));
        string end = string.Concat(Enumerable.Range(200, 200).Select(i => "TModule" + i + ","));

        ValueChange change = ValueDiff.Of(start + end, start + "TRout," + end);

        Assert.Equal(ValueChangeShape.Added, change.Shape);
        Assert.Equal(["TRout,"], Changed(change.After));
        Assert.Equal(ValuePieceKind.LeftOut, change.After[0].Kind);
        Assert.Equal(ValuePieceKind.LeftOut, change.After[^1].Kind);
        Assert.True(Text(change.After).Length < 120, Text(change.After));
    }

    [Fact]
    public void Two_changes_far_apart_keep_their_own_words_with_a_mark_between()
    {
        string middle = string.Concat(Enumerable.Range(0, 100).Select(i => "TModule" + i + ","));

        ValueChange change = ValueDiff.Of("Alpha," + middle + "Omega", "Beta," + middle + "Psi");

        Assert.Equal(["Alpha", "Omega"], Changed(change.Before));
        Assert.Equal(["Beta", "Psi"], Changed(change.After));
        Assert.Contains(change.After, piece => piece.Kind == ValuePieceKind.LeftOut);
    }

    [Fact]
    public void The_edit_logs_marks_show_as_marks()
    {
        string start = string.Concat(Enumerable.Range(0, 100).Select(i => "TModule" + i + ","));
        EditLogEntry entry = EditLogEntry.OfChange("D", "T", start + "Old," + start, start + "New," + start);

        ValueChange change = ValueDiff.Of(entry.Old, entry.New);

        Assert.Equal(ValueChangeShape.Changed, change.Shape);
        Assert.Equal(["Old"], Changed(change.Before));
        Assert.Equal(["New"], Changed(change.After));
        Assert.Equal(ValuePieceKind.LeftOut, change.After[0].Kind);
        Assert.Equal(ValuePieceKind.LeftOut, change.After[^1].Kind);
    }

    [Fact]
    public void Very_long_values_that_differ_throughout_still_come_back_quickly()
    {
        string one = string.Concat(Enumerable.Range(0, 2000).Select(i => "A" + i + ","));
        string two = string.Concat(Enumerable.Range(0, 2000).Select(i => "B" + i + ","));

        ValueChange change = ValueDiff.Of(one, two);

        Assert.Equal(ValueChangeShape.Changed, change.Shape);
        Assert.NotEmpty(Changed(change.After));
    }
}
