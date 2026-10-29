using System.Text;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Text;
using EasyNdf.Engine.Writing;

namespace EasyNdf.Engine.Rules;

/// <summary>What one action changed, as the Details list shows it.</summary>
/// <param name="Descriptor">The descriptor's name, or a stand-in's label.</param>
/// <param name="Target">The property or setting changed.</param>
/// <param name="Old">The value before, shortened to 120 characters.</param>
/// <param name="New">The value after, shortened to 120 characters.</param>
/// <param name="IsNote">
/// Whether it is a note rather than a change: "Not run", "Removed from the file.", a failed check and the like. The
/// conflicts between mods leave notes out; a descriptor a Clone or Add rule makes, or cannot make because its name is
/// taken, is not a note, so two mods making one name are found.
/// </param>
public sealed record EditLogEntry(string Descriptor, string Target, string Old, string New, bool IsNote = false);

/// <summary>Counts and logs for the rule that is running.</summary>
public sealed class EditContext
{
    public int Edits { get; set; }
    public int Skipped { get; set; }

    /// <summary>The rule's edit log. Entries stop being added at <see cref="LogLimit"/>; counting goes on.</summary>
    public required List<EditLogEntry> Log { get; init; }
    public required int LogLimit { get; init; }

    /// <summary>The name of the rule that is running, for the renamed-descriptor check.</summary>
    public required string RuleName { get; init; }

    /// <summary>Each name a rule renamed a descriptor to, and the rule that did it.</summary>
    public required OrderedDictionary<string, string> Renamed { get; init; }

    /// <summary>Called when a value is left alone because it has too many digits to calculate with exactly.</summary>
    public required Action<Declaration, string> TooManyDigits { get; init; }

    /// <summary>The mod's name for "Edited by" comments.</summary>
    public required string ModName { get; init; }

    /// <summary>The "Decimal places" setting, or null for no limit.</summary>
    public required int? DecimalPlaces { get; init; }
}

/// <summary>
/// Applies one action to one descriptor: finds every value of the target property, at any depth, and changes it.
/// A target can be a path, "Outer > Inner": then only the Inner found inside a value named Outer is changed (one cost
/// matrix's key among a file's many, for one). "Add Property" adds a property the descriptor does not have.
/// </summary>
public sealed partial class ActionApplier
{
    /// <summary>What separates the parts of a target path, as the stand-ins' labels write a path.</summary>
    public const string PathSeparator = " > ";

    /// <summary>
    /// The operator that sets a list or an object to a value typed in full. It is its own operator, not plain Set To,
    /// because the HTML app's engine leaves lists and objects alone on Set To, and the two must give the same output.
    /// </summary>
    public const string SetWholeOperator = "Set To [list, object]";

    /// <summary>The operator that takes a property out of the descriptor, its line and all.</summary>
    public const string RemovePropertyOperator = "Remove Property";

    /// <summary>Whether Remove Property found the property anywhere.</summary>
    private bool removedAny;

    private readonly Declaration descriptor;
    private readonly RuleAction action;
    private readonly EditContext context;

    /// <summary>The target as written, for the edit log.</summary>
    private readonly string fullTarget;

    /// <summary>The name changed: the target, or the last part of a target path.</summary>
    private readonly string target;

    /// <summary>The parts of a target path still to be found above the target, outermost first; none for a plain target.</summary>
    private string[] pathAbove;

    private readonly string op;
    private readonly string value;

    /// <summary>
    /// Whether the target path starts with "&gt;": it then names the descriptor's own properties, each part one level
    /// below the one before, rather than a property found at any depth.
    /// </summary>
    private readonly bool isAnchored;
    private bool IsListOperation => op is "Insert" or "Replace";

    /// <summary>Whether the action sets lists and objects whole (<see cref="SetWholeOperator"/>).</summary>
    private bool SetsWholeValues => action.Operator == SetWholeOperator;

    /// <summary>Whether every part of a target path has been found, so the target itself may now be changed.</summary>
    private bool IsAtTarget => pathAbove.Length == 0;

    private ActionApplier(Declaration descriptor, RuleAction action, EditContext context, string target, string op)
    {
        this.descriptor = descriptor;
        this.action = action;
        this.context = context;
        fullTarget = target;
        // "> A > B": a path that starts at the descriptor's own properties and steps down one level per part
        string trimmed = JsText.Trim(target);
        isAnchored = trimmed.StartsWith('>');
        if (isAnchored) target = JsText.Trim(trimmed[1..]);
        string[] parts = [.. target.Split(PathSeparator).Select(part => JsText.Trim(part))];
        this.target = parts[^1];
        pathAbove = parts[..^1];
        this.op = op;
        value = action.Value ?? "";
    }

    /// <summary>Applies the action to the descriptor.</summary>
    public static void Apply(Declaration descriptor, RuleAction action, EditContext context)
    {
        string? target = action.TargetInUse;
        string op = EditComments.OperatorName(action.Operator);
        if (string.IsNullOrEmpty(target) || op == "" || op == "Add descriptor") return; // Add descriptor is a file-level action

        new ActionApplier(descriptor, action, context, target, op).Run();
    }

    private void Run()
    {
        // An object inside a descriptor has no title of its own, and the file's constants have neither title nor type.
        if (descriptor.IsStandIn && (target == Conditions.DescriptorTitle || (descriptor.IsConstantsStandIn && target == Conditions.DescriptorType)))
        {
            context.Skipped++;
            return;
        }
        if (target == Conditions.DescriptorTitle || target == Conditions.DescriptorType)
        {
            EditNameOrType();
            return;
        }
        if (op == "Add Property")
        {
            AddProperty();
            return;
        }
        Walk(descriptor.Value);
        if (op == RemovePropertyOperator && !removedAny) context.Skipped++;
    }

    // ---- helpers ----

    /// <summary>"\n" typed into a rule's value box means a line break.</summary>
    private static string WithLineBreaks(string text) => text.Replace("\\n", "\n");

    private static string Shorten(string text) => text.Length > 120 ? text[..117] + "..." : text;

    private void Note(string oldText, string newText)
    {
        context.Edits++;
        if (context.Log.Count < context.LogLimit)
            context.Log.Add(new EditLogEntry(descriptor.LogName, fullTarget, Shorten(oldText), Shorten(newText)));
    }

    /// <summary>The text actions: Append, Prepend and Remove. Null when the operation does not apply to text.</summary>
    private string? TextOperation(string text)
    {
        string typed = WithLineBreaks(value);
        return op switch
        {
            "Append" => text + typed,
            "Prepend" => typed + text,
            "Remove" => typed == "" ? null : text.Replace(typed, ""),
            _ => null,
        };
    }

    private string CommentFor(string oldValue) => EditComments.For(action, oldValue, context.ModName);

    // ---- the descriptor's name and type ----

    private void EditNameOrType()
    {
        bool isTitle = target == Conditions.DescriptorTitle;
        var obj = descriptor.Value as ObjectValue;
        string current = isTitle ? descriptor.Name : obj?.TypeName ?? "";
        string? changed = TextOperation(current);
        if (changed == current) return;
        if (changed is null) { context.Skipped++; return; }

        // A comment written after the name or type is kept beside it, not inside it.
        var (name, comment) = CommentText.SplitTrailingComment(changed);
        if (isTitle)
        {
            descriptor.Name = name;
            if (comment != "") descriptor.NameComment = (descriptor.NameComment ?? "") + comment;
            context.Renamed[name] = context.RuleName;
        }
        else if (obj is not null)
        {
            obj.TypeName = name;
            if (comment != "") obj.TypeNameComment = (obj.TypeNameComment ?? "") + comment;
        }
        Note(current, changed);
    }

    // ---- walking the descriptor ----

    private void Walk(NdfValue node)
    {
        switch (node)
        {
            case ObjectValue obj:
                if (op == RemovePropertyOperator && IsAtTarget && RemoveFrom(obj)) break;
                foreach (var property in obj.Properties) property.Value = Visit(property.Name, property.Value);
                break;

            case ListValue list:
                for (int i = 0; i < list.Items.Count; i++) list.Items[i] = Visit("Index" + i, list.Items[i]);
                break;

            case PairValue { Kind: PairKind.Pair } pair:
                // (Key, Value) with the key as the target: the value is changed, for actions that set or calculate.
                bool keyIsTarget = IsAtTarget && pair.First.IsPrimitive && CanonicalWriter.PrimitiveText(pair.First) == target;
                bool setsOrCalculates = Arithmetic.Operations.ContainsKey(op) || op is "Set To" or "Scale" or "Formula";
                if (keyIsTarget && pair.Second.IsPrimitive && setsOrCalculates)
                {
                    pair.Second = EditPrimitive(pair.Second);
                    break;
                }
                // a key whose value is a list or an object, such as a cost matrix's (Factory/Tanks, [1, 1, 2]): Set To
                // writes the whole value
                if (keyIsTarget && SetsWholeValues && pair.Second is ListValue or ObjectValue)
                {
                    pair.Second = SetWhole(pair.Second);
                    break;
                }
                pair.First = Visit("Value1", pair.First);
                pair.Second = Visit("Value2", pair.Second);
                break;

            case PairValue or:
                or.First = Visit(null, or.First);
                or.Second = Visit(null, or.Second);
                break;

            case AssignValue assign:
                assign.Value = Visit(null, assign.Value);
                break;
        }
    }

    /// <summary>Visits one value found under a name. Returns the value to keep in its place.</summary>
    private NdfValue Visit(string? name, NdfValue node)
    {
        if (!IsAtTarget)
        {
            VisitOnPath(name, node);
            return node;
        }
        if (name == target)
        {
            // a single value under the name is set as plain Set To sets it; flags (A | B) and pairs ((1, 2)) are set whole
            if (SetsWholeValues && node is ListValue or ObjectValue or PairValue) return SetWhole(node);
            // a list an earlier action wrote in full (Set To [list, object]) is read back, so it can be added to
            if ((IsListOperation || op == "Remove") && node is ScalarValue { Start: null, Kind: ScalarKind.Raw } written && ListWrittenAs(written.Text) is ListValue writtenList)
                return EditWrittenList(writtenList, written);
            if (node is ListValue list)
            {
                if (IsListOperation || op == "Remove")
                {
                    EditList(list);
                    return node;
                }
                if (op == "Append")
                {
                    string typed = WithLineBreaks(value);
                    list.AddedComment = (list.AddedComment ?? "") + typed + CommentFor("");
                    Note("", JsText.Trim(typed));
                    return node;
                }
            }
            else if (node.IsPrimitive && !IsListOperation)
            {
                return EditPrimitive(node);
            }
        }
        // an anchored path goes no deeper than the level its last part names
        if (!node.IsPrimitive && !isAnchored) Walk(node);
        return node;
    }

    /// <summary>
    /// On the way to a path's target: a value named as the path's next part is looked inside for the rest of the path;
    /// any other value is looked inside for that part.
    /// </summary>
    private void VisitOnPath(string? name, NdfValue node)
    {
        if (node.IsPrimitive) return;
        if (name == pathAbove[0])
        {
            string[] before = pathAbove;
            pathAbove = pathAbove[1..];
            Walk(node);
            pathAbove = before;
            return;
        }
        // an anchored path only steps into the property its next part names
        if (!isAnchored) Walk(node);
    }

    // ---- a property removed ----

    /// <summary>
    /// "Remove Property": takes the target out of an object that has it. The writer leaves out its line, and the
    /// comments above it stay. True when the object had it.
    /// </summary>
    private bool RemoveFrom(ObjectValue obj)
    {
        int index = obj.Properties.FindIndex(property => property.Name == target && !property.IsAddedByRule);
        if (index < 0) return false;
        Property removed = obj.Properties[index];
        obj.Properties.RemoveAt(index);
        (obj.RemovedProperties ??= []).Add(removed);
        removedAny = true;
        Note(CanonicalWriter.Write(removed.Value), "(removed)");
        return true;
    }

    // ---- a list an action wrote in full ----

    /// <summary>The list a rule wrote as text, read back; null when the text is not one list.</summary>
    private static ListValue? ListWrittenAs(string text)
    {
        try
        {
            var document = Parser.Parse("ListWrittenByARule is " + text + "\n");
            return document.Declarations.Count == 1 ? document.Declarations[0].Value as ListValue : null;
        }
        catch (NdfParseException)
        {
            return null;
        }
    }

    /// <summary>Applies a list action to a list an earlier action wrote, and writes the list again.</summary>
    private NdfValue EditWrittenList(ListValue list, ScalarValue written)
    {
        int edits = context.Edits;
        EditList(list);
        if (context.Edits == edits) return written;
        var rewritten = ScalarValue.Written(CanonicalWriter.Write(new ListValue(list.TypeName, list.Items)));
        rewritten.FitsIndentation = true;
        rewritten.AddedComment = (written.AddedComment ?? "") + (list.AddedComment ?? "");
        if (written.LeadingComments is not null) rewritten.LeadingComments = written.LeadingComments;
        return rewritten.TakesPlaceOf(written);
    }

    /// <summary>
    /// Set To on a list, an object or a MAP: the whole value is replaced by the text typed, written as typed. It is
    /// the way to give a cost matrix's key a new list of costs.
    /// </summary>
    private NdfValue SetWhole(NdfValue node)
    {
        string old = CanonicalWriter.Write(node);
        var (text, comment) = CommentText.SplitTrailingComment(JsText.Trim(WithLineBreaks(value)));
        if (text == "" || text == JsText.Trim(old)) return node;
        ScalarValue written = ScalarValue.Written(text);
        written.FitsIndentation = true;
        written.AddedComment = CommentText.JoinComments(comment, (node.AddedComment ?? "") + CommentFor(old));
        if (node.LeadingComments is not null) written.LeadingComments = node.LeadingComments;
        Note(old, text + comment);
        return written.TakesPlaceOf(node);
    }

    // ---- a property added ----

    /// <summary>
    /// "Add Property": the descriptor (or the object a type scope found) gets a property it does not have, named as
    /// the target, with the value typed; one it has already is left alone, as Set To changes that. A unit's line in a
    /// division that needed no transport, for one, has no AvailableTransportList to insert into until one is added.
    /// </summary>
    private void AddProperty()
    {
        if (ObjectAtPath() is not ObjectValue obj || target == "")
        {
            context.Skipped++;
            return;
        }
        if (obj.Properties.Any(property => property.Name == target))
        {
            context.Skipped++;
            return;
        }
        var (text, comment) = CommentText.SplitTrailingComment(JsText.Trim(WithLineBreaks(value)));
        if (text == "")
        {
            context.Skipped++;
            return;
        }
        ScalarValue written = ScalarValue.Written(text);
        written.FitsIndentation = true;
        written.AddedComment = CommentText.JoinComments(comment, CommentFor(""));
        obj.Properties.Add(new Property(target, written) { IsAddedByRule = true });
        Note("", text + comment);
    }

    /// <summary>
    /// The object Add Property adds to: the descriptor itself, or, for a path such as "ComponentFrame &gt; Width", the
    /// object each part leads to in turn, one level at a time from the descriptor's own properties. Null when a part
    /// is missing or does not hold an object.
    /// </summary>
    private ObjectValue? ObjectAtPath()
    {
        if (descriptor.Value is not ObjectValue holder) return null;
        foreach (string part in pathAbove)
        {
            if (holder.Properties.LastOrDefault(property => property.Name == part)?.Value is not ObjectValue inner) return null;
            holder = inner;
        }
        return holder;
    }

    // ---- single values ----

    /// <summary>Changes one single value. Returns the new value, or the same one when nothing changed.</summary>
    private NdfValue EditPrimitive(NdfValue node)
    {
        string old = CanonicalWriter.PrimitiveText(node);
        ScalarValue written;
        string extraComment = "";

        if (Arithmetic.Operations.TryGetValue(op, out var calculate))
        {
            double number = NumberText.ParseTyped(value);
            if (!NumberText.IsPlainNumber(old) || double.IsNaN(number)) { context.Skipped++; return node; }
            string typed = JsText.Trim(value);
            double result = calculate(JsNumber.ParsePlain(old), number);

            bool longWholeNumbers = Arithmetic.IsWholeNumberText(old) && Arithmetic.IsWholeNumberText(typed) &&
                (Arithmetic.HasTooManyDigits(old) || Arithmetic.HasTooManyDigits(typed) || Arithmetic.IsUnsafe(result));
            if (longWholeNumbers)
            {
                string? exact = Arithmetic.Exact(op, old, typed);
                if (exact is null) { context.Skipped++; context.TooManyDigits(descriptor, old); return node; }
                written = new ScalarValue(ScalarKind.Number, exact);
            }
            else
            {
                if (!double.IsFinite(result)) { context.Skipped++; return node; }
                if (Arithmetic.HasTooManyDigits(old) || Arithmetic.HasTooManyDigits(typed) || Arithmetic.IsUnsafe(result))
                {
                    context.Skipped++;
                    context.TooManyDigits(descriptor, old);
                    return node;
                }
                written = new ScalarValue(ScalarKind.Number,
                    Arithmetic.Format(Arithmetic.LimitDecimals(result, context.DecimalPlaces), old.Contains('.')));
            }
        }
        else if (op == "Set To")
        {
            var (text, comment) = CommentText.SplitTrailingComment(WithLineBreaks(value));
            extraComment = comment;
            written = ScalarValue.Written(text);
            // a list or an object typed in place of a single value fits where it lands, as Set To a list does
            written.FitsIndentation = SetsWholeValues;
        }
        else if (op is "Append" or "Prepend" or "Remove")
        {
            if (node is ScalarValue { Kind: ScalarKind.String })
            {
                // Edit inside the quotes, and keep the quotes.
                char quote = old[0];
                string? changed = TextOperation(old[1..^1]);
                if (changed is null) { context.Skipped++; return node; }
                written = new ScalarValue(ScalarKind.String, quote + changed + quote);
            }
            else
            {
                string? changed = TextOperation(old);
                if (changed is null) { context.Skipped++; return node; }
                var (text, comment) = CommentText.SplitTrailingComment(changed);
                extraComment = comment;
                written = ScalarValue.Written(text);
            }
        }
        else if (op is "Scale" or "Formula")
        {
            double? result = op == "Scale" ? ScaleResult(old) : FormulaResult(old);
            if (result is null) { context.Skipped++; return node; }
            if (!double.IsFinite(result.Value)) { context.Skipped++; return node; }
            if (Arithmetic.HasTooManyDigits(old) || Arithmetic.IsUnsafe(result.Value))
            {
                context.Skipped++;
                context.TooManyDigits(descriptor, old);
                return node;
            }
            written = new ScalarValue(ScalarKind.Number, Arithmetic.Format(result.Value, old.Contains('.')));
            if (written.Text == old) return node;
        }
        else
        {
            return node;
        }

        // The value is already what the rule would make it: nothing is written, counted or commented.
        if (written.Text == old && extraComment == "") return node;

        written.AddedComment = CommentText.JoinComments(extraComment, (node.AddedComment ?? "") + CommentFor(old));
        if (node.LeadingComments is not null) written.LeadingComments = node.LeadingComments;
        Note(old, written.Text + extraComment);
        return written.TakesPlaceOf(node);
    }

    /// <summary>The Scale rule's result: calculate, then round to a step or to the decimal places, then limit.</summary>
    private double? ScaleResult(string old)
    {
        if (!NumberText.IsPlainNumber(old)) return null;
        double x = JsNumber.ParsePlain(old);
        var s = action.Scale!;
        double r = s.Operation is not null ? s.Operation(x, s.Value ?? double.NaN) : x;
        if (s.Step > 0)
        {
            double q = r / s.Step;
            double steps = s.RoundMode == "down" ? Math.Floor(q + 1e-9) : s.RoundMode == "up" ? Math.Ceiling(q - 1e-9) : JsNumber.Round(q);
            r = steps * s.Step;
        }
        else
        {
            r = Arithmetic.LimitDecimals(r, context.DecimalPlaces);
        }
        if (s.Min is not null && r < s.Min) r = s.Min.Value;
        if (s.Max is not null && r > s.Max) r = s.Max.Value;
        return r;
    }

    private double? FormulaResult(string old)
    {
        double x = NumberText.IsPlainNumber(old) ? JsNumber.ParsePlain(old) : double.NaN;
        return action.Formula!(x);
    }

    // ---- lists ----

    /// <summary>A list's items written on one line with no spaces, for the edit log.</summary>
    private static string Compact(ListValue list)
    {
        string text = CanonicalWriter.Write(new ListValue(list.TypeName, list.Items));
        var output = new StringBuilder(text.Length);
        foreach (char c in text)
            if (!JsText.IsWhiteSpace(c)) output.Append(c);
        return output.ToString();
    }

    /// <summary>Remove takes out one item; Insert and Replace take "index: value", where the index can be a number, FirstIndex or LastIndex, plus or minus a number.</summary>
    private void EditList(ListValue list)
    {
        string old = Compact(list);

        if (op == "Remove")
        {
            string wanted = TrailingCommas().Replace(JsText.Trim(WithLineBreaks(value)), "");
            int index = list.Items.FindIndex(item =>
            {
                string text = JsText.Trim(CanonicalWriter.Write(item));
                return text == wanted || (item is ScalarValue { Kind: ScalarKind.String } && JsText.Slice(text, 1, -1) == wanted);
            });
            if (index < 0) { context.Skipped++; return; }
            list.RemoveItemAt(index);
        }
        else
        {
            int colon = value.IndexOf(':');
            if (colon < 0) { context.Skipped++; return; }
            string indexText = JsText.Trim(value[..colon]);
            string itemText = JsText.Trim(WithLineBreaks(value[(colon + 1)..]));
            int length = list.Items.Count;
            bool insert = op == "Insert";

            double index;
            Match named = NamedIndex().Match(indexText);
            Match beside = BesideEntry().Match(indexText);
            if (insert && beside.Success)
            {
                // "Before X" or "After X": beside the first item whose text holds X, or at the end when none does
                int found = IndexOfEntryHolding(list, beside.Groups[2].Value);
                bool before = beside.Groups[1].Value.Equals("before", StringComparison.OrdinalIgnoreCase);
                index = found < 0 ? length : before ? found : found + 1;
            }
            else if (named.Success)
            {
                double start = named.Groups[1].Value.Equals("firstindex", StringComparison.OrdinalIgnoreCase) ? 0 : (insert ? length : length - 1);
                index = start + (named.Groups[2].Success ? ParseWhole(named.Groups[2].Value) : 0);
            }
            else if (NumberIndex().IsMatch(indexText))
            {
                index = ParseWhole(indexText);
            }
            else { context.Skipped++; return; }
            if (index < 0 || index > (insert ? length : length - 1)) { context.Skipped++; return; }

            var (body, trailingComment) = SplitItemComment(itemText);
            var item = ScalarValue.Written(body);
            item.TrailingLineComment = trailingComment;
            int at = (int)index;

            if (op == "Replace" && trailingComment is null && JsText.Trim(CanonicalWriter.Write(list.Items[at])) == body) return; // already that value

            if (insert)
            {
                list.Items.Insert(at, item);
            }
            else
            {
                if (list.Items[at].LeadingComments is not null) item.LeadingComments = list.Items[at].LeadingComments;
                list.Items[at] = item.TakesPlaceOf(list.Items[at]);
            }
        }
        list.AddedComment = (list.AddedComment ?? "") + CommentFor(old);
        Note(old, Compact(list));
    }

    /// <summary>A whole number that may have spaces after its sign, such as "+ 2".</summary>
    private static double ParseWhole(string text) =>
        double.Parse(Regex.Replace(text, @"\s+", ""), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Splits a value typed for Insert or Replace into the value and a // comment on its last line.
    /// A // comment on an earlier line stays part of the value.
    /// </summary>
    private static (string Body, string? Comment) SplitItemComment(string text)
    {
        int lastBreak = text.LastIndexOf('\n');
        char quote = '\0';
        for (int j = 0; j < text.Length; j++)
        {
            char ch = text[j];
            if (ch == '\n') quote = '\0';
            else if (quote != '\0') { if (ch == quote) quote = '\0'; }
            else if (ch is '"' or '\'') quote = ch;
            else if (ch == '/' && JsText.At(text, j + 1) == '/')
            {
                if (j > lastBreak) return (JsText.TrimEnd(text[..j]), text[j..]);
                j = text.IndexOf('\n', j) - 1;
            }
        }
        return (text, null);
    }

    [GeneratedRegex(@",+\z")]
    private static partial Regex TrailingCommas();

    [GeneratedRegex(@"\A(before|after)\s+(.+)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex BesideEntry();

    /// <summary>The first item whose text holds the given text, spaces and line breaks aside; -1 when none does.</summary>
    private static int IndexOfEntryHolding(ListValue list, string wanted)
    {
        string bare = WithoutWhiteSpace(wanted);
        return bare == "" ? -1 : list.Items.FindIndex(item => WithoutWhiteSpace(CanonicalWriter.Write(item)).Contains(bare, StringComparison.Ordinal));
    }

    private static string WithoutWhiteSpace(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (char c in text)
            if (!JsText.IsWhiteSpace(c)) output.Append(c);
        return output.ToString();
    }

    [GeneratedRegex(@"\A(lastindex|firstindex)\s*([+-]?\s*[0-9]+)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedIndex();

    [GeneratedRegex(@"\A-?[0-9]+\z")]
    private static partial Regex NumberIndex();
}
