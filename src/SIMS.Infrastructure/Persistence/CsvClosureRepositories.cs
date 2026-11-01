using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Events;
using SIMS.Domain.FeatureRequests;
using SIMS.Domain.ShortUrls;

namespace SIMS.Infrastructure.Persistence;

public sealed class CsvShortUrlRepository : CsvRepository<ShortUrl>, IShortUrlRepository
{
    public CsvShortUrlRepository(string filePath)
        : base(
            filePath,
            "id",
            "club_id",
            "code",
            "target_url",
            "title",
            "created_by",
            "expires_at_utc",
            "is_active",
            "created_at_utc")
    {
    }

    public CsvShortUrlRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<ShortUrl?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(url => string.Equals(url.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await FindByCodeAsync(code, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<ShortUrl>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(url => url.ClubId == clubId).ToArray();
    }

    protected override string[] ToCells(ShortUrl url) =>
    [
        url.Id.ToString("D"),
        url.ClubId.ToString("D"),
        url.Code,
        url.TargetUrl,
        url.Title,
        url.CreatedBy,
        url.ExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        url.IsActive ? "true" : "false",
        url.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override ShortUrl? Map(string[] cells)
    {
        if (cells.Length != 9 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var clubId) ||
            !bool.TryParse(cells[7], out var isActive) ||
            !DateTimeOffset.TryParse(cells[8], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(cells[6]))
        {
            if (!DateTimeOffset.TryParse(cells[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return null;
            }

            expiresAt = parsed;
        }

        try
        {
            return ShortUrl.Restore(
                id, clubId, cells[2], cells[3], cells[4], cells[5], expiresAt, isActive, createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvShortUrlVisitRepository
    : CsvRepository<ShortUrlVisit>, IShortUrlVisitRepository
{
    public CsvShortUrlVisitRepository(string filePath)
        : base(filePath, "id", "short_url_id", "visited_at_utc", "ip_address", "user_agent")
    {
    }

    public CsvShortUrlVisitRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<ShortUrlVisit>> ListByShortUrlAsync(
        Guid shortUrlId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(visit => visit.ShortUrlId == shortUrlId).ToArray();
    }

    public async Task<int> CountByShortUrlAsync(
        Guid shortUrlId,
        CancellationToken cancellationToken = default)
        => (await ListByShortUrlAsync(shortUrlId, cancellationToken).ConfigureAwait(false)).Count;

    protected override string[] ToCells(ShortUrlVisit visit) =>
    [
        visit.Id.ToString("D"),
        visit.ShortUrlId.ToString("D"),
        visit.VisitedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        visit.IpAddress,
        visit.UserAgent,
    ];

    protected override ShortUrlVisit? Map(string[] cells)
    {
        if (cells.Length != 5 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var shortUrlId) ||
            !DateTimeOffset.TryParse(cells[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var visitedAt))
        {
            return null;
        }

        try
        {
            return ShortUrlVisit.Restore(id, shortUrlId, visitedAt, cells[3], cells[4]);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvEventImageRepository : CsvRepository<EventImage>, IEventImageRepository
{
    public CsvEventImageRepository(string filePath)
        : base(filePath, "id", "event_id", "file_path", "caption", "uploaded_by", "created_at_utc")
    {
    }

    public CsvEventImageRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<EventImage>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(image => image.EventId == eventId).ToArray();
    }

    protected override string[] ToCells(EventImage image) =>
    [
        image.Id.ToString("D"),
        image.EventId.ToString("D"),
        image.FilePath,
        image.Caption,
        image.UploadedBy,
        image.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override EventImage? Map(string[] cells)
    {
        if (cells.Length != 6 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var eventId) ||
            !DateTimeOffset.TryParse(cells[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return EventImage.Restore(id, eventId, cells[2], cells[3], cells[4], createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvFeatureRequestRepository
    : CsvRepository<FeatureRequest>, IFeatureRequestRepository
{
    public CsvFeatureRequestRepository(string filePath)
        : base(
            filePath,
            "id",
            "club_id",
            "title",
            "content",
            "created_by",
            "status",
            "response",
            "created_at_utc",
            "updated_at_utc")
    {
    }

    public CsvFeatureRequestRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<FeatureRequest>> ListByCreatorAsync(
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items
            .Where(request => string.Equals(request.CreatedBy, createdBy, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public async Task<IReadOnlyList<FeatureRequest>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(request => request.ClubId == clubId).ToArray();
    }

    protected override string[] ToCells(FeatureRequest request) =>
    [
        request.Id.ToString("D"),
        request.ClubId?.ToString("D") ?? string.Empty,
        request.Title,
        request.Content,
        request.CreatedBy,
        request.Status.ToString(),
        request.Response,
        request.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        request.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override FeatureRequest? Map(string[] cells)
    {
        if (cells.Length != 9 || !Guid.TryParse(cells[0], out var id))
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

        if (!Enum.TryParse<FeatureRequestStatus>(cells[5], ignoreCase: true, out var status) ||
            !DateTimeOffset.TryParse(cells[7], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt) ||
            !DateTimeOffset.TryParse(cells[8], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            return null;
        }

        try
        {
            return FeatureRequest.Restore(
                id, clubId, cells[2], cells[3], cells[4], status, cells[6], createdAt, updatedAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
