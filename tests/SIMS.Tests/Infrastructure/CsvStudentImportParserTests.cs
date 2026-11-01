using System.Text;
using SIMS.Application.Students.Import;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvStudentImportParserTests
{
    private static Stream Utf8Stream(string content, bool withBom = false)
    {
        var body = Encoding.UTF8.GetBytes(content);
        if (!withBom)
        {
            return new MemoryStream(body);
        }

        var withPreamble = Encoding.UTF8.GetPreamble().Concat(body).ToArray();
        return new MemoryStream(withPreamble);
    }

    private static async Task<List<StudentImportRow>> ReadAllAsync(Stream stream)
    {
        var parser = new CsvStudentImportParser();
        var rows = new List<StudentImportRow>();

        await foreach (var row in parser.ParseAsync(stream))
        {
            rows.Add(row);
        }

        return rows;
    }

    [Fact]
    public async Task Parse_reads_rows_with_original_line_numbers()
    {
        using var stream = Utf8Stream(
            "student_code,full_name,email,date_of_birth,program\n" +
            "SV001,Nguyễn Văn A,a@example.com,2004-01-15,CNTT\n" +
            "SV002,Trần Thị B,b@example.com,2004-02-20,Kế toán\n");

        var rows = await ReadAllAsync(stream);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].RowNumber);
        Assert.Equal(3, rows[1].RowNumber);
        Assert.Equal("SV001", rows[0].StudentCode);
        Assert.Equal("Kế toán", rows[1].Program);
    }

    [Fact]
    public async Task Parse_handles_bom_and_quoted_values()
    {
        using var stream = Utf8Stream(
            "student_code,full_name,email,date_of_birth,program\n" +
            "SV003,\"Nguyễn Văn A, Jr\",c@example.com,2004-03-10,\"Công nghệ thông tin\"\n",
            withBom: true);

        var rows = await ReadAllAsync(stream);

        Assert.Single(rows);
        Assert.Equal("Nguyễn Văn A, Jr", rows[0].FullName);
        Assert.Equal("Công nghệ thông tin", rows[0].Program);
    }

    [Fact]
    public async Task Parse_skips_blank_lines_but_keeps_line_numbers()
    {
        using var stream = Utf8Stream(
            "student_code,full_name,email,date_of_birth,program\n" +
            "SV001,Nguyễn Văn A,a@example.com,2004-01-15,CNTT\n" +
            "\n" +
            "SV002,Trần Thị B,b@example.com,2004-02-20,CNTT\n");

        var rows = await ReadAllAsync(stream);

        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows[1].RowNumber);
    }

    [Fact]
    public async Task Parse_returns_empty_for_header_only_file()
    {
        using var stream = Utf8Stream("student_code,full_name,email,date_of_birth,program\n");

        var rows = await ReadAllAsync(stream);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Parse_returns_empty_for_empty_stream()
    {
        using var stream = Utf8Stream(string.Empty);

        var rows = await ReadAllAsync(stream);

        Assert.Empty(rows);
    }
}
