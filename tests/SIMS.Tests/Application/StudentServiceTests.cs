using SIMS.Application.Abstractions;
using SIMS.Application.Students;
using SIMS.Domain.Students;

namespace SIMS.Tests.Application;

public sealed class StudentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Dob = new(2004, 1, 1);
    private static readonly StudentInput ValidInput = new("sv001", "Nguyễn Văn A", "a@example.com", Dob, "CNTT");

    private static StudentService CreateService(out InMemoryStudentRepository repository, out InMemoryAuditLog audit)
    {
        var clock = new FixedClock(Now);
        repository = new InMemoryStudentRepository();
        audit = new InMemoryAuditLog();

        return new StudentService(repository, new StudentValidator(clock), audit, clock);
    }

    [Fact]
    public async Task Create_persists_student_and_writes_audit()
    {
        var service = CreateService(out var repository, out var audit);

        var result = await service.CreateAsync(ValidInput, "admin");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("SV001", result.Value!.StudentCode);
        Assert.Single(repository.Items);
        Assert.Single(audit.Entries);
        Assert.Equal("student.create", audit.Entries[0].Action);
        Assert.Equal("admin", audit.Entries[0].Actor);
    }

    [Fact]
    public async Task Create_rejects_duplicate_code_case_insensitive()
    {
        var service = CreateService(out _, out _);
        await service.CreateAsync(ValidInput, "admin");

        var second = await service.CreateAsync(
            new StudentInput("SV001", "Trần Thị B", "b@example.com", Dob, "Kế toán"),
            "admin");

        Assert.False(second.IsSuccess);
        Assert.Contains(second.Errors, e => e.Field == nameof(StudentInput.StudentCode));
    }

    [Fact]
    public async Task Create_returns_field_errors_when_validation_fails()
    {
        var service = CreateService(out var repository, out var audit);

        var result = await service.CreateAsync(
            new StudentInput("", "", "sai-email", default, ""),
            "admin");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Empty(repository.Items);
        Assert.Empty(audit.Entries);
        Assert.Contains(result.Errors, e => e.Field == nameof(StudentInput.StudentCode));
        Assert.Contains(result.Errors, e => e.Field == nameof(StudentInput.Email));
        Assert.Contains(result.Errors, e => e.Field == nameof(StudentInput.DateOfBirth));
        Assert.Contains(result.Errors, e => e.Field == nameof(StudentInput.Program));
    }

    [Fact]
    public async Task Update_changes_profile_but_keeps_student_code()
    {
        var service = CreateService(out _, out var audit);
        var created = await service.CreateAsync(ValidInput, "admin");

        var updated = await service.UpdateAsync(
            created.Value!.Id,
            new StudentInput("SV001", "Nguyễn Văn B", "b@example.com", Dob, "Kế toán"),
            "faculty");

        Assert.True(updated.IsSuccess);
        Assert.Equal("Nguyễn Văn B", updated.Value!.FullName);
        Assert.Equal("Kế toán", updated.Value.Program);
        Assert.Contains(audit.Entries, e => e.Action == "student.update" && e.Actor == "faculty");
    }

    [Fact]
    public async Task Update_rejects_student_code_change()
    {
        var service = CreateService(out _, out _);
        var created = await service.CreateAsync(ValidInput, "admin");

        var updated = await service.UpdateAsync(
            created.Value!.Id,
            new StudentInput("SV999", "Nguyễn Văn B", "b@example.com", Dob, "Kế toán"),
            "admin");

        Assert.False(updated.IsSuccess);
        Assert.Contains(updated.Errors, e => e.Field == nameof(StudentInput.StudentCode));
    }

    [Fact]
    public async Task Delete_removes_student_and_audits()
    {
        var service = CreateService(out var repository, out var audit);
        var created = await service.CreateAsync(ValidInput, "admin");

        var result = await service.DeleteAsync(created.Value!.Id, "admin");

        Assert.True(result.IsSuccess);
        Assert.Empty(repository.Items);
        Assert.Contains(audit.Entries, e => e.Action == "student.delete");
    }

    [Fact]
    public async Task Delete_missing_student_returns_error()
    {
        var service = CreateService(out _, out _);

        var result = await service.DeleteAsync(Guid.NewGuid(), "admin");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryAuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryStudentRepository : IStudentRepository
    {
        public List<Student> Items { get; } = new();

        public Task<IReadOnlyList<Student>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Student>>(Items.ToArray());

        public Task<Student?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(s => s.Id == id));

        public Task<Student?> FindByCodeAsync(string studentCode, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(
                s => string.Equals(s.StudentCode, studentCode, StringComparison.OrdinalIgnoreCase)));

        public Task<Student?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(s => s.UserId == userId));

        public Task<bool> ExistsByCodeAsync(
            string studentCode,
            Guid? excludeId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Any(s =>
                string.Equals(s.StudentCode, studentCode, StringComparison.OrdinalIgnoreCase) &&
                (excludeId is null || s.Id != excludeId)));

        public Task AddAsync(Student student, CancellationToken cancellationToken = default)
        {
            Items.Add(student);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Student student, CancellationToken cancellationToken = default)
        {
            var index = Items.FindIndex(s => s.Id == student.Id);
            if (index >= 0)
            {
                Items[index] = student;
            }

            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var removed = Items.RemoveAll(s => s.Id == id);
            return Task.FromResult(removed > 0);
        }
    }
}
