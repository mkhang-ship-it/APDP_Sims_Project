using SIMS.Domain.Courses;
using SIMS.Domain.Enrolments;
using SIMS.Domain.Students;

namespace SIMS.Web.Models;

public sealed record EnrolmentRow(Enrolment Enrolment, Student? Student, Course? Course);

public sealed class EnrolmentIndexViewModel
{
    public IReadOnlyList<EnrolmentRow> Rows { get; init; } = Array.Empty<EnrolmentRow>();

    public int ActiveCount => Rows.Count(r => r.Enrolment.IsActive);

    public int GradedCount => Rows.Count(r => r.Enrolment.HasGrade);
}

public sealed class EnrolFormModel
{
    public Guid StudentId { get; set; }

    public Guid CourseId { get; set; }

    public IReadOnlyList<Student> Students { get; init; } = Array.Empty<Student>();

    public IReadOnlyList<Course> Courses { get; init; } = Array.Empty<Course>();
}

public sealed class StudentImportViewModel
{
    public SIMS.Application.Students.Import.StudentImportResult? Result { get; init; }
}
