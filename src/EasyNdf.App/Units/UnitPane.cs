using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// The Unit pane beside or below the list: the selected unit's picture, name and what it is, buttons to show it in
/// the File Editor, make a rule for it or compare it, then its overview, armour, weapons, specialties and divisions,
/// and its descriptor's name at the foot.
/// </summary>
public static class UnitPane
{
    /// <summary>The widest the unit's picture is drawn: about the pane's width beside the list.</summary>
    private const double PictureWidth = 330;

    /// <summary>The pane's content for a unit; for none, a line saying to click one.</summary>
    public static FrameworkElement Build(Unit? unit, IUnitActions actions)
    {
        if (unit is null) return Muted(Texts.Get("Units.Pane.ClickAUnit"), new Thickness(10));
        var pane = new StackPanel { Margin = new Thickness(10) };
        // as wide as the pane beside the list, but no wider when the pane is below it and spans the window; larger than
        // the game's picture, so it is enlarged sharply
        pane.Children.Add(new UnitPicture { Source = unit.PictureSource, Factory = unit.Factory, MaxWidth = PictureWidth, EnlargesSharply = true, HorizontalAlignment = HorizontalAlignment.Left });
        pane.Children.Add(Title(unit));
        pane.Children.Add(Buttons(unit, actions));
        pane.Children.Add(Section(Texts.Get("Units.Pane.Overview"), null, Overview(unit, actions.Game)));
        pane.Children.Add(Section(Texts.Get("Units.Pane.Armor"), null, Armor(unit.Armor)));
        pane.Children.Add(Section(Texts.Get("Units.Weapons"), unit.Weapons.Count,
            WeaponLedger.Build(unit, actions.Game.RangeUnit, narrow: true, bestRange: null, (weapon, at) => actions.OpenWeaponMenu(unit, weapon, at))));
        if (unit.Specialties.Count > 0)
            pane.Children.Add(Section(Texts.Get("Units.Pane.Specialties"), null, Tags(unit.Specialties.Select(SpecialtyName), "")));
        pane.Children.Add(Section(Texts.Get("Units.Pane.Divisions"), unit.Divisions.Count, Tags(unit.Divisions, Texts.Get("Units.Pane.InNoDivision"))));
        var foot = new TextBlock { Text = unit.Descriptor, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        foot.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        foot.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        pane.Children.Add(foot);
        return pane;
    }

    /// <summary>The name, 14 pixels and bold, and under it the nation, side, factory and role, dim.</summary>
    private static StackPanel Title(Unit unit)
    {
        var name = new TextBlock { Text = unit.Name, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        string what = string.Join(" · ", UnitNames.Nation(unit.Nation), UnitNames.Side(unit.Side), UnitNames.Factory(unit.Factory), UnitNames.RoleOf(unit));
        return new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { name, Muted(what, new Thickness(0)) } };
    }

    /// <summary>Show in File Editor (dim while the unit's file is not on the Files tab), New Rule… and Compare.</summary>
    private static WrapRow Buttons(Unit unit, IUnitActions actions)
    {
        var showInEditor = new Button { Content = Texts.Get("Units.Pane.ShowInEditor"), IsEnabled = actions.CanShowInEditor(unit) };
        showInEditor.ToolTip = showInEditor.IsEnabled ? Texts.Get("Units.Pane.ShowInEditor.Tip") : Texts.Get("Units.Menu.NotLoaded.Tip", ("file", UnitFiles.Units));
        // a disabled button still shows why
        ToolTipService.SetShowOnDisabled(showInEditor, true);
        showInEditor.Click += (_, _) => actions.ShowInEditor(unit);
        var newRule = new Button { Content = Texts.Get("Units.Pane.NewRule"), ToolTip = Texts.Get("Units.Pane.NewRule.Tip") };
        newRule.Click += (_, _) => actions.NewRuleFor(unit);
        var compare = new Button { Content = Texts.Get("Units.Pane.Compare"), ToolTip = Texts.Get("Units.Pane.Compare.Tip") };
        compare.Click += (_, _) => actions.Compare(unit);
        return new WrapRow { Margin = new Thickness(0, 12, 0, 0), Children = { showInEditor, newRule, compare } };
    }

    /// <summary>
    /// The overview: two pairs of name and value a row, the names dim on the panel colour. Speed is in km/h in WARNO;
    /// Steel Division 2 gives it in its own unit.
    /// </summary>
    private static Bevel Overview(Unit unit, GameTitle game)
    {
        string KilometresPerHour(double? speed) => Texts.Get("Units.KilometresPerHour", ("speed", UnitNumbers.Of(speed)));
        var pairs = new List<(string Name, string Value)>();
        void Add(string key, double? value, Func<double?, string>? write = null)
        {
            if (value is not null) pairs.Add((Texts.Get(key), write is null ? UnitNumbers.Of(value) : write(value)));
        }
        Add("Units.Stat.Cost", unit.Cost);
        Add("Units.Stat.Strength", unit.Strength);
        Add("Units.Stat.Speed", unit.Speed, game == GameTitle.Warno ? KilometresPerHour : null);
        Add("Units.Stat.RoadSpeed", unit.RoadSpeed, KilometresPerHour);
        Add("Units.Stat.Optics", unit.Optics);
        Add("Units.Stat.Stealth", unit.Stealth);
        Add("Units.Pane.Fuel", unit.Fuel);
        Add("Units.Pane.Vision", unit.Vision);
        if (pairs.Count % 2 == 1) pairs.Add(("", ""));

        var grid = new Grid();
        foreach (GridLength width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        int rows = pairs.Count / 2;
        for (int at = 0; at < pairs.Count; at++)
        {
            int row = at / 2, column = at % 2 * 2;
            if (column == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool lastRow = row == rows - 1;
            grid.Children.Add(Cell(pairs[at].Name, row, column, isName: true, lastRow));
            grid.Children.Add(Cell(pairs[at].Value, row, column + 1, isName: false, lastRow));
        }
        return Boxed(grid);
    }

    /// <summary>A cell of the overview: 2 by 7 pixels in, a line under it unless it is on the last row.</summary>
    private static Bevel Cell(string text, int row, int column, bool isName, bool lastRow)
    {
        var block = new TextBlock { Text = text };
        if (isName) block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var cell = new Bevel { Child = block, Padding = new Thickness(7, 2, 7, 2), Edges = new Thickness(0, 0, 0, lastRow ? 0 : 1) };
        cell.SetResourceReference(Bevel.BottomRightProperty, "line");
        if (isName) cell.SetResourceReference(Bevel.BackgroundProperty, "panel");
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>The armour on each side, in a row of four cells under their names; "–" for a side the game gives none.</summary>
    private static Grid Armor(UnitArmor armor)
    {
        var grid = new Grid();
        string[] names = [Texts.Get("Units.Pane.Front"), Texts.Get("Units.Pane.Sides"), Texts.Get("Units.Pane.Rear"), Texts.Get("Units.Pane.Top")];
        int?[] values = [armor.Front, armor.Sides, armor.Rear, armor.Top];
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        for (int side = 0; side < 4; side++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var name = new TextBlock { Text = names[side], TextAlignment = TextAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            var value = new TextBlock { Text = values[side]?.ToString(Texts.Catalog.Culture) ?? UnitNumbers.None, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.Bold, FontSize = 13.5 };
            grid.Children.Add(TableCell(name, 0, side, onPanel: true));
            grid.Children.Add(TableCell(value, 1, side, onPanel: false));
        }
        return grid;
    }

    /// <summary>A cell of the armour table, with a line on every side, shared with its neighbours.</summary>
    private static Bevel TableCell(TextBlock text, int row, int column, bool onPanel)
    {
        // each cell draws its left and top lines; the last ones also their right and bottom lines
        var cell = new Bevel { Child = text, Padding = new Thickness(4, 2, 4, 2), Edges = new Thickness(1, 1, column == 3 ? 1 : 0, row == 1 ? 1 : 0) };
        cell.SetResourceReference(Bevel.TopLeftProperty, "line");
        cell.SetResourceReference(Bevel.BottomRightProperty, "line");
        if (onPanel) cell.SetResourceReference(Bevel.BackgroundProperty, "panel");
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>Small boxed words, wrapping: the specialties, the divisions; <paramref name="none"/> when there are none.</summary>
    private static FrameworkElement Tags(IEnumerable<string> words, string none)
    {
        var row = new WrapRow();
        foreach (string word in words)
        {
            var text = new TextBlock { Text = word };
            text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            var tag = new Bevel { Child = text, Padding = new Thickness(6, 0, 6, 0) };
            tag.SetResourceReference(Bevel.TopLeftProperty, "line");
            tag.SetResourceReference(Bevel.BottomRightProperty, "line");
            row.Children.Add(tag);
        }
        return row.Children.Count == 0 ? Muted(none, new Thickness(0)) : row;
    }

    /// <summary>A specialty as the game names it, made readable: "_ifv" is "ifv", "reco_inf" is "reco inf".</summary>
    private static string SpecialtyName(string specialty) => specialty.TrimStart('_').Replace('_', ' ');

    /// <summary>A section: its heading in the accent colour with a line under it (and a count, dim), then its content.</summary>
    private static StackPanel Section(string title, int? count, FrameworkElement content)
    {
        var heading = new TextBlock { Text = title };
        if (count is int number)
        {
            var countText = new Run(" " + number.ToString(Texts.Catalog.Culture)) { FontWeight = FontWeights.Normal };
            countText.SetResourceReference(TextElement.ForegroundProperty, "muted");
            heading.Inlines.Add(countText);
        }
        var bar = new Bevel { Child = heading, Style = (Style)Application.Current.FindResource("Heading") };
        return new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { bar, content } };
    }

    /// <summary>A grid in a box of lines.</summary>
    private static Bevel Boxed(UIElement content)
    {
        var box = new Bevel { Child = content };
        box.SetResourceReference(Bevel.TopLeftProperty, "line");
        box.SetResourceReference(Bevel.BottomRightProperty, "line");
        return box;
    }

    private static TextBlock Muted(string text, Thickness margin)
    {
        var block = new TextBlock { Text = text, Margin = margin, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return block;
    }
}
