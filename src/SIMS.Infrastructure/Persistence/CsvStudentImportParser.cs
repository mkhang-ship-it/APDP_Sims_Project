using System.Runtime.CompilerServices;
using System.Text;
using SIMS.Application.Students.Import;

namespace SIMS.Infrastructure.Persistence;

/// <summary>
/// Parser CSV streaming cho import sinh viên:
/// đọc từng dòng bằng StreamReader (không ReadAllLines), hỗ trợ BOM, trả số dòng gốc.
/// </summary>
public sealed class CsvStudentImportParser : IStudentImportParser
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async IAsyncEnumerable<StudentImportRow> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(
            stream,
            Utf8NoBom,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);

        var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (headerLine is null)
        {
            yield break;
        }

        var header = CsvFormat.Split(headerLine)
            .Select(h => h.Trim().TrimStart('\uFEFF').ToLowerInvariant())
            .ToArray();

        var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            columnIndex[header[i]] = i;
        }

        var rowNumber = 1;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            rowNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = CsvFormat.Split(line);

            yield return new StudentImportRow(
                rowNumber,
                Cell(cells, columnIndex, "student_code"),
                Cell(cells, columnIndex, "full_name"),
                Cell(cells, columnIndex, "email"),
                Cell(cells, columnIndex, "date_of_birth"),
                Cell(cells, columnIndex, "program"));
        }
    }

    private static string Cell(string[] cells, Dictionary<string, int> index, string column)
        => index.TryGetValue(column, out var position) && position < cells.Length
            ? cells[position].Trim()
            : string.Empty;
}
