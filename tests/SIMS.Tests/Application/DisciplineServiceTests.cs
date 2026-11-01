using SIMS.Application.Disciplines;
using SIMS.Domain.Clubs;
using SIMS.Domain.Disciplines;
using SIMS.Domain.Security;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class DisciplineServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 1);

    private sealed record Harness(
        DisciplineService Service,
        InMemoryDisciplineRepository Disciplines,
        InMemoryDisciplineRecordRepository Records,
        InMemoryClubMembershipRepository Memberships,
        RecordingAuditLog Audit);

    private static Harness Create()
    {
        var disciplines = new InMemoryDisciplineRepository();
        var records = new InMemoryDisciplineRecordRepository();
        var memberships = new InMemoryClubMembershipRepository();
        var audit = new RecordingAuditLog();
        var service = new DisciplineService(
            disciplines, records, memberships,
            new SequenceReferenceCodeGenerator(), audit, new FakeClock(Now));

        return new Harness(service, disciplines, records, memberships, audit);
    }

    [Fact]
    public void School_points_follow_php_formula()
    {
        Assert.Equal(2.5m, DisciplineService.ComputeSchoolPoints(5m, 10m, false));
        Assert.Equal(10m, DisciplineService.ComputeSchoolPoints(10m, 10m, true));
        Assert.Equal(0m, DisciplineService.ComputeSchoolPoints(-5m, 10m, false));
        Assert.Equal(5m, DisciplineService.ComputeSchoolPoints(0m, 0m, true));
    }

    [Fact]
    public async Task Create_apply_and_revoke_records()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var created = await harness.Service.CreateDisciplineAsync(
            clubId, "Tham gia đầy đủ", 10m, "Mô tả", "faculty1");

        var applied = await harness.Service.ApplyRecordAsync(
            clubId, userId, created.Value!.Id, Today, "HK1", "Ghi chú", "faculty1");
        var revoke = await harness.Service.RevokeRecordAsync(applied.Value!.Id, "faculty1");
        var records = await harness.Service.ListRecordsAsync(clubId, userId, false);

        Assert.True(created.IsSuccess);
        Assert.True(applied.IsSuccess);
        Assert.True(revoke.IsSuccess);
        Assert.Single(records);
        Assert.False(records[0].Record.IsActive);
        Assert.Equal("discipline.record.revoke", harness.Audit.Entries[^1].Action);
    }

    [Fact]
    public async Task Points_scale_and_rank_members_with_leader_bonus()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var facultyId = Guid.NewGuid();
        await harness.Memberships.AddAsync(
            ClubMembership.Create(studentId, clubId, Role.Student, Now));
        await harness.Memberships.AddAsync(
            ClubMembership.Create(facultyId, clubId, Role.Faculty, Now));

        var merit = (await harness.Service.CreateDisciplineAsync(
            clubId, "Tham gia đầy đủ", 10m, null, "faculty1")).Value!;
        var penalty = (await harness.Service.CreateDisciplineAsync(
            clubId, "Vắng không phép", -5m, null, "faculty1")).Value!;

        await harness.Service.ApplyRecordAsync(clubId, studentId, merit.Id, Today, "HK1", null, "faculty1");
        await harness.Service.ApplyRecordAsync(clubId, studentId, penalty.Id, Today, "HK1", null, "faculty1");
        await harness.Service.ApplyRecordAsync(clubId, facultyId, merit.Id, Today, "HK1", null, "faculty1");

        var rows = await harness.Service.GetMemberPointsAsync(clubId);
        var student = rows.Single(row => row.UserId == studentId);
        var faculty = rows.Single(row => row.UserId == facultyId);
        var studentPoints = await harness.Service.GetUserPointsAsync(clubId, studentId);

        Assert.Equal(5m, student.ClubPoints);
        Assert.Equal(2.5m, student.SchoolPoints);
        Assert.False(student.IsLeader);
        Assert.Equal(10m, faculty.ClubPoints);
        Assert.Equal(10m, faculty.SchoolPoints);
        Assert.True(faculty.IsLeader);
        Assert.Equal(1, rows.ToList().FindIndex(row => row.UserId == facultyId) + 1);
        Assert.Equal(2, studentPoints!.Rank);
        Assert.Equal(2, studentPoints.Records.Count);
    }

    [Fact]
    public async Task Revoked_records_do_not_count_towards_points()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await harness.Memberships.AddAsync(ClubMembership.Create(userId, clubId, Role.Student, Now));
        var merit = (await harness.Service.CreateDisciplineAsync(
            clubId, "Đóng góp", 10m, null, "faculty1")).Value!;
        var applied = (await harness.Service.ApplyRecordAsync(
            clubId, userId, merit.Id, Today, "HK1", null, "faculty1")).Value!;

        await harness.Service.RevokeRecordAsync(applied.Id, "faculty1");
        var rows = await harness.Service.GetMemberPointsAsync(clubId);

        Assert.Equal(0m, rows.Single().ClubPoints);
        Assert.Equal(0m, rows.Single().SchoolPoints);
    }

    [Fact]
    public async Task Update_record_and_delete_discipline_rules()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var merit = (await harness.Service.CreateDisciplineAsync(
            clubId, "Đóng góp", 10m, null, "faculty1")).Value!;
        var penalty = (await harness.Service.CreateDisciplineAsync(
            clubId, "Vi phạm", -5m, null, "faculty1")).Value!;
        var applied = (await harness.Service.ApplyRecordAsync(
            clubId, userId, merit.Id, Today, "HK1", null, "faculty1")).Value!;

        var updated = await harness.Service.UpdateRecordAsync(
            applied.Id, penalty.Id, Today.AddDays(1), "HK2", "Đổi danh mục", "faculty1");
        var deleteUsed = await harness.Service.DeleteDisciplineAsync(penalty.Id, "faculty1");

        await harness.Service.RevokeRecordAsync(applied.Id, "faculty1");
        var updateRevoked = await harness.Service.UpdateRecordAsync(
            applied.Id, penalty.Id, Today, null, null, "faculty1");

        Assert.True(updated.IsSuccess);
        Assert.Equal(penalty.Id, updated.Value!.DisciplineId);
        Assert.False(deleteUsed.IsSuccess);
        Assert.False(updateRevoked.IsSuccess);
    }

    [Fact]
    public async Task Apply_rejects_discipline_from_another_club()
    {
        var harness = Create();
        var discipline = (await harness.Service.CreateDisciplineAsync(
            Guid.NewGuid(), "Đóng góp", 10m, null, "faculty1")).Value!;

        var result = await harness.Service.ApplyRecordAsync(
            Guid.NewGuid(), Guid.NewGuid(), discipline.Id, Today, null, null, "faculty1");

        Assert.False(result.IsSuccess);
        Assert.Empty(harness.Records.All);
    }
}
