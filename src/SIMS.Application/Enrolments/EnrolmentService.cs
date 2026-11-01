using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Courses;
using SIMS.Domain.Enrolments;
using SIMS.Domain.Students;

namespace SIMS.Application.Enrolments;

public interface IEnrolmentService
{
    Task<IReadOnlyList<Enrolment>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Enrolment>> ListForStudentAsync(Guid studentId, CancellationToken cancellationToken = default);

    Task<Result<Enrolment>> EnrolAsync(
        Guid studentId,
        Guid courseId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DropAsync(Guid enrolmentId, string actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Use case Enrolments: chống trùng, sức chứa (atomic trong repository), trạng thái hoạt động,
/// điều kiện tiên quyết (Strategy), audit mọi thao tác ghi.
/// </summary>
public sealed class EnrolmentService : IEnrolmentService
{
    private readonly IEnrolmentRepository _enrolments;
    private readonly IStudentReader _students;
    private readonly ICourseRepository _courses;
    private readonly IPrerequisitePolicy _prerequisitePolicy;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public EnrolmentService(
        IEnrolmentRepository enrolments,
        IStudentReader students,
        ICourseRepository courses,
        IPrerequisitePolicy prerequisitePolicy,
        IAuditLog audit,
        IClock clock)
    {
        _enrolments = enrolments;
        _students = students;
        _courses = courses;
        _prerequisitePolicy = prerequisitePolicy;
        _audit = audit;
        _clock = clock;
    }

    public Task<IReadOnlyList<Enrolment>> ListAsync(CancellationToken cancellationToken = default)
        => _enrolments.ListAsync(cancellationToken);

    public Task<IReadOnlyList<Enrolment>> ListForStudentAsync(Guid studentId, CancellationToken cancellationToken = default)
        => _enrolments.ListByStudentAsync(studentId, cancellationToken);

    public async Task<Result<Enrolment>> EnrolAsync(
        Guid studentId,
        Guid courseId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var student = await _students.FindByIdAsync(studentId, cancellationToken);
        if (student is null)
        {
            return Result<Enrolment>.Fail(new ValidationError(string.Empty, "Không tìm thấy sinh viên."));
        }

        if (student.Status != StudentStatus.Active)
        {
            return Result<Enrolment>.Fail(new ValidationError(string.Empty, "Sinh viên đã ngừng học, không thể đăng ký."));
        }

        var course = await _courses.FindByIdAsync(courseId, cancellationToken);
        if (course is null)
        {
            return Result<Enrolment>.Fail(new ValidationError(string.Empty, "Không tìm thấy học phần."));
        }

        if (course.Status != CourseStatus.Active)
        {
            return Result<Enrolment>.Fail(new ValidationError(string.Empty, "Học phần đã đóng, không thể đăng ký."));
        }

        var studentEnrolments = await _enrolments.ListByStudentAsync(studentId, cancellationToken);
        var allCourses = await _courses.ListAsync(cancellationToken);

        if (!_prerequisitePolicy.IsSatisfied(course, studentEnrolments, allCourses))
        {
            return Result<Enrolment>.Fail(new ValidationError(
                string.Empty,
                $"Chưa đạt học phần tiên quyết '{course.PrerequisiteCode}' (cần điểm >= {CompletedCoursePrerequisitePolicy.PassThreshold})."));
        }

        var enrolment = Enrolment.Create(studentId, courseId, _clock.UtcNow);
        var attempt = await _enrolments.TryAddAsync(enrolment, course.Capacity, cancellationToken);

        if (!attempt.Success)
        {
            return Result<Enrolment>.Fail(new ValidationError(string.Empty, attempt.Error ?? "Không thể đăng ký học phần."));
        }

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "enrolment.create",
                "enrolment",
                enrolment.Id.ToString("D"),
                $"student={student.StudentCode} course={course.Code}"),
            cancellationToken);

        return Result<Enrolment>.Ok(enrolment);
    }

    public async Task<Result> DropAsync(Guid enrolmentId, string actor, CancellationToken cancellationToken = default)
    {
        var enrolment = await _enrolments.FindByIdAsync(enrolmentId, cancellationToken);
        if (enrolment is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy đăng ký."));
        }

        enrolment.Drop();
        await _enrolments.UpdateAsync(enrolment, cancellationToken);

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "enrolment.drop",
                "enrolment",
                enrolment.Id.ToString("D"),
                $"studentId={enrolment.StudentId} courseId={enrolment.CourseId}"),
            cancellationToken);

        return Result.Ok();
    }
}
