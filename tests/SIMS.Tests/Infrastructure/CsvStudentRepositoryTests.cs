using SIMS.Domain.Students;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvStudentRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Dob = new(2004, 3, 15);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-students-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private CsvStudentRepository CreateRepository()
        => new(Path.Combine(_directory, "students.csv"));

    [Fact]
    public async Task Add_then_list_round_trips_all_fields()
    {
        var repository = CreateRepository();
        var student = Student.Create("SV001", "Nguyễn Văn A", "a@example.com", Dob, "Công nghệ thông tin", Now);

        await repository.AddAsync(student);
        var items = await repository.ListAsync();

        Assert.Single(items);
        var loaded = items[0];
        Assert.Equal(student.Id, loaded.Id);
        Assert.Equal("SV001", loaded.StudentCode);
        Assert.Equal("Nguyễn Văn A", loaded.FullName);
        Assert.Equal("a@example.com", loaded.Email);
        Assert.Equal(Dob, loaded.DateOfBirth);
        Assert.Equal("Công nghệ thông tin", loaded.Program);
        Assert.Equal(StudentStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task Add_escapes_commas_in_names()
    {
        var repository = CreateRepository();
        await repository.AddAsync(
            Student.Create("SV002", "Nguyễn Văn A, Jr", "b@example.com", Dob, "CNTT", Now));

        var items = await repository.ListAsync();

        Assert.Equal("Nguyễn Văn A, Jr", items[0].FullName);
    }

    [Fact]
    public async Task Find_and_exists_by_code_ignore_case_and_support_exclusion()
    {
        var repository = CreateRepository();
        var student = Student.Create("SV001", "Nguyễn Văn A", "a@example.com", Dob, "CNTT", Now);
        await repository.AddAsync(student);

        Assert.NotNull(await repository.FindByCodeAsync("sv001"));
        Assert.True(await repository.ExistsByCodeAsync("SV001"));
        Assert.False(await repository.ExistsByCodeAsync("SV001", student.Id));
        Assert.False(await repository.ExistsByCodeAsync("SV999"));
    }

    [Fact]
    public async Task Update_rewrites_existing_row()
    {
        var repository = CreateRepository();
        var student = Student.Create("SV001", "Nguyễn Văn A", "a@example.com", Dob, "CNTT", Now);
        await repository.AddAsync(student);

        student.UpdateProfile("Trần Thị B", "b@example.com", new DateOnly(2005, 1, 2), "Kế toán", Now);
        await repository.UpdateAsync(student);

        var items = await repository.ListAsync();
        Assert.Single(items);
        Assert.Equal("Trần Thị B", items[0].FullName);
        Assert.Equal("Kế toán", items[0].Program);
    }

    [Fact]
    public async Task Update_missing_student_throws_domain_exception()
    {
        var repository = CreateRepository();
        var student = Student.Create("SV001", "Nguyễn Văn A", "a@example.com", Dob, "CNTT", Now);

        await Assert.ThrowsAsync<SIMS.Domain.Common.DomainException>(() => repository.UpdateAsync(student));
    }

    [Fact]
    public async Task Delete_removes_only_target_row()
    {
        var repository = CreateRepository();
        var first = Student.Create("SV001", "Nguyễn Văn A", "a@example.com", Dob, "CNTT", Now);
        var second = Student.Create("SV002", "Trần Thị B", "b@example.com", Dob, "CNTT", Now);
        await repository.AddAsync(first);
        await repository.AddAsync(second);

        var deleted = await repository.DeleteAsync(first.Id);

        Assert.True(deleted);
        var items = await repository.ListAsync();
        Assert.Single(items);
        Assert.Equal("SV002", items[0].StudentCode);
    }

    [Fact]
    public async Task Delete_missing_returns_false()
    {
        var repository = CreateRepository();

        Assert.False(await repository.DeleteAsync(Guid.NewGuid()));
    }
}
