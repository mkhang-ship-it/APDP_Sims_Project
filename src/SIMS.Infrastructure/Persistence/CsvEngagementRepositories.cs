using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.LuckyDraws;
using SIMS.Domain.Notifications;
using SIMS.Domain.Settings;

namespace SIMS.Infrastructure.Persistence;

public sealed class CsvLuckyDrawRepository : CsvRepository<LuckyDraw>, ILuckyDrawRepository
{
    private const int ColumnCount = 11;

    public CsvLuckyDrawRepository(string filePath)
        : base(
            filePath,
            "id",
            "club_id",
            "title",
            "slug",
            "security_code",
            "participants",
            "start_at_utc",
            "end_at_utc",
            "is_active",
            "created_by",
            "created_at_utc")
    {
    }

    public CsvLuckyDrawRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<LuckyDraw?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(draw => string.Equals(draw.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default)
        => await FindBySlugAsync(slug, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<LuckyDraw>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(draw => draw.ClubId == clubId).ToArray();
    }

    protected override string[] ToCells(LuckyDraw draw) =>
    [
        draw.Id.ToString("D"),
        draw.ClubId.ToString("D"),
        draw.Title,
        draw.Slug,
        draw.SecurityCode,
        draw.Participants,
        draw.StartAtUtc.ToString("O", CultureInfo.InvariantCulture),
        draw.EndAtUtc.ToString("O", CultureInfo.InvariantCulture),
        draw.IsActive ? "true" : "false",
        draw.CreatedBy,
        draw.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override LuckyDraw? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var clubId) ||
            !DateTimeOffset.TryParse(cells[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startAt) ||
            !DateTimeOffset.TryParse(cells[7], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var endAt) ||
            !bool.TryParse(cells[8], out var isActive) ||
            !DateTimeOffset.TryParse(cells[10], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return LuckyDraw.Restore(
                id, clubId, cells[2], cells[3], cells[4], cells[5], startAt, endAt, isActive,
                cells[9], createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvLuckyPrizeRepository : CsvRepository<LuckyPrize>, ILuckyPrizeRepository
{
    public CsvLuckyPrizeRepository(string filePath)
        : base(filePath, "id", "lucky_draw_id", "name", "quantity", "order", "is_special")
    {
    }

    public CsvLuckyPrizeRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<LuckyPrize>> ListByDrawAsync(
        Guid luckyDrawId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(prize => prize.LuckyDrawId == luckyDrawId).ToArray();
    }

    protected override string[] ToCells(LuckyPrize prize) =>
    [
        prize.Id.ToString("D"),
        prize.LuckyDrawId.ToString("D"),
        prize.Name,
        prize.Quantity.ToString(CultureInfo.InvariantCulture),
        prize.Order.ToString(CultureInfo.InvariantCulture),
        prize.IsSpecial ? "true" : "false",
    ];

    protected override LuckyPrize? Map(string[] cells)
    {
        if (cells.Length != 6 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var drawId) ||
            !int.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) ||
            !int.TryParse(cells[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ||
            !bool.TryParse(cells[5], out var isSpecial))
        {
            return null;
        }

        try
        {
            return LuckyPrize.Restore(id, drawId, cells[2], quantity, order, isSpecial);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvLuckyWinnerRepository : CsvRepository<LuckyWinner>, ILuckyWinnerRepository
{
    public CsvLuckyWinnerRepository(string filePath)
        : base(filePath, "id", "lucky_draw_id", "participant", "prize_name", "won_at_utc")
    {
    }

    public CsvLuckyWinnerRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<LuckyWinner>> ListByDrawAsync(
        Guid luckyDrawId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(winner => winner.LuckyDrawId == luckyDrawId).ToArray();
    }

    protected override string[] ToCells(LuckyWinner winner) =>
    [
        winner.Id.ToString("D"),
        winner.LuckyDrawId.ToString("D"),
        winner.Participant,
        winner.PrizeName,
        winner.WonAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override LuckyWinner? Map(string[] cells)
    {
        if (cells.Length != 5 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var drawId) ||
            !DateTimeOffset.TryParse(cells[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var wonAt))
        {
            return null;
        }

        try
        {
            return LuckyWinner.Restore(id, drawId, cells[2], cells[3], wonAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvNotificationRepository : CsvRepository<Notification>, INotificationRepository
{
    public CsvNotificationRepository(string filePath)
        : base(
            filePath,
            "id",
            "title",
            "content",
            "target_type",
            "sender",
            "is_active",
            "created_at_utc",
            "updated_at_utc")
    {
    }

    public CsvNotificationRepository(IRowStore store)
        : base(store)
    {
    }

    protected override string[] ToCells(Notification notification) =>
    [
        notification.Id.ToString("D"),
        notification.Title,
        notification.Content.Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal),
        notification.TargetType.ToString(),
        notification.Sender,
        notification.IsActive ? "true" : "false",
        notification.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        notification.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Notification? Map(string[] cells)
    {
        if (cells.Length != 8 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Enum.TryParse<NotificationTarget>(cells[3], ignoreCase: true, out var targetType) ||
            !bool.TryParse(cells[5], out var isActive) ||
            !DateTimeOffset.TryParse(cells[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt) ||
            !DateTimeOffset.TryParse(cells[7], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            return null;
        }

        try
        {
            return Notification.Restore(
                id, cells[1], cells[2].Replace("\\n", "\n", StringComparison.Ordinal), targetType,
                cells[4], isActive, createdAt, updatedAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvNotificationTargetRepository
    : CsvRepository<NotificationTargetClub>, INotificationTargetRepository
{
    public CsvNotificationTargetRepository(string filePath)
        : base(filePath, "id", "notification_id", "club_id")
    {
    }

    public CsvNotificationTargetRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<NotificationTargetClub>> ListByNotificationAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(target => target.NotificationId == notificationId).ToArray();
    }

    public async Task<IReadOnlyList<NotificationTargetClub>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(target => target.ClubId == clubId).ToArray();
    }

    protected override string[] ToCells(NotificationTargetClub target) =>
    [
        target.Id.ToString("D"),
        target.NotificationId.ToString("D"),
        target.ClubId.ToString("D"),
    ];

    protected override NotificationTargetClub? Map(string[] cells)
    {
        if (cells.Length != 3 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var notificationId) ||
            !Guid.TryParse(cells[2], out var clubId))
        {
            return null;
        }

        try
        {
            return NotificationTargetClub.Restore(id, notificationId, clubId);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvNotificationReadRepository
    : CsvRepository<NotificationRead>, INotificationReadRepository
{
    public CsvNotificationReadRepository(string filePath)
        : base(filePath, "id", "notification_id", "user_id", "read_at_utc")
    {
    }

    public CsvNotificationReadRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<NotificationRead?> FindAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(read =>
            read.NotificationId == notificationId && read.UserId == userId);
    }

    public async Task<IReadOnlyList<NotificationRead>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(read => read.UserId == userId).ToArray();
    }

    protected override string[] ToCells(NotificationRead read) =>
    [
        read.Id.ToString("D"),
        read.NotificationId.ToString("D"),
        read.UserId.ToString("D"),
        read.ReadAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override NotificationRead? Map(string[] cells)
    {
        if (cells.Length != 4 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var notificationId) ||
            !Guid.TryParse(cells[2], out var userId) ||
            !DateTimeOffset.TryParse(cells[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var readAt))
        {
            return null;
        }

        try
        {
            return NotificationRead.Restore(id, notificationId, userId, readAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvSettingRepository : CsvRepository<Setting>, ISettingRepository
{
    public CsvSettingRepository(string filePath)
        : base(filePath, "id", "club_id", "key", "value", "updated_at_utc")
    {
    }

    public CsvSettingRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<Setting?> FindAsync(
        Guid? clubId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(setting =>
            setting.ClubId == clubId &&
            string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<Setting>> ListByScopeAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(setting => setting.ClubId == clubId).ToArray();
    }

    protected override string[] ToCells(Setting setting) =>
    [
        setting.Id.ToString("D"),
        setting.ClubId?.ToString("D") ?? string.Empty,
        setting.Key,
        setting.Value,
        setting.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Setting? Map(string[] cells)
    {
        if (cells.Length != 5 || !Guid.TryParse(cells[0], out var id))
        {
            return null;
        }

        Guid? clubId = null;
        if (!string.IsNullOrWhiteSpace(cells[1]))
        {
            if (!Guid.TryParse(cells[1], out var parsedClubId))
            {
                return null;
            }

            clubId = parsedClubId;
        }

        if (!DateTimeOffset.TryParse(cells[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            return null;
        }

        try
        {
            return Setting.Restore(id, clubId, cells[2], cells[3], updatedAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
