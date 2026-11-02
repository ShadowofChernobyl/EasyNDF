using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Mods;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Files;

/// <summary>
/// The Files tab's Mods cell, and the Armory's line under its Source: the mods that run, in the order they run, joined by "›". Only whole names are
/// shown; those that don't fit the column become "2 more", in the accent colour, so a name is cut short (with "…") only
/// when the first alone does not fit; the tooltip lists them all, in run order. A click, or Enter or Space,
/// drops down the whole list, numbered in run order, with each mod's rule sets: it only shows them; the order is changed
/// on the Mods tab. A mod that changes a value another mod also changes in the file has ⚠ after its name.
/// </summary>
public sealed class ModsCell : Border
{
    // ends in "…" when even the first name alone does not fit, rather than being cut off mid-letter
    private readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>The tooltip the Armory's line gives; null for a file's mods, whose tooltip lists them.</summary>
    private string? givenTip;

    /// <summary>The mods shown, in the order they run.</summary>
    private IReadOnlyList<ModOnFile> mods = [];

    /// <summary>The file whose mods are shown, by its key on the Files tab, for the conflicts found in it; null for the Armory's line.</summary>
    private string? fileKey;

    /// <summary>Makes an empty cell; <see cref="Show"/> fills it.</summary>
    public ModsCell()
    {
        Child = text;
        // in the theme's text colour wherever the cell is put; the Armory's line makes it dim
        SetResourceReference(TextElement.ForegroundProperty, "ink");
        Padding = new Thickness(7, 3, 7, 3);
        // clicks anywhere in the cell count, not only on the text
        Background = Brushes.Transparent;
        // Tab reaches it, and Enter or Space drops the list down, as a click does
        Focusable = true;
        FocusVisualStyle = Application.Current.TryFindResource("KeyboardFocusOutline") as Style;
        SizeChanged += (_, e) =>
        {
            if (e.WidthChanged) ShowNames();
        };
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenList();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space) || Keyboard.Modifiers != ModifierKeys.None || mods.Count == 0) return;
            e.Handled = true;
            OpenList();
        };
    }

    /// <summary>Shows a file's mods.</summary>
    public void Show(FileEntry file)
    {
        fileKey = file.Key;
        mods = file.ModsOnFile;
        givenTip = null;
        ShowNames();
    }

    /// <summary>Shows the mods that are on, as the Armory's line under its Source does.</summary>
    /// <param name="modNames">The mods' names, in the order they run.</param>
    /// <param name="tip">The line's tooltip.</param>
    public void Show(IReadOnlyList<string> modNames, string tip)
    {
        fileKey = null;
        mods = [.. modNames.Select(name => new ModOnFile(name, [], HasArmoryChanges: false))];
        givenTip = tip;
        ShowNames();
    }

    /// <summary>How many mods a tooltip lists before it says how many more there are.</summary>
    private const int ListedInTip = 30;

    /// <summary>
    /// The cell's tooltip: the Armory line's own, or a file's mods one a line in run order, numbered, ⚠ after those in
    /// conflict. It is on the text too, so a name cut short gives this tooltip rather than only the cut line.
    /// </summary>
    private void ShowTip(HashSet<string> inConflict)
    {
        string? tip = givenTip;
        if (tip is null && mods.Count > 0)
        {
            IEnumerable<string> lines = mods.Take(ListedInTip).Select((mod, at) => Texts.Number(at + 1) + ". " + mod.Mod + (inConflict.Contains(mod.Mod) ? " ⚠" : ""));
            if (mods.Count > ListedInTip) lines = lines.Append(Texts.Get("Files.Mods.AndMore", ("count", Texts.Number(mods.Count - ListedInTip))));
            tip = Texts.Get("Files.Mods.Tip") + "\n" + string.Join("\n", lines);
        }
        ToolTip = tip;
        text.ToolTip = tip;
        // the hand only where a click drops a list down
        Cursor = mods.Count > 0 ? Cursors.Hand : null;
    }

    /// <summary>The mods that change a value another mod also changes in the file, by name, as the last Preview or export found.</summary>
    private HashSet<string> ModsInConflict() =>
        fileKey is null ? [] : [.. ModConflictList.InFile(fileKey).SelectMany(conflict => new[] { conflict.Winner, conflict.Overridden })];

    /// <summary>
    /// Fills the cell: as many whole names as fit, then "N more"; at least the first name, cut short only when even it
    /// alone does not fit; "–" when no mod runs on the file.
    /// </summary>
    private void ShowNames()
    {
        text.Inlines.Clear();
        HashSet<string> inConflict = ModsInConflict();
        ShowTip(inConflict);
        if (mods.Count == 0)
        {
            text.Inlines.Add(Coloured("–", "muted"));
            return;
        }
        double room = ActualWidth - Padding.Left - Padding.Right;
        int shown = mods.Count;
        while (shown > 1 && TextWidth(LineOf(mods, shown, inConflict)) > room) shown--;
        for (int at = 0; at < shown; at++)
        {
            if (at > 0) text.Inlines.Add(Coloured(Separator, "muted"));
            text.Inlines.Add(new Run(mods[at].Mod));
            if (inConflict.Contains(mods[at].Mod)) text.Inlines.Add(Coloured(" ⚠", "caution"));
        }
        if (shown < mods.Count)
        {
            text.Inlines.Add(Coloured(Separator, "muted"));
            text.Inlines.Add(Coloured(Texts.Get("Files.Mods.More", ("count", Texts.Number(mods.Count - shown))), "accent"));
        }
    }

    /// <summary>What goes between two names.</summary>
    private const string Separator = "  ›  ";

    /// <summary>The cell's text with the first names shown, and "N more" when some are left out, to measure.</summary>
    private static string LineOf(IReadOnlyList<ModOnFile> mods, int shown, HashSet<string> inConflict)
    {
        IEnumerable<string> names = mods.Take(shown).Select(mod => mod.Mod + (inConflict.Contains(mod.Mod) ? " ⚠" : ""));
        string line = string.Join(Separator, names);
        if (shown < mods.Count) line += Separator + Texts.Get("Files.Mods.More", ("count", Texts.Number(mods.Count - shown)));
        return line;
    }

    /// <summary>How wide a text is in the cell's font.</summary>
    private double TextWidth(string line)
    {
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        var formatted = new FormattedText(line, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, text.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    /// <summary>A piece of text in one of the theme's colours.</summary>
    private static Run Coloured(string words, string colour)
    {
        var run = new Run(words);
        run.SetResourceReference(TextElement.ForegroundProperty, colour);
        return run;
    }

    /// <summary>
    /// The list that drops down under the cell: each mod, numbered in run order, with its rule sets for the file (or
    /// "Armory changes only"). Esc or a click elsewhere closes it.
    /// </summary>
    private void OpenList()
    {
        if (mods.Count == 0) return;
        HashSet<string> inConflict = ModsInConflict();
        var lines = new StackPanel();
        // the names line up in a column of their own, as wide as the longest
        Grid.SetIsSharedSizeScope(lines, true);
        var heading = new TextBlock { Text = Texts.Get("Files.Mods.ListHeading"), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 4) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        lines.Children.Add(heading);
        for (int at = 0; at < mods.Count; at++) lines.Children.Add(ListLine(at + 1, mods[at], inConflict));
        // fifty mods are taller than the window: the list is held to the window's height (and the screen's) and scrolls
        double windowHeight = Window.GetWindow(this)?.ActualHeight ?? SystemParameters.WorkArea.Height;
        var scroller = new ScrollViewer
        {
            Content = lines, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(120, Math.Min(windowHeight, SystemParameters.WorkArea.Height) * 0.7),
            MaxWidth = Math.Max(240, SystemParameters.WorkArea.Width * 0.6),
            FocusVisualStyle = null,
        };
        var box = new Bevel { Child = scroller, Padding = new Thickness(10, 6, 10, 7) };
        box.SetResourceReference(Bevel.TopLeftProperty, "hi");
        box.SetResourceReference(Bevel.BottomRightProperty, "lo");
        box.SetResourceReference(Bevel.BackgroundProperty, "panel");
        var popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = box };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            popup.IsOpen = false;
        };
        // the keyboard goes into the list, so Escape closes it and the arrow keys scroll it; it comes back to the cell after
        IInputElement? before = Keyboard.FocusedElement;
        popup.Opened += (_, _) => scroller.Focus();
        popup.Closed += (_, _) =>
        {
            if (Keyboard.FocusedElement is null || scroller.IsKeyboardFocusWithin) before?.Focus();
        };
        popup.IsOpen = true;
    }

    /// <summary>One mod in the list: "1  WARNO Tactical Overhaul   WTO (Ammunition.ndf)".</summary>
    private static Grid ListLine(int number, ModOnFile mod, HashSet<string> inConflict)
    {
        var line = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ModName" });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var place = new TextBlock { Text = Texts.Number(number) };
        place.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var name = new TextBlock { Margin = new Thickness(0, 0, 16, 0) };
        name.Inlines.Add(new Run(mod.Mod));
        if (inConflict.Contains(mod.Mod)) name.Inlines.Add(Coloured(" ⚠", "caution"));
        var what = new TextBlock { Text = WhatRuns(mod) };
        what.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        Grid.SetColumn(name, 1);
        Grid.SetColumn(what, 2);
        line.Children.Add(place);
        line.Children.Add(name);
        line.Children.Add(what);
        return line;
    }

    /// <summary>What a mod runs on the file: its rule sets, and "Armory changes" when its Armory has some.</summary>
    private static string WhatRuns(ModOnFile mod)
    {
        List<string> parts = [.. mod.RuleSets];
        if (mod.HasArmoryChanges) parts.Add(Texts.Get("Files.Mods.ArmoryChanges"));
        return string.Join(", ", parts);
    }
}
