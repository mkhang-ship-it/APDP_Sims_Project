using SIMS.Application.Clubs;
using SIMS.Domain.Security;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class ClubServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static ClubService Create(
        out InMemoryClubRepository clubs,
        out InMemoryClubMembershipRepository memberships,
        out InMemoryUserRepository users,
        out RecordingAuditLog audit)
    {
        clubs = new InMemoryClubRepository();
        memberships = new InMemoryClubMembershipRepository();
        users = new InMemoryUserRepository();
        audit = new RecordingAuditLog();

        return new ClubService(clubs, memberships, users, new ClubValidator(), audit, new FakeClock(Now));
    }

    [Fact]
    public async Task Create_persists_and_audits()
    {
        var service = Create(out var clubs, out _, out _, out var audit);

        var result = await service.CreateAsync(new ClubInput("clb01", "CLB Công nghệ"), "admin");

        Assert.True(result.IsSuccess);
        Assert.Equal("CLB01", result.Value!.Code);
        Assert.Single(clubs.All);
        Assert.Equal("club.create", audit.Entries[0].Action);
    }

    [Fact]
    public async Task Create_rejects_duplicate_code()
    {
        var service = Create(out _, out _, out _, out _);
        await service.CreateAsync(new ClubInput("CLB01", "CLB A"), "admin");

        var duplicate = await service.CreateAsync(new ClubInput("clb01", "CLB B"), "admin");

        Assert.False(duplicate.IsSuccess);
        Assert.Contains(duplicate.Errors, e => e.Field == nameof(ClubInput.Code));
    }

    [Fact]
    public async Task Assign_member_creates_membership_and_primary_club()
    {
        var service = Create(out _, out _, out var users, out var audit);
        var user = User.Create("sv1", "Sinh viên 1", Role.Student, new string('x', 24), Now);
        users.Seed(user);
        var club = await service.CreateAsync(new ClubInput("CLB01", "CLB A"), "admin");

        var assigned = await service.AssignMemberAsync(club.Value!.Id, user.Id, Role.Student, "admin");
        var memberships = await service.ListMembershipsAsync(user.Id);
        var primary = await service.GetPrimaryClubAsync(user.Id);

        Assert.True(assigned.IsSuccess);
        Assert.Single(memberships);
        Assert.Equal("CLB01", primary!.Code);
        Assert.Contains(audit.Entries, e => e.Action == "club.member.assign");
    }

    [Fact]
    public async Task Assign_member_rejects_duplicate_and_missing_user()
    {
        var service = Create(out _, out _, out var users, out _);
        var user = User.Create("sv1", "Sinh viên 1", Role.Student, new string('x', 24), Now);
        users.Seed(user);
        var club = await service.CreateAsync(new ClubInput("CLB01", "CLB A"), "admin");
        await service.AssignMemberAsync(club.Value!.Id, user.Id, Role.Student, "admin");

        var duplicate = await service.AssignMemberAsync(club.Value.Id, user.Id, Role.Student, "admin");
        var missingUser = await service.AssignMemberAsync(club.Value.Id, Guid.NewGuid(), Role.Student, "admin");

        Assert.False(duplicate.IsSuccess);
        Assert.False(missingUser.IsSuccess);
    }

    [Fact]
    public async Task Update_rejects_code_change_and_missing_club()
    {
        var service = Create(out _, out _, out _, out _);
        var created = await service.CreateAsync(new ClubInput("CLB01", "CLB A"), "admin");

        var changed = await service.UpdateAsync(created.Value!.Id, new ClubInput("CLB99", "CLB A"), "admin");
        var missing = await service.UpdateAsync(Guid.NewGuid(), new ClubInput("CLB01", "CLB A"), "admin");

        Assert.False(changed.IsSuccess);
        Assert.False(missing.IsSuccess);
    }
}
