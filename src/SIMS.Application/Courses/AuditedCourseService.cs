using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Domain.Courses;

namespace SIMS.Application.Courses;

/// <summary>
/// Decorator: bọc ICourseService để ghi audit cho mọi thao tác ghi,
/// giữ CourseService thuần nghiệp vụ (SRP/OCP).
/// </summary>
public sealed class AuditedCourseService : ICourseService
{
    private readonly ICourseService _inner;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AuditedCourseService(ICourseService inner, IAuditLog audit, IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _clock = clock;
    }

    public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default)
        => _inner.ListAsync(cancellationToken);

    public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _inner.GetAsync(id, cancellationToken);

    public async Task<Result<Course>> CreateAsync(
        CourseInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.CreateAsync(input, actor, cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            await WriteAuditAsync("course.create", result.Value, actor, cancellationToken);
        }

        return result;
    }

    public async Task<Result<Course>> UpdateAsync(
        Guid id,
        CourseInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.UpdateAsync(id, input, actor, cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            await WriteAuditAsync("course.update", result.Value, actor, cancellationToken);
        }

        return result;
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var existing = await _inner.GetAsync(id, cancellationToken);
        var result = await _inner.DeleteAsync(id, actor, cancellationToken);

        if (result.IsSuccess)
        {
            await _audit.WriteAsync(
                new AuditEntry(
                    _clock.UtcNow,
                    actor,
                    "course.delete",
                    "course",
                    id.ToString("D"),
                    existing is null ? string.Empty : $"{existing.Code} · {existing.Title}"),
                cancellationToken);
        }

        return result;
    }

    private Task WriteAuditAsync(string action, Course course, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                action,
                "course",
                course.Id.ToString("D"),
                $"{course.Code} · {course.Title}"),
            cancellationToken);
}
