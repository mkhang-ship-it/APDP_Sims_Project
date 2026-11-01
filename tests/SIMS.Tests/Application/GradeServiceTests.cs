using SIMS.Application.Grades;
using SIMS.Domain.Enrolments;
using SIMS.Domain.Students;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class GradeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(GradeService Service, Enrolment Enrolment, RecordingAuditLog Audit, InMemoryEnrolmentRepository Enrolments)> CreateAsync()
    {
        var clock = new FakeClock(Now);
        var students = new InMemoryStudentRepository();
        var enrolments = new InMemoryEnrolmentRepository();
        var audit = new RecordingAuditLog();

        var student = Student.Create("SV001", "Nguyễn Văn A", "a@example.com", new DateOnly(2004, 1, 1), "CNTT", Now);
        await students.AddAsync(student);

        var enrolment = Enrolment.Create(student.Id, Guid.NewGuid(), Now);
        await enrolments.AddAsync(enrolment);

        var service = new GradeService(enrolments, new TenPointGradePolicy(), audit, clock);
        return (service, enrolment, audit, enrolments);
    }

    [Theory]
    [InlineData(9.5, "A")]
    [InlineData(8.0, "B")]
    [InlineData(7.0, "C")]
    [InlineData(6.0, "D")]
    [InlineData(5.0, "E")]
    [InlineData(4.9, "F")]
    public async Task Assign_maps_score_to_letter(decimal score, string expectedLetter)
    {
        var (service, enrolment, _, _) = await CreateAsync();

        var result = await service.AssignAsync(enrolment.Id, score, "faculty1");

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedLetter, result.Value!.GradeLetter);
        Assert.Equal(score, result.Value.Score);
    }

    [Fact]
    public async Task Assign_writes_audit()
    {
        var (service, enrolment, audit, _) = await CreateAsync();

        await service.AssignAsync(enrolment.Id, 8m, "faculty1");

        Assert.Single(audit.Entries);
        Assert.Equal("grade.assign", audit.Entries[0].Action);
    }

    [Fact]
    public async Task Assign_rejects_out_of_range_score()
    {
        var (service, enrolment, _, _) = await CreateAsync();

        var result = await service.AssignAsync(enrolment.Id, 11m, "faculty1");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Assign_rejects_dropped_enrolment_and_missing_id()
    {
        var (service, enrolment, _, _) = await CreateAsync();
        enrolment.Drop();

        var dropped = await service.AssignAsync(enrolment.Id, 7m, "faculty1");
        var missing = await service.AssignAsync(Guid.NewGuid(), 7m, "faculty1");

        Assert.False(dropped.IsSuccess);
        Assert.False(missing.IsSuccess);
    }
}
