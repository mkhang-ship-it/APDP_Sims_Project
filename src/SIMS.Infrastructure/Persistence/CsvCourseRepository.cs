using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Courses;

namespace SIMS.Infrastructure.Persistence;

/// <summary>courses.csv — học phần + sức chứa + tiên quyết.</summary>
public sealed class CsvCourseRepository : CsvRepository<Course>, ICourseRepository
{
    private const int ColumnCount = 7;

    public CsvCourseRepository(string filePath)
        : base(
            filePath,
            "id",
            "code",
            "title",
            "capacity",
            "prerequisite_code",
            "status",
            "created_at_utc")
    {
    }

    public CsvCourseRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<Course?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var target = (code ?? string.Empty).Trim();
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(c => string.Equals(c.Code, target, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ExistsByCodeAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var target = (code ?? string.Empty).Trim();
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Any(c =>
            string.Equals(c.Code, target, StringComparison.OrdinalIgnoreCase) &&
            (excludeId is null || c.Id != excludeId));
    }

    protected override string[] ToCells(Course course) =>
    [
        course.Id.ToString("D"),
        course.Code,
        course.Title,
        course.Capacity.ToString(CultureInfo.InvariantCulture),
        course.PrerequisiteCode ?? string.Empty,
        course.Status.ToString(),
        course.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Course? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !int.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var capacity) ||
            !Enum.TryParse<CourseStatus>(cells[5], ignoreCase: true, out var status) ||
            !DateTimeOffset.TryParse(cells[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return Course.Restore(id, cells[1], cells[2], capacity, cells[4], status, createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
