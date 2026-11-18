using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Rules;

namespace EasyNdf.App.Files;

/// <summary>
/// The rules and edits of Details, side by side when the pane is wide and one above the other when it is not, with a
/// bar between them.
/// </summary>
internal sealed class DetailsColumns(UIElement rules, SplitBar bar, UIElement edits) : Panel
{
    /// <summary>From this wide, the two go side by side.</summary>
    private const double SideBySideFrom = 900;

    /// <summary>The rules' part of the room at first: at the first window size, four filters and Find fit on one line.</summary>
    public const double FirstShare = 0.46;

    /// <summary>The narrowest the rules and the edits are, side by side.</summary>
    private const double LeastRules = 270, LeastEdits = 320;

    /// <summary>The lowest the rules and the edits are, one above the other, when there is room.</summary>
    private const double LeastHeight = 90;

    /// <summary>The rules' part of the room.</summary>
    public double Share
    {
        get;
        set
        {
            field = value;
            InvalidateMeasure();
        }
    } = FirstShare;

    /// <summary>Whether the two are side by side.</summary>
    public bool IsSideBySide { get; private set; }

    /// <summary>Adds the three parts, once the panel exists.</summary>
    public void AddParts()
    {
        Children.Add(rules);
        Children.Add(bar);
        Children.Add(edits);
    }

    private double RulesSize(double room) => IsSideBySide
        ? Math.Round(Math.Clamp(room * Share, LeastRules, Math.Max(LeastRules, room - LeastEdits)))
        : Math.Round(Math.Clamp(room * Share, Math.Min(LeastHeight, room / 2), Math.Max(Math.Min(LeastHeight, room / 2), room - Math.Min(LeastHeight, room / 2))));

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height;
        bool side = width >= SideBySideFrom;
        if (side != IsSideBySide || bar.LiesFlat == side)
        {
            IsSideBySide = side;
            bar.LiesFlat = !side;
        }
        if (side)
        {
            double room = Math.Max(0, width - 9), first = RulesSize(room);
            rules.Measure(new Size(first, height));
            bar.Measure(new Size(9, height));
            edits.Measure(new Size(Math.Max(0, room - first), height));
        }
        else
        {
            double room = Math.Max(0, height - 9), first = RulesSize(room);
            rules.Measure(new Size(width, first));
            bar.Measure(new Size(width, 9));
            edits.Measure(new Size(width, Math.Max(0, room - first)));
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (IsSideBySide)
        {
            double room = Math.Max(0, finalSize.Width - 9), first = RulesSize(room);
            rules.Arrange(new Rect(0, 0, first, finalSize.Height));
            bar.Arrange(new Rect(first, 0, 9, finalSize.Height));
            edits.Arrange(new Rect(first + 9, 0, Math.Max(0, room - first), finalSize.Height));
        }
        else
        {
            double room = Math.Max(0, finalSize.Height - 9), first = RulesSize(room);
            rules.Arrange(new Rect(0, 0, finalSize.Width, first));
            bar.Arrange(new Rect(0, first, finalSize.Width, 9));
            edits.Arrange(new Rect(0, first + 9, finalSize.Width, Math.Max(0, room - first)));
        }
        return finalSize;
    }
}

/// <summary>
/// The Files tab's Details pane: what the chosen file's last run did. A line says how the run went; below it are the
/// rules that ran, and the edits of the rule chosen among them.
/// </summary>
public sealed class DetailsPanel : DockPanel
{
    /// <summary>The name the rules' part of the room is remembered under.</summary>
    private const string ShareKey = "detailsRulesShare";

    /// <summary>How many warnings of no rule are listed before the rest are counted.</summary>
    private const int ShownWarnings = 20;

    private readonly FlexRow runLine = new() { Gap = 10, LineGap = 0, Margin = new Thickness(0, 0, 0, 5) };
    private readonly StackPanel banners = new();
    private readonly ScrollViewer bannersScroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly DetailsRuleList rules = new();
    private readonly DetailsEditList edits = new();
    private readonly SplitBar bar = new();
    private readonly DetailsColumns columns;
    private readonly Bevel nothing = Sunken();

    private FileEntry? shownRow;
    private RunDetails? shown;

    /// <summary>Makes the pane, showing nothing until <see cref="Show"/>.</summary>
    public DetailsPanel()
    {
        LastChildFill = true;
        columns = new DetailsColumns(rules, bar, edits);
        columns.AddParts();
        if ((double?)AppData.Current.State["sizes"]?[ShareKey] is double share) columns.Share = share;
        bar.Moved += at =>
        {
            double room = (columns.IsSideBySide ? columns.ActualWidth : columns.ActualHeight) - 9;
            if (room > 0) columns.Share = Math.Clamp(at / room, 0.22, 0.78);
        };
        bar.Reset += () => columns.Share = DetailsColumns.FirstShare;
        bar.Settled += () => Pages.FilesPage.RememberSize(ShareKey, columns.Share);
        rules.ChosenChanged += ShowChosenRule;
        edits.OpenRuleClicked += OpenChosenRule;

        SetDock(runLine, Dock.Top);
        bannersScroller.Content = banners;
        SetDock(bannersScroller, Dock.Top);
        Children.Add(runLine);
        Children.Add(bannersScroller);
        Children.Add(columns);
        Children.Add(nothing);
        // the banners never take the room of the rules
        SizeChanged += (_, e) =>
        {
            if (e.HeightChanged) bannersScroller.MaxHeight = Math.Max(48, e.NewSize.Height * 0.4);
        };
    }

    private static Bevel Sunken()
    {
        var box = new Bevel();
        box.SetResourceReference(Bevel.TopLeftProperty, "lo");
        box.SetResourceReference(Bevel.BottomRightProperty, "hi");
        box.SetResourceReference(Bevel.BackgroundProperty, "well");
        return box;
    }

    /// <summary>Shows a file's last run; with no file or no run, says what to do.</summary>
    public void Show(FileEntry? row)
    {
        if (row?.LastRun is not RunRecord run)
        {
            shown = null;
            shownRow = row;
            ShowNothing(row);
            return;
        }
        bool newFile = !ReferenceEquals(row, shownRow);
        // a new run of the file stays on the rule it was on
        if (!newFile && shown is { Record: var before } && !ReferenceEquals(before, run)) KeepPlace(before, run);
        shownRow = row;
        shown = new RunDetails(run);
        nothing.Visibility = Visibility.Collapsed;
        runLine.Visibility = Visibility.Visible;
        columns.Visibility = Visibility.Visible;
        ShowRunLine(row, run);
        ShowBanners(run);
        rules.Show(shown, newFile);
        ShowChosenRule();
    }

    /// <summary>Lists the rules the file's status points at (its errors, warnings or rules that changed nothing) and shows the first.</summary>
    public void ShowWhatStatusSays() => rules.ShowWhatStatusSays();

    private static void KeepPlace(RunRecord before, RunRecord run)
    {
        List<RuleStats> was = before.Result.Stats, now = run.Result.Stats;
        if (was.Count != now.Count || !was.Select(rule => rule.Name).SequenceEqual(now.Select(rule => rule.Name))) return;
        run.ShownRule = before.ShownRule;
        run.FoldedGroups = before.FoldedGroups;
    }

    /// <summary>In place of the rules: a file with no run yet can be given one, here and now.</summary>
    private void ShowNothing(FileEntry? row)
    {
        runLine.Visibility = Visibility.Collapsed;
        bannersScroller.Visibility = Visibility.Collapsed;
        columns.Visibility = Visibility.Collapsed;
        nothing.Visibility = Visibility.Visible;
        TextBlock words = DetailsText.Line("muted");
        words.TextAlignment = TextAlignment.Center;
        words.Text = row is null ? Texts.Get("Files.Details.ClickARow") : Texts.Get("Files.Details.NoRunYet", ("file", row.Name));
        var middle = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12), Children = { words } };
        if (row is not null)
        {
            var preview = new Button { Content = Texts.Get("Files.Preview"), ToolTip = Texts.Get("Files.Details.PreviewThis.Tip"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            preview.Click += (_, _) => _ = MainView.Of(this).FilesTab.PreviewOnly(row);
            middle.Children.Add(preview);
        }
        nothing.Child = middle;
    }

    /// <summary>The file, the rule sets it ran with, how it ended, and what it came to.</summary>
    private void ShowRunLine(FileEntry row, RunRecord run)
    {
        runLine.Children.Clear();
        TextBlock named = DetailsText.Line();
        System.Windows.Documents.Run file = DetailsText.Piece(DetailsText.Soft(row.Name), null);
        file.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "mono");
        file.FontSize = 12;
        named.Inlines.Add(file);
        if (run.RuleSets != "")
        {
            named.Inlines.Add(DetailsText.Piece(" " + Texts.Get("Files.Details.With") + " ", "muted"));
            named.Inlines.Add(DetailsText.Piece(run.RuleSets, null));
        }
        runLine.Children.Add(named);

        int errors = run.ValueErrors.Count;
        string what = run.Held && errors > 0 ? DetailsCounts.NotExportedFor(run.ValueErrors)
            : run.Held ? Texts.Get("Files.Details.NotExported")
            : run.Wrote ? (run.Unchanged ? Texts.Get("Files.Details.ExportedUnchanged") : Texts.Get("Files.Details.Exported"))
            : run.Unchanged ? Texts.Get("Files.Details.NothingChanged")
            : Texts.Get("Files.Details.PreviewOnly");
        TextBlock ended = DetailsText.Line(run.Held ? "warn" : null);
        ended.Text = what;
        // a Preview says what an export would hold back, after saying it saved nothing
        if (!run.Held && errors > 0)
            ended.Inlines.Add(DetailsText.Piece(" " + DetailsCounts.WhatErrorsDo(run.ValueErrors), "warn"));
        if (run.Wrote && !run.Held)
        {
            FrameworkElement done = NoticeLayer.Icon(NoticeKind.Done, 14);
            done.Margin = new Thickness(0, 0, 5, 0);
            done.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(done, Dock.Left);
            runLine.Children.Add(new DockPanel { Children = { done, ended } });
        }
        else runLine.Children.Add(ended);

        TextBlock counts = DetailsText.Line("muted");
        counts.Text = Texts.Get("Files.Details.RunCounts", ("edits", DetailsCounts.Edits(run.Result.Edits)),
            ("holds", FileStatus.Holds(run.Info.Descriptors, run.Info.Constants, run.Info.Unnamed)), ("milliseconds", Texts.Number(run.Milliseconds)));
        runLine.Children.Add(counts);
    }

    /// <summary>
    /// What goes above the rules: that the file is not valid NDF, values that would stop mod generation where no rule
    /// shows an edit, and any warnings that are not about a rule.
    /// </summary>
    private void ShowBanners(RunRecord run)
    {
        banners.Children.Clear();
        if (run.Result.ParseError != "")
            banners.Children.Add(Banner(NoticeKind.Problem, "warn",
                (run.Held ? Texts.Get("Files.Details.InvalidHeld") : Texts.Get("Files.Details.InvalidWouldBeHeld"))
                + "\n" + Texts.Get("Files.Details.InvalidProblem", ("problem", run.Result.ParseError)) + "\n" + Texts.Get("Files.Details.InvalidCause")));
        // the values that would stop mod generation, then those that would crash the game, each kind in a box of its own
        foreach (bool crashing in (bool[])[false, true])
        {
            List<RunValueError> otherErrors = [.. shown!.OtherErrors.Where(error => error.Error.Problem.CrashesGame == crashing)];
            if (otherErrors.Count == 0) continue;
            IEnumerable<string> lines = otherErrors.Take(ShownWarnings).Select(DetailsCounts.ErrorLine);
            if (otherErrors.Count > ShownWarnings) lines = lines.Append(Texts.Get("Files.Details.MoreWarnings", ("count", Texts.Number(otherErrors.Count - ShownWarnings))));
            string heading = Texts.Get(crashing ? "Files.Details.ValueErrors.Other.Crash" : "Files.Details.ValueErrors.Other");
            banners.Children.Add(Banner(NoticeKind.Problem, "warn", heading + "\n" + string.Join("\n", lines)));
        }
        IReadOnlyList<RunWarning> others = shown.OtherWarnings;
        if (others.Count > 0)
        {
            // the first ones, then how many more: a rule that warns on every descriptor made a wall of text
            IEnumerable<string> lines = others.Take(ShownWarnings).Select(warning =>
                (warning.Type == "number" ? Texts.Get("Files.Details.RuleWarning", ("rule", warning.Rule)) : Texts.Get("Files.Details.CheckFailed", ("rule", warning.Rule))) + " " + warning.Message);
            if (others.Count > ShownWarnings) lines = lines.Append(Texts.Get("Files.Details.MoreWarnings", ("count", Texts.Number(others.Count - ShownWarnings))));
            banners.Children.Add(Banner(NoticeKind.Caution, "caution", string.Join("\n", lines)));
        }
        bannersScroller.Visibility = banners.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Bevel Banner(NoticeKind kind, string colour, string text)
    {
        FrameworkElement icon = NoticeLayer.Icon(kind, 18);
        icon.Margin = new Thickness(0, 0, 9, 0);
        SetDock(icon, Dock.Left);
        TextBlock words = DetailsText.Line(colour);
        words.Text = DetailsText.Soft(text);
        Bevel box = Sunken();
        box.Padding = new Thickness(8, 6, 8, 6);
        box.Margin = new Thickness(0, 0, 0, 5);
        box.Child = new DockPanel { Children = { icon, words } };
        return box;
    }

    /// <summary>Lists the edits of the rule chosen.</summary>
    private void ShowChosenRule()
    {
        if (shown is null || shownRow is null) return;
        edits.Show(shown, shown.ChosenRule, rules.Find, shownRow);
    }

    /// <summary>Open Rule: the Mods tab, with the rule's mod and rule set open, and the rule's window.</summary>
    private void OpenChosenRule()
    {
        if (shown is null || shownRow is null || shown.ChosenRule < 0) return;
        MainView.Of(this).RulesTab.OpenRuleOfRun(shown.Record, shown.ChosenRule, shownRow);
    }

    /// <summary>
    /// A key while the keyboard is in the pane: the pane takes it, so that what would act on the file list above, such
    /// as Delete, cannot remove a file by surprise.
    /// </summary>
    public void HandleShortcut(System.Windows.Input.KeyEventArgs e) => rules.HandleFoldKey(e);

    /// <summary>Settings › Wrap text in lists changed: the edits wrap, or keep to one line each.</summary>
    public void ApplyListWrap() => edits.ApplyListWrap();
}
