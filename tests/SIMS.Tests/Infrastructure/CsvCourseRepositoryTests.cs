using SIMS.Domain.Courses;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvCourseRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-courses-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private CsvCourseRepository CreateRepository()
        => new(Path.Combine(_directory, "courses.csv"));

    [Fact]
    public async Task Add_then_list_round_trips_course_with_prerequisite()
    {
        var repository = CreateRepository();
        var course = Course.Create("CS102", "Cấu trúc dữ liệu", 20, "CS101", Now);

        await repository.AddAsync(course);
        var items = await repository.ListAsync();

        Assert.Single(items);
        Assert.Equal(course.Id, items[0].Id);
        Assert.Equal("CS102", items[0].Code);
        Assert.Equal(20, items[0].Capacity);
        Assert.Equal("CS101", items[0].PrerequisiteCode);
        Assert.Equal(CourseStatus.Active, items[0].Status);
    }

    [Fact]
    public async Task Find_and_exists_by_code_ignore_case()
    {
        var repository = CreateRepository();
        var course = Course.Create("CS101", "Nhập môn", 30, null, Now);
        await repository.AddAsync(course);

        Assert.NotNull(await repository.FindByCodeAsync("cs101"));
        Assert.True(await repository.ExistsByCodeAsync("CS101"));
        Assert.False(await repository.ExistsByCodeAsync("CS101", course.Id));
        Assert.False(await repository.ExistsByCodeAsync("CS999"));
    }

    [Fact]
    public async Task Update_and_delete_rewrite_file_atomically()
    {
        var repository = CreateRepository();
        var course = Course.Create("CS101", "Nhập môn", 30, null, Now);
        await repository.AddAsync(course);

        course.UpdateDetails("Nhập môn lập trình", 40, null);
        await repository.UpdateAsync(course);

        var updated = await repository.FindByIdAsync(course.Id);
        Assert.Equal("Nhập môn lập trình", updated!.Title);
        Assert.Equal(40, updated.Capacity);

        Assert.True(await repository.DeleteAsync(course.Id));
        Assert.Empty(await repository.ListAsync());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*"));
    }
}
