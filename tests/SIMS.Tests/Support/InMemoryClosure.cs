using SIMS.Application.Abstractions;
using SIMS.Domain.Events;
using SIMS.Domain.FeatureRequests;
using SIMS.Domain.ShortUrls;

namespace SIMS.Tests.Support;

public sealed class InMemoryShortUrlRepository : InMemoryRepository<ShortUrl>, IShortUrlRepository
{
    public Task<ShortUrl?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(url =>
            string.Equals(url.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(url =>
            string.Equals(url.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<ShortUrl>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ShortUrl>>(Items.Where(url => url.ClubId == clubId).ToArray());
}

public sealed class InMemoryShortUrlVisitRepository
    : InMemoryRepository<ShortUrlVisit>, IShortUrlVisitRepository
{
    public Task<IReadOnlyList<ShortUrlVisit>> ListByShortUrlAsync(
        Guid shortUrlId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ShortUrlVisit>>(
            Items.Where(visit => visit.ShortUrlId == shortUrlId).ToArray());

    public Task<int> CountByShortUrlAsync(Guid shortUrlId, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Count(visit => visit.ShortUrlId == shortUrlId));
}

public sealed class InMemoryEventImageRepository
    : InMemoryRepository<EventImage>, IEventImageRepository
{
    public Task<IReadOnlyList<EventImage>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<EventImage>>(
            Items.Where(image => image.EventId == eventId).ToArray());
}

public sealed class InMemoryFeatureRequestRepository
    : InMemoryRepository<FeatureRequest>, IFeatureRequestRepository
{
    public Task<IReadOnlyList<FeatureRequest>> ListByCreatorAsync(
        string createdBy,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FeatureRequest>>(Items
            .Where(request => string.Equals(request.CreatedBy, createdBy, StringComparison.OrdinalIgnoreCase))
            .ToArray());

    public Task<IReadOnlyList<FeatureRequest>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FeatureRequest>>(
            Items.Where(request => request.ClubId == clubId).ToArray());
}
