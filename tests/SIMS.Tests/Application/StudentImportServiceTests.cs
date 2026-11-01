using System.Runtime.CompilerServices;
using SIMS.Application.Students;
using SIMS.Application.Students.Import;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class StudentImportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static StudentImportService Create(
        out InMemoryStudentRepository students,
        out RecordingAuditLog audit,
        params StudentImportRow[] rows)
    {
        var clock = new FakeClock(Now);
        students = new InMemoryStudentRepository();
        audit = new RecordingAuditLog();

        return new StudentImportService(new FakeParser(rows), students, students, new StudentValidator(clock), clock);
    }

    [Fact]
    public async Task Import_keeps_valid_rows_and_reports_errors_with_row_numbers()
    {
        var service = Create(
            out var students,
            out _,
            new StudentImportRow(2, "SV001", "Nguyễn Văn A", "a@example.com", "2004-01-15", "CNTT"),
            new StudentImportRow(3, "SV002", "Trần Thị B", "sai-email", "2004-02-20", "Kế toán"),
            new StudentImportRow(4, "SV003", "Lê Văn C", "c@example.com", "31/02/2004", "QTKD"));

        using var stream = new MemoryStream();
        var result = await service.ImportAsync(stream, "faculty1");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.TotalRows);
        Assert.Equal(1, result.Value.Imported);
        Assert.Equal(2, result.Value.Errors.Count);
        Assert.Equal(3, result.Value.Errors[0].RowNumber);
        Assert.Equal(4, result.Value.Errors[1].RowNumber);
        Assert.Single(students.All);
    }

    [Fact]
    public async Task Import_detects_duplicate_code_inside_file()
    {
        var service = Create(
            out var students,
            out _,
            new StudentImportRow(2, "SV001", "Nguyễn Văn A", "a@example.com", "2004-01-15", "CNTT"),
            new StudentImportRow(3, "sv001", "Trần Thị B", "b@example.com", "2004-02-20", "CNTT"));

        using var stream = new MemoryStream();
        var result = await service.ImportAsync(stream, "faculty1");

        Assert.Equal(1, result.Value!.Imported);
        Assert.Single(result.Value.Errors);
        Assert.Contains("trùng trong file", result.Value.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(students.All);
    }

    [Fact]
    public async Task Import_rejects_code_already_in_repository()
    {
        var service = Create(
            out var students,
            out _,
            new StudentImportRow(2, "SV001", "Nguyễn Văn A", "a@example.com", "2004-01-15", "CNTT"));

        await students.AddAsync(SIMS.Domain.Students.Student.Create(
            "SV001", "Đã có", "existing@example.com", new DateOnly(2004, 1, 1), "CNTT", Now));

        using var stream = new MemoryStream();
        var result = await service.ImportAsync(stream, "faculty1");

        Assert.Equal(0, result.Value!.Imported);
        Assert.Contains("đã tồn tại", result.Value.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_rejects_unreadable_stream()
    {
        var service = Create(out _, out _);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 }, writable: false);
        stream.Dispose();

        var result = await service.ImportAsync(stream, "faculty1");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Audited_decorator_writes_summary()
    {
        var inner = Create(
            out _,
            out _,
            new StudentImportRow(2, "SV001", "Nguyễn Văn A", "a@example.com", "2004-01-15", "CNTT"));

        var audit = new RecordingAuditLog();
        var decorated = new AuditedStudentImportService(inner, audit, new FakeClock(Now));

        using var stream = new MemoryStream();
        await decorated.ImportAsync(stream, "faculty1");

        Assert.Single(audit.Entries);
        Assert.Equal("student.import", audit.Entries[0].Action);
        Assert.Contains("imported=1", audit.Entries[0].Details, StringComparison.Ordinal);
    }

    private sealed class FakeParser : IStudentImportParser
    {
        private readonly StudentImportRow[] _rows;

        public FakeParser(StudentImportRow[] rows) => _rows = rows;

        public async IAsyncEnumerable<StudentImportRow> ParseAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var row in _rows)
            {
                await Task.Yield();
                yield return row;
            }
        }
    }
}
