using SIMS.Application.Abstractions;
using SIMS.Domain.Courses;
using SIMS.Domain.Enrolments;
using SIMS.Domain.Students;

namespace SIMS.Application.Learning;

public sealed record MyLearningView(
    Student? Student,
    IReadOnlyList<Enrolment> Enrolments,
    IReadOnlyList<Course> Courses);

public interface IMyLearningService
{
    Task<MyLearningView> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Vai trò Student xem hồ sơ + đăng ký + điểm của chính mình (liên kết Student↔User).
/// Chỉ đọc — phụ thuộc reader/repository, không phụ thuộc service ghi.
/// </summary>
public sealed class MyLearningService : IMyLearningService
{
    private readonly IStudentReader _students;
    private readonly IEnrolmentRepository _enrolments;
    private readonly ICourseRepository _courses;

    public MyLearningService(
        IStudentReader students,
        IEnrolmentRepository enrolments,
        ICourseRepository courses)
    {
        _students = students;
        _enrolments = enrolments;
        _courses = courses;
    }

    public async Task<MyLearningView> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var student = await _students.FindByUserIdAsync(userId, cancellationToken);
        if (student is null)
        {
            return new MyLearningView(null, Array.Empty<Enrolment>(), Array.Empty<Course>());
        }

        var enrolments = await _enrolments.ListByStudentAsync(student.Id, cancellationToken);
        var courses = await _courses.ListAsync(cancellationToken);

        return new MyLearningView(student, enrolments, courses);
    }
}
