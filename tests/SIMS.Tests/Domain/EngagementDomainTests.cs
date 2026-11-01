using SIMS.Domain.Common;
using SIMS.Domain.LuckyDraws;
using SIMS.Domain.Notifications;
using SIMS.Domain.Settings;

namespace SIMS.Tests.Domain;

public sealed class EngagementDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lucky_draw_normalizes_participants_and_checks_open_window()
    {
        var draw = LuckyDraw.Create(
            Guid.NewGuid(),
            "  Bốc thăm tháng 11  ",
            "boc-tham-thang-11-abcd1234",
            "123456",
            new[] { " An ", "Bình", "an", "", "Cường" },
            Now.AddHours(-1),
            Now.AddHours(2),
            isActive: true,
            "faculty1",
            Now);

        Assert.Equal("Bốc thăm tháng 11", draw.Title);
        Assert.Equal(new[] { "An", "Bình", "Cường" }, draw.ParticipantList);
        Assert.True(draw.IsOpen(Now));
        Assert.False(draw.IsOpen(Now.AddHours(3)));

        Assert.Throws<DomainException>(() => LuckyDraw.Create(
            Guid.Empty, "T", "slug-a", null, null, Now, Now.AddHours(1), true, "x", Now));
        Assert.Throws<DomainException>(() => LuckyDraw.Create(
            Guid.NewGuid(), "T", "Slug Sai", null, null, Now, Now.AddHours(1), true, "x", Now));
        Assert.Throws<DomainException>(() => LuckyDraw.Create(
            Guid.NewGuid(), "T", "slug-b", null, null, Now.AddHours(2), Now, true, "x", Now));
    }

    [Fact]
    public void Lucky_prize_and_winner_validation()
    {
        var prize = LuckyPrize.Create(Guid.NewGuid(), " Giải nhất ", 1, 1, isSpecial: true);

        Assert.Equal("Giải nhất", prize.Name);
        Assert.True(prize.IsSpecial);
        Assert.Throws<DomainException>(() => LuckyPrize.Create(Guid.NewGuid(), "Giải", 0, 1, false));
        Assert.Throws<DomainException>(() => LuckyPrize.Create(Guid.Empty, "Giải", 1, 1, false));

        var winner = LuckyWinner.Create(prize.LuckyDrawId, " An ", prize.Name, Now);
        Assert.Equal("An", winner.Participant);
        Assert.Throws<DomainException>(() => LuckyWinner.Create(Guid.NewGuid(), " ", "Giải", Now));
    }

    [Fact]
    public void Notification_normalizes_content_and_requires_title()
    {
        var notification = Notification.Create(
            "  Thông báo  ", "Dòng 1\r\nDòng 2", NotificationTarget.AllClubs, "faculty1", Now);

        Assert.Equal("Thông báo", notification.Title);
        Assert.Equal("Dòng 1\nDòng 2", notification.Content);
        Assert.True(notification.IsActive);
        Assert.Throws<DomainException>(() => Notification.Create(
            " ", "Nội dung", NotificationTarget.AllClubs, "x", Now));
        Assert.Throws<DomainException>(() => Notification.Create(
            "Tiêu đề", " ", NotificationTarget.AllClubs, "x", Now));
    }

    [Fact]
    public void Setting_validates_key_and_scope()
    {
        var global = Setting.Create(null, "site.title", "SIMS", Now);
        var club = Setting.Create(Guid.NewGuid(), "club.contact_email", "a@example.com", Now);

        Assert.Null(global.ClubId);
        Assert.NotNull(club.ClubId);
        Assert.Throws<DomainException>(() => Setting.Create(null, "Site Title", "x", Now));
        Assert.Throws<DomainException>(() => Setting.Create(Guid.Empty, "site.title", "x", Now));
        Assert.Throws<DomainException>(() => Setting.Create(null, "site.title", new string('x', 2001), Now));
    }
}
