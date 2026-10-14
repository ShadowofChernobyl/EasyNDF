using System.Text;

namespace EasyNdf.Engine.Rules;

/// <summary>Reads a Table rule's text: cells separated by commas, semicolons or tabs, with "quoted" cells allowed.</summary>
public static class TableText
{
    /// <summary>
    /// The rows of the table, each a list of trimmed cells. Rows with only empty cells are left out.
    /// </summary>
    /// <remarks>
    /// The separator is chosen from the first line: a tab if it has one, otherwise a semicolon if it has one and no comma,
    /// otherwise a comma. That way text pasted from a spreadsheet works as it is.
    /// </remarks>
    public static List<List<string>> Read(string? text)
    {
        text = (text ?? "").Replace("\r", "");
        string firstLine = text.Split('\n')[0];
        char separator = firstLine.Contains('\t') ? '\t' : (firstLine.Contains(';') && !firstLine.Contains(',') ? ';' : ',');

        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;

        void EndCell()
        {
            row.Add(Text.JsText.Trim(cell.ToString()));
            cell.Clear();
        }

        void EndRow()
        {
            EndCell();
            if (row.Any(c => c != "")) rows.Add(row);
            row = [];
        }

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } // "" inside quotes is one quote
                    else inQuotes = false;
                }
                else cell.Append(ch);
            }
            else if (ch == '"' && Text.JsText.Trim(cell.ToString()) == "") inQuotes = true;
            else if (ch == separator) EndCell();
            else if (ch == '\n') EndRow();
            else cell.Append(ch);
        }
        EndRow();
        return rows;
    }
}
