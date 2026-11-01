using SIMS.Domain.Clubs;
using SIMS.Domain.Security;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvClubRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-clubs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Club_round_trip_and_exists_by_code()
    {
        var repository = new CsvClubRepository(Path.Combine(_directory, "clubs.csv"));
        var club = Club.Create("CLB01", "Câu lạc bộ Công nghệ", Now);

        await repository.AddAsync(club);

        var items = await repository.ListAsync();
        Assert.Single(items);
        Assert.Equal(club.Id, items[0].Id);
        Assert.Equal("CLB01", items[0].Code);
        Assert.True(await repository.ExistsByCodeAsync("clb01"));
        Assert.False(await repository.ExistsByCodeAsync("clb01", club.Id));
        Assert.NotNull(await repository.FindByCodeAsync("CLB01"));
    }

    [Fact]
    public async Task Membership_round_trip_and_filters()
    {
        var repository = new CsvClubMembershipRepository(Path.Combine(_directory, "club_memberships.csv"));
        var userId = Guid.NewGuid();
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();

        await repository.AddAsync(ClubMembership.Create(userId, clubA, Role.Faculty, Now));
        await repository.AddAsync(ClubMembership.Create(userId, clubB, Role.Student, Now));

        var byUser = await repository.ListByUserAsync(userId);
        var byClub = await repository.ListByClubAsync(clubA);
        var pair = await repository.FindByPairAsync(userId, clubA);

        Assert.Equal(2, byUser.Count);
        Assert.Single(byClub);
        Assert.Equal(Role.Faculty, pair!.Role);
    }
}

public sealed class CsvPermissionRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-perms-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Add_range_then_list_round_trips_permissions()
    {
        var repository = new CsvPermissionRepository(Path.Combine(_directory, "permissions.csv"));

        await repository.AddRangeAsync(new[]
        {
            RolePermission.Create(Role.Admin, "students", "view"),
            RolePermission.Create(Role.Student, "learning", "view_own"),
        });

        var items = await repository.ListAsync();

        Assert.Equal(2, items.Count);
        Assert.Contains(items, p => p.Role == Role.Admin && p.Module == "students" && p.Action == "view");
        Assert.Contains(items, p => p.Role == Role.Student && p.Module == "learning" && p.Action == "view_own");
    }
}
