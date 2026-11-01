using SIMS.Domain.Enrolments;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvEnrolmentRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-enrolments-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private CsvEnrolmentRepository CreateRepository()
        => new(Path.Combine(_directory, "enrolments.csv"));

    [Fact]
    public async Task TryAdd_writes_when_capacity_available()
    {
        var repository = CreateRepository();
        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        var outcome = await repository.TryAddAsync(Enrolment.Create(studentId, courseId, Now), capacity: 2);

        Assert.True(outcome.Success);
        var items = await repository.ListAsync();
        Assert.Single(items);
        Assert.Equal(studentId, items[0].StudentId);
    }

    [Fact]
    public async Task TryAdd_rejects_duplicate_pair_without_writing()
    {
        var repository = CreateRepository();
        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        await repository.TryAddAsync(Enrolment.Create(studentId, courseId, Now), capacity: 10);

        var duplicate = await repository.TryAddAsync(Enrolment.Create(studentId, courseId, Now), capacity: 10);

        Assert.False(duplicate.Success);
        Assert.Contains("đã có đăng ký", duplicate.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await repository.ListAsync());
    }

    [Fact]
    public async Task TryAdd_rejects_when_course_is_full()
    {
        var repository = CreateRepository();
        var courseId = Guid.NewGuid();
        await repository.TryAddAsync(Enrolment.Create(Guid.NewGuid(), courseId, Now), capacity: 1);

        var overflow = await repository.TryAddAsync(Enrolment.Create(Guid.NewGuid(), courseId, Now), capacity: 1);

        Assert.False(overflow.Success);
        Assert.Contains("sức chứa", overflow.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await repository.ListAsync());
    }

    [Fact]
    public async Task List_by_student_and_course_filters_rows()
    {
        var repository = CreateRepository();
        var studentA = Guid.NewGuid();
        var studentB = Guid.NewGuid();
        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();

        await repository.TryAddAsync(Enrolment.Create(studentA, courseA, Now), 10);
        await repository.TryAddAsync(Enrolment.Create(studentA, courseB, Now), 10);
        await repository.TryAddAsync(Enrolment.Create(studentB, courseA, Now), 10);

        Assert.Equal(2, (await repository.ListByStudentAsync(studentA)).Count);
        Assert.Equal(2, (await repository.ListByCourseAsync(courseA)).Count);
        Assert.NotNull(await repository.FindByPairAsync(studentA, courseA));
    }

    [Fact]
    public async Task Update_persists_grade_and_letter()
    {
        var repository = CreateRepository();
        var enrolment = Enrolment.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        await repository.TryAddAsync(enrolment, 10);

        enrolment.AssignGrade(8.5m, "B");
        await repository.UpdateAsync(enrolment);

        var reloaded = await repository.FindByIdAsync(enrolment.Id);
        Assert.Equal(8.5m, reloaded!.Score);
        Assert.Equal("B", reloaded.GradeLetter);
        Assert.Equal(EnrolmentStatus.Completed, reloaded.Status);
    }

    [Fact]
    public async Task Delete_removes_only_target_row()
    {
        var repository = CreateRepository();
        var first = Enrolment.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        var second = Enrolment.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        await repository.TryAddAsync(first, 10);
        await repository.TryAddAsync(second, 10);

        Assert.True(await repository.DeleteAsync(first.Id));
        var items = await repository.ListAsync();
        Assert.Single(items);
        Assert.Equal(second.Id, items[0].Id);
    }
}
