namespace EasyNdf.Engine.Rules;

/// <summary>The comments actions write next to the values they change.</summary>
public static class EditComments
{
    /// <summary>The operator's name without the part in brackets: "Multiply [int, float]" gives "Multiply".</summary>
    public static string OperatorName(string? op)
    {
        string text = op ?? "";
        int bracket = text.IndexOf('[');
        return bracket < 0 ? text : Text.JsText.TrimEnd(text[..bracket]);
    }

    /// <summary>What an action does, in a few words: the comment written when the comment box is left empty.</summary>
    public static string Automatic(RuleAction? action)
    {
        string op = OperatorName(action?.Operator);
        string value = Text.JsText.Trim(string.Join(" ", (action?.Value ?? "").Split("\\n")));

        // "2: Value" in the list actions: the index before the colon, the value after it.
        (string Index, string Item) IndexAndValue()
        {
            int colon = value.IndexOf(':');
            return colon < 0 ? ("", value) : (Text.JsText.Trim(value[..colon]), Text.JsText.Trim(value[(colon + 1)..]));
        }

        switch (op)
        {
            case "Set To": return "Set to " + value;
            case "Add": return "Added " + value;
            case "Subtract": return "Subtracted " + value;
            case "Multiply": return "Multiplied by " + value;
            case "Divide": return "Divided by " + value;
            case "Append": return "Appended " + value;
            case "Prepend": return "Prepended " + value;
            case "Remove": return "Removed " + value;
            case "Add Property": return "Added";
            case "Remove Property": return "Removed";
            case "Insert":
            {
                var (index, item) = IndexAndValue();
                return "Inserted " + item + (index != "" ? " at " + index : "");
            }
            case "Replace":
            {
                var (index, item) = IndexAndValue();
                return index != "" ? "Replaced " + index + " with " + item : "Replaced with " + item;
            }
            case "Scale": return "Scaled";
            case "Formula": return "Set by formula";
        }
        return op != "" ? op + " " + value : "";
    }

    /// <summary>
    /// The comment written after a changed value, such as " /*Multiplied by 2 Original Value: 10*/", or "" when the
    /// action's comment is off.
    /// </summary>
    /// <param name="action">The action, with its comment settings.</param>
    /// <param name="originalValue">The value before the change.</param>
    /// <param name="modName">The mod's name for "Edited by", with any "*/" already made harmless.</param>
    public static string For(RuleAction action, string originalValue, string modName)
    {
        var c = action.Comment;
        if (c is null || !c.Enabled) return "";
        string own = c.Text ?? "";
        string text = Text.JsText.Trim(own) != "" ? own : Automatic(action).Replace("*/", "* /");
        string mod = c.IncludeMod && modName != "" ? " Edited by " + modName : "";
        string time = c.IncludeTimestamp ? " [" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " UTC]" : "";
        // an added property had no value before
        bool hadValue = OperatorName(action.Operator) != "Add Property";
        string original = c.IncludeOriginalValue && hadValue ? " Original Value: " + originalValue : "";
        return " /*" + text + mod + time + original + "*/";
    }
}
