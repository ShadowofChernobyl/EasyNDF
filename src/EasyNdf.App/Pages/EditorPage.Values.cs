using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Editor;
using EasyNdf.Engine.Editing;

namespace EasyNdf.App.Pages;

/// <summary>
/// The File Editor's help with values, for WARNO's .ndf files: the value check's wavy lines (with the syntax check's),
/// the tips on names and values, and the suggestions while typing, all from the editor's guide (<see cref="EditorGuide"/>),
/// each open file with its own. Steel Division 2's files and dictionary files get none of it: their names and values
/// differ.
/// </summary>
public partial class EditorPage
{
    /// <summary>The suggestions while typing in the file in use; for the checks.</summary>
    internal EditorSuggestions Suggestions => suggestions;

    /// <summary>Gives a file's tips and suggestions the guide of its own text.</summary>
    private void StartValueHelp(FileView v)
    {
        v.SyntaxProblems.TipAt = (offset, problem) => TipAt(v, offset, problem);
        v.SyntaxProblems.TipsWait = () => v.Suggestions.IsOpen;
        v.Suggestions.Guide = () => v.Guide;
    }

    /// <summary>
    /// Takes the guide for a text put in a box: WARNO's .ndf files (a file of the Files tab, its export, or one opened on
    /// its own) get it; once it is read, if it wasn't yet, the text is checked again with it.
    /// </summary>
    private void UseGuideFor(FileView v, ShownText? text, bool isNdf)
    {
        v.WantsGuide = text is not null && isNdf && (text.Entry?.Game ?? Data.Game.GameTitle.Warno) == Data.Game.GameTitle.Warno;
        v.Guide = v.WantsGuide ? EditorGuide.IfRead : null;
        if (v.WantsGuide && v.Guide is null) _ = UseGuideOnceRead(v);
    }

    private static async Task UseGuideOnceRead(FileView v)
    {
        ValueGuide guide = await EditorGuide.Get();
        if (!v.WantsGuide) return;
        v.Guide = guide;
        if (v.Shown is { IsFile: true }) v.SyntaxProblems.Guide = guide;
    }

    /// <summary>
    /// Gives the check of a file put in a box the impact names its ammunition can take (<see cref="ImpactNames"/>): its
    /// mod's, or the game's, read away from the screen's thread. A file opened on its own goes by its folder, as a row
    /// of the Files tab does; one in no mod's folder is taken to be the game's.
    /// </summary>
    private static async Task UseImpactsFor(FileView v, ShownText? text)
    {
        if (text is not { IsFile: true } || !v.WantsGuide) return;
        (string? path, string? source) = (text.Path, text.Entry?.Source);
        Data.Game.GameTitle game = text.Entry?.Game ?? Data.Game.GameTitle.Warno;
        IReadOnlySet<string>? names = await Task.Run(() => ImpactNames.For(game, path, source));
        if (ReferenceEquals(v.Shown, text)) v.SyntaxProblems.Impacts = names;
    }

    /// <summary>
    /// The tip at a place of a file's text: what is wrong there (on a wavy line), then, on a name or its value, what the
    /// name takes; on any other coloured word, a tip of its own (<see cref="SyntaxTipAt"/>). Null for nothing to say;
    /// without a guide, the syntax check shows the problem alone.
    /// </summary>
    /// <summary>
    /// The Help page about the word at a place: a variable's place on its type's page ("ref:TType#Name"), or a type's page;
    /// null when the Reference has none.
    /// </summary>
    private static string? HelpIdAt(FileView v, int offset)
    {
        if (v.Guide is not { } guide || v.Editor.Document is not { } document) return null;
        NdfPlace? place = EditorPlaces.At(document, offset);
        if (place is not null && place.IsName && offset >= place.WordStart && offset < place.WordEnd && guide.Find(place.ObjectType, place.Name) is not null)
            return Help.ReferencePages.IdOf(place.ObjectType) + "#" + place.Name;
        if (ColouredWordAt(v, document, offset) is { Word: var word } && guide.Knows(word) && word != "meta")
            return Help.ReferencePages.IdOf(word);
        return null;
    }

    /// <summary>A tip with "Open in Help ›" under it, when the word has a page in the Help tab's Reference.</summary>
    private object? WithHelpLink(FileView v, object? tip, string? helpId)
    {
        if (tip is null || helpId is null) return tip;
        var panel = new StackPanel { MaxWidth = ValueTips.Width };
        panel.Children.Add(tip as UIElement ?? new TextBlock { Text = tip.ToString(), TextWrapping = TextWrapping.Wrap });
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Texts.Get("Editor.Tip.OpenInHelp")))
        {
            Style = (Style)FindResource("CrumbLink"),
            ToolTip = Texts.Get("Editor.Tip.OpenInHelp.Tip"),
        };
        link.Click += (_, _) =>
        {
            v.SyntaxProblems.CloseTip();
            MainView.Of(this).ShowHelpPage(helpId);
        };
        var line = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontSize = 11.5 };
        line.Inlines.Add(link);
        var foot = new Border { Child = line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 2, 0, 0), Margin = new Thickness(0, 5, 0, 0) };
        foot.SetResourceReference(Border.BorderBrushProperty, "line");
        panel.Children.Add(foot);
        return panel;
    }

    /// <summary>F1 in the text: the Help page of the word at the caret, as the tip's link opens it.</summary>
    internal bool OpenHelpAtCaret()
    {
        if (view.Shown is null || HelpIdAt(view, view.Editor.CaretOffset) is not string id) return false;
        MainView.Of(this).ShowHelpPage(id);
        return true;
    }

    private object? TipAt(FileView v, int offset, ProblemMark? problem)
    {
        if (v.Guide is not { } guide || v.Editor.Document is not { } document) return null;
        NdfPlace? place = EditorPlaces.At(document, offset);
        // only the word under the mouse: a name that "=" follows, or the value after it
        bool onWord = place is not null && (place.IsName || place.IsInValue) && offset >= place.WordStart && offset < place.WordEnd;
        (string, string, bool)? wrong = problem is null ? null : (SyntaxProblems.Capitalised(problem.Message), problem.Why, problem.IsWarning);
        if (ColouredWordAt(v, document, offset) is { } coloured && HasOwnTip(coloured.Part.Colour, onWord && place!.IsInValue)
            && SyntaxTipAt(v, guide, document, offset, coloured.Part, coloured.Word, wrong) is { } own)
            return WithHelpLink(v, own, HelpIdAt(v, offset));
        ValueInfo? info = onWord ? guide.Find(place!.ObjectType, place.Name) : null;
        return WithHelpLink(v, ValueTips.For(wrong, place?.Name ?? "", info, info is null ? null : place!.ValueText), HelpIdAt(v, offset));
    }
}
