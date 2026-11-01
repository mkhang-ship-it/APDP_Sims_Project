using SIMS.Domain.LuckyDraws;
using SIMS.Domain.Notifications;
using SIMS.Domain.Settings;

namespace SIMS.Application.Abstractions;

public interface ILuckyDrawRepository : IRepository<LuckyDraw>
{
    Task<LuckyDraw?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LuckyDraw>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
}

public interface ILuckyPrizeRepository : IRepository<LuckyPrize>
{
    Task<IReadOnlyList<LuckyPrize>> ListByDrawAsync(
        Guid luckyDrawId,
        CancellationToken cancellationToken = default);
}

public interface ILuckyWinnerRepository : IRepository<LuckyWinner>
{
    Task<IReadOnlyList<LuckyWinner>> ListByDrawAsync(
        Guid luckyDrawId,
        CancellationToken cancellationToken = default);
}

public interface INotificationRepository : IRepository<Notification>
{
}

public interface INotificationTargetRepository : IRepository<NotificationTargetClub>
{
    Task<IReadOnlyList<NotificationTargetClub>> ListByNotificationAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationTargetClub>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);
}

public interface INotificationReadRepository : IRepository<NotificationRead>
{
    Task<NotificationRead?> FindAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationRead>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface ISettingRepository : IRepository<Setting>
{
    Task<Setting?> FindAsync(
        Guid? clubId,
        string key,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Setting>> ListByScopeAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default);
}
