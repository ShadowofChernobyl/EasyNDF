using System.IO;
using System.Windows;
using EasyNdf.App.Editor;
using EasyNdf.App.Files;
using EasyNdf.App.Rules;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Editing;
using EasyNdf.Engine.Rules;
using ICSharpCode.AvalonEdit.Document;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// "Show export" and "Changes only" (the HTML app's <c>edExp</c>, <c>edDiff</c>, <c>edSwap</c> and <c>edSwapKeep</c>):
/// what the rules make of the file, read-only, in place of the file; and only the lines they change, set against it.
/// </summary>
public partial class EditorPage
{
    /// <summary>"Show export" shows what the file's preset would export now; "Show source" goes back to the file.</summary>
    private async void ExportClicked(object sender, RoutedEventArgs e) => await ShowExportOrSource();

    /// <summary>
    /// The rules a file's export runs, by name: the rule sets that apply to it, then the Armory's parts with rules for
    /// it ("WTO (Ammunition.ndf) + Units"); the Armory's alone for a file no rule set applies to, such as a new unit's.
    /// </summary>
    private static string RulesNameOf(FileEntry entry)
    {
        IEnumerable<string> armory = FileRunner.ArmoryPartsFor(entry.Name, entry.Game).Select(ArmoryPartTexts.Name);
        IEnumerable<string> ruleSets = entry.ModsOnFile.SelectMany(mod => mod.RuleSets);
        return string.Join(" + ", ruleSets.Concat(armory));
    }

    /// <param name="loading">The load the loading card shows already ("Changes only" running the export first), or 0.</param>
    internal async Task ShowExportOrSource(int loading = 0)
    {
        if (shown is null) return;
        if (shown.IsExport)
        {
            // back to the source: quick, but a big one holds the screen while it is laid out
            ShownText source = shown.File;
            int back = StartLoading(Texts.Get("Editor.Loading.Opening", ("file", source.Name)), source.Entry?.Folder ?? "", Texts.Get("Editor.Loading.Showing"));
            try
            {
                await ShowStepThatHolds(back, 0, source.Document.TextLength);
                if (ReferenceEquals(shown?.File, source) && shown.IsExport) SwapKeepingPlace(source);
            }
            finally
            {
                EndLoading(back);
            }
            return;
        }
        if (shown.Entry is not FileEntry entry || Files is null || !Files.Entries.Contains(entry) || entry.Source is null) return;
        // a division file can have the Armory presets' rules without a rule set of its own
        if (FileRunner.RulesToRun(entry).Count == 0)
        {
            Say(Texts.Get("Editor.Export.NoPreset", ("file", entry.Name)), "caution");
            return;
        }

        ShownText file = shown;
        // Handwritten edits: those typed here and not yet used, and those already handed to the file list ("Use in file list").
        // The file as first loaded is what they are counted against.
        string? typed = file.IsEdited ? file.Document.Text : null;
        string? current = null, first = null;
        try
        {
            current = typed ?? Normalised(FileContent.Text(entry));
            first = entry.FirstContent is PackedBytes firstBytes ? Normalised(FileContent.TextOf(firstBytes.Unpack())) : typed is not null ? file.Original : current;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

        Say(Texts.Get("Editor.Working"));
        int exporting = loading != 0 ? loading
            : StartLoading(Texts.Get("Editor.Loading.Exporting", ("file", entry.Name)), RulesNameOf(entry), Texts.Get("Editor.Loading.Rules"), Texts.Get("Editor.Loading.ShowingExport"));
        try
        {
            await ShowExportAfterRun(file, entry, typed, current, first, exporting);
        }
        finally
        {
            if (loading == 0) EndLoading(exporting);
        }
    }

    /// <summary>Runs the file's rules and shows the export, the loading card's steps following along.</summary>
    private async Task ShowExportAfterRun(ShownText file, FileEntry entry, string? typed, string? current, string? first, int loading)
    {
        RunResult result;
        string exported;
        try
        {
            FileRunner runner = new(Files!, RunSettings.From(AppData.Current.Settings));
            (result, exported) = await Task.Run(() => runner.WouldExport(entry, typed ?? FileContent.Text(entry)));
        }
        catch (Exception error)
        {
            Say(Texts.Get("Editor.Export.Failed", ("problem", error.Message)), "warn");
            return;
        }
        if (!ReferenceEquals(shown, file)) return;
        await ShowStepThatHolds(loading, 1, exported.Length);
        if (!ReferenceEquals(shown, file)) return;

        string edits = Texts.CountWide("Editor.Export.Edits", result.Edits);
        string how = typed is not null
            ? Texts.Get("Editor.Export.HowEdited", ("preset", RulesNameOf(entry)), ("edits", edits))
            : Texts.Get("Editor.Export.How", ("preset", RulesNameOf(entry)), ("edits", edits));
        string warning = result.ParseError != "" ? Texts.Get("Editor.Export.NotValid", ("problem", result.ParseError)) : "";
        if (typed is not null)
            warning = (warning != "" ? warning + " " : "") + Texts.Get("Editor.Export.UnsavedEdits");
        int? handEdits = first is not null && current is not null && first != current
            ? LineComparison.Differences(first.Split('\n'), current.Split('\n')).Count
            : null;

        var document = new TextDocument(Normalised(exported));
        SwapKeepingPlace(new ShownText
        {
            Name = file.Name, Entry = entry, Document = document, Colourizer = new NdfColourizer(document),
            IsExport = true, Source = file, How = how, Warning = warning, RuleEdits = result.Edits, HandEdits = handEdits,
            FirstText = handEdits is > 0 ? first : null,
        });
    }

    /// <summary>
    /// "Changes only" shows the lines the export changes, with two lines around each place, set against the file (or,
    /// when it was edited by hand, against the file as first loaded, so both kinds of edit show). Pressed again, it goes
    /// back to the whole export.
    /// </summary>
    private async void ChangesClicked(object sender, RoutedEventArgs e)
    {
        await ShowChangesOnly();
        Show();
    }

    internal async Task ShowChangesOnly()
    {
        if (shown is null) return;
        if (shown.Parent is ShownText whole)
        {
            Swap(whole);
            return;
        }
        if (!shown.IsExport)
        {
            // the changes are those of a run made on the Files tab: until there is one, a message says how to make it,
            // and the button is not left pressed
            if (shown.Entry is { LastRun: null } notRun)
            {
                ChangesButton.IsChecked = false;
                await Messages.Tell(Texts.Get("Editor.Export.RunFirst", ("file", notRun.Name)));
                return;
            }
        }
        string changesOf = shown.Name;
        string rules = shown.Entry is FileEntry ofEntry ? RulesNameOf(ofEntry) : "";
        int loading = StartLoading(Texts.Get("Editor.Loading.Changes", ("file", changesOf)), rules,
            Texts.Get("Editor.Loading.Rules"), Texts.Get("Editor.Loading.Finding"), Texts.Get("Editor.Loading.ShowingChanges"));
        try
        {
            await ShowChangesOnly(loading);
        }
        finally
        {
            EndLoading(loading);
        }
    }

    private async Task ShowChangesOnly(int loading)
    {
        if (shown is null) return;
        if (!shown.IsExport)
        {
            await ShowExportOrSource(loading);
            if (shown is not { IsExport: true }) return;
        }

        ShownText export = shown;
        await ShowStepThatHolds(loading, 1, export.Document.TextLength);
        if (!ReferenceEquals(shown, export)) return;
        List<string> before = [.. (export.FirstText ?? export.File.Document.Text).Split('\n')];
        List<string> after = [.. export.Document.Text.Split('\n')];
        ChangedLines? changes = LineComparison.ChangesOnly(before, NdfColouring.BlocksOfLines(before), after, export.Colourizer!.Blocks);
        if (changes is null)
        {
            Say(Texts.Get("Editor.Export.Same", ("file", export.Name)));
            return;
        }
        var document = new TextDocument(string.Join("\n", changes.Lines));
        await ShowStepThatHolds(loading, 2, document.TextLength);
        if (!ReferenceEquals(shown, export)) return;
        Swap(new ShownText
        {
            Name = export.Name, Entry = export.Entry, Document = document, Colourizer = new NdfColourizer(document, changes.Blocks),
            IsExport = true, Source = export.Source, Parent = export, Changes = changes, How = export.How, Warning = export.Warning,
            RuleEdits = export.RuleEdits, HandEdits = export.HandEdits,
        });
    }

    /// <summary>A text with no byte order mark, and with "\n" for every line end.</summary>
    private static string Normalised(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    /// <summary>Shows another text where it was last left (the HTML app's <c>edSwap</c>).</summary>
    private void Swap(ShownText to)
    {
        if (shown is not null) shown.Top = Text.VerticalOffset;
        shown = to;
        ShowText();
        Text.UpdateLayout();
        Text.ScrollToVerticalOffset(to.Top);
    }

    /// <summary>
    /// Shows the other side of a file and its export at the same place (the HTML app's <c>edSwapKeep</c>): the line at
    /// the top is found again in the other text, and the text stays scrolled as far to the side.
    /// </summary>
    private void SwapKeepingPlace(ShownText to)
    {
        List<string> fromLines = [.. Text.Document.Text.Split('\n')];
        int topLine = Text.TextArea.TextView.GetDocumentLineByVisualTop(Text.VerticalOffset)?.LineNumber - 1 ?? 0;
        double left = Text.HorizontalOffset;
        if (shown is not null) shown.Top = Text.VerticalOffset;
        shown = to;
        ShowText();
        List<string> toLines = [.. to.Document.Text.Split('\n')];
        int line = LineComparison.MatchingLine(fromLines, toLines, Math.Min(topLine, fromLines.Count - 1));
        Text.UpdateLayout();
        Text.ScrollToVerticalOffset(Text.TextArea.TextView.GetVisualTopByDocumentLine(line + 1));
        Text.ScrollToHorizontalOffset(left);
    }
}
