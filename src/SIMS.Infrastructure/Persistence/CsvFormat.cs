using System.Text;

namespace SIMS.Infrastructure.Persistence;

/// <summary>Đọc/ghi CSV tối giản: escape dấu phẩy, ngoặc kép, xuống dòng.</summary>
public static class CsvFormat
{
    public static string Join(IEnumerable<string> cells)
        => string.Join(',', cells.Select(Escape));

    public static string Escape(string? value)
    {
        var safe = value ?? string.Empty;

        return safe.Contains(',') || safe.Contains('"') || safe.Contains('\n') || safe.Contains('\r')
            ? '"' + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : safe;
    }

    public static string[] Split(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (insideQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    insideQuotes = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                insideQuotes = true;
            }
            else if (ch == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        cells.Add(current.ToString());
        return cells.ToArray();
    }
}
