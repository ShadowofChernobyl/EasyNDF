using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Rules;

namespace EasyNdf.App.Files;

/// <summary>
/// Details' list of the rules that ran on a file, with a filter and Find above it. Rules sit under their groups, which
/// fold; each says what it did at its right; a click lists its edits.
/// </summary>
internal sealed partial class DetailsRuleList : DockPanel
{
    private const string FoldAllIcon = "M 1.5,1.5 H 12.5 V 12.5 H 1.5 Z M 4,7 H 10";
    private const string UnfoldAllIcon = "M 1.5,1.5 H 12.5 V 12.5 H 1.5 Z M 4,7 H 10 M 7,4 V 10";

    private readonly FlexRow tools = new() { Gap = 4, LineGap = 4, Margin = new Thickness(0, 0, 0, 5) };
    private readonly DockPanel findPart = new();
    private readonly TextBox findBox = new() { Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button foldAll = new() { Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0) };
    private readonly Path foldAllIcon = new() { Width = 14, Height = 14, StrokeThickness = 1, Stretch = Stretch.None };
    private readonly DetailsRowList list = new() { Focusable = true };
    private readonly DispatcherTimer findPause = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly List<(RuleFilter Filter, ToggleButton Button)> pills = [];

    private RunDetails? details;
    private RuleFilter filter = RuleFilter.All;

    /// <summary>The run shown last, so that a run new to the list opens on its errors.</summary>
    private RunRecord? shownRecord;
    private List<RuleLine> lines = [];
    private bool settingFind;

    /// <summary>Raised when another rule is chosen, or Find leaves the chosen rule's edits to be listed again.</summary>
    public event Action? ChosenChanged;

    /// <summary>Makes the list, showing nothing until <see cref="Show"/>.</summary>
    public DetailsRuleList()
    {
        LastChildFill = true;
        SetDock(tools, Dock.Top);
        Children.Add(tools);

        findBox.Tag = Texts.Get("Files.Details.Find");
        findBox.ToolTip = Texts.Get("Files.Details.Find.Tip");
        findBox.TextChanged += (_, _) =>
        {
            if (settingFind) return;
            findPause.Stop();
            findPause.Start();
        };
        findPause.Tick += (_, _) =>
        {
            findPause.Stop();
            Narrowed();
        };
        foldAllIcon.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source = foldAll });
        foldAll.Content = foldAllIcon;
        foldAll.Click += (_, _) => FoldOrUnfoldAll();
        SetDock(foldAll, Dock.Right);
        findPart.Children.Add(foldAll);
        findPart.Children.Add(findBox);
        FlexRow.SetGrow(findPart, 1);
        // as wide as it takes to read its hint, with Fold all beside it
        FlexRow.SetBasis(findPart, 158);

        list.LineOf = item => item switch
        {
            RuleLine { IsGroup: true } group => GroupRow(group.Index),
            RuleLine rule => RuleRow(rule.Index),
            _ => NothingToShow((string)item),
        };
        list.PreviewKeyDown += ListKeyDown;
        var box = new Bevel { Child = new WholeRows { Child = list } };
        box.SetResourceReference(Bevel.TopLeftProperty, "lo");
        box.SetResourceReference(Bevel.BottomRightProperty, "hi");
        box.SetResourceReference(Bevel.BackgroundProperty, "well");
        Children.Add(box);
    }

    /// <summary>What Find looks for.</summary>
    public string Find => findBox.Text.Trim();

    /// <summary>Whether the keyboard is in the list, for the keys that fold its groups.</summary>
    public void GiveKeysToList() => list.Focus();

    /// <summary>Shows a run's rules; a new file starts with every rule listed and nothing to find.</summary>
    public void Show(RunDetails run, bool newFile)
    {
        details = run;
        if (newFile)
        {
            filter = RuleFilter.All;
            findPause.Stop();
            settingFind = true;
            findBox.Text = "";
            settingFind = false;
        }
        // a run with values that would stop mod generation opens on the rules that wrote them
        if (!ReferenceEquals(run.Record, shownRecord) && run.Count(RuleFilter.Errors) > 0) filter = RuleFilter.Errors;
        shownRecord = run.Record;
        ShowTools();
        // a filter left on from the run before may list nothing now
        if (filter != RuleFilter.All && run.Count(filter) == 0) filter = RuleFilter.All;
        ShowPressedPill();
        ChooseOneThatShows();
        Draw(fromTheTop: true);
        RevealChosen();
    }

    /// <summary>
    /// Lists the rules the file's status points at, the most serious it has: those with errors, else those with
    /// warnings, else those that changed nothing; one of them is chosen, its edits shown. Find is emptied so they all
    /// show. A run with none of them is left as it is.
    /// </summary>
    public void ShowWhatStatusSays()
    {
        if (details is not RunDetails run) return;
        foreach (RuleFilter said in (RuleFilter[])[RuleFilter.Errors, RuleFilter.Warnings, RuleFilter.ChangedNothing])
        {
            if (run.Count(said) == 0) continue;
            findPause.Stop();
            settingFind = true;
            findBox.Text = "";
            settingFind = false;
            filter = said;
            ShowPressedPill();
            Narrowed();
            return;
        }
    }

    /// <summary>The filters that apply to the run, then Find and Fold all, which keep together.</summary>
    private void ShowTools()
    {
        RunDetails run = details!;
        tools.Children.Clear();
        pills.Clear();
        AddPill(RuleFilter.All, Texts.Get("Files.Details.Filter.All"), Texts.Get("Files.Details.Filter.All.Tip"), run.RuleCount, icon: null);
        AddPill(RuleFilter.WithEdits, Texts.Get("Files.Details.Filter.WithEdits"), Texts.Get("Files.Details.Filter.WithEdits.Tip"), run.Count(RuleFilter.WithEdits), icon: null);
        if (run.Count(RuleFilter.ChangedNothing) is > 0 and var idle)
            AddPill(RuleFilter.ChangedNothing, Texts.Get("Files.Details.Filter.ChangedNothing"), Texts.Get("Files.Details.Filter.ChangedNothing.Tip"), idle, NoticeKind.Caution);
        if (run.Count(RuleFilter.Skipped) is > 0 and var skipped)
            AddPill(RuleFilter.Skipped, Texts.Get("Files.Details.Filter.Skipped"), Texts.Get("Files.Details.Filter.Skipped.Tip"), skipped, NoticeKind.Caution);
        if (run.Count(RuleFilter.Warnings) is > 0 and var warned)
            AddPill(RuleFilter.Warnings, Texts.Get("Files.Details.Filter.Warnings"), Texts.Get("Files.Details.Filter.Warnings.Tip"), warned, NoticeKind.Caution);
        if (run.Count(RuleFilter.Errors) is > 0 and var wrong)
            AddPill(RuleFilter.Errors, Texts.Get("Files.Details.Filter.Errors"), Texts.Get("Files.Details.Filter.Errors.Tip"), wrong, NoticeKind.Problem);
        tools.Children.Add(findPart);
        foldAll.Visibility = run.HasGroups ? Visibility.Visible : Visibility.Collapsed;
        ShowFoldAll();
    }

    /// <summary>A filter's button, with the icon of what it finds when that is something to look at: a caution, or an error in the error colour.</summary>
    private void AddPill(RuleFilter which, string label, string tip, int count, NoticeKind? icon)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon is NoticeKind kind)
        {
            FrameworkElement shown = NoticeLayer.Icon(kind, 12);
            shown.Margin = new Thickness(0, 0, 5, 0);
            shown.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(shown);
        }
        var words = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        if (icon == NoticeKind.Problem) words.SetResourceReference(TextBlock.ForegroundProperty, "warn");
        content.Children.Add(words);
        TextBlock counted = DetailsText.Line("muted");
        counted.Text = Texts.Number(count);
        counted.FontSize = 11.5;
        counted.Margin = new Thickness(5, 0, 0, 0);
        counted.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(counted);
        var pill = new ToggleButton { Content = content, Height = 24, ToolTip = tip };
        pill.SetResourceReference(StyleProperty, "PressedToggle");
        pill.Padding = new Thickness(8, 0, 8, 0);
        pill.Click += (_, _) =>
        {
            filter = which;
            ShowPressedPill();
            Narrowed();
        };
        pills.Add((which, pill));
        tools.Children.Add(pill);
    }

    private void ShowPressedPill()
    {
        foreach ((RuleFilter which, ToggleButton pill) in pills) pill.IsChecked = which == filter;
    }

    /// <summary>The filter or Find changed: the rules that show, and the chosen one's edits, are listed again.</summary>
    private void Narrowed()
    {
        if (details is null) return;
        ChooseOneThatShows();
        Draw(fromTheTop: true);
        RevealChosen();
        ChosenChanged?.Invoke();
    }

    /// <summary>When the chosen rule does not show, the first that does is chosen.</summary>
    private void ChooseOneThatShows()
    {
        RunDetails run = details!;
        int chosen = run.ChosenRule;
        if (chosen >= 0 && run.Shows(chosen, filter, Find)) return;
        for (int index = 0; index < run.Stats.Count; index++)
            if (run.Shows(index, filter, Find))
            {
                run.ChosenRule = index;
                return;
            }
    }

    private bool IsNarrowed => filter != RuleFilter.All || Find != "";

    /// <summary>Lists the rules. Tuned for speed: choosing a rule only marks its line; a list is made again only when its lines change. Keep it that way when tidying.</summary>
    private void Draw(bool fromTheTop)
    {
        lines = details!.Lines(filter, Find);
        if (lines.Count > 0)
        {
            list.ShowLines(lines, fromTheTop);
            return;
        }
        string nothing = Find != "" ? Texts.Get("Files.Details.NoRulesMatch", ("text", Find)) : Texts.Get("Files.Details.NoRulesToShow");
        list.ShowLines(new object[] { nothing }, fromTheTop);
    }

    private void RevealChosen()
    {
        int at = lines.IndexOf(new RuleLine(details!.ChosenRule, IsGroup: false));
        if (at >= 0) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => list.Reveal(at));
    }

    /// <summary>Chooses a rule: its line is marked, and its edits are listed.</summary>
    private void Choose(int rule, bool reveal)
    {
        RunDetails run = details!;
        MarkChosen(run.ChosenRule, false);
        run.ChosenRule = rule;
        MarkChosen(rule, true);
        if (reveal)
        {
            int at = lines.IndexOf(new RuleLine(rule, IsGroup: false));
            if (at >= 0) list.Reveal(at);
        }
        ChosenChanged?.Invoke();
    }

    private void MarkChosen(int rule, bool chosen)
    {
        int at = lines.IndexOf(new RuleLine(rule, IsGroup: false));
        if (at >= 0 && list.LineAt(at) is Border line) ShowChosen(line, chosen);
    }

    private static void ShowChosen(Border line, bool chosen)
    {
        if (chosen) line.SetResourceReference(Border.BackgroundProperty, "sel");
        else line.Background = Brushes.Transparent;
    }

    private void ListKeyDown(object sender, KeyEventArgs e)
    {
        if (details is null || Keyboard.Modifiers != ModifierKeys.None) return;
        List<RuleLine> rules = [.. lines.Where(line => !line.IsGroup)];
        if (rules.Count == 0) return;
        int at = rules.FindIndex(line => line.Index == details.ChosenRule);
        int? to = e.Key switch
        {
            Key.Up => at - 1,
            Key.Down => at + 1,
            Key.PageUp => at - 10,
            Key.PageDown => at + 10,
            Key.Home => 0,
            Key.End => rules.Count - 1,
            _ => null,
        };
        if (to is not int target) return;
        e.Handled = true;
        Choose(rules[Math.Clamp(target, 0, rules.Count - 1)].Index, reveal: true);
    }

    /// <summary>The keys that fold every group, as on the Mods tab's rule list and the file list.</summary>
    public bool HandleFoldKey(KeyEventArgs e)
    {
        bool collapse = Pages.RulesPage.IsCollapseAllKey(e), expand = Pages.RulesPage.IsExpandAllKey(e);
        if (details is null || !details.HasGroups || !(collapse || expand)) return false;
        e.Handled = true;
        SetAllFolded(collapse);
        return true;
    }

    private void FoldOrUnfoldAll() => SetAllFolded(!AllFolded);

    private bool AllFolded
    {
        get
        {
            RunDetails run = details!;
            return Enumerable.Range(0, run.Stats.Count).All(index => run.Stats[index].Kind != "Group" || run.Folds.Contains(index));
        }
    }

    private void SetAllFolded(bool folded)
    {
        RunDetails run = details!;
        for (int index = 0; index < run.Stats.Count; index++)
        {
            if (run.Stats[index].Kind != "Group") continue;
            if (folded) run.Folds.Add(index);
            else run.Folds.Remove(index);
        }
        ShowFoldAll();
        Draw(fromTheTop: false);
    }

    private void ShowFoldAll()
    {
        if (details is null || !details.HasGroups) return;
        bool all = AllFolded;
        foldAllIcon.Data = Geometry.Parse(all ? UnfoldAllIcon : FoldAllIcon);
        foldAll.ToolTip = Texts.Get(all ? "Files.Details.UnfoldAll" : "Files.Details.FoldAll");
    }

    private void ToggleFold(int group)
    {
        // with a filter or Find on, every group shows what it has
        if (IsNarrowed) return;
        HashSet<int> folded = details!.Folds;
        if (!folded.Remove(group)) folded.Add(group);
        ShowFoldAll();
        Draw(fromTheTop: false);
    }

    private static FrameworkElement NothingToShow(string text)
    {
        TextBlock line = DetailsText.Line("muted");
        line.Text = text;
        line.Padding = new Thickness(10);
        return line;
    }

    /// <summary>A group's line: a click folds it, and at its right it says what its rules did, folded or not.</summary>
    private FrameworkElement GroupRow(int group)
    {
        RunDetails run = details!;
        IReadOnlyList<int> rules = run.RulesIn(group);
        int edits = 0, idle = 0, skipped = 0, warned = 0;
        foreach (int rule in rules)
        {
            RuleStats stats = run.Stats[rule];
            edits += stats.Edits;
            skipped += stats.Skipped;
            if (RunRecord.IsIdle(stats)) idle++;
            if (run.WarningsOf(rule).Count > 0) warned++;
        }
        var line = new Bevel { Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(5, 3, 8, 3), Cursor = Cursors.Hand };
        line.SetResourceReference(Bevel.BottomRightProperty, "line");
        line.SetResourceReference(Bevel.BackgroundProperty, "panel");
        string ruleCount = Texts.CountWide("Files.Details.Rules.Count", rules.Count);
        line.ToolTip = idle > 0 ? Texts.Get("Files.Details.GroupTip.Idle", ("rules", ruleCount), ("idle", Texts.Number(idle))) : ruleCount;

        TextBlock said = DetailsText.Line("muted");
        said.TextWrapping = TextWrapping.NoWrap;
        said.FontSize = 11.5;
        said.VerticalAlignment = VerticalAlignment.Center;
        int problems = idle + warned;
        if (problems > 0)
        {
            said.Inlines.Add(DetailsText.Piece(Texts.Number(problems), "caution"));
            said.Inlines.Add(DetailsText.Piece(" · ", null));
        }
        if (skipped > 0)
        {
            said.Inlines.Add(DetailsText.Piece(DetailsCounts.Skipped(skipped), "caution"));
            said.Inlines.Add(DetailsText.Piece(" · ", null));
        }
        said.Inlines.Add(DetailsText.Piece(
            edits > 0 ? DetailsCounts.Edits(edits)
            : rules.Count > 0 && rules.All(rule => run.Stats[rule].Kind == "File") ? Texts.CountWide("Files.Details.Files.Count", rules.Count)
            : ruleCount, null));
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (problems > 0)
        {
            FrameworkElement icon = NoticeLayer.Icon(NoticeKind.Caution, 12);
            icon.Margin = new Thickness(0, 0, 4, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            meta.Children.Add(icon);
        }
        meta.Children.Add(said);

        bool folded = !IsNarrowed && run.Folds.Contains(group);
        TextBlock arrow = DetailsText.Line();
        arrow.Text = folded ? "▸" : "▾";
        arrow.FontSize = 11;
        arrow.HorizontalAlignment = HorizontalAlignment.Center;
        arrow.VerticalAlignment = VerticalAlignment.Center;
        var fold = new Bevel { Width = 16, Height = 16, Child = arrow, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        fold.SetResourceReference(Bevel.TopLeftProperty, "hi");
        fold.SetResourceReference(Bevel.BottomRightProperty, "lo");
        fold.SetResourceReference(Bevel.BackgroundProperty, "panel");
        FrameworkElement glyph = FolderGlyph.For(Rules.RuleRow.FolderSymbol, "accent", 16);
        glyph.Margin = new Thickness(0, 0, 6, 0);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        TextBlock name = DetailsText.Line();
        name.VerticalAlignment = VerticalAlignment.Center;
        DetailsText.Add(name.Inlines, run.Stats[group].Name, null, Find);

        var row = new DockPanel();
        SetDock(meta, Dock.Right);
        SetDock(fold, Dock.Left);
        SetDock(glyph, Dock.Left);
        row.Children.Add(meta);
        row.Children.Add(fold);
        row.Children.Add(glyph);
        row.Children.Add(name);
        line.Child = row;
        line.MouseEnter += (_, _) => arrow.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        line.MouseLeave += (_, _) => arrow.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        line.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            list.Focus();
            ToggleFold(group);
        };
        return line;
    }

    /// <summary>A rule's line: its kind's symbol, its name, and what it did.</summary>
    private FrameworkElement RuleRow(int rule)
    {
        RunDetails run = details!;
        RuleStats stats = run.Stats[rule];
        bool isIdle = RunRecord.IsIdle(stats);
        IReadOnlyList<RunValueError> ruleErrors = run.ErrorsOf(rule);
        int errors = ruleErrors.Count;
        // a rule that wrote values that would stop mod generation (or crash the game) reads as an error: its icon and name in the error colour
        string nameColour = errors > 0 ? "warn" : isIdle ? "muted" : "ink";
        var line = new Border
        {
            // a rule in a group sits in from its group's line
            Padding = new Thickness(run.GroupOf(rule) >= 0 ? 26 : 8, 3, 8, 3),
            Cursor = Cursors.Hand,
            ToolTip = errors > 0 ? DetailsCounts.RuleTip(stats) + "\n" + DetailsCounts.WhatErrorsDo(ruleErrors) : DetailsCounts.RuleTip(stats),
        };
        ShowChosen(line, run.ChosenRule == rule);
        (string symbol, string colour) = Rules.RuleRow.SymbolOf(stats.Kind);
        FrameworkElement glyph = errors > 0 ? NoticeLayer.Icon(NoticeKind.Problem, 16) : FolderGlyph.For(symbol, colour, 18);
        glyph.Margin = errors > 0 ? new Thickness(1, 1, 7, 1) : new Thickness(0, 0, 6, 0);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        TextBlock name = DetailsText.Line(nameColour);
        Match named = NamedAfterDescriptor().Match(stats.Name);
        if (named.Success)
        {
            DetailsText.Add(name.Inlines, named.Groups[1].Value, errors > 0 ? null : "muted", Find);
            name.Inlines.Add(DetailsText.Piece(" ", null));
            DetailsText.Add(name.Inlines, named.Groups[2].Value, null, Find);
        }
        else DetailsText.Add(name.Inlines, stats.Name, null, Find);

        FrameworkElement meta = Said(rule, stats);
        var row = new DockPanel();
        SetDock(meta, Dock.Right);
        SetDock(glyph, Dock.Left);
        row.Children.Add(meta);
        row.Children.Add(glyph);
        row.Children.Add(name);
        line.Child = row;
        line.MouseEnter += (_, _) => name.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        line.MouseLeave += (_, _) => name.SetResourceReference(TextBlock.ForegroundProperty, nameColour);
        line.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            list.Focus();
            if (run.ChosenRule != rule) Choose(rule, reveal: false);
        };
        return line;
    }

    /// <summary>What a rule did, at its line's right.</summary>
    private FrameworkElement Said(int rule, RuleStats stats)
    {
        bool warned = details!.WarningsOf(rule).Count > 0;
        TextBlock said = DetailsText.Line("muted");
        said.TextWrapping = TextWrapping.NoWrap;
        said.FontSize = 11.5;
        void Word(string text) => said.Inlines.Add(DetailsText.Piece(text, null));
        void Caution(string text) => said.Inlines.Add(DetailsText.Piece(text, "caution"));
        switch (stats.Kind)
        {
            case "File": Word(Texts.Get("Files.Details.Meta.File")); break;
            case "Check":
                if (warned) Caution(Texts.Get("Files.Details.Meta.Failed"));
                else Word(Texts.Get("Files.Details.Meta.Passed"));
                break;
            case "Variable": Word(Texts.Get("Files.Details.Meta.Variable")); break;
            case "Text": Word(Texts.Get("Files.Details.Meta.Texts")); break;
            default:
                if (!RunDetails.IsOn(stats)) Word(Texts.Get("Files.Details.Meta.Off"));
                else if (stats.Edits == 0) Caution(Texts.Get("Files.Details.Meta.NoEdits"));
                else said.Inlines.Add(DetailsText.Piece(DetailsCounts.Edits(stats.Edits), null));
                break;
        }
        if (stats.Skipped > 0)
        {
            said.Inlines.Add(DetailsText.Piece(" · ", null));
            Caution(DetailsCounts.Skipped(stats.Skipped));
        }
        if (!warned)
        {
            said.Margin = new Thickness(6, 1, 0, 0);
            return said;
        }
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 1, 0, 0) };
        FrameworkElement icon = NoticeLayer.Icon(NoticeKind.Caution, 12);
        icon.Margin = new Thickness(0, 2, 4, 0);
        meta.Children.Add(icon);
        meta.Children.Add(said);
        return meta;
    }

    /// <summary>A rule named after its descriptor ("Unit_T80 (Grenadiers): more armour"), whose own words are the ones to read.</summary>
    [GeneratedRegex(@"^([^\s:]+(?: \([^)]*\))?:) (.*)$", RegexOptions.Singleline)]
    private static partial Regex NamedAfterDescriptor();
}
