using System.Globalization;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Text;
using EasyNdf.Engine.Writing;

namespace EasyNdf.Engine.Rules;

/// <summary>
/// Runs a list of rules over one loaded file, in list order, and writes the result.
/// </summary>
/// <remarks>
/// <para>Each rule type has its own method below. They share a few things, set up in <see cref="Run"/>:</para>
/// <list type="bullet">
/// <item>Variables: {Name} in any value is replaced by the value of a Variable rule.</item>
/// <item>Groups: a Group rule's conditions apply to the rules under it, up to the next group; a switched-off group switches them off.</item>
/// <item>Scope: some rule types can work on every object of one type, or on the file's constants, instead of on each descriptor.</item>
/// </list>
/// </remarks>
public sealed partial class RuleRunner
{
    private readonly NdfDocument document;
    private readonly List<Rule?> rules;
    private readonly RunOptions options;
    private readonly string modName;
    private readonly int? decimalPlaces;
    private readonly string lineBreak; // the file's: "\r\n" or "\n"

    private readonly List<WorkItem> work;
    private readonly Dictionary<string, string> variables = [];
    private readonly List<RuleStats> stats;
    private readonly List<List<EditLogEntry>> logs;
    private readonly List<RunWarning> warnings = [];
    private readonly OrderedDictionary<string, string> renamed = new();
    private readonly Dictionary<string, NdfDocument> otherFilesRead = [];

    // the rule that is running
    private List<EditLogEntry> log = [];
    private RuleStats current = null!;
    private Group? group;

    private sealed record Group(bool On, List<Condition> Conditions, bool GateIsAll);

    private RuleRunner(NdfDocument document, List<Rule?> rules, RunOptions options)
    {
        this.rules = rules;
        this.options = options;
        modName = options.ModName.Replace("*/", "* /");
        decimalPlaces = options.DecimalPlaces is int d && d >= 0 ? Math.Min(12, d) : null;

        // With "Keep source comments" off, the rules run on the file as it reads without its comments.
        this.document = document;
        if (!options.KeepSourceComments && (options.WantText || options.CheckOutput))
        {
            string bare = CommentText.RemoveComments(document.Source, startsAtLineStart: true);
            if (bare != document.Source) this.document = Parser.Parse(bare);
        }
        lineBreak = document.Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        work = this.document.Declarations.Select(d => new WorkItem { Declaration = d }).ToList();
        stats = rules.Select(r => new RuleStats
        {
            Name = r?.DisplayName ?? "(unnamed)",
            Kind = r?.KindOrEdit ?? "Edit",
            Enabled = r is null || r.Enabled,
        }).ToList();
        logs = rules.Select(_ => new List<EditLogEntry>()).ToList();
    }

    /// <summary>Runs the rules over a loaded file.</summary>
    public static RunResult Run(NdfDocument document, List<Rule?> rules, RunOptions options) =>
        new RuleRunner(document, rules, options).RunAll();

    private RunResult RunAll()
    {
        CollectVariables();

        for (int ri = 0; ri < rules.Count; ri++)
        {
            Rule rule = rules[ri] ?? new Rule();
            string kind = rule.KindOrEdit;
            current = stats[ri];
            log = logs[ri];
            tooManyDigitsWarned = false;

            if (kind == "Group")
            {
                group = new Group(rule.Enabled, Prepare(rule.Conditions), rule.LogicGate == "All");
                if (group.On) WarnAboutNumberConditions(group.Conditions, current.Name);
                continue;
            }
            if (kind == "Variable" || !rule.Enabled) continue;
            if (group is { On: false })
            {
                current.OffByGroup = true;
                continue;
            }

            var conditions = Prepare(rule.Conditions);
            bool gateIsAll = rule.LogicGate == "All";
            WarnAboutNumberConditions(conditions, current.Name);

            var scope = new Scope(this, rule, kind, conditions, gateIsAll);
            if (scope.IsType && scope.TypeName == "")
            {
                current.Skipped++;
                FileNote(current.Name, kind, "Not run. The rule needs an object type.");
                continue;
            }

            switch (kind)
            {
                case "Edit": RunEdit(rule, scope, gateIsAll); break;
                case "Remove": RunRemove(scope, conditions); break;
                case "Add": RunAdd(rule); break;
                case "Clone": RunClone(rule); break;
                case "Replace": RunReplace(rule, scope); break;
                case "Scale": RunScale(rule, scope); break;
                case "Formula": RunFormula(rule, scope); break;
                case "Table": RunTable(rule); break;
                case "Lookup": RunLookup(rule, scope); break;
                case "Check": RunCheck(rule, scope); break;
            }
        }

        WarnAboutDuplicateNames();
        ForgetConditionTexts();
        return Finish();
    }

    // ---- shared helpers ----

    /// <summary>
    /// Variables are collected before any rule runs, so a variable applies to the whole preset wherever it sits.
    /// A switched-off group switches off the variables in it too.
    /// </summary>
    private void CollectVariables()
    {
        bool groupOn = true;
        foreach (var rule in rules)
        {
            if (rule is null) continue;
            if (rule.Kind == "Group") { groupOn = rule.Enabled; continue; }
            if (!groupOn || rule.Kind != "Variable" || !rule.Enabled || rule.Params is null) continue;
            string name = JsText.Trim(rule.P.TextOr("VarName", ""));
            if (name != "") variables[name] = JsText.Trim(rule.P.Text("Value"));
        }
    }

    /// <summary>Replaces {Name} with the value of the variable called Name. Unknown names are left as they are.</summary>
    private string Substitute(string? text) =>
        VariableReference().Replace(text ?? "", m => variables.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>The conditions that have an operator, with variables filled in.</summary>
    private List<Condition> Prepare(List<Condition?>? conditions) =>
        (conditions ?? []).Where(c => c is not null && !string.IsNullOrEmpty(c.Operator))
            .Select(c => c!.With(Substitute(c!.Value))).ToList();

    /// <summary>The actions, with variables filled in.</summary>
    private List<RuleAction> PrepareActions(Rule rule) =>
        (rule.Actions ?? []).Where(a => a is not null).Select(a => a!.With(Substitute(a!.Value))).ToList();

    /// <summary>The comment the rule types other than Edit write, from their "Comment on each edit" box.</summary>
    private ActionComment? CommentSetting(RuleParams p) =>
        p.TextOr("CommentText", "") != ""
            ? new ActionComment { Enabled = true, Text = Substitute(p.Text("CommentText")), IncludeOriginalValue = p.IsOn("CommentOriginal") }
            : null;

    /// <summary>A note in the rule's edit log that is not an edit, such as "Not run".</summary>
    private void FileNote(string label, string target, string message)
    {
        if (log.Count < options.LogLimit) log.Add(new EditLogEntry(label, target, "", message));
    }

    /// <summary>A number typed into a setting: null when empty, NaN when it is not a number.</summary>
    private double? NumberSetting(string text)
    {
        string t = JsText.Trim(Substitute(text));
        return t == "" ? null : NumberText.ParseTyped(t);
    }

    private bool IsBadNumber(string text)
    {
        string t = JsText.Trim(Substitute(text));
        return t != "" && double.IsNaN(NumberText.ParseTyped(t));
    }

    private void Warn(string message, string? type = null) => warnings.Add(new RunWarning(current.Name, message, type));

    /// <summary>A number condition with a value that is not a number can never match: say so.</summary>
    private void WarnAboutNumberConditions(List<Condition> conditions, string label)
    {
        foreach (var c in conditions)
        {
            if (!Conditions.IsNumberTest(c.Operator) || !double.IsNaN(NumberText.ParseTyped(c.Value))) continue;
            string message = "Condition \"" + (c.OperandInUse ?? "undefined") + " " + c.Operator + " " + c.Value +
                "\" can never match. " + NumberText.NotANumberMessage(c.Value);
            FileNote(label, "Condition", message);
            warnings.Add(new RunWarning(label, message, "number"));
        }
    }

    private bool HasName(string name) => work.Any(w => !w.IsRemoved &&
        (w.IsAdded ? w.AddedNames.Contains(name) : (!w.Declaration!.IsUnnamed && w.Declaration.Name == name)));

    private bool InGroup(Declaration d) => group is null || Conditions.Pass(d, group.Conditions, group.GateIsAll, whenNoConditions: true);

    private EditContext NewContext()
    {
        string ruleName = current.Name;
        return new EditContext
        {
            Log = log,
            LogLimit = options.LogLimit,
            RuleName = ruleName,
            Renamed = renamed,
            ModName = modName,
            DecimalPlaces = decimalPlaces,
            TooManyDigits = (d, old) =>
            {
                string message = "\"" + Shorten(old) + "\" was left alone: it has more digits than this calculation can keep exactly. " +
                    "Adding, subtracting and multiplying whole numbers works at any length.";
                FileNote(d.LogName, "Number", message);
                if (!tooManyDigitsWarned)
                {
                    tooManyDigitsWarned = true;
                    warnings.Add(new RunWarning(ruleName, "Some values were left alone, starting with " + d.LogName + ": " + message, "number"));
                }
            },
        };
    }

    // the "too many digits" warning is given once per rule
    private bool tooManyDigitsWarned;

    private static string Shorten(string text) => text.Length > 120 ? text[..117] + "..." : text;

    private void Tally(EditContext c)
    {
        current.Edits += c.Edits;
        current.Skipped += c.Skipped;
    }

    // ---- what a rule works on ----

    /// <summary>
    /// What a rule works on: usually each named descriptor. Some rule types can instead be pointed at every object of one
    /// type, wherever it sits (a terrain inside a list, a module inside a unit), or at the constants written at the top
    /// level of the file ("Name is 5").
    /// </summary>
    private sealed class Scope
    {
        private static readonly HashSet<string> ScopedKinds = ["Edit", "Replace", "Scale", "Formula", "Check"];

        private readonly RuleRunner runner;
        private readonly List<Condition> conditions;
        private readonly bool gateIsAll;

        public bool IsType { get; }
        public bool IsConstants { get; }
        public string TypeName { get; } = "";

        public Scope(RuleRunner runner, Rule rule, string kind, List<Condition> conditions, bool gateIsAll)
        {
            this.runner = runner;
            this.conditions = conditions;
            this.gateIsAll = gateIsAll;
            string scope = ScopedKinds.Contains(kind) ? rule.P.TextOr("Scope", "") : "";
            IsType = scope == "type";
            IsConstants = scope == "constants";
            if (IsType) TypeName = JsText.Trim(runner.Substitute(rule.P.Text("ScopeType")));
        }

        /// <summary>
        /// Calls <paramref name="apply"/> for everything the rule works on that passes its conditions.
        /// </summary>
        /// <param name="whenNoConditions">Whether a rule with no conditions applies to everything.</param>
        /// <param name="apply">What to do with each one.</param>
        public void ForEach(bool whenNoConditions, Action<WorkItem> apply)
        {
            if (IsType) ForEachObjectOfType(whenNoConditions, apply);
            else if (IsConstants) ForConstants(whenNoConditions, apply);
            else
            {
                foreach (var w in runner.work.ToList())
                {
                    if (!w.IsLiveDescriptor || !runner.InGroup(w.Declaration!) ||
                        !Conditions.Pass(w.Declaration!, conditions, gateIsAll, whenNoConditions)) continue;
                    apply(w);
                }
            }
        }

        private List<ObjectValue> ObjectsOfType(NdfValue value, List<ObjectValue> found)
        {
            switch (value)
            {
                case ObjectValue obj:
                    if (obj.TypeName == TypeName) found.Add(obj);
                    foreach (var p in obj.Properties) ObjectsOfType(p.Value, found);
                    break;
                case ListValue list:
                    foreach (var item in list.Items) ObjectsOfType(item, found);
                    break;
                case PairValue pair:
                    ObjectsOfType(pair.First, found);
                    ObjectsOfType(pair.Second, found);
                    break;
                case AssignValue assign:
                    ObjectsOfType(assign.Value, found);
                    break;
            }
            return found;
        }

        /// <summary>An object inside a descriptor, standing in for one: its title is the descriptor's, its type its own.</summary>
        private static Declaration StandIn(Declaration top, ObjectValue obj)
        {
            string name = "";
            foreach (var p in obj.Properties)
            {
                if (p.Name == "Name" && p.Value.IsPrimitive)
                {
                    name = " " + CanonicalWriter.PrimitiveText(p.Value);
                    break;
                }
            }
            return new Declaration
            {
                Name = top.Name,
                Value = obj,
                IsStandIn = true,
                Label = (top.Name != "" ? top.Name + " > " : "") + obj.TypeName + name,
            };
        }

        private void ForEachObjectOfType(bool whenNoConditions, Action<WorkItem> apply)
        {
            foreach (var w in runner.work.ToList())
            {
                if (w.IsRemoved || w.IsAdded || !runner.InGroup(w.Declaration!)) continue;
                var found = ObjectsOfType(w.Declaration!.Value, []);
                if (found.Count == 0) continue;

                var hits = new List<int>();
                for (int k = 0; k < found.Count; k++)
                    if (Conditions.Pass(StandIn(w.Declaration, found[k]), conditions, gateIsAll, whenNoConditions)) hits.Add(k);
                if (hits.Count == 0) continue;

                // Conditions were read before anything changed; the edits go into the rule's own copy.
                Declaration own = w.Own();
                var mine = ObjectsOfType(own.Value, []);
                foreach (int k in hits)
                    apply(new WorkItem { Declaration = StandIn(own, mine[k]), IsOwnCopy = true });
            }
        }

        /// <summary>All the file's constants together stand in for one descriptor, each constant a property of it.</summary>
        private void ForConstants(bool whenNoConditions, Action<WorkItem> apply)
        {
            var constants = runner.work.Where(w => !w.IsRemoved && !w.IsAdded && !w.Declaration!.IsUnnamed && w.Declaration.Value is not ObjectValue).ToList();
            if (constants.Count == 0) return;

            Declaration AllConstants() => new()
            {
                Name = "",
                IsStandIn = true,
                IsConstantsStandIn = true,
                Label = "(constants)",
                Value = new ObjectValue("", constants.Select(w => new Property(w.Declaration!.Name, w.Declaration.Value)).ToList()),
            };

            var probe = AllConstants();
            if (!runner.InGroup(probe) || !Conditions.Pass(probe, conditions, gateIsAll, whenNoConditions)) return;

            foreach (var w in constants) w.Own();
            var standIn = AllConstants();
            apply(new WorkItem { Declaration = standIn, IsOwnCopy = true });
            var properties = ((ObjectValue)standIn.Value).Properties;
            for (int k = 0; k < properties.Count; k++) constants[k].Declaration!.Value = properties[k].Value;
        }
    }

    // ---- the rule types ----

    /// <summary>Edit: changes values in each descriptor that passes the conditions.</summary>
    private void RunEdit(Rule rule, Scope scope, bool gateIsAll)
    {
        var actions = PrepareActions(rule).Where(a =>
        {
            string op = EditComments.OperatorName(a.Operator);
            if (!Arithmetic.Operations.ContainsKey(op) || !double.IsNaN(NumberText.ParseTyped(a.Value))) return true;
            current.Skipped++;
            string message = op + " was not applied to \"" + (a.TargetInUse ?? "") + "\". " +
                (JsText.Trim(a.Value ?? "") == "" ? "The action needs a number." : NumberText.NotANumberMessage(a.Value));
            FileNote(current.Name, op, message);
            Warn(message, "number");
            return false;
        }).ToList();

        // An Edit rule with no conditions applies to everything only when its gate is "All".
        scope.ForEach(whenNoConditions: gateIsAll, w =>
        {
            var d = w.Own();
            var c = NewContext();
            foreach (var a in actions) ActionApplier.Apply(d, a, c);
            current.Matched++;
            Tally(c);
        });
    }

    /// <summary>Remove: takes out every descriptor that passes the conditions. It needs at least one.</summary>
    private void RunRemove(Scope scope, List<Condition> conditions)
    {
        if (conditions.Count == 0)
        {
            current.Skipped++;
            FileNote(current.Name, "Remove", "Nothing removed. A Remove rule needs at least one condition.");
            return;
        }
        scope.ForEach(whenNoConditions: false, w =>
        {
            w.IsRemoved = true;
            current.Matched++;
            current.Edits++;
            FileNote(w.Declaration!.Name, "Remove", "Removed from the file.");
        });
    }

    /// <summary>Add: adds a descriptor written in full, at the end of the file or after a named descriptor.</summary>
    private void RunAdd(Rule rule)
    {
        var p = rule.P;
        string text = JsText.Trim(p.Text("Text").Replace("\\n", "\n"));
        string target = JsText.Trim(p.TextOr("Name", ""));
        List<Declaration> declarations = [];
        string error = "";
        try { declarations = Parser.Parse(text).Declarations; }
        catch (NdfParseException e) { error = e.Message; }

        var names = declarations.Where(d => !d.IsUnnamed).Select(d => d.Name).ToList();
        string label = target != "" ? target : names.FirstOrDefault() is { Length: > 0 } n ? n : "(no name)";
        void NotAdded(string message)
        {
            current.Skipped++;
            FileNote(label, "Add", "Not added. " + message);
        }

        if (text == "") { NotAdded("The descriptor text is empty."); return; }
        if (error != "") { NotAdded("The descriptor text is not valid NDF. " + error); return; }
        if (declarations.Count == 0) { NotAdded("The text holds no descriptor."); return; }
        string? clash = new[] { target }.Concat(names).FirstOrDefault(x => x != "" && HasName(x));
        if (clash is not null) { NotAdded("\"" + clash + "\" is already in the file."); return; }

        var item = new WorkItem
        {
            AddedText = text,
            AddedNames = target != "" && !names.Contains(target) ? [.. names, target] : names,
            AddedDeclarations = declarations,
        };
        string after = JsText.Trim(p.TextOr("After", ""));
        bool wantsAfter = p.Is("Position", "after") && after != "";
        int at = wantsAfter
            ? work.FindIndex(w => !w.IsRemoved && !w.IsAdded && !w.Declaration!.IsUnnamed && w.Declaration.Name == after)
            : -1;
        if (at >= 0) work.Insert(at + 1, item);
        else work.Add(item);
        current.Edits++;

        string where = at >= 0
            ? "Added after \"" + after + "\"."
            : "Added at the end of the file." + (wantsAfter ? " \"" + after + "\" was not found." : "");
        string mismatch = target != "" && names.Count > 0 && !names.Contains(target)
            ? " Note: the name field does not match the name in the text (\"" + names[0] + "\")."
            : "";
        FileNote(label, "Add", where + mismatch);
    }

    /// <summary>Clone: copies a descriptor under a new name, with new GUIDs, and applies the rule's actions to the copy.</summary>
    private void RunClone(Rule rule)
    {
        var p = rule.P;
        string from = JsText.Trim(p.TextOr("Source", ""));
        string to = JsText.Trim(p.TextOr("NewName", ""));
        void NotCloned(string message)
        {
            current.Skipped++;
            FileNote(to != "" ? to : "(no name)", "Clone", "Not cloned. " + message);
        }

        if (from == "" || to == "") { NotCloned("The rule needs a source name and a new name."); return; }
        if (!DescriptorName().IsMatch(to)) { NotCloned("\"" + to + "\" is not a valid descriptor name."); return; }
        if (HasName(to)) { NotCloned("\"" + to + "\" is already in the file."); return; }

        int sourceAt = work.FindIndex(w => !w.IsRemoved && !w.IsAdded && !w.Declaration!.IsUnnamed && w.Declaration.Name == from);
        Declaration? source = sourceAt >= 0 ? work[sourceAt].Declaration : null;
        for (int k = 0; k < work.Count && source is null; k++)
        {
            // it can also be a descriptor an Add rule added
            var w = work[k];
            if (!w.IsAdded || w.IsRemoved) continue;
            var found = w.AddedDeclarations.FirstOrDefault(d => !d.IsUnnamed && d.Name == from);
            if (found is not null) { source = found; sourceAt = k; }
        }
        if (source is null) { NotCloned("\"" + from + "\" was not found."); return; }

        // The copy keeps only these parts of the source declaration, as in the HTML app.
        var copy = new Declaration
        {
            IsExported = source.IsExported,
            Name = to,
            Value = source.Value.CopyForEdit(),
            Start = source.Start,
            End = source.End,
            NameStart = source.NameStart,
            NameLength = source.NameLength,
            Source = source.Source,
        };

        if (p.IsNotFalse("NewIds"))
        {
            string seed = p.IsOn("Seed") ? p.Text("Seed") + "|" : "";
            int n = 0;
            void NewIds(NdfValue v)
            {
                switch (v)
                {
                    case ScalarValue { Kind: ScalarKind.Guid } guid:
                        guid.Text = StableGuid.From(seed + to + "#" + n++);
                        if (guid.Start is not null)
                        {
                            guid.ReplacedStart = guid.Start;
                            guid.ReplacedEnd = guid.End;
                            guid.Start = guid.End = null;
                        }
                        break;
                    case ObjectValue obj: foreach (var prop in obj.Properties) NewIds(prop.Value); break;
                    case ListValue list: foreach (var item in list.Items) NewIds(item); break;
                    case PairValue pair: NewIds(pair.First); NewIds(pair.Second); break;
                    case AssignValue assign: NewIds(assign.Value); break;
                }
            }
            NewIds(copy.Value);
        }

        FileNote(to, "Clone", "Cloned from \"" + from + "\".");
        var c = NewContext();
        if (copy.Value is ObjectValue)
            foreach (var a in PrepareActions(rule)) ActionApplier.Apply(copy, a, c);
        work.Insert(sourceAt + 1, new WorkItem { Declaration = copy, IsOwnCopy = true, IsClone = true });
        current.Matched++;
        current.Edits += 1 + c.Edits;
        current.Skipped += c.Skipped;
    }

    /// <summary>Find and replace: replaces a value everywhere in the descriptor, whole values or any part of them.</summary>
    private void RunReplace(Rule rule, Scope scope)
    {
        var p = rule.P;
        string find = Substitute(p.Text("Find"));
        string replacement = Substitute(p.Text("Replace"));
        bool anyPart = p.Is("Mode", "part");
        var comment = new RuleAction { Comment = CommentSetting(p) };

        if (find == "")
        {
            current.Skipped++;
            FileNote(current.Name, "Replace", "Nothing replaced. The text to find is empty.");
            return;
        }

        bool Hit(NdfValue v)
        {
            string t = CanonicalWriter.PrimitiveText(v);
            return anyPart
                ? t.Contains(find, StringComparison.Ordinal)
                : t == find || (v is ScalarValue { Kind: ScalarKind.String } && JsText.Slice(t, 1, -1) == find);
        }

        bool AnyHit(NdfValue v) => v switch
        {
            ObjectValue obj => obj.Properties.Any(pr => AnyHit(pr.Value)),
            ListValue list => list.Items.Any(AnyHit),
            PairValue pair => AnyHit(pair.First) || AnyHit(pair.Second),
            AssignValue assign => AnyHit(assign.Value),
            _ => Hit(v),
        };

        scope.ForEach(whenNoConditions: true, w =>
        {
            if (!AnyHit(w.Declaration!.Value)) return;
            var d = w.Own();
            int replaced = 0;

            NdfValue Fix(NdfValue v)
            {
                if (!v.IsPrimitive) { Deep(v); return v; }
                if (!Hit(v)) return v;
                string old = CanonicalWriter.PrimitiveText(v);
                string changed = anyPart
                    ? old.Replace(find, replacement)
                    : (v is ScalarValue { Kind: ScalarKind.String } && old != find ? old[0] + replacement + old[0] : replacement);
                if (changed == old) return v;
                replaced++;
                if (log.Count < options.LogLimit) log.Add(new EditLogEntry(d.LogName, "Replace", Shorten(old), Shorten(changed)));

                var (text, trailing) = CommentText.SplitTrailingComment(changed);
                var kind = IsQuoted(text) ? ScalarKind.String : NumberText.IsPlainNumber(text) ? ScalarKind.Number : ScalarKind.Raw;
                var written = new ScalarValue(kind, text)
                {
                    AddedComment = CommentText.JoinComments(trailing, (v.AddedComment ?? "") + EditComments.For(comment, old, modName)),
                    LeadingComments = v.LeadingComments,
                };
                return written.TakesPlaceOf(v);
            }

            void Deep(NdfValue v)
            {
                switch (v)
                {
                    case ObjectValue obj: foreach (var pr in obj.Properties) pr.Value = Fix(pr.Value); break;
                    case ListValue list: for (int i = 0; i < list.Items.Count; i++) list.Items[i] = Fix(list.Items[i]); break;
                    case PairValue pair: pair.First = Fix(pair.First); pair.Second = Fix(pair.Second); break;
                    case AssignValue assign: assign.Value = Fix(assign.Value); break;
                }
            }

            Deep(d.Value);
            current.Matched++;
            current.Edits += replaced;
        });
    }

    /// <summary>True for a quoted text on one line: /^(['"]).*\1$/.</summary>
    private static bool IsQuoted(string text) =>
        text.Length >= 2 && text[0] is '\'' or '"' && text[^1] == text[0] && text.IndexOfAny(['\n', '\r', '\u2028', '\u2029']) < 0;

    /// <summary>Scale: calculates on one property, then rounds it to a step and keeps it within limits.</summary>
    private void RunScale(Rule rule, Scope scope)
    {
        var p = rule.P;
        string target = JsText.Trim(Substitute(p.Text("Target")));
        double? value = NumberSetting(p.Text("Value"));
        Arithmetic.Operations.TryGetValue(p.Text("Op"), out var operation);

        var settings = new[]
        {
            ("Value", operation is not null ? p.Text("Value") : ""),
            ("Round to a step of", p.Text("Step")),
            ("Minimum", p.Text("Min")),
            ("Maximum", p.Text("Max")),
        };
        foreach (var (label, text) in settings)
        {
            if (!IsBadNumber(text)) continue;
            current.Skipped++;
            string message = "Not run. In \"" + label + "\", " + NumberText.NotANumberMessage(Substitute(text));
            FileNote(current.Name, "Scale", message);
            Warn(message, "number");
            return;
        }
        if (target == "" || (operation is not null && value is null))
        {
            current.Skipped++;
            FileNote(current.Name, "Scale", "Not run. The rule needs a target" + (operation is not null ? " and a number." : "."));
            return;
        }

        double? step = NumberSetting(p.Text("Step"));
        string? roundMode = p.Is("RoundMode", "down") ? "down" : p.Is("RoundMode", "up") ? "up" : null;
        var action = new RuleAction
        {
            Target = target,
            Operator = "Scale",
            Comment = CommentSetting(p),
            Scale = new ScaleSettings(operation, value, step > 0 ? step.Value : 0, roundMode, NumberSetting(p.Text("Min")), NumberSetting(p.Text("Max"))),
        };
        scope.ForEach(whenNoConditions: true, w =>
        {
            var d = w.Own();
            var c = NewContext();
            ActionApplier.Apply(d, action, c);
            current.Matched++;
            Tally(c);
        });
    }

    /// <summary>Formula: sets one property from a formula over the descriptor's other properties.</summary>
    private void RunFormula(Rule rule, Scope scope)
    {
        var p = rule.P;
        string target = JsText.Trim(Substitute(p.Text("Target")));
        Formula formula;
        try
        {
            if (target == "") throw new FormatException("The rule needs a target.");
            formula = Formula.Compile(Substitute(p.Text("Expression")));
        }
        catch (FormatException e)
        {
            current.Skipped++;
            FileNote(current.Name, "Formula", "Not run. " + e.Message);
            return;
        }

        scope.ForEach(whenNoConditions: true, w =>
        {
            var d = w.Own();
            var c = NewContext();
            var seen = new Dictionary<string, double?>();

            // A name is a variable holding a number, or the first plain number found under that property name.
            double? Look(string name)
            {
                if (seen.TryGetValue(name, out var known)) return known;
                double? v;
                if (variables.TryGetValue(name, out var text) && NumberText.IsPlainNumber(text)) v = JsNumber.ParsePlain(text);
                else
                {
                    var found = new List<NdfValue>();
                    Conditions.Collect(d.Value, name, found);
                    var first = found.FirstOrDefault(x => x.IsPrimitive && NumberText.IsPlainNumber(CanonicalWriter.PrimitiveText(x)));
                    v = first is null ? null : JsNumber.ParsePlain(CanonicalWriter.PrimitiveText(first));
                }
                seen[name] = v;
                return v;
            }

            // Everything is read before the first edit.
            foreach (string name in formula.Names)
                if (name != "value") Look(name);

            var action = new RuleAction
            {
                Target = target,
                Operator = "Formula",
                Comment = CommentSetting(p),
                Formula = old =>
                {
                    try
                    {
                        return formula.Evaluate(name =>
                        {
                            double? v = name == "value" ? old : Look(name);
                            if (v is null || double.IsNaN(v.Value)) throw new Formula.MissingValueException();
                            return v.Value;
                        });
                    }
                    catch (Formula.MissingValueException) { return null; }
                },
            };
            ActionApplier.Apply(d, action, c);
            current.Matched++;
            Tally(c);
        });
    }

    /// <summary>Table: sets, multiplies or adds values from a table with one row per descriptor and one column per property.</summary>
    private void RunTable(Rule rule)
    {
        var p = rule.P;
        var rows = TableText.Read(p.Text("Text"));
        if (rows.Count < 2 || rows[0].Count < 2)
        {
            current.Skipped++;
            FileNote(current.Name, "Table", "Not run. The table needs a header row, a name column and at least one property column.");
            return;
        }

        var header = rows[0];
        var byName = new OrderedDictionary<string, List<string>>();
        var twice = new List<string>();
        foreach (var row in rows.Skip(1))
        {
            if (byName.ContainsKey(row[0]) && !twice.Contains(row[0])) twice.Add(row[0]);
            byName[row[0]] = row;
        }
        if (twice.Count > 0)
        {
            string message = "The table has more than one row for " + string.Join(", ", twice.Take(5).Select(n => "\"" + n + "\"")) +
                (twice.Count > 5 ? " and " + (twice.Count - 5) + " more" : "") + ". The last row is used.";
            FileNote(current.Name, "Table", message);
            Warn(message);
        }

        string op = p.Is("Op", "Multiply") ? "Multiply" : p.Is("Op", "Add") ? "Add" : "Set To";
        var comment = CommentSetting(p);
        var seenRows = new HashSet<string>();
        bool warned = false;

        foreach (var w in work.ToList())
        {
            if (!w.IsLiveDescriptor || !byName.TryGetValue(w.Declaration!.Name, out var row)) continue;
            seenRows.Add(w.Declaration.Name);
            if (!InGroup(w.Declaration)) continue;

            var d = w.Own();
            var c = NewContext();
            for (int k = 1; k < header.Count; k++)
            {
                string? cell = k < row.Count ? row[k] : null;
                if (string.IsNullOrEmpty(cell) || header[k] == "") continue;
                int editsBefore = c.Edits, skippedBefore = c.Skipped;
                if (op != "Set To" && IsBadNumber(cell))
                {
                    c.Skipped++;
                    string message = "\"" + header[k] + "\" was left alone. " + NumberText.NotANumberMessage(Substitute(cell));
                    FileNote(d.Name, "Table", message);
                    if (!warned)
                    {
                        warned = true;
                        Warn("Some cells were skipped, starting with " + d.Name + ": " + message, "number");
                    }
                    continue;
                }
                ActionApplier.Apply(d, new RuleAction { Target = header[k], Operator = op, Value = Substitute(cell), Comment = comment }, c);
                if (c.Edits == editsBefore && c.Skipped == skippedBefore)
                {
                    c.Skipped++;
                    FileNote(d.Name, "Table", "No property named \"" + header[k] + "\" in this descriptor.");
                }
            }
            current.Matched++;
            Tally(c);
        }
        foreach (string name in byName.Keys)
        {
            if (seenRows.Contains(name)) continue;
            current.Skipped++;
            FileNote(name, "Table", "No descriptor with this name in the file.");
        }
    }

    /// <summary>
    /// Lookup: collects values from descriptors in another loaded file, then edits the descriptors here that match one.
    /// </summary>
    private void RunLookup(Rule rule, Scope scope)
    {
        var p = rule.P;
        string file = JsText.Trim(p.TextOr("SourceFile", ""));
        string key = file.ToLowerInvariant();
        if (file == "" || !options.OtherFiles.TryGetValue(key, out string? otherText))
        {
            current.Skipped++;
            FileNote(current.Name, "Lookup", "Not run. " + (file != "" ? "\"" + file + "\" is not loaded on the Files page." : "The rule needs a source file."));
            return;
        }
        if (!otherFilesRead.TryGetValue(key, out var other))
        {
            try
            {
                other = otherFilesRead[key] = Parser.Parse(otherText.Length > 0 && otherText[0] == '﻿' ? otherText[1..] : otherText);
            }
            catch (NdfParseException e)
            {
                current.Skipped++;
                FileNote(current.Name, "Lookup", "Not run. \"" + file + "\" could not be parsed. " + e.Message);
                return;
            }
        }

        var sourceConditions = Prepare(p.Conditions("SourceConditions"));
        var wanted = new HashSet<string>();
        string collect = p.TextOr("Collect", Conditions.DescriptorTitle);
        int sourceCount = 0, valueCount = 0;

        // A quoted value matches without its quotes; a reference such as $/GFX/Weapon/X also matches by its last part.
        static string[] Forms(string text)
        {
            string unquoted = IsQuoted(text) ? text[1..^1] : text;
            int slash = unquoted.LastIndexOf('/');
            return slash >= 0 ? [unquoted, unquoted[(slash + 1)..]] : [unquoted];
        }
        static List<NdfValue> Flatten(List<NdfValue> found)
        {
            var output = new List<NdfValue>();
            foreach (var x in found)
            {
                if (x.IsPrimitive) output.Add(x);
                else if (x is ListValue list) output.AddRange(list.Items.Where(i => i.IsPrimitive));
            }
            return output;
        }

        foreach (var sd in other.Declarations)
        {
            if (sd.IsUnnamed || sd.Value is not ObjectValue || !Conditions.Pass(sd, sourceConditions, p.Is("SourceGate", "All"), true)) continue;
            sourceCount++;
            if (collect == Conditions.DescriptorTitle)
            {
                wanted.Add(sd.Name);
                valueCount++;
            }
            else
            {
                var found = new List<NdfValue>();
                Conditions.Collect(sd.Value, collect, found);
                foreach (var x in Flatten(found))
                {
                    valueCount++;
                    foreach (string f in Forms(CanonicalWriter.PrimitiveText(x))) wanted.Add(f);
                }
            }
        }

        string matchOn = p.TextOr("MatchOn", Conditions.DescriptorTitle);
        var actions = PrepareActions(rule);
        scope.ForEach(whenNoConditions: true, w =>
        {
            bool matches;
            if (matchOn == Conditions.DescriptorTitle) matches = wanted.Contains(w.Declaration!.Name);
            else
            {
                var found = new List<NdfValue>();
                Conditions.Collect(w.Declaration!.Value, matchOn, found);
                matches = Flatten(found).Any(x => Forms(CanonicalWriter.PrimitiveText(x)).Any(wanted.Contains));
            }
            if (!matches) return;
            var d = w.Own();
            var c = NewContext();
            foreach (var a in actions) ActionApplier.Apply(d, a, c);
            current.Matched++;
            Tally(c);
        });

        static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
        FileNote(current.Name, "Lookup", Count(valueCount) + " value(s) collected from " + Count(sourceCount) + " descriptor(s) in \"" + file + "\"; " +
            Count(current.Matched) + " descriptor(s) matched here.");
    }

    /// <summary>Check: changes nothing; warns when the number of matching descriptors is not as expected.</summary>
    private void RunCheck(Rule rule, Scope scope)
    {
        var p = rule.P;
        int n = 0;
        scope.ForEach(whenNoConditions: true, _ => n++);
        current.Matched = n;

        string count = JsText.Trim(Substitute(p.Text("Count")));
        string mode = p.TextOr("Expect", "atLeast");
        if (mode != "none" && count != "" && !WholeNumber().IsMatch(count))
        {
            current.Skipped++;
            string message = "Not run. \"" + count + "\" is not a whole number.";
            FileNote(current.Name, "Check", message);
            Warn(message, "number");
            return;
        }

        double want = count == "" ? 1 : (WholeNumber().IsMatch(count) ? double.Parse(count, CultureInfo.InvariantCulture) : double.NaN);
        bool ok = mode switch
        {
            "none" => n == 0,
            "atMost" => n <= want,
            "exactly" => n == want,
            _ => n >= want,
        };
        if (ok) return;

        string expected = mode == "none"
            ? "no matching descriptors"
            : (mode == "atMost" ? "at most " : mode == "exactly" ? "exactly " : "at least ") + JsNumber.ToText(want);
        string note = JsText.Trim(p.TextOr("Message", "")) != "" ? JsText.Trim(Substitute(p.Text("Message"))) + " " : "";
        string text = note + "Expected " + expected + ", found " + n + ".";
        Warn(text);
        FileNote(current.Name, "Check", "Failed. " + text);
    }

    // ---- after the rules ----

    /// <summary>A rule that renamed a descriptor to a name another descriptor already has is warned about.</summary>
    private void WarnAboutDuplicateNames()
    {
        if (renamed.Count == 0) return;
        var counts = new Dictionary<string, int>();
        foreach (var w in work)
        {
            if (w.IsRemoved) continue;
            IEnumerable<string> names = w.IsAdded ? w.AddedNames : w.Declaration!.IsUnnamed ? [] : [w.Declaration.Name];
            foreach (string name in names) counts[name] = counts.GetValueOrDefault(name) + 1;
        }
        foreach (var (name, by) in renamed)
            if (counts.GetValueOrDefault(name) > 1)
                warnings.Add(new RunWarning(by, "A descriptor was renamed to \"" + name + "\", a name another descriptor already has."));
    }

    /// <summary>
    /// Drops the descriptors' whole text kept for "Entire Descriptor" conditions, now that the rules are done: in a large
    /// file it is as large as the file itself (83 MB for UniteDescriptor.ndf), and writing the export needs that room.
    /// </summary>
    private void ForgetConditionTexts()
    {
        foreach (WorkItem item in work)
            if (item.Declaration is not null) item.Declaration.CachedConditionText = null;
        foreach (Declaration declaration in document.Declarations) declaration.CachedConditionText = null;
    }

    private RunResult Finish()
    {
        string? text = null;
        string parseError = "";
        if (options.WantText || options.CheckOutput)
        {
            var written = ExportWriter.Write(work, document.Source, lineBreak);
            text = written.Text;
            // Safety net: what the rules wrote is read back, so text with unbalanced brackets or quotes is caught before
            // the file is saved. Only the declarations a rule changed or added are read: the rest is the file's own text.
            if (options.CheckOutput) parseError = CheckWrittenParts(written);
        }
        return new RunResult
        {
            Text = options.WantText ? text : null,
            ParseError = parseError,
            Stats = stats,
            Logs = logs,
            Warnings = warnings,
        };
    }

    private static string CheckWrittenParts(ExportWriter.Result written)
    {
        // The parts are read on several processor cores at once, as each is read on its own: in a large file with many
        // changes (UniteDescriptor.ndf) this check takes a third of an export's time. The first part that fails, in
        // file order, is the one reported, as when they were read one after another.
        List<(int Start, int End)> parts = written.ChangedSegments;
        var failures = new Exception?[parts.Count];
        Parallel.For(0, parts.Count, k =>
        {
            (int start, int end) = parts[k];
            try
            {
                Parser.Parse(string.Concat(written.Pieces.Skip(start).Take(end - start)));
            }
            catch (Exception e)
            {
                failures[k] = e;
            }
        });

        for (int k = 0; k < parts.Count; k++)
        {
            if (failures[k] is null) continue;
            // anything but a parse error goes on to the caller as it would have without the cores
            if (failures[k] is not NdfParseException e) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failures[k]!);
            else return WithFileLineNumber(e, written, parts[k].Start);
        }
        return "";
    }

    /// <summary>A parse error's message with its line counted from the start of the exported file, not of the part read.</summary>
    private static string WithFileLineNumber(NdfParseException e, ExportWriter.Result written, int start)
    {
        int line = 1;
        for (int q = 0; q < start; q++)
            foreach (char ch in written.Pieces[q])
                if (ch == '\n') line++;
        string message = LineNumberAtStart().Replace(e.Message, m => "Line " + (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + line - 1), 1);
        int endOfFile = message.IndexOf("(end of file)", StringComparison.Ordinal);
        return endOfFile < 0 ? message : message[..endOfFile] + "(end of the descriptor)" + message[(endOfFile + "(end of file)".Length)..];
    }

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex VariableReference();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z")]
    private static partial Regex DescriptorName();

    [GeneratedRegex(@"\A[0-9]+\z")]
    private static partial Regex WholeNumber();

    [GeneratedRegex(@"\ALine ([0-9]+)")]
    private static partial Regex LineNumberAtStart();
}
