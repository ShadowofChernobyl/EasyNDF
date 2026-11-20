using System.Text.RegularExpressions;

namespace EasyNdf.App.Files;

/// <summary>The divisions and units DeckSerializer.ndf names, and the names a file declares, for Export's check that each is made.</summary>
public static partial class DeckNames
{
    /// <summary>The names a list of DeckSerializer.ndf gives numbers to: DivisionIds = MAP [ (Descriptor_Deck_Division_..., 4), ... ].</summary>
    public static List<string> Keys(string deckSerializer, string list)
    {
        Match start = Regex.Match(deckSerializer, @"\b" + Regex.Escape(list) + @"\s*=\s*MAP\s*\[");
        if (!start.Success) return [];
        int end = deckSerializer.IndexOf(']', start.Index + start.Length);
        if (end < 0) end = deckSerializer.Length;
        return [.. Key().Matches(deckSerializer, start.Index + start.Length).TakeWhile(match => match.Index < end).Select(match => match.Groups[1].Value)];
    }

    /// <summary>The names a file declares at its top level: "export Descriptor_Deck_Division_... is TDeckDivisionDescriptor".</summary>
    public static IEnumerable<string> Declared(string text) => Declaration().Matches(text).Select(match => match.Groups[1].Value);

    /// <summary>The units a card's text names: its Unit and its Transport ($/GFX/Unit/Descriptor_Unit_...).</summary>
    public static IEnumerable<string> UnitsIn(string card) => UnitReference().Matches(card).Select(match => match.Groups[1].Value);

    /// <summary>A battlegroup's text without the cards given: their "~/Descriptor_Deck_Pack_...," lines in its DeckPackList.</summary>
    public static string WithoutPacks(string deck, IReadOnlySet<string> packs) =>
        PackLine().Replace(deck, line => packs.Contains(line.Groups[1].Value) ? "" : line.Value);

    [GeneratedRegex(@"\b(?:Unit|Transport)\s*=\s*\$/GFX/Unit/([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex UnitReference();

    [GeneratedRegex(@"(?m)^[ \t]*~/([A-Za-z_][A-Za-z0-9_]*)[ \t]*,?[ \t]*\r?\n")]
    private static partial Regex PackLine();

    [GeneratedRegex(@"\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*,")]
    private static partial Regex Key();

    [GeneratedRegex(@"(?m)^[ \t]*(?:export[ \t]+)?([A-Za-z_][A-Za-z0-9_]*)[ \t]+is\b")]
    private static partial Regex Declaration();
}
