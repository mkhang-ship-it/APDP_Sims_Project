using SIMS.Domain.Notifications;
using SIMS.Domain.LuckyDraws;
using SIMS.Domain.Settings;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvEngagementRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-engage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task Lucky_draw_prize_and_winner_round_trip()
    {
        var draws = new CsvLuckyDrawRepository(PathFor("lucky_draws.csv"));
        var prizes = new CsvLuckyPrizeRepository(PathFor("lucky_prizes.csv"));
        var winners = new CsvLuckyWinnerRepository(PathFor("lucky_winners.csv"));
        var clubId = Guid.NewGuid();

        var draw = LuckyDraw.Create(
            clubId, "Bốc thăm", "boc-tham-abc12345", "123456", new[] { "An", "Bình" },
            Now.AddHours(-1), Now.AddHours(3), true, "faculty1", Now);
        await draws.AddAsync(draw);
        var prize = LuckyPrize.Create(draw.Id, "Giải nhất", 1, 1, true);
        await prizes.AddAsync(prize);
        await winners.AddAsync(LuckyWinner.Create(draw.Id, "An", prize.Name, Now));

        var loaded = await draws.FindBySlugAsync("boc-tham-abc12345");
        var loadedPrizes = await prizes.ListByDrawAsync(draw.Id);
        var loadedWinners = await winners.ListByDrawAsync(draw.Id);

        Assert.NotNull(loaded);
        Assert.Equal("123456", loaded!.SecurityCode);
        Assert.Equal(new[] { "An", "Bình" }, loaded.ParticipantList);
        Assert.Single(loadedPrizes);
        Assert.True(loadedPrizes[0].IsSpecial);
        Assert.Single(loadedWinners);
        Assert.Equal("An", loadedWinners[0].Participant);
        Assert.True(await draws.ExistsBySlugAsync("boc-tham-abc12345"));
    }

    [Fact]
    public async Task Notification_content_newlines_round_trip()
    {
        var notifications = new CsvNotificationRepository(PathFor("notifications.csv"));
        var notification = Notification.Create(
            "Thông báo", "Dòng 1\nDòng 2", NotificationTarget.SpecificClubs, "admin", Now);

        await notifications.AddAsync(notification);
        var loaded = await notifications.FindByIdAsync(notification.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Dòng 1\nDòng 2", loaded!.Content);
        Assert.Equal(NotificationTarget.SpecificClubs, loaded.TargetType);
    }

    [Fact]
    public async Task Notification_targets_and_reads_round_trip()
    {
        var targets = new CsvNotificationTargetRepository(PathFor("notification_targets.csv"));
        var reads = new CsvNotificationReadRepository(PathFor("notification_reads.csv"));
        var notificationId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await targets.AddAsync(NotificationTargetClub.Create(notificationId, clubId));
        await reads.AddAsync(NotificationRead.Create(notificationId, userId, Now));

        Assert.Single(await targets.ListByNotificationAsync(notificationId));
        Assert.Single(await targets.ListByClubAsync(clubId));
        Assert.NotNull(await reads.FindAsync(notificationId, userId));
        Assert.Single(await reads.ListByUserAsync(userId));
    }

    [Fact]
    public async Task Settings_round_trip_global_and_club()
    {
        var repository = new CsvSettingRepository(PathFor("settings.csv"));
        var clubId = Guid.NewGuid();
        await repository.AddAsync(Setting.Create(null, "site.title", "SIMS", Now));
        await repository.AddAsync(Setting.Create(clubId, "club.address", "Đà Nẵng, Việt Nam", Now));

        var global = await repository.FindAsync(null, "site.title");
        var club = await repository.FindAsync(clubId, "club.address");
        var globalList = await repository.ListByScopeAsync(null);

        Assert.Equal("SIMS", global!.Value);
        Assert.Equal("Đà Nẵng, Việt Nam", club!.Value);
        Assert.Single(globalList);
        Assert.Null(await repository.FindAsync(Guid.NewGuid(), "site.title"));
    }
}
