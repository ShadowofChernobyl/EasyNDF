using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.App.Weapons;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Dialogs;

/// <summary>A weapon offered in the Swap window, how well it fits the place, and why it cannot go there when it cannot.</summary>
/// <param name="Weapon">The weapon.</param>
/// <param name="Fit">How well it fits.</param>
/// <param name="Reason">For one that cannot go there, why, in words; empty otherwise.</param>
public sealed record SwapChoice(Weapon Weapon, LoadoutRules.Fit Fit, string Reason);

/// <summary>A line of the Swap window's list: a group's heading, or a weapon.</summary>
/// <param name="Heading">The group's heading; null for a weapon's line.</param>
/// <param name="Choice">The weapon; null for a heading.</param>
/// <param name="Note">The heading's dim note, such as "work; fire with the M240's flash and sound".</param>
/// <param name="Group">The heading's group, which its colour shows; null for a weapon's line.</param>
public sealed record SwapLine(string? Heading, SwapChoice? Choice, string Note = "", LoadoutRules.Fit? Group = null);

/// <summary>What the Swap window is opened for, which its groups' headings say.</summary>
public enum SwapKind
{
    /// <summary>Another weapon in the place of one mounted on a unit.</summary>
    Swap,
    /// <summary>A weapon added to a turret.</summary>
    Add,
    /// <summary>A weapon to copy as a new weapon of the open mod's.</summary>
    New,
}

/// <summary>The ammo box of the place swapped: its number, as the editor shows it, and the other weapons firing from it.</summary>
/// <param name="Number">The box's number, from 1.</param>
/// <param name="SharedWith">The other weapons mounted with the same box; none for a box of its own.</param>
public sealed record SwapBox(int Number, IReadOnlyList<Weapon> SharedWith);

/// <summary>
/// The Swap window (see SwapWeaponWindow.xaml): a weapon is picked for a place on a unit, then used as it is or as a
/// copy of the user's own, to change without changing the other units carrying it. The same window adds a weapon to a
/// turret, and picks the weapon a new one is copied from. The caller works out which weapons fit
/// (<see cref="LoadoutRules.FitOf"/>).
/// </summary>
public partial class SwapWeaponWindow : UserControl
{
    private IReadOnlyList<SwapChoice> choices = [];
    private Weapon? now;
    private SwapKind kind;
    private SwapBox? box;
    private Func<Weapon, bool> isOwn = _ => false;
    private Action<Weapon, bool> chosen = (_, _) => { };
    private bool offersCopy;

    /// <summary>Builds the window, hidden until <see cref="Open"/>.</summary>
    public SwapWeaponWindow()
    {
        InitializeComponent();
    }

    /// <summary>Whether the window shows.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Opens the window.</summary>
    /// <param name="title">Its title: "Swap: M2HB 12.7 mm", or "Add to Turret 1".</param>
    /// <param name="subtitle">The unit and the place, dim beside the title.</param>
    /// <param name="weaponNow">The weapon there now, compared with the one picked; null when adding.</param>
    /// <param name="offered">The weapons, each with how well it fits.</param>
    /// <param name="isModsOwn">Whether a weapon is one the open mod adds, for "Only my mod's weapons".</param>
    /// <param name="offerCopy">Whether "Use a Copy to Change…" is offered.</param>
    /// <param name="use">What is done with the weapon picked: given the weapon, and whether a copy of it is wanted.</param>
    /// <param name="openedFor">What the window is opened for, which its groups' headings say.</param>
    /// <param name="placeBox">For a swap, the place's ammo box; null otherwise.</param>
    public void Open(string title, string subtitle, Weapon? weaponNow, IReadOnlyList<SwapChoice> offered, Func<Weapon, bool> isModsOwn, bool offerCopy,
        Action<Weapon, bool> use, SwapKind openedFor = SwapKind.Swap, SwapBox? placeBox = null)
    {
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        now = weaponNow;
        kind = openedFor;
        box = placeBox;
        choices = offered;
        isOwn = isModsOwn;
        offersCopy = offerCopy;
        chosen = use;
        SearchBox.Text = "";
        OnlyOwn.IsChecked = false;
        ShowChoices();
        ShowPicked();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => SearchBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// The weapons the search leaves, under their groups' headings, as the mock's: "✓ Fits this slot", "⚠ Other
    /// kinds", then "✕ May not work here", each with a dim line saying what it means.
    /// </summary>
    private void ShowChoices()
    {
        string typed = SearchBox.Text.Trim();
        bool onlyOwn = OnlyOwn.IsChecked == true;
        bool Shown(SwapChoice choice) =>
            (typed == "" || choice.Weapon.Name.Contains(typed, StringComparison.OrdinalIgnoreCase) || choice.Weapon.Descriptor.Contains(typed, StringComparison.OrdinalIgnoreCase))
            && (!onlyOwn || isOwn(choice.Weapon));
        var lines = new List<SwapLine>();
        void Group(LoadoutRules.Fit fit, string heading, string note)
        {
            List<SwapChoice> inGroup = [.. choices.Where(choice => choice.Fit == fit && Shown(choice)).OrderBy(choice => choice.Weapon.Name, StringComparer.CurrentCultureIgnoreCase)];
            if (inGroup.Count == 0) return;
            lines.Add(new SwapLine(heading, null, note, fit));
            lines.AddRange(inGroup.Select(choice => new SwapLine(null, choice)));
        }
        string nowName = now?.Name ?? "";
        switch (kind)
        {
            case SwapKind.New:
                Group(LoadoutRules.Fit.Fits, Texts.Get("Weapons.New.Group"), Texts.Get("Weapons.New.Group.Note"));
                break;
            case SwapKind.Add:
                Group(LoadoutRules.Fit.Fits, Texts.Get("Weapons.Swap.Fits"), Texts.Get("Weapons.Swap.Fits.AddNote"));
                Group(LoadoutRules.Fit.OtherKind, Texts.Get("Weapons.Swap.OtherKind"), Texts.Get("Weapons.Swap.OtherKind.AddNote"));
                Group(LoadoutRules.Fit.CannotGoThere, Texts.Get("Weapons.Swap.Cannot"), Texts.Get("Weapons.Swap.Cannot.Note"));
                break;
            default:
                Group(LoadoutRules.Fit.Fits, Texts.Get("Weapons.Swap.Fits"), Texts.Get("Weapons.Swap.Fits.Note"));
                Group(LoadoutRules.Fit.OtherKind, Texts.Get("Weapons.Swap.OtherKind"), Texts.Get("Weapons.Swap.OtherKind.Note", ("weapon", nowName)));
                Group(LoadoutRules.Fit.CannotGoThere, Texts.Get("Weapons.Swap.Cannot"), Texts.Get("Weapons.Swap.Cannot.Note"));
                break;
        }
        if (lines.Count == 0) lines.Add(new SwapLine(Texts.Get("Weapons.NoneShown"), null, "", null));
        SwapChoiceRow.Selected = null;
        ChoiceList.ItemsSource = lines;
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => ShowChoices();

    /// <summary>A heading is never picked: the list keeps the weapon picked before.</summary>
    private void ChoiceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChoiceList.SelectedItem is SwapLine { Choice: null })
        {
            ChoiceList.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            return;
        }
        SwapChoiceRow.Selected = Picked?.Weapon.Descriptor;
        foreach (SwapChoiceRow row in Weapons.WeaponsArmory.VisualsIn<SwapChoiceRow>(ChoiceList)) row.ShowAgain();
        ShowPicked();
    }

    private SwapChoice? Picked => (ChoiceList.SelectedItem as SwapLine)?.Choice;

    /// <summary>
    /// The right of the window, as the mock's: "Y in place of X", their values side by side, the ammo box, why it
    /// cannot go there when it cannot, and the buttons, with what a copy is under them.
    /// </summary>
    private void ShowPicked()
    {
        SwapChoice? picked = Picked;
        // a weapon that may not work here can be used too: the user is warned first (see Use)
        bool usable = picked is not null;
        UseButton.IsEnabled = usable;
        UseCopyButton.IsEnabled = usable;
        UseCopyButton.Visibility = offersCopy ? Visibility.Visible : Visibility.Collapsed;
        PickedTitle.Text = picked is null ? Texts.Get("Weapons.Swap.PickOne")
            : now is null ? picked.Weapon.Name : Texts.Get("Weapons.Swap.InPlaceOf", ("weapon", picked.Weapon.Name), ("now", now.Name));
        Comparison.Content = picked is null ? null : Compared(now, picked.Weapon, box);
        BoxNote.Text = picked is null ? "" : BoxNoteFor(picked.Weapon);
        ReasonNote.Text = picked is null ? "" : picked.Fit switch
        {
            LoadoutRules.Fit.CannotGoThere => picked.Reason,
            LoadoutRules.Fit.OtherKind when now is not null => Texts.Get("Weapons.Swap.OtherKind.Note", ("weapon", now.Name)),
            _ => "",
        };
        CopyNote.Text = offersCopy && picked is not null ? Texts.Count("Weapons.Swap.Copy.Note", picked.Weapon.Users.Count) : "";
    }

    /// <summary>"Same box as the HEAT round: both fire 1 per salvo, so they can share it."; empty when the box is the weapon's own.</summary>
    private string BoxNoteFor(Weapon picked)
    {
        if (box is null || box.SharedWith.Count == 0 || picked.ShotsPerSalvo is not double shots || box.SharedWith.Any(weapon => weapon.ShotsPerSalvo != shots)) return "";
        return Texts.Get("Weapons.Swap.SameBox", ("weapons", string.Join(", ", box.SharedWith.Select(weapon => weapon.Name))), ("shots", UnitNumbers.Of(shots)));
    }

    /// <summary>
    /// The values of the weapon there now and of the one picked, side by side, as the mock's: Damage, Range,
    /// Suppression and Per salvo, each going up (▲, in the good colour) or down (▼, in the warning colour); the ammo box
    /// after Per salvo when the weapons sharing it fire the same.
    /// </summary>
    private static Grid Compared(Weapon? before, Weapon after, SwapBox? box)
    {
        var grid = new Grid();
        foreach (GridLength width in new[] { new GridLength(1.3, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star), GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        void Line(string name, string beforeText, string afterText, UIElement mark, bool isHeading = false)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            UIElement[] cells = [UnitPane.Muted(name, new Thickness(0, 1, 6, 1)), Cell(beforeText, isHeading), Cell(afterText, isHeading), mark];
            for (int column = 0; column < cells.Length; column++)
            {
                Grid.SetRow(cells[column], row);
                Grid.SetColumn(cells[column], column);
                grid.Children.Add(cells[column]);
            }
        }
        Line("", before is null ? "" : Texts.Get("Weapons.Swap.Now"), Texts.Get("Weapons.Swap.Then"), new TextBlock(), isHeading: true);
        Line(Texts.Get("Weapons.Pane.Damage"), before is null ? "" : WeaponNames.Damage(before), WeaponNames.Damage(after),
            Arrow(before?.DamageLevel, after.DamageLevel, moreIsBetter: true));
        Line(Texts.Get("Weapons.Pane.Range"), before is null ? "" : WeaponNames.Range(before.LongestRange), WeaponNames.Range(after.LongestRange),
            Arrow(before?.LongestRange, after.LongestRange, moreIsBetter: true));
        Line(Texts.Get("Weapons.Pane.Suppression"), before is null ? "" : WeaponNames.Number(before.Suppression), WeaponNames.Number(after.Suppression),
            Arrow(before?.Suppression, after.Suppression, moreIsBetter: true));
        bool sharesWell = box is not null && after.ShotsPerSalvo is double shots && box.SharedWith.All(weapon => weapon.ShotsPerSalvo == shots);
        UIElement salvoMark = sharesWell ? Signed("✓ " + Texts.Get("Weapons.Swap.Box", ("box", box!.Number)), "ok") : new TextBlock();
        Line(Texts.Get("Weapons.Pane.PerSalvo"), before is null ? "" : WeaponNames.Number(before.ShotsPerSalvo), WeaponNames.Number(after.ShotsPerSalvo), salvoMark);
        return grid;
    }

    private static TextBlock Cell(string text, bool isHeading)
    {
        var cell = new TextBlock { Text = text, Margin = new Thickness(0, 1, 6, 1) };
        if (isHeading) cell.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return cell;
    }

    /// <summary>▲ in the good colour when the value is better, ▼ in the warning colour when worse; nothing when the same or not known.</summary>
    private static UIElement Arrow(double? before, double? after, bool moreIsBetter)
    {
        if (before is not double from || after is not double to || from == to) return new TextBlock();
        bool better = to > from == moreIsBetter;
        return Signed(to > from ? "▲" : "▼", better ? "ok" : "warn");
    }

    private static TextBlock Signed(string text, string colour)
    {
        var sign = new TextBlock { Text = text, Margin = new Thickness(0, 1, 0, 1) };
        sign.SetResourceReference(TextBlock.ForegroundProperty, colour);
        return sign;
    }

    private async void UseClicked(object sender, RoutedEventArgs e) => await Use(asCopy: false);

    private async void UseCopyClicked(object sender, RoutedEventArgs e) => await Use(asCopy: true);

    private async void ChoiceDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (UseButton.IsEnabled) await Use(asCopy: false);
    }

    /// <summary>"Don't ask again during this session" was ticked in the warning before a weapon that may not work: it is not shown again until the app restarts.</summary>
    private static bool riskyWeaponsAllowed;

    /// <summary>
    /// Closes the window, then uses the weapon picked, as it is or as a copy. One that may not work in the place (a
    /// missile without a launcher, a weapon that must deploy where none does, a box shared with weapons firing another
    /// number a salvo) is never refused: the user is told why it may not work and goes on only when they say they know
    /// what they are doing.
    /// </summary>
    private async Task Use(bool asCopy)
    {
        if (Picked is not SwapChoice picked) return;
        if (picked.Fit == LoadoutRules.Fit.CannotGoThere && !riskyWeaponsAllowed)
        {
            DialogAnswer answer = await MainView.Of(this).Messages.Ask(Texts.Get("Weapons.Swap.Risky", ("weapon", picked.Weapon.Name), ("reason", picked.Reason)),
                new DialogOptions { Ok = Texts.Get("Weapons.Risky.GoOn"), CheckText = Texts.Get("Weapons.Risky.HideForSession") });
            if (answer.Choice != DialogChoice.Ok) return;
            if (answer.IsChecked) riskyWeaponsAllowed = true;
        }
        Close();
        chosen(picked.Weapon, asCopy);
    }

    private void Close() => Visibility = Visibility.Collapsed;

    private void CancelClicked(object sender, RoutedEventArgs e) => Close();

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && UseButton.IsEnabled)
        {
            e.Handled = true;
            _ = Use(asCopy: false);
        }
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>900 by 560 pixels, as the mock's, or 96% of the window when it is smaller.</summary>
    private void BackdropResized(object sender, SizeChangedEventArgs e)
    {
        Box.Width = Math.Min(900, (e.NewSize.Width + 20) * 0.96);
        Box.Height = Math.Min(560, (e.NewSize.Height + 20) * 0.96);
    }

    // ---- for the workbench's checks ----

    /// <summary>The weapons offered now, by line.</summary>
    internal IReadOnlyList<SwapLine> LinesNow => (IReadOnlyList<SwapLine>?)ChoiceList.ItemsSource ?? [];

    /// <summary>Picks a weapon in the list, as a click does, without using it.</summary>
    internal void ListNow(Weapon weapon) =>
        ChoiceList.SelectedItem = LinesNow.FirstOrDefault(line => line.Choice?.Weapon.Descriptor == weapon.Descriptor);

    /// <summary>Picks a weapon and uses it, as a double-click does.</summary>
    internal void UseNow(Weapon weapon, bool asCopy)
    {
        ChoiceList.SelectedItem = LinesNow.FirstOrDefault(line => line.Choice?.Weapon.Descriptor == weapon.Descriptor);
        _ = Use(asCopy);
    }
}

/// <summary>
/// A line of the Swap window's list, as the mock's: a group's heading ("✓ Fits this slot", in the good colour, with a
/// dim note); or a weapon: its picture (84 wide), its name, kind and how many units carry it, why it cannot go there
/// (in the warning colour), its range, its damage and "Use". Picked, it is on the selection colour with a bar in the
/// accent colour at its left; one that may not work there is a little faded.
/// </summary>
public sealed class SwapChoiceRow : Bevel
{
    /// <summary>The descriptor of the weapon picked, whose row shows picked; set by the window.</summary>
    public static string? Selected { get; set; }

    private readonly WeaponPicture picture = new() { Width = 84, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock name = new() { FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock what = new() { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock why = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock range = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    private readonly TextBlock level = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock use = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock heading = new() { FontWeight = FontWeights.Bold };
    private readonly TextBlock headingNote = new() { FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel headingLine;
    private readonly Grid weaponLine = new();

    /// <summary>Builds the line for the item it is given as its data.</summary>
    public SwapChoiceRow()
    {
        what.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        why.SetResourceReference(TextBlock.ForegroundProperty, "warn");
        use.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        headingNote.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        range.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        headingLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2), Children = { heading, headingNote } };
        // the damage is as long as "MISSILE HE 1"
        foreach (GridLength width in new[] { new GridLength(84), new GridLength(1, GridUnitType.Star), new GridLength(64), new GridLength(112), GridLength.Auto })
            weaponLine.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var words = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Children = { name, what, why } };
        UIElement[] cells = [picture, words, range, level, use];
        for (int column = 0; column < cells.Length; column++)
        {
            Grid.SetColumn(cells[column], column);
            weaponLine.Children.Add(cells[column]);
        }
        level.Margin = new Thickness(12, 0, 0, 0);
        use.Margin = new Thickness(8, 0, 0, 0);
        Padding = new Thickness(8, 3, 8, 3);
        Edges = new Thickness(0, 0, 0, 1);
        SetResourceReference(BottomRightProperty, "line");
        Child = new Grid { Children = { headingLine, weaponLine } };
        DataContextChanged += (_, _) => Show();
    }

    /// <summary>Draws the line again, as the weapon picked changed.</summary>
    public void ShowAgain() => Show();

    private void Show()
    {
        if (DataContext is not SwapLine line) return;
        headingLine.Visibility = line.Heading is null ? Visibility.Collapsed : Visibility.Visible;
        weaponLine.Visibility = line.Choice is null ? Visibility.Collapsed : Visibility.Visible;
        bool picked = line.Choice is not null && line.Choice.Weapon.Descriptor == Selected;
        // the bar is drawn over the padding, as the mock's inset shadow, so the line's content does not move
        Edges = new Thickness(picked ? 3 : 0, 0, 0, 1);
        Padding = new Thickness(picked ? 5 : 8, 3, 8, 3);
        SetResourceReference(TopLeftProperty, "accent");
        if (picked) SetResourceReference(BackgroundProperty, "sel");
        else Background = System.Windows.Media.Brushes.Transparent;
        Opacity = 1;
        ToolTip = null;
        if (line.Choice is not SwapChoice choice)
        {
            ShowHeading(line);
            return;
        }
        Weapon weapon = choice.Weapon;
        bool mayNotWork = choice.Fit == LoadoutRules.Fit.CannotGoThere;
        picture.Source = weapon.PictureSource;
        name.Text = weapon.Name;
        what.Text = WeaponNames.Kind(weapon) + " · " + Texts.Count("Weapons.UnitCount", weapon.Users.Count);
        why.Text = choice.Reason;
        why.Visibility = choice.Reason == "" ? Visibility.Collapsed : Visibility.Visible;
        range.Text = WeaponNames.Range(weapon.LongestRange);
        level.Text = weapon.DamageLevel > 0 ? WeaponNames.Damage(weapon) : UnitNumbers.None;
        level.ToolTip = level.Text;
        use.Text = Texts.Get("Weapons.Swap.UseRow");
        // a little faded, as it may not work; it can still be picked, with a warning first
        if (mayNotWork)
        {
            Opacity = 0.75;
            ToolTip = choice.Reason;
        }
    }

    /// <summary>A group's heading: its sign and name in the group's colour (good, caution or dim), and its note.</summary>
    private void ShowHeading(SwapLine line)
    {
        heading.Text = line.Heading ?? "";
        headingNote.Text = line.Note;
        heading.SetResourceReference(TextBlock.ForegroundProperty, line.Group switch
        {
            LoadoutRules.Fit.Fits => "ok",
            LoadoutRules.Fit.OtherKind => "caution",
            _ => "warn",
        });
    }
}
