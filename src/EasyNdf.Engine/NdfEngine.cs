using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Rules;
using EasyNdf.Engine.Writing;

namespace EasyNdf.Engine;

/// <summary>What loading a file found.</summary>
public sealed record LoadSummary(
    int Declarations,
    int Descriptors,
    int Constants,
    int Unnamed,
    int Lines,
    /// <summary>Every property name and top-level constant, sorted: what fills the Target and Operand lists.</summary>
    List<string> Names,
    /// <summary>Every object type, sorted.</summary>
    List<string> Types,
    /// <summary>The names of the declarations, in file order.</summary>
    List<string> Titles,
    /// <summary>For each declaration: its name, its type, and the names its values refer to.</summary>
    List<DeclarationReferences> References);

/// <summary>One declaration's name, type and the names it refers to.</summary>
public sealed record DeclarationReferences(string Name, string Type, List<string> Refers);

/// <summary>
/// The rule engine: load one NDF file, then run rules over it as many times as needed.
/// </summary>
/// <example>
/// <code>
/// var engine = new NdfEngine();
/// engine.Load(File.ReadAllText("Ammunition.ndf"));
/// var result = engine.Run(preset.Rules, new RunOptions());
/// File.WriteAllText("out/Ammunition.ndf", result.Text);
/// </code>
/// </example>
public sealed class NdfEngine
{
    private NdfDocument? document;

    /// <summary>Reads a file. Throws <see cref="NdfParseException"/> when it is not valid NDF.</summary>
    public LoadSummary Load(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        document = Parser.Parse(text);
        return Summarise(document, text);
    }

    /// <summary>Runs rules over the loaded file. The file itself is not changed, so it can be run again.</summary>
    public RunResult Run(List<Rule?> rules, RunOptions options)
    {
        if (document is null) throw new InvalidOperationException("No file loaded");
        return RuleRunner.Run(document, rules, options);
    }

    /// <summary>The comment an action writes when its comment box is left empty, such as "Multiplied by 2".</summary>
    public static string AutomaticComment(RuleAction action) => EditComments.Automatic(action);

    /// <summary>A text with its comments taken out, as "Keep source comments" off does.</summary>
    public static string RemoveComments(string text, bool startsAtLineStart) => CommentText.RemoveComments(text, startsAtLineStart);

    private static LoadSummary Summarise(NdfDocument doc, string text)
    {
        var names = new HashSet<string>();
        var types = new HashSet<string>();
        int descriptors = 0, constants = 0, unnamed = 0;

        foreach (var d in doc.Declarations)
        {
            if (d.IsUnnamed) unnamed++;
            else if (d.Value is not ObjectValue)
            {
                // a top-level constant can be a rule's target, like a property
                constants++;
                names.Add(d.Name);
            }
            if (!d.IsUnnamed && d.Value is ObjectValue) descriptors++;
        }

        void Scan(NdfValue value)
        {
            switch (value)
            {
                case ObjectValue obj:
                    if (obj.TypeName != "") types.Add(obj.TypeName);
                    foreach (var p in obj.Properties) { names.Add(p.Name); Scan(p.Value); }
                    break;
                case ListValue list:
                    foreach (var item in list.Items) Scan(item);
                    break;
                case PairValue { Kind: PairKind.Pair } pair:
                    if (pair.First is ScalarValue { Kind: ScalarKind.Reference or ScalarKind.Identifier } key) names.Add(key.Text);
                    Scan(pair.First);
                    Scan(pair.Second);
                    break;
                case PairValue or:
                    Scan(or.First);
                    Scan(or.Second);
                    break;
                case AssignValue assign:
                    Scan(assign.Value);
                    break;
                case ScalarValue { Kind: ScalarKind.Path } path:
                    names.Add(path.Text);
                    break;
            }
        }
        foreach (var d in doc.Declarations) Scan(d.Value);

        int lines = text.Length > 0 ? 1 : 0;
        foreach (char c in text) if (c == '\n') lines++;

        return new LoadSummary(
            doc.Declarations.Count, descriptors, constants, unnamed, lines,
            names.Order(StringComparer.Ordinal).ToList(),
            types.Order(StringComparer.Ordinal).ToList(),
            doc.Declarations.Where(d => !d.IsUnnamed).Select(d => d.Name).ToList(),
            doc.Declarations.Select(References).ToList());
    }

    /// <summary>A declaration's name, its type, and every name its values refer to, in the order they first appear.</summary>
    private static DeclarationReferences References(Declaration d)
    {
        var refers = new List<string>();
        var seen = new HashSet<string>();
        void Add(string name) { if (seen.Add(name)) refers.Add(name); }
        void Walk(NdfValue value)
        {
            switch (value)
            {
                case ObjectValue obj: foreach (var p in obj.Properties) Walk(p.Value); break;
                case ListValue list: foreach (var item in list.Items) Walk(item); break;
                case PairValue pair: Walk(pair.First); Walk(pair.Second); break;
                case AssignValue assign: Walk(assign.Value); break;
                // $/Some/Path/Name and ~/Name: the name the other declaration goes by
                case ScalarValue { Kind: ScalarKind.Reference } r: Add(r.Text[(r.Text.LastIndexOf('/') + 1)..]); break;
                case ScalarValue { Kind: ScalarKind.Identifier } id: Add(id.Text); break;
            }
        }
        Walk(d.Value);
        return new DeclarationReferences(d.IsUnnamed ? "" : d.Name, d.Value is ObjectValue o ? o.TypeName : "", refers);
    }
}
