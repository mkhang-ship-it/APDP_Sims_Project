using SIMS.Application.Abstractions;
using SIMS.Domain.LuckyDraws;
using SIMS.Domain.Notifications;
using SIMS.Domain.Settings;

namespace SIMS.Tests.Support;

public sealed class InMemoryLuckyDrawRepository : InMemoryRepository<LuckyDraw>, ILuckyDrawRepository
{
    public Task<LuckyDraw?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(draw =>
            string.Equals(draw.Slug, slug, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(draw =>
            string.Equals(draw.Slug, slug, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<LuckyDraw>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LuckyDraw>>(Items.Where(draw => draw.ClubId == clubId).ToArray());
}

public sealed class InMemoryLuckyPrizeRepository : InMemoryRepository<LuckyPrize>, ILuckyPrizeRepository
{
    public Task<IReadOnlyList<LuckyPrize>> ListByDrawAsync(
        Guid luckyDrawId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LuckyPrize>>(
            Items.Where(prize => prize.LuckyDrawId == luckyDrawId).ToArray());
}

public sealed class InMemoryLuckyWinnerRepository : InMemoryRepository<LuckyWinner>, ILuckyWinnerRepository
{
    public Task<IReadOnlyList<LuckyWinner>> ListByDrawAsync(
        Guid luckyDrawId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LuckyWinner>>(
            Items.Where(winner => winner.LuckyDrawId == luckyDrawId).ToArray());
}

public sealed class InMemoryNotificationRepository
    : InMemoryRepository<Notification>, INotificationRepository
{
}

public sealed class InMemoryNotificationTargetRepository
    : InMemoryRepository<NotificationTargetClub>, INotificationTargetRepository
{
    public Task<IReadOnlyList<NotificationTargetClub>> ListByNotificationAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<NotificationTargetClub>>(
            Items.Where(target => target.NotificationId == notificationId).ToArray());

    public Task<IReadOnlyList<NotificationTargetClub>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<NotificationTargetClub>>(
            Items.Where(target => target.ClubId == clubId).ToArray());
}

public sealed class InMemoryNotificationReadRepository
    : InMemoryRepository<NotificationRead>, INotificationReadRepository
{
    public Task<NotificationRead?> FindAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(read =>
            read.NotificationId == notificationId && read.UserId == userId));

    public Task<IReadOnlyList<NotificationRead>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<NotificationRead>>(
            Items.Where(read => read.UserId == userId).ToArray());
}

public sealed class InMemorySettingRepository : InMemoryRepository<Setting>, ISettingRepository
{
    public Task<Setting?> FindAsync(
        Guid? clubId,
        string key,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(setting =>
            setting.ClubId == clubId &&
            string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Setting>> ListByScopeAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Setting>>(
            Items.Where(setting => setting.ClubId == clubId).ToArray());
}
