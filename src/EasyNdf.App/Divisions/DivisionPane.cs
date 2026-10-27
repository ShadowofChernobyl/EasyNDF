using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The Division pane: at the top the division's emblem (with ✎ to change it), name, what it is, and its activation
/// points and number of units; then its sections, each folding under a header the width of the pane: the division's
/// details; its texts; its signature units; its Official Battlegroup, a link to it; and its files.
/// </summary>
public static class DivisionPane
{
    /// <summary>How large the emblem is drawn.</summary>
    private const double EmblemSize = 64;

    /// <summary>
    /// The pane's content for a division: only what belongs to the whole division. What belongs to one of its units
    /// (cards, units per card, veterancy, transports) is in the pop-up from that unit's tile (<see cref="DivisionUnitPopup"/>).
    /// </summary>
    /// <param name="division">The division shown.</param>
    /// <param name="units">The source's units by descriptor, to name transports and standout units.</param>
    /// <param name="game">The game the division is from.</param>
    /// <param name="editing">What changes the division, saved in the open mod.</param>
    public static FrameworkElement Build(Division division, IReadOnlyDictionary<string, Unit> units, GameTitle game, IDivisionEditing editing)
    {
        var pane = new StackPanel();
        pane.Children.Add(Title(division, game, editing));
        pane.Children.Add(new FoldSection("details", Texts.Get("Divisions.Pane.Division"), DetailsSummary(division), DivisionDetailsEditor.Details(division, editing)));
        pane.Children.Add(new FoldSection("texts", Texts.Get("Divisions.Pane.Texts"), TextsSummary(division), DivisionDetailsEditor.DivisionTexts(division, editing)));
        pane.Children.Add(new FoldSection("standout", Texts.Get("Divisions.Pane.Standout"), Texts.Count("Divisions.Pane.Standout.Count", division.StandoutUnits.Count),
            DivisionDetailsEditor.StandoutUnits(division, units, editing)));
        pane.Children.Add(new FoldSection("battlegroup", Texts.Get("Divisions.Pane.Battlegroup"),
            Texts.Count("Divisions.Battlegroup.CardCount", division.Battlegroup?.Cards.Count ?? 0), BattlegroupLink(division, units, editing)));
        int missing = division.Units.Count(unit => !units.ContainsKey(unit.Unit));
        pane.Children.Add(new FoldSection("files", Texts.Get("Divisions.Pane.Files"), missing > 0 ? Texts.Count("Divisions.Pane.Missing", missing) : "",
            DivisionDetailsEditor.Files(division, units, editing)));
        return pane;
    }

    /// <summary>
    /// The Official Battlegroup section: a link, "26 cards, 31 of 50 points", that shows it on the Battlegroups sub-tab;
    /// "None" for a division that has none.
    /// </summary>
    private static TextBlock BattlegroupLink(Division division, IReadOnlyDictionary<string, Unit> units, IDivisionEditing editing)
    {
        var line = new TextBlock { Margin = new Thickness(10, 6, 10, 8), TextWrapping = TextWrapping.Wrap };
        if (division.Battlegroup is not Battlegroup battlegroup)
        {
            line.Text = Texts.Get("Divisions.Pane.Battlegroup.None");
            line.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            return line;
        }
        int points = BattlegroupCheck.Points(battlegroup.Cards, division, unit => units.TryGetValue(unit, out Unit? found) ? found.Factory : null);
        string words = Texts.Get("Divisions.Pane.Battlegroup.Link",
            ("cards", Texts.Count("Divisions.Battlegroup.CardCount", battlegroup.Cards.Count)), ("points", Texts.Number(points)), ("allowed", UnitNumbers.Of(division.ActivationPoints)));
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(words)) { ToolTip = Texts.Get("Divisions.Pane.Battlegroup.Link.Tip"), Cursor = System.Windows.Input.Cursors.Hand };
        link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "accent");
        link.Click += (_, _) => editing.ShowBattlegroup();
        line.Inlines.Add(link);
        return line;
    }

    /// <summary>What the Texts section's header says while folded: the summary's start, or nothing.</summary>
    private static string TextsSummary(Division division) =>
        division.Summary.Length <= 40 ? division.Summary : division.Summary[..40].TrimEnd() + "…";

    /// <summary>What the Division section's header says while folded: "Belgium · Territorial · 50 AP".</summary>
    private static string DetailsSummary(Division division)
    {
        IEnumerable<string> parts = new[]
        {
            UnitNames.Nation(division.Country),
            DivisionNames.Type(division.TypeToken),
            division.ActivationPoints is int points ? Texts.Get("Divisions.Pane.PointsShort", ("points", Texts.Number(points))) : "",
        };
        return string.Join(" · ", parts.Where(part => part != ""));
    }

    /// <summary>
    /// The emblem at the left, with ✎ on its corner to change it; at its right the name, 14 pixels and bold, under it the
    /// nation, side and type, dim, and under that the activation points and number of units, the numbers in bold.
    /// </summary>
    private static DockPanel Title(Division division, GameTitle game, IDivisionEditing editing)
    {
        var name = new TextBlock { Text = division.Name, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        DivisionMarks.MarkText(name, ["Name"], each => each.Name);
        IEnumerable<string> parts = new[] { UnitNames.Nation(division.Country), UnitNames.Side(division.Side), DivisionNames.Type(division.TypeToken) }
            .Where(part => part != "");
        var what = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        if (division.Country != "")
            what.Children.Add(new NationMark(division.Country, division.Side == UnitNames.SidesOf(game)[0]) { Margin = new Thickness(0, 0, 5, 0) });
        what.Children.Add(UnitPane.Muted(string.Join(" · ", parts), new Thickness(0)));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, what, Totals(division) } };
        var title = new DockPanel { Margin = new Thickness(10, 12, 10, 12) };
        FrameworkElement emblem = EmblemWithPencil(division, editing);
        DockPanel.SetDock(emblem, Dock.Left);
        title.Children.Add(emblem);
        title.Children.Add(words);
        return title;
    }

    /// <summary>"50 activation points · 57 units", the numbers in bold and the words dim.</summary>
    private static TextBlock Totals(Division division)
    {
        var totals = new TextBlock { Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
        string points = division.ActivationPoints is int value ? Texts.Number(value) : "–";
        string units = Texts.Number(division.Units.Count);
        string text = Texts.Get("Divisions.Pane.Totals", ("points", points), ("units", units));
        // the numbers in bold wherever the language puts them
        int at = 0;
        foreach (string number in new[] { points, units })
        {
            int found = text.IndexOf(number, at, StringComparison.Ordinal);
            if (found < 0) continue;
            totals.Inlines.Add(Dim(text[at..found]));
            totals.Inlines.Add(new System.Windows.Documents.Run(number) { FontWeight = FontWeights.Bold });
            at = found + number.Length;
        }
        totals.Inlines.Add(Dim(text[at..]));
        return totals;
    }

    private static System.Windows.Documents.Run Dim(string text)
    {
        var run = new System.Windows.Documents.Run(text);
        run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "muted");
        return run;
    }

    /// <summary>The emblem, with a small ✎ on its bottom right corner whose menu offers one of the game's emblems or a picture.</summary>
    private static Grid EmblemWithPencil(Division division, IDivisionEditing editing)
    {
        var emblem = new DivisionEmblem(division.EmblemSource, EmblemSize, keepsRoom: false, division.EmblemFile)
        {
            ToolTip = division.EmblemFile is string file ? System.IO.Path.GetFileName(file) : null,
        };
        var pencil = new Button
        {
            Content = "✎", Style = (Style)Application.Current.FindResource("HeadingButton"), Padding = new Thickness(0), Width = 18, MinHeight = 18, Height = 18,
            FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -6, -4),
            ToolTip = Texts.Get("Divisions.Pane.Emblem.Tip"),
        };
        pencil.Click += (_, _) => Menus.ShowBelow(pencil,
        [
            new MenuChoice(Texts.Get("Divisions.Pane.Emblem.Game"), () => editing.ChooseGameEmblem(pencil), Tip: Texts.Get("Divisions.Pane.Emblem.Game.Tip")),
            new MenuChoice(Texts.Get("Divisions.Pane.Emblem.Picture"), editing.ChooseEmblemPicture, Tip: Texts.Get("Divisions.Pane.Emblem.Picture.Tip")),
        ]);
        var holder = new Grid { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Children = { emblem, pencil } };
        // an emblem the open mod changed: "*" at its top right, as a changed value has after it
        if (DivisionMarks.TipIfChanged(["Emblem"], each => each.EmblemFile ?? each.EmblemTexture) is string tip)
        {
            emblem.ToolTip = tip;
            if (!ModMarks.AreShown) return holder;
            var star = new TextBlock { Text = "*", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -8, 0), ToolTip = tip };
            star.SetResourceReference(TextBlock.ForegroundProperty, ModMarks.ChangedColour);
            holder.Children.Add(star);
        }
        return holder;
    }
}
