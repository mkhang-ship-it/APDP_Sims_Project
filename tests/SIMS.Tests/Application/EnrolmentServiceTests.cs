using SIMS.Application.Enrolments;
using SIMS.Domain.Courses;
using SIMS.Domain.Enrolments;
using SIMS.Domain.Students;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class EnrolmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static (EnrolmentService Service, InMemoryEnrolmentRepository Enrolments, InMemoryStudentRepository Students, InMemoryCourseRepository Courses, RecordingAuditLog Audit) Create()
    {
        var clock = new FakeClock(Now);
        var students = new InMemoryStudentRepository();
        var courses = new InMemoryCourseRepository();
        var enrolments = new InMemoryEnrolmentRepository();
        var audit = new RecordingAuditLog();

        var service = new EnrolmentService(
            enrolments,
            students,
            courses,
            new CompletedCoursePrerequisitePolicy(),
            audit,
            clock);

        return (service, enrolments, students, courses, audit);
    }

    private static Student AddStudent(InMemoryStudentRepository students, string code)
    {
        var student = Student.Create(code, $"Sinh viên {code}", $"{code.ToLowerInvariant()}@example.com", new DateOnly(2004, 1, 1), "CNTT", Now);
        students.AddAsync(student).GetAwaiter().GetResult();
        return student;
    }

    private static Course AddCourse(InMemoryCourseRepository courses, string code, int capacity, string? prerequisite = null)
    {
        var course = Course.Create(code, $"Học phần {code}", capacity, prerequisite, Now);
        courses.AddAsync(course).GetAwaiter().GetResult();
        return course;
    }

    [Fact]
    public async Task Enrol_succeeds_and_writes_audit()
    {
        var (service, _, students, courses, audit) = Create();
        var student = AddStudent(students, "SV001");
        var course = AddCourse(courses, "CS101", 30);

        var result = await service.EnrolAsync(student.Id, course.Id, "faculty1");

        Assert.True(result.IsSuccess);
        Assert.Single(audit.Entries);
        Assert.Equal("enrolment.create", audit.Entries[0].Action);
    }

    [Fact]
    public async Task Enrol_rejects_duplicate_pair()
    {
        var (service, _, students, courses, _) = Create();
        var student = AddStudent(students, "SV001");
        var course = AddCourse(courses, "CS101", 30);
        await service.EnrolAsync(student.Id, course.Id, "faculty1");

        var second = await service.EnrolAsync(student.Id, course.Id, "faculty1");

        Assert.False(second.IsSuccess);
        Assert.Contains("đã có đăng ký", second.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enrol_rejects_when_course_is_full()
    {
        var (service, _, students, courses, _) = Create();
        var first = AddStudent(students, "SV001");
        var second = AddStudent(students, "SV002");
        var course = AddCourse(courses, "CAP1", 1);
        await service.EnrolAsync(first.Id, course.Id, "faculty1");

        var overflow = await service.EnrolAsync(second.Id, course.Id, "faculty1");

        Assert.False(overflow.IsSuccess);
        Assert.Contains("sức chứa", overflow.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enrol_rejects_when_prerequisite_not_met()
    {
        var (service, _, students, courses, _) = Create();
        var student = AddStudent(students, "SV001");
        AddCourse(courses, "CS101", 30);
        var advanced = AddCourse(courses, "CS102", 20, "CS101");

        var result = await service.EnrolAsync(student.Id, advanced.Id, "faculty1");

        Assert.False(result.IsSuccess);
        Assert.Contains("tiên quyết", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enrol_succeeds_when_prerequisite_passed()
    {
        var (service, enrolments, students, courses, _) = Create();
        var student = AddStudent(students, "SV001");
        var basic = AddCourse(courses, "CS101", 30);
        var advanced = AddCourse(courses, "CS102", 20, "CS101");

        var completed = Enrolment.Create(student.Id, basic.Id, Now.AddDays(-30));
        completed.AssignGrade(8.5m, "B");
        await enrolments.AddAsync(completed);

        var result = await service.EnrolAsync(student.Id, advanced.Id, "faculty1");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Drop_marks_enrolment_dropped_and_audits()
    {
        var (service, _, students, courses, audit) = Create();
        var student = AddStudent(students, "SV001");
        var course = AddCourse(courses, "CS101", 30);
        var enrolled = await service.EnrolAsync(student.Id, course.Id, "faculty1");

        var result = await service.DropAsync(enrolled.Value!.Id, "faculty1");
        var reloaded = await service.ListAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(EnrolmentStatus.Dropped, reloaded[0].Status);
        Assert.Contains(audit.Entries, e => e.Action == "enrolment.drop");
    }
}
