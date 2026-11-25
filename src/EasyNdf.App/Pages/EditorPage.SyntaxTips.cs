using System.IO;
using System.Windows.Controls;
using EasyNdf.App.Editor;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Editing;
using EasyNdf.Engine.Viewer;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Pages;

/// <summary>
/// The File Editor's tips for every coloured word (every coloured word has a tip): a setting's name and the plain values after it keep the setting's tip; a type, template,
/// name, reference, template value, keyword or comment gets one of its own (<see cref="SyntaxTips"/>). A name declared
/// in another file is looked up in the Files tab's read files; its file, line and first settings come in once that
/// file is read (the descriptor viewer's kept files are used).
/// </summary>
public partial class EditorPage
{
    /// <summary>The coloured word at a place of a file's text, as the editor colours it; null where nothing is.</summary>
    private static (ColouredPart Part, string Word)? ColouredWordAt(FileView v, TextDocument document, int offset)
    {
        DocumentLine line = document.GetLineByOffset(offset);
        return SyntaxWords.PartAt(document.GetText(line), BlockOf(v, document, line), offset - line.Offset);
    }

    /// <summary>The block a line starts inside, as the editor's colouring has it: none, { }, /* */, '...' or "...".</summary>
    private static int BlockOf(FileView v, TextDocument document, DocumentLine line) =>
        v.Shown is { Colourizer: { } colours } shown && ReferenceEquals(shown.Document, document)
            ? colours.BlockOf(line.LineNumber - 1) : NdfColouring.Outside;

    /// <summary>Whether a coloured word has a tip of its own: a setting's name doesn't, nor does a plain value in a setting's value (a number, a text, True, False or nil).</summary>
    private static bool HasOwnTip(string colour, bool inValue) => colour switch
    {
        "field" => false,
        "number" or "string" or "const" => !inValue,
        _ => true,
    };

    /// <summary>The tip of a coloured word that isn't a setting or its value; null for nothing to say.</summary>
    private StackPanel? SyntaxTipAt(FileView v, ValueGuide guide, TextDocument document, int offset, ColouredPart part, string word,
        (string Message, string Why, bool IsWarning)? wrong)
    {
        string text = TipText(document);
        string line = document.GetText(document.GetLineByOffset(offset));
        switch (part.Colour)
        {
            case "keyword":
                return SyntaxWords.KeywordNote(word) is { } note
                    ? SyntaxTips.Note(wrong, word, Texts.Get("Editor.Syntax.Kind.Keyword"), Texts.Get(note)) : null;
            case "comment":
                // a line that starts inside a { } comment begins with that comment's rest
                bool insideBraces = part.Start == 0 && BlockOf(v, document, document.GetLineByOffset(offset)) == NdfColouring.InBraces;
                return SyntaxTips.Note(wrong, SyntaxWords.CommentMark(word, insideBraces), Texts.Get("Editor.Syntax.Kind.Comment"), Texts.Get("Editor.Syntax.Comment"));
            case "type":
                return TypeTip(wrong, word, guide, text);
            case "func":
                return SyntaxWords.IsTypeName(word) ? TypeTip(wrong, word, guide, text) : TemplateTip(wrong, word, guide, text);
            case "param":
                string value = word.Trim('<', '>');
                return SyntaxWords.TemplateAround(text, offset) is { } around ? ValueTip(wrong, value, around, guide)
                    : SyntaxTips.Note(wrong, value, Texts.Get("Editor.Syntax.Kind.Value"), Texts.Get("Editor.Syntax.TemplateValueAlone"));
            case "name":
                return NameTip(v, wrong, word, text, line, definedHere: true);
            case "ref":
                // ~/ETerrainType/Default names a value inside ETerrainType, not an object called Default: the whole path is the name
                return NameTip(v, wrong, word.StartsWith("~/", StringComparison.Ordinal) && word.IndexOf('/', 2) > 0 ? word[2..] : SyntaxWords.ReferredName(word),
                    text, line, definedHere: false);
            case "const":
                if (word is "True" or "False" or "true" or "false")
                    return SyntaxTips.Note(wrong, word, Texts.Get("Editor.Syntax.Kind.YesNo"), Texts.Get("Editor.Syntax.TrueFalse"));
                if (word == "nil") return SyntaxTips.Note(wrong, word, Texts.Get("Editor.Syntax.Kind.NoValue"), Texts.Get("Editor.Syntax.Nil"));
                return NameTip(v, wrong, word, text, line, definedHere: false);
            case "number" or "string":
                // a template value's default, or a constant's value: the tip of what it belongs to
                if (SyntaxWords.ValueDeclaredOn(line) is { } declaredValue && SyntaxWords.TemplateAround(text, offset) is { } template
                    && offset > template.Open && offset < template.Close)
                    return ValueTip(wrong, declaredValue, template, guide);
                if (SyntaxWords.DeclaredOn(line) is { } constant) return NameTip(v, wrong, constant, text, line, definedHere: true);
                return part.Colour == "number"
                    ? SyntaxTips.Note(wrong, word, Texts.Get("Editor.Syntax.Kind.Number"), Texts.Get("Editor.Syntax.Number"))
                    : SyntaxTips.Note(wrong, word.Length > 40 ? word[..40] + "…" : word, Texts.Get("Editor.Syntax.Kind.Text"), Texts.Get("Editor.Syntax.Text"));
        }
        return null;
    }

    /// <summary>"TAmmunitionDescriptor: type", how many settings the guide knows for it, and how many named objects of it the read files have (or how often this file uses it).</summary>
    private StackPanel TypeTip((string, string, bool)? wrong, string type, ValueGuide guide, string text)
    {
        var said = new List<string>();
        // what the type is for, then the counts
        if (guide.AboutType(type) is string about) said.Add(about);
        int settings = guide.NamesOf(type).Count();
        if (settings > 0) said.Add(Texts.Count("Editor.Syntax.Settings", settings, ("number", Texts.Number(settings))));
        int named = NamedOfType(type);
        if (named > 0) said.Add(Texts.Count("Editor.Syntax.NamedObjects", named, ("number", Texts.Number(named))) + UnreadNote());
        said.Add(UsedInFile(UsesInText(text, type)));
        return SyntaxTips.Note(wrong, type, Texts.Get("Editor.Syntax.Kind.Type"), string.Join(" ", said));
    }

    /// <summary>"DebriefXpLine: template", what it is built on (when this file declares it), how often this file uses it, and its values.</summary>
    private StackPanel TemplateTip((string, string, bool)? wrong, string name, ValueGuide guide, string text)
    {
        TemplateSignature? signature = SyntaxWords.TemplateIn(text, name);
        if (signature is null && !guide.Knows(name))
            return SyntaxTips.Note(wrong, name, Texts.Get("Editor.Syntax.Kind.Call"), Texts.Get("Editor.Syntax.Call"));
        var said = new List<string>();
        if (signature is { BuiltOn.Length: > 0 }) said.Add(Texts.Get("Editor.Syntax.BuiltOn", ("base", signature.BuiltOn)));
        said.Add(UsedInFile(Math.Max(0, UsesInText(text, name) - (signature is null ? 0 : 1))));
        StackPanel tip = SyntaxTips.Note(wrong, name, Texts.Get("Editor.Syntax.Kind.Template"), string.Join(" ", said));
        SyntaxTips.AddList(tip, Texts.Get("Editor.Syntax.Values"), signature?.Values.Select(x => x.Name) ?? guide.NamesOf(name));
        return tip;
    }

    /// <summary>"MagnifiableHeight: float, default 24.0", that it is filled in where the template is used, and what it does when the guide says.</summary>
    private static StackPanel ValueTip((string, string, bool)? wrong, string name, TemplateSignature template, ValueGuide guide)
    {
        TemplateValue? value = template.Values.FirstOrDefault(x => x.Name == name);
        string type = value is null || value.Type == "" ? Texts.Get("Editor.Syntax.Kind.Value") : value.Type;
        string kind = value?.Default is { } given ? Texts.Get("Editor.Syntax.WithDefault", ("type", type), ("value", given)) : type;
        StackPanel tip = SyntaxTips.Note(wrong, name, kind, Texts.Get("Editor.Syntax.TemplateValue", ("template", template.Name)));
        if (guide.Find(template.Name, name) is { Description.Length: > 0 } info)
        {
            SyntaxTips.AddUnderLine(tip, ValueTips.Words(info.Description));
            if (ValueTips.CertaintyLine(info.Certainty) is { } sure) tip.Children.Add(sure);
        }
        return tip;
    }

    /// <summary>How many of the Files tab's files are not read yet: what a tip counts or finds leaves them out.</summary>
    private int UnreadFiles()
    {
        List<FileEntry> listed = [.. (Files?.Entries ?? []).Where(entry => entry.Source is not null)];
        return listed.Count - ReferenceIndex.Of([.. Files?.Entries ?? []]).Files.Count;
    }

    /// <summary>" N listed files are not read yet." after a count over the files read, or "" when all are read.</summary>
    private string UnreadNote() =>
        UnreadFiles() is > 0 and int unread ? " " + Texts.Count("Editor.Syntax.UnreadNote", unread, ("number", Texts.Number(unread))) : "";

    /// <summary>
    /// A name's tip: its type, where it is declared, how many descriptors of the read files use it, and its first
    /// settings. One declared in another read file shows "Reading its settings…" until that file is read.
    /// </summary>
    private StackPanel NameTip(FileView v, (string, string, bool)? wrong, string name, string text, string line, bool definedHere)
    {
        ReferenceIndex index = ReferenceIndex.Of([.. Files?.Entries ?? []]);
        bool known = index.Names.TryGetValue(name, out ReferenceIndex.Declared declared);
        (int Start, int End)? here = SyntaxWords.DeclarationIn(text, name);
        // not in the files read: where a listed file that is not read yet declares it
        ListedNames.Place? listed = null;
        bool listedReady = true;
        if (!known && here is null && !definedHere) listed = ListedNames.Find([.. Files?.Entries ?? []], name, out listedReady);
        bool inThisFile = definedHere || (here is not null && (!known || declared.Entry == v.Entry));
        // the read files' declaration is this one, unless this file declares its own (a private constant of the same
        // name in another file, or a file opened on its own): then only this file's uses are counted
        bool sameDeclaration = known && (!inThisFile || declared.Entry == v.Entry);
        string type = sameDeclaration && declared.Type != "" ? declared.Type
            : here is { } at ? SyntaxWords.TypeDeclaredOn(text[at.Start..Math.Min(text.Length, at.Start + 400)].Split('\n')[0], name) ?? "" : "";
        if (type == "" && listed is { } elsewhere) type = elsewhere.Type;
        string kind = type != "" ? type : inThisFile || known || listed is not null ? Texts.Get("Editor.Syntax.Kind.Constant") : Texts.Get("Editor.Syntax.Kind.Reference");
        StackPanel tip = SyntaxTips.Start(wrong, name, kind);

        var said = new List<string>();
        if (definedHere) said.Add(Texts.Get("Editor.Syntax.DefinedHere"));
        else if (inThisFile && here is { } own) said.Add(Texts.Get("Editor.Syntax.DefinedIn", ("file", v.Name), ("line", Texts.Number(SyntaxWords.LineOf(text, own.Start)))));
        if (sameDeclaration)
        {
            int users = index.UsersOf(name).Count;
            said.Add((users > 0 ? Texts.Count("Editor.Syntax.UsedBy", users, ("number", Texts.Number(users))) : Texts.Get("Editor.Syntax.UsedByNone")) + UnreadNote());
        }
        else if (inThisFile) said.Add(UsedInFile(Math.Max(0, UsesInText(text, name) - 1)));
        else if (listed is { } place) said.Add(Texts.Get("Editor.Syntax.DefinedIn", ("file", place.Entry.Name), ("line", Texts.Number(place.Line))));
        else if (!listedReady)
        {
            // the listed files' names are being gathered: until they are, the files read are all that is known
            (int done, int total) = ListedNames.Progress;
            said.Add(Texts.Get("Editor.Syntax.LookingInListed", ("done", Texts.Number(done)), ("total", Texts.Number(total))));
        }
        else said.Add(Texts.Get("Editor.Syntax.NotListed"));
        if (said.Count > 0) SyntaxTips.Add(tip, string.Join(" ", said));

        if (inThisFile && here is { } span)
        {
            if (SyntaxWords.KeyValuesOf(text[span.Start..span.End]) is { } values) SyntaxTips.AddKeyValues(tip, name, values);
        }
        else if (known)
        {
            TextBlock waiting = ValueTips.Dim(ValueTips.Words(Texts.Get("Editor.Syntax.Reading")));
            tip.Children.Add(waiting);
            _ = FillFromOtherFile(tip, waiting, declared.Entry, name);
        }
        return tip;
    }

    /// <summary>Puts a name's file, line and first settings into its tip, in place of "Reading its settings…", once its file is read.</summary>
    private async Task FillFromOtherFile(StackPanel tip, TextBlock waiting, FileEntry entry, string name)
    {
        OtherFile file;
        try { file = await ReadOtherFile(entry); }
        catch (Exception e) when (e is IOException or InvalidDataException or Engine.Ndf.NdfParseException)
        {
            tip.Children.Remove(waiting);
            return;
        }
        int at = tip.Children.IndexOf(waiting);
        if (at < 0) return;
        tip.Children.RemoveAt(at);
        if (!file.ByName.TryGetValue(name, out OutlineEntry? found)) return;
        tip.Children.Insert(at, ValueTips.Words(Texts.Get("Editor.Syntax.DefinedIn", ("file", entry.Name), ("line", Texts.Number(SyntaxWords.LineOf(file.Text, found.Start))))));
        string declaration = file.Text[found.Start..found.End];
        if (await Task.Run(() => SyntaxWords.KeyValuesOf(declaration)) is { } values) SyntaxTips.AddKeyValues(tip, name, values);
    }

    /// <summary>"Used 951 times in this file."</summary>
    private static string UsedInFile(int count) => Texts.Count("Editor.Syntax.UsedInFile", count, ("number", Texts.Number(count)));

    // ---- kept between tips: a file's text, a word's count in it, and each type's named objects ----

    private (TextDocument Document, ITextSourceVersion Version, string Text)? tipText;
    private (string Text, string Word, int Count)? tipCount;
    private (ReferenceIndex Index, Dictionary<string, int> Counts)? namedByType;

    /// <summary>A document's text, made once for each version of it rather than at each tip.</summary>
    private string TipText(TextDocument document)
    {
        if (tipText is { } known && ReferenceEquals(known.Document, document) && ReferenceEquals(known.Version, document.Version)) return known.Text;
        string text = document.Text;
        tipText = (document, document.Version, text);
        return text;
    }

    /// <summary>How many times a word stands on its own in a text, counted once for each text and word.</summary>
    private int UsesInText(string text, string word)
    {
        if (tipCount is { } known && ReferenceEquals(known.Text, text) && known.Word == word) return known.Count;
        int count = SyntaxWords.CountOf(text, word);
        tipCount = (text, word, count);
        return count;
    }

    /// <summary>How many named objects of a type the Files tab's read files declare, counted once for each index.</summary>
    private int NamedOfType(string type)
    {
        ReferenceIndex index = ReferenceIndex.Of([.. Files?.Entries ?? []]);
        if (namedByType is not { } known || !ReferenceEquals(known.Index, index))
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ReferenceIndex.Declared declared in index.Names.Values)
                if (declared.Type != "") counts[declared.Type] = counts.GetValueOrDefault(declared.Type) + 1;
            namedByType = known = (index, counts);
        }
        return known.Counts.GetValueOrDefault(type);
    }
}
