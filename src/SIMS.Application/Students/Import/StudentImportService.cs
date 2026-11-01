using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Students;

namespace SIMS.Application.Students.Import;

/// <summary>Một dòng CSV đã tách cột nhưng chưa validate (giữ số dòng gốc để báo lỗi theo Row N).</summary>
public sealed record StudentImportRow(
    int RowNumber,
    string StudentCode,
    string FullName,
    string Email,
    string DateOfBirth,
    string Program);

public sealed record StudentImportError(int RowNumber, string Message);

public sealed record StudentImportResult(int TotalRows, int Imported, IReadOnlyList<StudentImportError> Errors);

/// <summary>
/// OCP: tầng Application chỉ phụ thuộc interface; CSV là một cài đặt,
/// fake/in-memory là cài đặt khác cho test — không sửa service khi thêm định dạng mới.
/// </summary>
public interface IStudentImportParser
{
    IAsyncEnumerable<StudentImportRow> ParseAsync(Stream stream, CancellationToken cancellationToken = default);
}

public interface IStudentImportService
{
    Task<Result<StudentImportResult>> ImportAsync(
        Stream stream,
        string actor,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Import streaming: dòng lỗi được ghi nhận theo số dòng và <b>bỏ qua</b>,
/// các dòng hợp lệ vẫn được ghi; không đọc toàn bộ file vào bộ nhớ.
/// </summary>
public sealed class StudentImportService : IStudentImportService
{
    private readonly IStudentImportParser _parser;
    private readonly IStudentReader _reader;
    private readonly IStudentWriter _writer;
    private readonly IValidator<StudentInput> _validator;
    private readonly IClock _clock;

    public StudentImportService(
        IStudentImportParser parser,
        IStudentReader reader,
        IStudentWriter writer,
        IValidator<StudentInput> validator,
        IClock clock)
    {
        _parser = parser;
        _reader = reader;
        _writer = writer;
        _validator = validator;
        _clock = clock;
    }

    public async Task<Result<StudentImportResult>> ImportAsync(
        Stream stream,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (stream is null || !stream.CanRead)
        {
            return Result<StudentImportResult>.Fail(
                new ValidationError(string.Empty, "File CSV không hợp lệ."));
        }

        var errors = new List<StudentImportError>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var imported = 0;

        await foreach (var row in _parser.ParseAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            total++;

            var studentCode = row.StudentCode ?? string.Empty;
            var fullName = row.FullName ?? string.Empty;
            var email = row.Email ?? string.Empty;
            var program = row.Program ?? string.Empty;

            if (!DateOnly.TryParseExact(
                    (row.DateOfBirth ?? string.Empty).Trim(),
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var dateOfBirth))
            {
                errors.Add(new StudentImportError(row.RowNumber, "Ngày sinh không hợp lệ (định dạng yyyy-MM-dd)."));
                continue;
            }

            var input = new StudentInput(studentCode, fullName, email, dateOfBirth, program);
            var validation = _validator.Validate(input);
            if (!validation.IsValid)
            {
                errors.Add(new StudentImportError(
                    row.RowNumber,
                    string.Join(" ", validation.Errors.Select(e => e.Message))));
                continue;
            }

            var code = studentCode.Trim().ToUpperInvariant();

            if (!seenCodes.Add(code))
            {
                errors.Add(new StudentImportError(row.RowNumber, $"MSSV '{code}' bị trùng trong file."));
                continue;
            }

            if (await _reader.ExistsByCodeAsync(code, null, cancellationToken).ConfigureAwait(false))
            {
                errors.Add(new StudentImportError(row.RowNumber, $"MSSV '{code}' đã tồn tại trong hệ thống."));
                continue;
            }

            try
            {
                var student = Student.Create(
                    studentCode,
                    fullName,
                    email,
                    dateOfBirth,
                    program,
                    _clock.UtcNow);

                await _writer.AddAsync(student, cancellationToken).ConfigureAwait(false);
                imported++;
            }
            catch (DomainException ex)
            {
                errors.Add(new StudentImportError(row.RowNumber, ex.Message));
            }
        }

        return Result<StudentImportResult>.Ok(new StudentImportResult(total, imported, errors));
    }
}

/// <summary>Decorator: ghi audit tổng kết mỗi lần import (thành công hay có dòng lỗi).</summary>
public sealed class AuditedStudentImportService : IStudentImportService
{
    private readonly IStudentImportService _inner;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AuditedStudentImportService(IStudentImportService inner, IAuditLog audit, IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<StudentImportResult>> ImportAsync(
        Stream stream,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.ImportAsync(stream, actor, cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            var summary = result.Value;
            await _audit.WriteAsync(
                new AuditEntry(
                    _clock.UtcNow,
                    actor,
                    "student.import",
                    "student",
                    "batch",
                    $"total={summary.TotalRows} imported={summary.Imported} errors={summary.Errors.Count}"),
                cancellationToken);
        }

        return result;
    }
}
