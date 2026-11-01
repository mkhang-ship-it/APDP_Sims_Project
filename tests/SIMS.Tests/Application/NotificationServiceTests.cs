using SIMS.Application.Notifications;
using SIMS.Domain.Notifications;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class NotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        NotificationService Service,
        InMemoryNotificationRepository Notifications,
        InMemoryNotificationTargetRepository Targets,
        InMemoryNotificationReadRepository Reads,
        RecordingAuditLog Audit);

    private static Harness Create()
    {
        var notifications = new InMemoryNotificationRepository();
        var targets = new InMemoryNotificationTargetRepository();
        var reads = new InMemoryNotificationReadRepository();
        var audit = new RecordingAuditLog();
        var service = new NotificationService(notifications, targets, reads, audit, new FakeClock(Now));

        return new Harness(service, notifications, targets, reads, audit);
    }

    [Fact]
    public async Task Create_specific_and_all_club_notifications_with_targets()
    {
        var harness = Create();
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();

        var all = await harness.Service.CreateAsync(
            "Chung", "Nội dung", true, Array.Empty<Guid>(), "admin", "admin");
        var specific = await harness.Service.CreateAsync(
            "CLB A", "Nội dung A", false, new[] { clubA, clubB }, "admin", "admin");
        var withoutClubs = await harness.Service.CreateAsync(
            "Thiếu CLB", "Nội dung", false, Array.Empty<Guid>(), "admin", "admin");

        Assert.True(all.IsSuccess);
        Assert.True(specific.IsSuccess);
        Assert.False(withoutClubs.IsSuccess);
        Assert.Equal(NotificationTarget.SpecificClubs, specific.Value!.TargetType);
        Assert.Equal(2, harness.Targets.All.Count);
        Assert.Equal("notification.create", harness.Audit.Entries[0].Action);
    }

    [Fact]
    public async Task Management_list_scopes_by_club()
    {
        var harness = Create();
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        await harness.Service.CreateAsync("Chung", "Nội dung", true, Array.Empty<Guid>(), "admin", "admin");
        await harness.Service.CreateAsync("CLB A", "Nội dung", false, new[] { clubA }, "admin", "admin");
        await harness.Service.CreateAsync("CLB B", "Nội dung", false, new[] { clubB }, "admin", "admin");

        var all = await harness.Service.ListForManagementAsync(null);
        var forA = await harness.Service.ListForManagementAsync(clubA);

        Assert.Equal(3, all.Count);
        Assert.Equal(2, forA.Count);
        Assert.Contains(forA, row => row.Notification.Title == "Chung");
        Assert.DoesNotContain(forA, row => row.Notification.Title == "CLB B");
    }

    [Fact]
    public async Task Inbox_visibility_unread_and_mark_read()
    {
        var harness = Create();
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await harness.Service.CreateAsync("Chung", "Nội dung", true, Array.Empty<Guid>(), "admin", "admin");
        var clubNotice = await harness.Service.CreateAsync(
            "CLB A", "Nội dung", false, new[] { clubA }, "admin", "admin");
        await harness.Service.CreateAsync("CLB B", "Nội dung", false, new[] { clubB }, "admin", "admin");

        var inbox = await harness.Service.InboxAsync(userId, clubA);
        var unread = await harness.Service.UnreadCountAsync(userId, clubA);
        await harness.Service.MarkReadAsync(clubNotice.Value!.Id, userId);
        var unreadAfter = await harness.Service.UnreadCountAsync(userId, clubA);
        var markAll = await harness.Service.MarkAllReadAsync(userId, clubA);
        var finalUnread = await harness.Service.UnreadCountAsync(userId, clubA);

        Assert.Equal(2, inbox.Count);
        Assert.Equal(2, unread);
        Assert.Equal(1, unreadAfter);
        Assert.Equal(1, markAll.Value);
        Assert.Equal(0, finalUnread);
    }

    [Fact]
    public async Task Update_replaces_targets_and_delete_cleans_reads()
    {
        var harness = Create();
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var created = await harness.Service.CreateAsync(
            "Thông báo", "Nội dung", false, new[] { clubA }, "admin", "admin");
        await harness.Service.MarkReadAsync(created.Value!.Id, userId);

        var updated = await harness.Service.UpdateAsync(
            created.Value.Id, "Thông báo", "Nội dung mới", false, new[] { clubB }, true, "admin");
        var targetsAfter = await harness.Targets.ListByNotificationAsync(created.Value.Id);
        var deleted = await harness.Service.DeleteAsync(created.Value.Id, "admin");

        Assert.True(updated.IsSuccess);
        Assert.Single(targetsAfter);
        Assert.Equal(clubB, targetsAfter[0].ClubId);
        Assert.True(deleted.IsSuccess);
        Assert.Empty(harness.Notifications.All);
        Assert.Empty(harness.Targets.All);
        Assert.Empty(harness.Reads.All);
    }

    [Fact]
    public async Task Inactive_notifications_are_hidden_from_inbox_but_kept_in_management()
    {
        var harness = Create();
        var created = await harness.Service.CreateAsync(
            "Tạm ẩn", "Nội dung", true, Array.Empty<Guid>(), "admin", "admin");
        await harness.Service.UpdateAsync(
            created.Value!.Id, "Tạm ẩn", "Nội dung", true, Array.Empty<Guid>(), false, "admin");

        var inbox = await harness.Service.InboxAsync(Guid.NewGuid(), null);
        var management = await harness.Service.ListForManagementAsync(null);

        Assert.Empty(inbox);
        Assert.Single(management);
    }
}
