using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// The Weapons sub-tab's Table view, as the mock's: the weapons under a heading a category ("GUNS", "MACHINE GUNS"), a
/// weapon a row, its picture in the Weapon column before its name, then every number side by side. A click on a
/// heading sorts by it within each category, again the other way; a click on a row selects its weapon. Only the rows
/// in view are made; the headings move sideways with the rows.
/// </summary>
public sealed class WeaponTable : UserControl
{
    /// <summary>
    /// A column: its heading's text, its width, how a weapon's value is written, what it is sorted by, and the value
    /// it shows (<see cref="WeaponField"/>), marked when the open mod changes it; empty for none.
    /// </summary>
    private sealed record Column(string HeadingKey, double Width, Func<Weapon, string> Write, Func<Weapon, IComparable?> SortBy, bool AlignRight, string Field = "");

    private static readonly Column[] Columns =
    [
        new("Weapons.Column.Name", 300, weapon => weapon.Name, weapon => weapon.Name, AlignRight: false, WeaponField.Name),
        new("Weapons.Column.DamageType", 100, WeaponNames.Damage, weapon => weapon.DamageFamily + weapon.DamageLevel.ToString("000", System.Globalization.CultureInfo.InvariantCulture), AlignRight: false, WeaponField.DamageLevel),
        new("Weapons.Column.Damage", 64, weapon => WeaponNames.Number(weapon.Damage), weapon => weapon.Damage, AlignRight: true, WeaponField.Damage),
        new("Weapons.Column.Suppression", 72, weapon => WeaponNames.Number(weapon.Suppression), weapon => weapon.Suppression, AlignRight: true, WeaponField.Suppression),
        new("Weapons.Column.Range", 72, weapon => WeaponNames.Range(weapon.LongestRange), weapon => weapon.LongestRange, AlignRight: true, WeaponField.Range),
        new("Weapons.Column.Aiming", 64, weapon => WeaponNames.Seconds(weapon.AimingTime), weapon => weapon.AimingTime, AlignRight: true, WeaponField.AimingTime),
        new("Weapons.Column.Reload", 64, weapon => WeaponNames.Seconds(weapon.Reload), weapon => weapon.Reload, AlignRight: true, WeaponField.Reload),
        new("Weapons.Column.Supply", 64, weapon => WeaponNames.Number(weapon.SupplyCost), weapon => weapon.SupplyCost, AlignRight: true, WeaponField.SupplyCost),
        new("Weapons.Column.Units", 56, weapon => Texts.Number(weapon.Users.Count), weapon => weapon.Users.Count, AlignRight: true, WeaponField.Users),
    ];

    /// <summary>The picture's width, in the Weapon column before the name.</summary>
    private const double PictureWidth = 48;

    /// <summary>A category's heading row, between the weapons of the one before and its own.</summary>
    private sealed record CategoryLine(string Heading);

    /// <summary>
    /// The Weapon column's width: its own (the narrowest it is made) or wider, so the columns fill the table's width.
    /// Set by the table as it is resized; the rows read it as they are drawn.
    /// </summary>
    private static double nameWidth = 300;

    private readonly StackPanel headings = new() { Orientation = Orientation.Horizontal };
    private double scale = 1;
    private readonly ItemsControl rows;
    private IReadOnlyList<Weapon> weapons = [];
    private int sortColumn = -1;
    private bool sortDown;

    /// <summary>Builds the table, empty.</summary>
    public WeaponTable()
    {
        rows = new ItemsControl { Focusable = false };
        VirtualizingPanel.SetIsVirtualizing(rows, true);
        VirtualizingPanel.SetVirtualizationMode(rows, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(rows, ScrollUnit.Pixel);
        rows.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        var scroller = new FrameworkElementFactory(typeof(ScrollViewer));
        scroller.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroller.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroller.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroller.SetValue(FocusableProperty, false);
        scroller.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        rows.Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scroller };
        // a template a sort of line: a weapon's row, or a category's heading
        rows.Resources.Add(new DataTemplateKey(typeof(Weapon)), new DataTemplate(typeof(Weapon)) { VisualTree = new FrameworkElementFactory(typeof(WeaponTableRow)) });
        rows.Resources.Add(new DataTemplateKey(typeof(CategoryLine)), new DataTemplate(typeof(CategoryLine)) { VisualTree = new FrameworkElementFactory(typeof(CategoryRow)) });
        rows.MouseLeftButtonUp += RowClicked;
        SizeChanged += (_, _) => FitNameColumn();
        rows.MouseRightButtonUp += RowRightClicked;
        FillHeadings();
        var heading = new Bevel { Child = headings, Edges = new Thickness(0, 0, 0, 1) };
        heading.SetResourceReference(Bevel.BottomRightProperty, "lo");
        heading.SetResourceReference(Bevel.BackgroundProperty, "panel");
        DockPanel.SetDock(heading, Dock.Top);
        // wider than the room: the headings and rows move sideways together
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
            Content = new DockPanel { Children = { heading, rows } },
        };
    }

    /// <summary>Raised when a row is clicked, with its weapon.</summary>
    public event Action<Weapon>? WeaponClicked;

    /// <summary>Raised when a row is right-clicked, with its weapon and the row, for the weapon's menu.</summary>
    public event Action<Weapon, FrameworkElement>? WeaponRightClicked;

    /// <summary>
    /// Draws the table at a zoom: its headings and each row scaled, so they still line up and move sideways together.
    /// A transform in a style cannot change once the style is in use, so each zoom gets a new style.
    /// </summary>
    public void SetScale(double scale)
    {
        this.scale = scale;
        var transform = new ScaleTransform(scale, scale);
        transform.Freeze();
        headings.LayoutTransform = transform;
        rows.ItemContainerStyle = new Style(typeof(ContentPresenter)) { Setters = { new Setter(LayoutTransformProperty, transform) } };
        FitNameColumn();
    }

    /// <summary>
    /// The Weapon column widened so the columns fill the table's width beside the scrollbar, at the zoom; never
    /// narrower than its own width, past which the table scrolls sideways.
    /// </summary>
    private void FitNameColumn()
    {
        if (ActualWidth <= 0) return;
        double others = Columns.Skip(1).Sum(column => column.Width);
        double room = (ActualWidth - SystemParameters.VerticalScrollBarWidth - 2) / scale;
        double wanted = Math.Floor(Math.Max(Columns[0].Width, room - others));
        if (wanted == nameWidth) return;
        nameWidth = wanted;
        FillHeadings();
        foreach (WeaponTableRow row in WeaponsArmory.VisualsIn<WeaponTableRow>(rows)) row.ShowAgain();
        foreach (CategoryRow row in WeaponsArmory.VisualsIn<CategoryRow>(rows)) row.Width = RowWidth;
    }

    /// <summary>A column's width: the Weapon column's as fitted, the others' their own.</summary>
    private static double WidthOf(int column) => column == 0 ? nameWidth : Columns[column].Width;

    /// <summary>The total width of the columns, which each row takes.</summary>
    internal static double RowWidth => Enumerable.Range(0, Columns.Length).Sum(WidthOf);

    /// <summary>Shows weapons, sorted as the user last chose; <paramref name="selected"/> (a descriptor) shows selected.</summary>
    public void Show(IReadOnlyList<Weapon> shown, string? selected)
    {
        weapons = shown;
        WeaponTableRow.Selected = selected;
        rows.ItemsSource = Sorted();
    }

    /// <summary>A weapon's row brought into view, by its descriptor.</summary>
    public void BringIntoView(string descriptor)
    {
        if (rows.ItemsSource is not List<object> lines) return;
        int at = lines.FindIndex(line => line is Weapon weapon && weapon.Descriptor == descriptor);
        if (at >= 0 && WeaponsArmory.VisualsIn<VirtualizingStackPanel>(rows).FirstOrDefault() is VirtualizingStackPanel panel) panel.BringIndexIntoViewPublic(at);
    }

    /// <summary>
    /// Shows another weapon selected: the rows on screen drawn again in place. The list is not made again, which moved
    /// it under the mouse.
    /// </summary>
    public void ShowSelected(string? selected)
    {
        WeaponTableRow.Selected = selected;
        foreach (WeaponTableRow row in WeaponsArmory.VisualsIn<WeaponTableRow>(rows)) row.ShowAgain();
    }

    /// <summary>The lines shown: each category's heading, then its weapons, sorted as the user last chose.</summary>
    private List<object> Sorted()
    {
        var lines = new List<object>();
        foreach (IGrouping<WeaponCategory, Weapon> category in weapons.GroupBy(weapon => weapon.Category).OrderBy(group => group.Key))
        {
            lines.Add(new CategoryLine(WeaponNames.CategoryHeading(category.Key)));
            lines.AddRange(SortedWithin(category));
        }
        return lines;
    }

    /// <summary>A category's weapons, in the column chosen's order; as given before any is chosen.</summary>
    private IEnumerable<Weapon> SortedWithin(IEnumerable<Weapon> category)
    {
        if (sortColumn < 0) return category;
        Column column = Columns[sortColumn];
        // weapons without the value go last, either way
        IEnumerable<Weapon> withValue = category.Where(weapon => column.SortBy(weapon) is not null);
        IEnumerable<Weapon> without = category.Where(weapon => column.SortBy(weapon) is null);
        IEnumerable<Weapon> sorted = sortDown ? withValue.OrderByDescending(column.SortBy) : withValue.OrderBy(column.SortBy);
        return [.. sorted, .. without];
    }

    /// <summary>A heading a column; a click sorts by it, a second click the other way.</summary>
    private void FillHeadings()
    {
        headings.Children.Clear();
        for (int at = 0; at < Columns.Length; at++)
        {
            int index = at;
            string arrow = index == sortColumn ? (sortDown ? " ▾" : " ▴") : "";
            var text = new TextBlock { Text = Texts.Get(Columns[at].HeadingKey) + arrow, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            var cell = new Border
            {
                Width = WidthOf(at), Padding = new Thickness(6, 3, 6, 3), Child = text, Background = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = Texts.Get("Weapons.Column.Sort.Tip"),
            };
            if (Columns[at].AlignRight) text.TextAlignment = TextAlignment.Right;
            cell.MouseLeftButtonUp += (_, _) => SortBy(index);
            headings.Children.Add(cell);
        }
    }

    private void SortBy(int index)
    {
        sortDown = index == sortColumn ? !sortDown : false;
        sortColumn = index;
        FillHeadings();
        rows.ItemsSource = Sorted();
    }

    private void RowClicked(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e.OriginalSource) is { DataContext: Weapon weapon }) WeaponClicked?.Invoke(weapon);
    }

    private void RowRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e.OriginalSource) is not { DataContext: Weapon weapon } row) return;
        e.Handled = true;
        WeaponRightClicked?.Invoke(weapon, row);
    }

    /// <summary>The row an element clicked is in; null for none.</summary>
    private static WeaponTableRow? RowAt(object clicked)
    {
        for (DependencyObject? at = clicked as DependencyObject; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is WeaponTableRow row) return row;
        return null;
    }

    /// <summary>A category's heading: its name in capitals, bold in the accent colour, on the panel colour, the row's whole width.</summary>
    private sealed class CategoryRow : Bevel
    {
        private readonly TextBlock name = new() { FontWeight = FontWeights.Bold, FontSize = 11.5 };

        public CategoryRow()
        {
            Width = RowWidth;
            // at the left, with the rows, however wide the list is
            HorizontalAlignment = HorizontalAlignment.Left;
            Padding = new Thickness(6, 3, 6, 2);
            Edges = new Thickness(0, 0, 0, 1);
            Child = name;
            name.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            SetResourceReference(BackgroundProperty, "panel");
            SetResourceReference(BottomRightProperty, "line");
            DataContextChanged += (_, _) =>
            {
                name.Text = (DataContext as CategoryLine)?.Heading ?? "";
                Width = RowWidth;
            };
        }
    }

    /// <summary>One row: the weapon's picture and name, then its values in the columns' widths; on the selection colour when selected.</summary>
    private sealed class WeaponTableRow : StackPanel
    {
        /// <summary>The descriptor of the weapon selected; set by the table before it shows its rows.</summary>
        public static string? Selected { get; set; }

        private readonly WeaponPicture picture = new() { Width = PictureWidth, Margin = new Thickness(4, 1, 0, 1) };

        public WeaponTableRow()
        {
            Orientation = Orientation.Horizontal;
            Cursor = Cursors.Hand;
            foreach (Column column in Columns)
            {
                // the Weapon column holds the picture, then the name in the room left
                bool isFirst = Children.Count == 0;
                var text = new TextBlock
                {
                    Width = isFirst ? column.Width - PictureWidth - 4 : column.Width, Padding = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                };
                if (isFirst) Children.Add(picture);
                if (column.AlignRight)
                {
                    text.TextAlignment = TextAlignment.Right;
                    text.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
                }
                Children.Add(text);
            }
            DataContextChanged += (_, _) => Show();
        }

        /// <summary>Draws the row again, as the selection changed.</summary>
        public void ShowAgain() => Show();

        private void Show()
        {
            if (DataContext is not Weapon weapon) return;
            picture.Source = weapon.PictureSource;
            for (int at = 0; at < Columns.Length; at++)
            {
                // the picture is before the first column's text, which takes the rest of the Weapon column
                var cell = (TextBlock)Children[at + 1];
                if (at == 0) cell.Width = nameWidth - PictureWidth - 4;
                Column column = Columns[at];
                if (column.Field == "") cell.Text = column.Write(weapon);
                else WeaponMarks.Show(cell, weapon, column.Field, column.Write(weapon), column.Write);
            }
            if (weapon.Descriptor == Selected) SetResourceReference(BackgroundProperty, "sel");
            else Background = Brushes.Transparent;
        }
    }
}
