using System.IO;
using System.Windows;
using EasyNdf.App.Editor;
using EasyNdf.App.Files;
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

    internal async Task ShowExportOrSource()
    {
        if (shown is null) return;
        if (shown.IsExport)
        {
            SwapKeepingPlace(shown.File);
            return;
        }
        if (shown.Entry is not FileEntry entry || Files is null || !Files.Entries.Contains(entry) || entry.Source is null) return;
        if (entry.Preset is null)
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
        RunResult result;
        string exported;
        try
        {
            FileRunner runner = new(Files, RunSettings.From(AppData.Current.Settings));
            (result, exported) = await Task.Run(() => runner.WouldExport(entry, typed ?? FileContent.Text(entry)));
        }
        catch (Exception error)
        {
            Say(Texts.Get("Editor.Export.Failed", ("problem", error.Message)), "warn");
            return;
        }
        if (!ReferenceEquals(shown, file)) return;

        string edits = Texts.CountWide("Editor.Export.Edits", result.Edits);
        string how = typed is not null
            ? Texts.Get("Editor.Export.HowEdited", ("preset", entry.Preset.Name), ("edits", edits))
            : Texts.Get("Editor.Export.How", ("preset", entry.Preset.Name), ("edits", edits));
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
            await ShowExportOrSource();
            if (shown is not { IsExport: true }) return;
        }

        ShownText export = shown;
        List<string> before = [.. (export.FirstText ?? export.File.Document.Text).Split('\n')];
        List<string> after = [.. export.Document.Text.Split('\n')];
        ChangedLines? changes = LineComparison.ChangesOnly(before, NdfColouring.BlocksOfLines(before), after, export.Colourizer!.Blocks);
        if (changes is null)
        {
            Say(Texts.Get("Editor.Export.Same", ("file", export.Name)));
            return;
        }
        var document = new TextDocument(string.Join("\n", changes.Lines));
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
