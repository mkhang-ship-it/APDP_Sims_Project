using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Notifications;

namespace SIMS.Application.Notifications;

public sealed record NotificationRow(
    Notification Notification,
    IReadOnlyList<Guid> TargetClubIds,
    bool IsRead);

public interface INotificationService
{
    Task<Result<Notification>> CreateAsync(
        string title,
        string content,
        bool targetAllClubs,
        IReadOnlyCollection<Guid> clubIds,
        string sender,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<Notification>> UpdateAsync(
        Guid id,
        string title,
        string content,
        bool targetAllClubs,
        IReadOnlyCollection<Guid> clubIds,
        bool isActive,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationRow>> ListForManagementAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationRow>> InboxAsync(
        Guid userId,
        Guid? clubId,
        CancellationToken cancellationToken = default);

    Task<int> UnreadCountAsync(
        Guid userId,
        Guid? clubId,
        CancellationToken cancellationToken = default);

    Task<Result> MarkReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<int>> MarkAllReadAsync(
        Guid userId,
        Guid? clubId,
        CancellationToken cancellationToken = default);

    Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _notifications;
    private readonly INotificationTargetRepository _targets;
    private readonly INotificationReadRepository _reads;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public NotificationService(
        INotificationRepository notifications,
        INotificationTargetRepository targets,
        INotificationReadRepository reads,
        IAuditLog audit,
        IClock clock)
    {
        _notifications = notifications;
        _targets = targets;
        _reads = reads;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<Notification>> CreateAsync(
        string title,
        string content,
        bool targetAllClubs,
        IReadOnlyCollection<Guid> clubIds,
        string sender,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var targetType = targetAllClubs ? NotificationTarget.AllClubs : NotificationTarget.SpecificClubs;
        if (!targetAllClubs && clubIds.Count == 0)
        {
            return Fail("Chọn ít nhất một CLB nhận thông báo.");
        }

        Notification notification;
        try
        {
            notification = Notification.Create(title, content, targetType, sender, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _notifications.AddAsync(notification, cancellationToken).ConfigureAwait(false);
        await ReplaceTargetsAsync(notification, clubIds, cancellationToken).ConfigureAwait(false);
        await AuditAsync("notification.create", notification.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<Notification>.Ok(notification);
    }

    public async Task<Result<Notification>> UpdateAsync(
        Guid id,
        string title,
        string content,
        bool targetAllClubs,
        IReadOnlyCollection<Guid> clubIds,
        bool isActive,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (notification is null)
        {
            return Fail("Không tìm thấy thông báo.");
        }

        if (!targetAllClubs && clubIds.Count == 0)
        {
            return Fail("Chọn ít nhất một CLB nhận thông báo.");
        }

        try
        {
            notification.Update(
                title,
                content,
                targetAllClubs ? NotificationTarget.AllClubs : NotificationTarget.SpecificClubs,
                isActive,
                _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _notifications.UpdateAsync(notification, cancellationToken).ConfigureAwait(false);
        await ReplaceTargetsAsync(notification, clubIds, cancellationToken).ConfigureAwait(false);
        await AuditAsync("notification.update", notification.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<Notification>.Ok(notification);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (notification is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy thông báo."));
        }

        foreach (var target in await _targets.ListByNotificationAsync(id, cancellationToken).ConfigureAwait(false))
        {
            await _targets.DeleteAsync(target.Id, cancellationToken).ConfigureAwait(false);
        }

        var reads = await _reads.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var read in reads.Where(item => item.NotificationId == id))
        {
            await _reads.DeleteAsync(read.Id, cancellationToken).ConfigureAwait(false);
        }

        await _notifications.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditAsync("notification.delete", id, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<IReadOnlyList<NotificationRow>> ListForManagementAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<NotificationRow>(notifications.Count);

        foreach (var notification in notifications.OrderByDescending(item => item.CreatedAtUtc))
        {
            var targets = await _targets
                .ListByNotificationAsync(notification.Id, cancellationToken)
                .ConfigureAwait(false);
            var targetIds = targets.Select(target => target.ClubId).ToArray();

            var visible = clubId is null ||
                notification.TargetType == NotificationTarget.AllClubs ||
                targetIds.Contains(clubId.Value);

            if (visible)
            {
                rows.Add(new NotificationRow(notification, targetIds, false));
            }
        }

        return rows;
    }

    public async Task<IReadOnlyList<NotificationRow>> InboxAsync(
        Guid userId,
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        var notifications = await VisibleAsync(clubId, cancellationToken).ConfigureAwait(false);
        var reads = (await _reads.ListByUserAsync(userId, cancellationToken).ConfigureAwait(false))
            .Select(read => read.NotificationId)
            .ToHashSet();

        return notifications
            .Where(notification => notification.IsActive)
            .Select(notification => new NotificationRow(
                notification,
                Array.Empty<Guid>(),
                reads.Contains(notification.Id)))
            .ToArray();
    }

    public async Task<int> UnreadCountAsync(
        Guid userId,
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        var inbox = await InboxAsync(userId, clubId, cancellationToken).ConfigureAwait(false);

        return inbox.Count(row => !row.IsRead);
    }

    public async Task<Result> MarkReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FindByIdAsync(notificationId, cancellationToken).ConfigureAwait(false);
        if (notification is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy thông báo."));
        }

        if (await _reads.FindAsync(notificationId, userId, cancellationToken).ConfigureAwait(false) is null)
        {
            await _reads
                .AddAsync(NotificationRead.Create(notificationId, userId, _clock.UtcNow), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Ok();
    }

    public async Task<Result<int>> MarkAllReadAsync(
        Guid userId,
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        var unread = (await InboxAsync(userId, clubId, cancellationToken).ConfigureAwait(false))
            .Where(row => !row.IsRead)
            .ToArray();

        foreach (var row in unread)
        {
            await _reads
                .AddAsync(NotificationRead.Create(row.Notification.Id, userId, _clock.UtcNow), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<int>.Ok(unread.Length);
    }

    public Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _notifications.FindByIdAsync(id, cancellationToken);

    private async Task<IReadOnlyList<Notification>> VisibleAsync(
        Guid? clubId,
        CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(cancellationToken).ConfigureAwait(false);
        if (clubId is null)
        {
            return notifications;
        }

        var targets = await _targets.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);
        var targetIds = targets.Select(target => target.NotificationId).ToHashSet();

        return notifications
            .Where(notification =>
                notification.TargetType == NotificationTarget.AllClubs ||
                targetIds.Contains(notification.Id))
            .ToArray();
    }

    private async Task ReplaceTargetsAsync(
        Notification notification,
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken)
    {
        foreach (var target in await _targets
                     .ListByNotificationAsync(notification.Id, cancellationToken)
                     .ConfigureAwait(false))
        {
            await _targets.DeleteAsync(target.Id, cancellationToken).ConfigureAwait(false);
        }

        if (notification.TargetType != NotificationTarget.SpecificClubs)
        {
            return;
        }

        foreach (var clubId in clubIds.Distinct().Where(id => id != Guid.Empty))
        {
            await _targets
                .AddAsync(NotificationTargetClub.Create(notification.Id, clubId), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task AuditAsync(string action, Guid entityId, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(_clock.UtcNow, actor, action, "notification", entityId.ToString("D"), string.Empty),
            cancellationToken);

    private static Result<Notification> Fail(string message)
        => Result<Notification>.Fail(new ValidationError(string.Empty, message));
}
