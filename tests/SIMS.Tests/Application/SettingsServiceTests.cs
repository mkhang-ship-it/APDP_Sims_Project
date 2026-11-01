using SIMS.Application.Settings;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class SettingsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SettingsService Service,
        InMemorySettingRepository Repository,
        RecordingAuditLog Audit);

    private static Harness Create()
    {
        var repository = new InMemorySettingRepository();
        var audit = new RecordingAuditLog();
        var service = new SettingsService(repository, audit, new FakeClock(Now));

        return new Harness(service, repository, audit);
    }

    [Fact]
    public async Task Set_creates_then_updates_same_row()
    {
        var harness = Create();

        await harness.Service.SetAsync(null, SettingKeys.SiteTitle, "SIMS", "admin");
        await harness.Service.SetAsync(null, SettingKeys.SiteTitle, "SIMS mới", "admin");

        Assert.Single(harness.Repository.All);
        Assert.Equal("SIMS mới", await harness.Service.GetAsync(null, SettingKeys.SiteTitle));
        Assert.Equal(2, harness.Audit.Entries.Count);
    }

    [Fact]
    public async Task Global_and_club_scopes_are_isolated()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();

        await harness.Service.SetAsync(null, SettingKeys.SiteTitle, "Toàn hệ thống", "admin");
        await harness.Service.SetAsync(clubId, SettingKeys.ClubContactEmail, "clb@example.com", "faculty1");

        var global = await harness.Service.GetManyAsync(
            null, new[] { SettingKeys.SiteTitle, SettingKeys.ClubContactEmail });
        var club = await harness.Service.GetManyAsync(
            clubId, new[] { SettingKeys.SiteTitle, SettingKeys.ClubContactEmail });

        Assert.Single(global);
        Assert.Equal("Toàn hệ thống", global[SettingKeys.SiteTitle]);
        Assert.Single(club);
        Assert.Equal("clb@example.com", club[SettingKeys.ClubContactEmail]);
        Assert.Equal(2, harness.Repository.All.Count);
    }

    [Fact]
    public async Task Invalid_key_is_rejected_without_writing()
    {
        var harness = Create();

        var result = await harness.Service.SetAsync(null, "Site Title", "x", "admin");

        Assert.False(result.IsSuccess);
        Assert.Empty(harness.Repository.All);
    }

    [Fact]
    public async Task List_returns_only_scope_ordered_by_key()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        await harness.Service.SetAsync(clubId, SettingKeys.ClubContactPhone, "0900", "faculty1");
        await harness.Service.SetAsync(clubId, SettingKeys.ClubAddress, "Đà Nẵng", "faculty1");

        var settings = await harness.Service.ListAsync(clubId);

        Assert.Equal(2, settings.Count);
        Assert.Equal(SettingKeys.ClubAddress, settings[0].Key);
        Assert.Equal(SettingKeys.ClubContactPhone, settings[1].Key);
    }
}
