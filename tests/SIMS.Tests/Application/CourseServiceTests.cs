using SIMS.Application.Courses;
using SIMS.Domain.Courses;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class CourseServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static CourseService CreateService(out InMemoryCourseRepository repository)
    {
        var clock = new FakeClock(Now);
        repository = new InMemoryCourseRepository();
        return new CourseService(repository, new CourseValidator(), clock);
    }

    [Fact]
    public async Task Create_persists_course()
    {
        var service = CreateService(out var repository);

        var result = await service.CreateAsync(new CourseInput("cs101", "Nhập môn lập trình", 30, null), "admin");

        Assert.True(result.IsSuccess);
        Assert.Equal("CS101", result.Value!.Code);
        Assert.Single(repository.All);
    }

    [Fact]
    public async Task Create_rejects_duplicate_code()
    {
        var service = CreateService(out _);
        await service.CreateAsync(new CourseInput("CS101", "Nhập môn", 30, null), "admin");

        var duplicate = await service.CreateAsync(new CourseInput("cs101", "Khác", 10, null), "admin");

        Assert.False(duplicate.IsSuccess);
        Assert.Contains(duplicate.Errors, e => e.Field == nameof(CourseInput.Code));
    }

    [Fact]
    public async Task Create_rejects_missing_prerequisite()
    {
        var service = CreateService(out _);

        var result = await service.CreateAsync(new CourseInput("CS102", "Cấu trúc dữ liệu", 20, "CS999"), "admin");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == nameof(CourseInput.PrerequisiteCode));
    }

    [Fact]
    public async Task Create_accepts_existing_prerequisite()
    {
        var service = CreateService(out _);
        await service.CreateAsync(new CourseInput("CS101", "Nhập môn", 30, null), "admin");

        var result = await service.CreateAsync(new CourseInput("CS102", "Cấu trúc dữ liệu", 20, "CS101"), "admin");

        Assert.True(result.IsSuccess);
        Assert.Equal("CS101", result.Value!.PrerequisiteCode);
    }

    [Fact]
    public async Task Update_rejects_code_change_and_missing_course()
    {
        var service = CreateService(out _);
        var created = await service.CreateAsync(new CourseInput("CS101", "Nhập môn", 30, null), "admin");

        var codeChanged = await service.UpdateAsync(
            created.Value!.Id,
            new CourseInput("CS999", "Nhập môn", 30, null),
            "admin");

        var missing = await service.UpdateAsync(Guid.NewGuid(), new CourseInput("CS101", "Nhập môn", 30, null), "admin");

        Assert.False(codeChanged.IsSuccess);
        Assert.False(missing.IsSuccess);
    }

    [Fact]
    public async Task Delete_removes_course_and_reports_missing()
    {
        var service = CreateService(out var repository);
        var created = await service.CreateAsync(new CourseInput("CS101", "Nhập môn", 30, null), "admin");

        var deleted = await service.DeleteAsync(created.Value!.Id, "admin");
        var missing = await service.DeleteAsync(created.Value.Id, "admin");

        Assert.True(deleted.IsSuccess);
        Assert.Empty(repository.All);
        Assert.False(missing.IsSuccess);
    }

    [Fact]
    public async Task Audited_decorator_writes_audit_for_writes()
    {
        var inner = CreateService(out _);
        var audit = new RecordingAuditLog();
        var decorated = new AuditedCourseService(inner, audit, new FakeClock(Now));

        await decorated.CreateAsync(new CourseInput("CS101", "Nhập môn", 30, null), "admin");

        Assert.Single(audit.Entries);
        Assert.Equal("course.create", audit.Entries[0].Action);
        Assert.Equal("admin", audit.Entries[0].Actor);
    }
}
