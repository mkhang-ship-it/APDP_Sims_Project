using SIMS.Application.Learning;
using SIMS.Domain.Courses;
using SIMS.Domain.Enrolments;
using SIMS.Domain.Students;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class MyLearningServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Linked_student_returns_profile_enrolments_and_courses()
    {
        var users = new InMemoryUserRepository();
        var students = new InMemoryStudentRepository();
        var courses = new InMemoryCourseRepository();
        var enrolments = new InMemoryEnrolmentRepository();

        var userId = Guid.NewGuid();
        var student = Student.Create(
            "SV001", "Nguyễn Văn A", "a@example.com", new DateOnly(2004, 1, 1), "CNTT", Now,
            userId: userId);
        await students.AddAsync(student);

        var course = Course.Create("CS101", "Nhập môn", 30, null, Now);
        await courses.AddAsync(course);

        var enrolment = Enrolment.Create(student.Id, course.Id, Now);
        enrolment.AssignGrade(8.5m, "B");
        await enrolments.AddAsync(enrolment);

        var service = new MyLearningService(students, enrolments, courses);

        var view = await service.GetAsync(userId);

        Assert.NotNull(view.Student);
        Assert.Equal("SV001", view.Student!.StudentCode);
        Assert.Single(view.Enrolments);
        Assert.Single(view.Courses);
        Assert.Equal(8.5m, view.Enrolments[0].Score);
        Assert.Equal("B", view.Enrolments[0].GradeLetter);
    }

    [Fact]
    public async Task Unlinked_user_returns_empty_view()
    {
        var service = new MyLearningService(
            new InMemoryStudentRepository(),
            new InMemoryEnrolmentRepository(),
            new InMemoryCourseRepository());

        var view = await service.GetAsync(Guid.NewGuid());

        Assert.Null(view.Student);
        Assert.Empty(view.Enrolments);
        Assert.Empty(view.Courses);
    }
}
