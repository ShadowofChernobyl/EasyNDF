using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Rules;

namespace EasyNdf.App.Files;

/// <summary>
/// The Files tab's Details section (the HTML app's <c>renderResults</c>): what the chosen file's last run did. A summary,
/// any problems, a table of the preset's rules with their counts, and the edits of the rule picked in that table.
/// </summary>
public sealed class DetailsPanel : StackPanel
{
    private readonly Action<string> showTitle;
    private double? chosenEditListHeight = (double?)AppData.Current.State["sizes"]?["detBox"];

    /// <summary>The space each block wants above and below it, before <see cref="SetGaps"/> merges them.</summary>
    private readonly Dictionary<FrameworkElement, (double Above, double Below)> wantedSpace = [];

    /// <summary>Makes the panel, showing nothing until <see cref="Show"/>.</summary>
    /// <param name="showTitle">Shows the section's heading, which names the file and its preset.</param>
    public DetailsPanel(Action<string> showTitle) => this.showTitle = showTitle;

    /// <summary>Shows a file's last run; with no file, says to click one.</summary>
    public void Show(FileEntry? row)
    {
        Children.Clear();
        wantedSpace.Clear();
        ShowRun(row);
        SetGaps();
    }

    private void ShowRun(FileEntry? row)
    {
        if (row?.LastRun is not RunRecord run)
        {
            showTitle("Details");
            AddEmpty(row is null ? "Click a row to see what its last run changed." : "No run yet for " + row.Name + ". Use Preview or Run export.");
            return;
        }
        RunResult result = run.Result;
        showTitle("Details: " + row.Name + " with " + run.Preset.Name);
        List<List<EditLogEntry>> logs = result.Logs;
        if (run.ShownRule is not int shown || shown >= result.Stats.Count)
        {
            int first = logs.FindIndex(log => log.Count > 0);
            run.ShownRule = first < 0 ? 0 : first;
        }

        string what = run.Held ? "Not exported."
            : run.Wrote ? (run.Unchanged ? "Exported as an unchanged copy." : "Exported.")
            : run.Unchanged ? "The rules changed nothing, so it was not exported."
            : "Preview only, nothing was saved.";
        AddParagraph(FileStatus.Count(result.Edits) + " edits across " + FileStatus.Holds(run.Info.Descriptors, run.Info.Constants, run.Info.Unnamed)
            + ", " + run.Milliseconds + " ms. " + what + " Click a rule to list its edits; double-click an edit to see it in the exported file.", "muted", 0, 8);

        if (result.ParseError != "")
            AddParagraph("The file these rules produce would not be valid NDF, so it "
                + (run.Held ? "was held back: it was not written to its export folder. \"Save anyway\" on the row saves it so you can look." : "would be held back on export.")
                + "\nProblem found when reading it back: " + result.ParseError + ".\nThe usual cause is a rule that writes text with a bracket or quote that is never closed, such as an Insert, Set To, Append or Find and replace value.",
                "warn", 12.5, 12.5);
        if (result.Warnings.Count > 0)
            AddParagraph(string.Join("\n", result.Warnings.Select(w => (w.Type == "number" ? "Rule \"" + w.Rule + "\": " : "Check \"" + w.Rule + "\" failed. ") + w.Message)),
                "caution", 12.5, 12.5);
        List<RuleStats> idle = result.Stats.Where(RunRecord.IsIdle).ToList();
        if (idle.Count > 0 && result.Edits > 0)
            AddParagraph(FileStatus.IdleText(idle.Count) + " in this file: " + string.Join(", ", idle.Take(8).Select(s => "\"" + s.Name + "\""))
                + (idle.Count > 8 ? " and " + (idle.Count - 8) + " more" : "") + ". That is not an error. If it should have, a name or value it looks for may have changed.",
                "caution", 0, 8);

        AddRuleTable(row, run);
        if (result.Stats.Any(s => s.Skipped > 0))
            AddParagraph("Skipped means the target was found but the action did not apply, such as arithmetic on a non-numeric value or an index out of range.", "muted", 0, 8);
        AddEditList(run);
    }

    /// <summary>The table of the preset's rules: click one to list its edits below.</summary>
    private void AddRuleTable(FileEntry row, RunRecord run)
    {
        var table = new WebTable(["Rule", "Type", "Descriptors matched", "Edits", "Skipped"], [false, false, false, false, false]);
        List<RuleStats> stats = run.Result.Stats;
        for (int index = 0; index < stats.Count; index++)
        {
            RuleStats rule = stats[index];
            bool isIdle = RunRecord.IsIdle(rule);
            WebTableRow tableRow = table.AddRow([
                rule.Name + (rule.Enabled && !rule.OffByGroup ? "" : " (off)"),
                rule.Kind == "" ? "Edit" : rule.Kind,
                FileStatus.Count(rule.Matched), FileStatus.Count(rule.Edits), FileStatus.Count(rule.Skipped),
            ]);
            tableRow.IsSelected = index == run.ShownRule;
            tableRow.AccentUnderMouse = true;
            tableRow.Cursor = Cursors.Hand;
            if (isIdle)
            {
                // a rule that changed nothing: dim, with its edit count in the caution colour
                for (int column = 0; column < 5; column++) tableRow.SetColour(column, "muted");
                tableRow.SetColour(3, "caution");
                tableRow.ToolTip = "This rule changed nothing in this file.";
            }
            int picked = index;
            tableRow.Clicked += (_, _) =>
            {
                run.ShownRule = picked;
                Show(row);
            };
        }
        Add(table, 0, 0);
    }

    /// <summary>The edits of the rule picked in the table, in a list box of their own.</summary>
    private void AddEditList(RunRecord run)
    {
        int shown = run.ShownRule ?? 0;
        if (shown >= run.Result.Stats.Count) return;
        RuleStats rule = run.Result.Stats[shown];
        List<EditLogEntry> log = shown < run.Result.Logs.Count ? run.Result.Logs[shown] : [];
        // h3: dim, 12 pixels above, 5 below
        AddParagraph((log.Count > 0 ? "Edits by" : "No edits listed for") + " \"" + rule.Name + "\"", "muted", 12, 5);
        if (log.Count == 0)
        {
            AddParagraph(rule.Edits > 0 ? "The edit list is switched off in Settings." : "This rule changed nothing in this file.", "muted", 0, 8);
            return;
        }

        // only the edits in sight are made: up to 500 rows made at once froze the app while the panes were resized
        // (Double-clicking an edit to see it in the exported file comes with the Editor tab.)
        var table = new LongTable(["Descriptor", "Target", "Before", "After"], [true, true, true, true],
            [.. log.Select(edit => (IReadOnlyList<string>)[edit.Descriptor, edit.Target, edit.Old, edit.New])]);
        // #detBox: a sunken list, at most max(14rem, 46% of the window's height less 40 pixels) tall, with a grip under it
        var box = new Bevel { Child = table };
        box.SetResourceReference(Bevel.TopLeftProperty, "lo");
        box.SetResourceReference(Bevel.BottomRightProperty, "hi");
        box.SetResourceReference(Bevel.BackgroundProperty, "well");
        FitEditListHeight(box);
        Add(box, 0, 0);
        var grip = new ResizeGrip { Target = box, ToolTip = "Drag to resize. Double-click to reset." };
        grip.HeightChosen += height =>
        {
            chosenEditListHeight = height is double chosen ? Math.Clamp(chosen, 96, EditListCap()) : null;
            FitEditListHeight(box);
        };
        grip.HeightSettled += () => Pages.FilesPage.RememberSize("detBox", chosenEditListHeight);
        Add(grip, 1, 9);
        if (rule.Edits > log.Count)
            AddParagraph("Showing the first " + FileStatus.Count(log.Count) + " of " + FileStatus.Count(rule.Edits) + " edits by this rule. Settings sets how many are listed.", "muted", 0, 8);
    }

    private void FitEditListHeight(Bevel box)
    {
        double windowHeight = Window.GetWindow(this)?.ActualHeight ?? 800;
        if (chosenEditListHeight is double chosen)
        {
            box.Height = Math.Min(chosen, EditListCap());
            box.MaxHeight = double.PositiveInfinity;
        }
        else
        {
            box.Height = double.NaN;
            box.MaxHeight = Math.Max(224, windowHeight * 0.46 - 40);
        }
    }

    private double EditListCap() => Math.Max(240, Math.Round((Window.GetWindow(this)?.ActualHeight ?? 800) * 0.85, MidpointRounding.AwayFromZero));

    /// <summary>.empty: a dim line with 10 pixels of padding around it.</summary>
    private void AddEmpty(string text)
    {
        var empty = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(10) };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        Add(empty, 0, 0);
    }

    /// <summary>A paragraph in a theme colour, wanting space above and below it.</summary>
    private void AddParagraph(string text, string colour, double above, double below)
    {
        var paragraph = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        paragraph.SetResourceReference(TextBlock.ForegroundProperty, colour);
        Add(paragraph, above, below);
    }

    private void Add(FrameworkElement block, double above, double below)
    {
        wantedSpace[block] = (above, below);
        Children.Add(block);
    }

    /// <summary>
    /// Sets the gaps between the blocks as a web page does: where two blocks meet, the larger of the spaces they want is
    /// used, not both. The first block's space merges with the 6 pixels under the heading above it in the same way.
    /// </summary>
    private void SetGaps()
    {
        double spaceAbove = 6; // what the heading already leaves (see FilesPage.xaml)
        for (int index = 0; index < Children.Count; index++)
        {
            var block = (FrameworkElement)Children[index];
            (double above, double below) = wantedSpace[block];
            double gap = Math.Max(spaceAbove, above) - (index == 0 ? 6 : 0);
            bool isLast = index == Children.Count - 1;
            block.Margin = new Thickness(0, gap, 0, isLast ? below : 0);
            spaceAbove = below;
        }
    }
}
