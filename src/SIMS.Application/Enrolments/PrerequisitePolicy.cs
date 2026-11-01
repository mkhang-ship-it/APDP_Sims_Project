using SIMS.Domain.Courses;
using SIMS.Domain.Enrolments;

namespace SIMS.Application.Enrolments;

/// <summary>Strategy: quy tắc kiểm tra học phần tiên quyết.</summary>
public interface IPrerequisitePolicy
{
    bool IsSatisfied(Course course, IReadOnlyList<Enrolment> studentEnrolments, IReadOnlyList<Course> allCourses);
}

/// <summary>Mặc định: phải đã hoàn thành học phần tiên quyết với điểm đạt (>= 5).</summary>
public sealed class CompletedCoursePrerequisitePolicy : IPrerequisitePolicy
{
    public const decimal PassThreshold = 5m;

    public bool IsSatisfied(Course course, IReadOnlyList<Enrolment> studentEnrolments, IReadOnlyList<Course> allCourses)
    {
        ArgumentNullException.ThrowIfNull(course);

        if (!course.HasPrerequisite)
        {
            return true;
        }

        var prerequisite = allCourses.FirstOrDefault(
            c => string.Equals(c.Code, course.PrerequisiteCode, StringComparison.OrdinalIgnoreCase));

        if (prerequisite is null)
        {
            return false;
        }

        return studentEnrolments.Any(e => e.CourseId == prerequisite.Id && e.IsPassed(PassThreshold));
    }
}
