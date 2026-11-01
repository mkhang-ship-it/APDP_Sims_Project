using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Students;

namespace SIMS.Infrastructure.Persistence;

/// <summary>students.csv — dùng CsvRepository&lt;Student&gt; + truy vấn theo MSSV/User.</summary>
public sealed class CsvStudentRepository : CsvRepository<Student>, IStudentRepository
{
    private const int ColumnCount = 10;

    public CsvStudentRepository(string filePath)
        : base(
            filePath,
            "id",
            "student_code",
            "full_name",
            "email",
            "date_of_birth",
            "program",
            "status",
            "user_id",
            "club_id",
            "created_at_utc")
    {
    }

    public CsvStudentRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<Student?> FindByCodeAsync(string studentCode, CancellationToken cancellationToken = default)
    {
        var target = (studentCode ?? string.Empty).Trim();
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(
            s => string.Equals(s.StudentCode, target, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Student?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.FirstOrDefault(s => s.UserId == userId);
    }

    public async Task<bool> ExistsByCodeAsync(
        string studentCode,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var target = (studentCode ?? string.Empty).Trim();
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Any(s =>
            string.Equals(s.StudentCode, target, StringComparison.OrdinalIgnoreCase) &&
            (excludeId is null || s.Id != excludeId));
    }

    protected override string[] ToCells(Student student) =>
    [
        student.Id.ToString("D"),
        student.StudentCode,
        student.FullName,
        student.Email,
        student.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        student.Program,
        student.Status.ToString(),
        student.UserId?.ToString("D") ?? string.Empty,
        student.ClubId?.ToString("D") ?? string.Empty,
        student.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Student? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !DateOnly.TryParseExact(cells[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOfBirth) ||
            !Enum.TryParse<StudentStatus>(cells[6], ignoreCase: true, out var status) ||
            !DateTimeOffset.TryParse(cells[9], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        Guid? userId = Guid.TryParse(cells[7], out var parsedUserId) ? parsedUserId : null;
        Guid? clubId = Guid.TryParse(cells[8], out var parsedClubId) ? parsedClubId : null;

        try
        {
            return Student.Restore(id, cells[1], cells[2], cells[3], dateOfBirth, cells[5], status, createdAt, userId, clubId);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
