using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace SIMS.Application.Common;

/// <summary>
/// Đọc/ghi XLSX tối giản bằng BCL (System.IO.Compression + XML), không cần package ngoài.
/// Chỉ hỗ trợ một sheet, ô kiểu text/số/boolean — đủ cho import/export danh sách.
/// </summary>
public static class Xlsx
{
    public const int MaxRows = 50_000;
    public const int MaxColumns = 200;

    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Ghi một sheet; mọi giá trị được lưu dưới dạng text (inline string).</summary>
    public static byte[] Write(string sheetName, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var safeName = NormalizeSheetName(sheetName);
        var sheetData = new XElement(MainNs + "sheetData");
        var rowIndex = 0;

        foreach (var row in rows)
        {
            rowIndex++;

            if (rowIndex > MaxRows)
            {
                throw new InvalidOperationException($"XLSX tối đa {MaxRows} dòng.");
            }

            if (row.Count > MaxColumns)
            {
                throw new InvalidOperationException($"XLSX tối đa {MaxColumns} cột.");
            }

            var rowElement = new XElement(MainNs + "row", new XAttribute("r", rowIndex));

            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                var value = row[columnIndex];
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                rowElement.Add(new XElement(
                    MainNs + "c",
                    new XAttribute("r", CellReference(rowIndex, columnIndex + 1)),
                    new XAttribute("t", "inlineStr"),
                    new XElement(
                        MainNs + "is",
                        new XElement(
                            MainNs + "t",
                            new XAttribute(XNamespace.Xml + "space", "preserve"),
                            value))));
            }

            sheetData.Add(rowElement);
        }

        var worksheet = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(MainNs + "worksheet", sheetData));

        var workbook = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                MainNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", RelNs),
                new XElement(
                    MainNs + "sheets",
                    new XElement(
                        MainNs + "sheet",
                        new XAttribute("name", safeName),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(RelNs + "id", "rId1")))));

        var workbookRels = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships") + "Relationships",
                Relationship(
                    "rId1",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                    "worksheets/sheet1.xml")));

        var rootRels = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships") + "Relationships",
                Relationship(
                    "rId1",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
                    "xl/workbook.xml")));

        var contentTypes = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Types",
                new XElement(
                    XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute(
                        "ContentType",
                        "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(
                    XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                Override(
                    "/xl/workbook.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                Override(
                    "/xl/worksheets/sheet1.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", contentTypes);
            AddEntry(archive, "_rels/.rels", rootRels);
            AddEntry(archive, "xl/workbook.xml", workbook);
            AddEntry(archive, "xl/_rels/workbook.xml.rels", workbookRels);
            AddEntry(archive, "xl/worksheets/sheet1.xml", worksheet);
        }

        return buffer.ToArray();
    }

    /// <summary>Đọc sheet đầu tiên; trả về các dòng, ô trống = chuỗi rỗng.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var shared = ReadSharedStrings(archive);
        var sheet = ReadSheet(archive, shared);

        return sheet;
    }

    /// <summary>Đọc XLSX rồi chuyển thành CSV (dùng lại bộ parse CSV hiện có).</summary>
    public static string ToCsv(Stream stream, char delimiter = ',')
    {
        var rows = Read(stream);
        var builder = new StringBuilder();

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(delimiter, row.Select(value => Quote(value, delimiter))));
        }

        return builder.ToString();
    }

    public static bool LooksLikeXlsx(ReadOnlySpan<byte> header)
        => header.Length >= 2 && header[0] == 0x50 && header[1] == 0x4B; // "PK"

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return Array.Empty<string>();
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var values = new List<string>();

        foreach (var si in document.Descendants().Where(e => e.Name.LocalName == "si"))
        {
            values.Add(string.Concat(
                si.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value)));
        }

        return values;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadSheet(ZipArchive archive, IReadOnlyList<string> shared)
    {
        var entry = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new InvalidDataException("Tệp XLSX không có worksheet.");

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var rows = new List<IReadOnlyList<string>>();

        foreach (var rowElement in document.Descendants().Where(e => e.Name.LocalName == "row"))
        {
            if (rows.Count >= MaxRows)
            {
                throw new InvalidDataException($"Tệp XLSX vượt quá {MaxRows} dòng.");
            }

            var cells = new SortedDictionary<int, string>();
            var fallbackIndex = 0;

            foreach (var cell in rowElement.Elements().Where(e => e.Name.LocalName == "c"))
            {
                var reference = cell.Attribute("r")?.Value;
                var columnIndex = TryParseColumn(reference, out var parsed) ? parsed : fallbackIndex;
                fallbackIndex = columnIndex + 1;

                if (columnIndex >= MaxColumns)
                {
                    throw new InvalidDataException($"Tệp XLSX vượt quá {MaxColumns} cột.");
                }

                cells[columnIndex] = ReadCell(cell, shared);
            }

            if (cells.Count == 0)
            {
                rows.Add(Array.Empty<string>());
                continue;
            }

            var width = cells.Keys.Max() + 1;
            var values = new string[width];

            for (var i = 0; i < width; i++)
            {
                values[i] = cells.TryGetValue(i, out var value) ? value : string.Empty;
            }

            rows.Add(values);
        }

        return rows;
    }

    private static string ReadCell(XElement cell, IReadOnlyList<string> shared)
    {
        var type = cell.Attribute("t")?.Value;

        if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(
                cell.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
        }

        var raw = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? string.Empty;

        if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var sharedIndex) &&
            sharedIndex >= 0 && sharedIndex < shared.Count)
        {
            return shared[sharedIndex];
        }

        if (string.Equals(type, "b", StringComparison.OrdinalIgnoreCase))
        {
            return raw == "1" ? "TRUE" : "FALSE";
        }

        return raw;
    }

    private static bool TryParseColumn(string? reference, out int index)
    {
        index = 0;

        if (string.IsNullOrEmpty(reference))
        {
            return false;
        }

        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            index = (index * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        index -= 1;

        return index >= 0;
    }

    private static string CellReference(int row, int column)
    {
        var letters = new StringBuilder();
        var value = column;

        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            letters.Insert(0, (char)('A' + remainder));
            value = (value - 1) / 26;
        }

        return letters.ToString() + row.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string NormalizeSheetName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "Sheet1" : name.Trim();

        foreach (var invalid in new[] { '\\', '/', '*', '?', ':', '[', ']' })
        {
            value = value.Replace(invalid, ' ');
        }

        value = value.Trim('\'').Trim();

        if (value.Length == 0)
        {
            value = "Sheet1";
        }

        return value.Length > 31 ? value[..31] : value;
    }

    private static XElement Relationship(string id, string type, string target)
        => new(
            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships") + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target));

    private static XElement Override(string partName, string contentType)
        => new(
            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType));

    private static void AddEntry(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static string Quote(string value, char delimiter)
    {
        if (value.IndexOf(delimiter) < 0 && value.IndexOf('"') < 0 &&
            value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}
