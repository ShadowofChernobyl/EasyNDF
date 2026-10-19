using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>The Divisions tab's views of the division chosen: the category tabs, Cards or Table, and the Division pane.</summary>
public partial class DivisionsPage
{
    /// <summary>The room a vertical scrollbar takes, kept free so the tiles do not move when it shows.</summary>
    private const double ScrollBarWidth = 14;

    /// <summary>The category whose units are shown: empty for all.</summary>
    private string factory = "";

    /// <summary>Whether the Table view is shown rather than Cards.</summary>
    private bool tableView;

    /// <summary>The categories folded away in the Cards view, by code.</summary>
    private readonly HashSet<string> foldedCategories = [];

    /// <summary>How many tiles a row of the Cards view had when it was last filled.</summary>
    private int cardColumns;

    /// <summary>The unit selected, shown in the Division pane.</summary>
    private readonly UnitSelection selection = new();

    /// <summary>The division chosen: its name and size in the heading, the category tabs, the view and the pane.</summary>
    private void ShowDivision()
    {
        if (chosen is null) return;
        TitleText.Text = chosen.Name;
        SavedInText.Text = Texts.Get("Divisions.SavedIn", ("source", loaded?.Source.Name), ("mod", OpenMod.Name));
        SavedInText.ToolTip = Texts.Get("Divisions.SavedIn.Tip", ("mod", OpenMod.Name));
        SummaryText.Text = Texts.Get("Divisions.Summary",
            ("units", Texts.Count("Divisions.UnitCount", chosen.Units.Count)), ("points", UnitNumbers.Of(chosen.ActivationPoints)));
        FillFactoryTabs();
        ShowView();
        ShowPane();
    }

    /// <summary>All, then each category the game has, with how many of the division's units are in it.</summary>
    private void FillFactoryTabs()
    {
        FactoryTabs.Children.Clear();
        if (chosen is null) return;
        Dictionary<string, int> counts = DivisionGroups.CountsByFactory(chosen, unitsByDescriptor);
        FactoryTabs.Children.Add(FactoryTab("", Texts.Get("Units.Factory.All.Short"), Texts.Get("Units.Factory.All"), counts.Values.Sum()));
        foreach (string each in UnitNames.FactoriesOf(Game))
            FactoryTabs.Children.Add(FactoryTab(each, UnitNames.FactoryShort(each), UnitNames.Factory(each), counts.GetValueOrDefault(each)));
    }

    private ToggleButton FactoryTab(string code, string shortName, string name, int count)
    {
        var countText = new System.Windows.Documents.Run(" " + Texts.Number(count)) { FontWeight = FontWeights.Normal, FontSize = 11 };
        countText.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "muted");
        var label = new TextBlock { Inlines = { new System.Windows.Documents.Run(shortName), countText } };
        var tab = new ToggleButton { Content = label, Style = (Style)FindResource("FactoryTab"), IsChecked = factory == code, ToolTip = name };
        tab.Click += (_, _) =>
        {
            factory = code;
            FillFactoryTabs();
            ShowView();
        };
        return tab;
    }

    /// <summary>Cards or Table, of the category chosen; how many units they show at the foot.</summary>
    private void ShowView()
    {
        if (chosen is null) return;
        List<DivisionGroup> groups = DivisionGroups.Of(chosen, unitsByDescriptor, Game, factory);
        ViewScroller.Visibility = tableView ? Visibility.Collapsed : Visibility.Visible;
        TableList.Visibility = tableView ? Visibility.Visible : Visibility.Collapsed;
        if (tableView)
        {
            List<Unit> units = [.. groups.SelectMany(group => group.Entries).Select(entry => entry.Unit)];
            TableList.Show(units, DivisionTableColumns.For(chosen, KeepRule), Game, selection, this, Texts.Get("Divisions.NoUnits"));
        }
        else
        {
            cardColumns = CardRowView.ColumnsFor(CardsRoom());
            ViewContent.Content = DivisionCards.Build(groups, Game, selection, cardColumns, foldedCategories, FoldClicked);
        }
        CountText.Text = Texts.CountWide("Units.Shown", groups.Sum(group => group.Entries.Count));
    }

    /// <summary>A category's heading clicked: its tiles fold away or come back.</summary>
    private void FoldClicked(string factory)
    {
        if (!foldedCategories.Remove(factory)) foldedCategories.Add(factory);
        ShowView();
    }

    /// <summary>
    /// In the Cards view, Ctrl+Shift+[ folds every category's tiles away and Ctrl+Shift+] brings them all back, as on
    /// the Units tab; not while typing in a box.
    /// </summary>
    private void FoldKeys(object sender, KeyEventArgs e)
    {
        if (tableView || chosen is null || Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        if (RulesPage.IsCollapseAllKey(e))
        {
            e.Handled = true;
            foldedCategories.UnionWith(DivisionGroups.CountsByFactory(chosen, unitsByDescriptor).Keys);
            ShowView();
        }
        else if (RulesPage.IsExpandAllKey(e))
        {
            e.Handled = true;
            foldedCategories.Clear();
            ShowView();
        }
    }

    /// <summary>The width the cards have across, beside the scrollbar.</summary>
    private double CardsRoom() => ViewScroller.ActualWidth - ScrollBarWidth;

    /// <summary>The view was resized: the cards are laid out again when a different number fits across.</summary>
    private void ViewResized(object sender, SizeChangedEventArgs e)
    {
        if (chosen is null || tableView) return;
        if (CardRowView.ColumnsFor(CardsRoom()) != cardColumns) ShowView();
    }

    /// <summary>The Division pane, with the selected unit's part when it is one of the division's.</summary>
    private void ShowPane()
    {
        if (chosen is null) return;
        DivisionEntry? entry = DivisionGroups.Entries(chosen, unitsByDescriptor).FirstOrDefault(each => ReferenceEquals(each.Unit, selection.Selected));
        PaneContent.Content = DivisionPane.Build(chosen, entry, unitsByDescriptor, Game, this);
        ShowModNote(DivisionChanges.EntryOf(OpenMod, chosen.Descriptor));
    }

    /// <summary>
    /// At the pane's foot, what the open mod does to the division (new in it, changed, or taken out of the game's list),
    /// its tooltip saying what that means; nothing for a division as the game has it.
    /// </summary>
    private void ShowModNote(ModDivision? inMod)
    {
        ModNote.Visibility = inMod is null ? Visibility.Collapsed : Visibility.Visible;
        if (inMod is null) return;
        string mod = OpenMod.Name;
        (ModNote.Text, ModNote.ToolTip) = inMod switch
        {
            { IsNew: true } => (Texts.Get("Divisions.Pane.NewInMod", ("mod", mod)), Texts.Get("Divisions.Pane.NewInMod.Tip", ("mod", mod), ("division", StartingName(inMod)))),
            { IsRemoved: true } => (Texts.Get("Divisions.Pane.RemovedInMod", ("mod", mod)), Texts.Get("Divisions.Pane.RemovedInMod.Tip", ("mod", mod))),
            _ => (Texts.Get("Divisions.Pane.ChangedInMod", ("mod", mod)), Texts.Get("Divisions.Pane.ChangedInMod.Tip", ("mod", mod))),
        };
    }

    /// <summary>The name of the game division a new division starts from.</summary>
    private string StartingName(ModDivision division) =>
        GameDivisions.FirstOrDefault(each => each.Descriptor == division.StartsFrom)?.Name ?? division.StartsFrom;

    // ---- clicks on the views ----

    /// <summary>A click on a unit, in either view, selects it.</summary>
    private void UnitClicked(object sender, MouseButtonEventArgs e)
    {
        if (UnitAt(e.OriginalSource) is Unit unit) selection.Selected = unit;
    }

    /// <summary>
    /// A double-click on a unit shows it in the File Editor, when its file is on the Files tab, as on the Units tab; not
    /// in a box of the table, where a double-click picks a word.
    /// </summary>
    private void UnitDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (IsInBox(e.OriginalSource)) return;
        if (e.ClickCount != 2 || UnitAt(e.OriginalSource) is not Unit unit || !CanShowInEditor(unit)) return;
        e.Handled = true;
        ShowInEditor(unit);
    }

    /// <summary>A right-click on a unit selects it and opens its menu.</summary>
    private void UnitRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (UnitAt(e.OriginalSource) is not Unit unit) return;
        e.Handled = true;
        selection.Selected = unit;
        OpenUnitMenu(unit, (FrameworkElement)sender);
    }

    /// <summary>Whether a click is in a box of the table, where cards, units per card and veterancy are changed.</summary>
    private static bool IsInBox(object? source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is EditBox) return true;
        return false;
    }

    /// <summary>The unit under the mouse: a card's or a table row's; null elsewhere.</summary>
    private static Unit? UnitAt(object? source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is UnitTile tile) return tile.Unit;
            if (at is UnitTableRowView row) return row.Unit;
        }
        return null;
    }

    // ---- the view buttons ----

    private void ViewClicked(object sender, RoutedEventArgs e)
    {
        tableView = sender == TableButton;
        AppData.Current.State["divisionView"] = tableView ? "Table" : "Cards";
        AppData.Current.SaveState();
        ShowViewButtons();
        ShowView();
    }

    /// <summary>The view chosen stays pressed.</summary>
    private void ShowViewButtons()
    {
        CardsButton.IsChecked = !tableView;
        TableButton.IsChecked = tableView;
    }
}
