using SIMS.Domain.Events;
using SIMS.Domain.FeatureRequests;
using SIMS.Domain.ShortUrls;

namespace SIMS.Application.Abstractions;

public interface IShortUrlRepository : IRepository<ShortUrl>
{
    Task<ShortUrl?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShortUrl>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
}

public interface IShortUrlVisitRepository : IRepository<ShortUrlVisit>
{
    Task<IReadOnlyList<ShortUrlVisit>> ListByShortUrlAsync(
        Guid shortUrlId,
        CancellationToken cancellationToken = default);

    Task<int> CountByShortUrlAsync(Guid shortUrlId, CancellationToken cancellationToken = default);
}

public interface IEventImageRepository : IRepository<EventImage>
{
    Task<IReadOnlyList<EventImage>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public interface IFeatureRequestRepository : IRepository<FeatureRequest>
{
    Task<IReadOnlyList<FeatureRequest>> ListByCreatorAsync(
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureRequest>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);
}

public interface IAuditLogReader
{
    Task<IReadOnlyList<AuditEntry>> ReadAsync(int maxEntries = 200, CancellationToken cancellationToken = default);
}
