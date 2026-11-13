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
    /// The tip at a place of a file's text: what is wrong there (on a wavy line), then, on a name or its value, what the
    /// name takes; on any other coloured word, a tip of its own (<see cref="SyntaxTipAt"/>). Null for nothing to say;
    /// without a guide, the syntax check shows the problem alone.
    /// </summary>
    private object? TipAt(FileView v, int offset, ProblemMark? problem)
    {
        if (v.Guide is not { } guide || v.Editor.Document is not { } document) return null;
        NdfPlace? place = EditorPlaces.At(document, offset);
        // only the word under the mouse: a name that "=" follows, or the value after it
        bool onWord = place is not null && (place.IsName || place.IsInValue) && offset >= place.WordStart && offset < place.WordEnd;
        (string, string, bool)? wrong = problem is null ? null : (SyntaxProblems.Capitalised(problem.Message), problem.Why, problem.IsWarning);
        if (ColouredWordAt(v, document, offset) is { } coloured && HasOwnTip(coloured.Part.Colour, onWord && place!.IsInValue)
            && SyntaxTipAt(v, guide, document, offset, coloured.Part, coloured.Word, wrong) is { } own)
            return own;
        ValueInfo? info = onWord ? guide.Find(place!.ObjectType, place.Name) : null;
        return ValueTips.For(wrong, place?.Name ?? "", info, info is null ? null : place!.ValueText);
    }
}
