using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Enrolments;

namespace SIMS.Infrastructure.Persistence;

/// <summary>enrolments.csv — đăng ký học phần, điểm và xếp loại.</summary>
public sealed class CsvEnrolmentRepository : CsvRepository<Enrolment>, IEnrolmentRepository
{
    private const int ColumnCount = 7;

    public CsvEnrolmentRepository(string filePath)
        : base(
            filePath,
            "id",
            "student_id",
            "course_id",
            "status",
            "enrolled_at_utc",
            "score",
            "grade_letter")
    {
    }

    public CsvEnrolmentRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<Enrolment>> ListByStudentAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.Where(e => e.StudentId == studentId).ToArray();
    }

    public async Task<IReadOnlyList<Enrolment>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.Where(e => e.CourseId == courseId).ToArray();
    }

    public async Task<Enrolment?> FindByPairAsync(
        Guid studentId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.FirstOrDefault(e => e.StudentId == studentId && e.CourseId == courseId);
    }

    /// <summary>
    /// Kiểm tra trùng + sức chứa và ghi trong CÙNG một khoá file (atomic, rollback tự nhiên):
    /// vi phạm thì không ghi gì.
    /// </summary>
    public async Task<TryEnrolResult> TryAddAsync(
        Enrolment enrolment,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enrolment);

        var outcome = TryEnrolResult.Fail("Không thể đăng ký học phần.");

        await Store.MutateAsync(rows =>
        {
            var existing = rows
                .Select(Map)
                .Where(e => e is not null)
                .Any(e => e!.StudentId == enrolment.StudentId && e.CourseId == enrolment.CourseId);

            if (existing)
            {
                outcome = TryEnrolResult.Fail("Sinh viên đã có đăng ký cho học phần này (kể cả đã huỷ).");
                return null;
            }

            var activeCount = rows
                .Select(Map)
                .Count(e => e is not null && e.CourseId == enrolment.CourseId && e.IsActive);

            if (activeCount >= capacity)
            {
                outcome = TryEnrolResult.Fail($"Học phần đã đủ sức chứa ({capacity}).");
                return null;
            }

            var updated = rows.ToList();
            updated.Add(ToCells(enrolment));
            outcome = TryEnrolResult.Ok();

            return updated;
        }, cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    protected override string[] ToCells(Enrolment enrolment) =>
    [
        enrolment.Id.ToString("D"),
        enrolment.StudentId.ToString("D"),
        enrolment.CourseId.ToString("D"),
        enrolment.Status.ToString(),
        enrolment.EnrolledAtUtc.ToString("O", CultureInfo.InvariantCulture),
        enrolment.Score?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
        enrolment.GradeLetter ?? string.Empty,
    ];

    protected override Enrolment? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var studentId) ||
            !Guid.TryParse(cells[2], out var courseId) ||
            !Enum.TryParse<EnrolmentStatus>(cells[3], ignoreCase: true, out var status) ||
            !DateTimeOffset.TryParse(cells[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var enrolledAt))
        {
            return null;
        }

        decimal? score = null;
        if (!string.IsNullOrWhiteSpace(cells[5]) &&
            decimal.TryParse(cells[5], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedScore))
        {
            score = parsedScore;
        }

        var letter = string.IsNullOrWhiteSpace(cells[6]) ? null : cells[6];

        try
        {
            return Enrolment.Restore(id, studentId, courseId, status, enrolledAt, score, letter);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
