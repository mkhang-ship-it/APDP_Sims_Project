using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Courses;
using SIMS.Domain.Enrolments;
using SIMS.Domain.Students;

namespace SIMS.Tests.Support;

/// <summary>In-memory repository (LSP: thay thế CsvRepository trong test mà không đổi hành vi gọi).</summary>
public class InMemoryRepository<T> : IRepository<T> where T : Entity
{
    protected readonly List<T> Items = new();

    public IReadOnlyList<T> All => Items;

    public virtual Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<T>>(Items.ToArray());

    public virtual Task<T?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public virtual Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public virtual Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        var index = Items.FindIndex(item => item.Id == entity.Id);
        if (index >= 0)
        {
            Items[index] = entity;
        }

        return Task.CompletedTask;
    }

    public virtual Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.RemoveAll(item => item.Id == id) > 0);
}

public sealed class InMemoryStudentRepository : InMemoryRepository<Student>, IStudentRepository
{
    public Task<Student?> FindByCodeAsync(string studentCode, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(
            s => string.Equals(s.StudentCode, studentCode, StringComparison.OrdinalIgnoreCase)));

    public Task<Student?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(s => s.UserId == userId));

    public Task<bool> ExistsByCodeAsync(
        string studentCode,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(s =>
            string.Equals(s.StudentCode, studentCode, StringComparison.OrdinalIgnoreCase) &&
            (excludeId is null || s.Id != excludeId)));
}

public sealed class InMemoryCourseRepository : InMemoryRepository<Course>, ICourseRepository
{
    public Task<Course?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(
            c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsByCodeAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(c =>
            string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase) &&
            (excludeId is null || c.Id != excludeId)));
}

/// <summary>Mô phỏng đúng hợp đồng atomic của CsvEnrolmentRepository (kiểm tra trùng + sức chứa).</summary>
public sealed class InMemoryEnrolmentRepository : InMemoryRepository<Enrolment>, IEnrolmentRepository
{
    public Task<IReadOnlyList<Enrolment>> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Enrolment>>(Items.Where(e => e.StudentId == studentId).ToArray());

    public Task<IReadOnlyList<Enrolment>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Enrolment>>(Items.Where(e => e.CourseId == courseId).ToArray());

    public Task<Enrolment?> FindByPairAsync(Guid studentId, Guid courseId, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(e => e.StudentId == studentId && e.CourseId == courseId));

    public Task<TryEnrolResult> TryAddAsync(
        Enrolment enrolment,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        if (Items.Any(e => e.StudentId == enrolment.StudentId && e.CourseId == enrolment.CourseId))
        {
            return Task.FromResult(TryEnrolResult.Fail("Sinh viên đã có đăng ký cho học phần này (kể cả đã huỷ)."));
        }

        if (Items.Count(e => e.CourseId == enrolment.CourseId && e.IsActive) >= capacity)
        {
            return Task.FromResult(TryEnrolResult.Fail($"Học phần đã đủ sức chứa ({capacity})."));
        }

        Items.Add(enrolment);
        return Task.FromResult(TryEnrolResult.Ok());
    }
}

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; }
}

public sealed class RecordingAuditLog : IAuditLog
{
    public List<AuditEntry> Entries { get; } = new();

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
