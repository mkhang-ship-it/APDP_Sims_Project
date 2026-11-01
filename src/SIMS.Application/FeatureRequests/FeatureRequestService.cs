using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.FeatureRequests;

namespace SIMS.Application.FeatureRequests;

public interface IFeatureRequestService
{
    Task<Result<FeatureRequest>> CreateAsync(
        Guid? clubId, string title, string content, string actor,
        CancellationToken cancellationToken = default);

    Task<Result<FeatureRequest>> UpdateStatusAsync(
        Guid id, FeatureRequestStatus status, string? response, string actor,
        CancellationToken cancellationToken = default);

    Task<Result<FeatureRequest>> UpdateContentAsync(
        Guid id, string title, string content, string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureRequest>> ListForStaffAsync(
        Guid? clubId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureRequest>> ListMineAsync(
        string actor, CancellationToken cancellationToken = default);

    Task<FeatureRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class FeatureRequestService : IFeatureRequestService
{
    private readonly IFeatureRequestRepository _requests;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public FeatureRequestService(IFeatureRequestRepository requests, IAuditLog audit, IClock clock)
    {
        _requests = requests;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<FeatureRequest>> CreateAsync(
        Guid? clubId,
        string title,
        string content,
        string actor,
        CancellationToken cancellationToken = default)
    {
        FeatureRequest request;
        try
        {
            request = FeatureRequest.Create(clubId, title, content, actor, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _requests.AddAsync(request, cancellationToken).ConfigureAwait(false);
        await AuditAsync("feature.create", request.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<FeatureRequest>.Ok(request);
    }

    public async Task<Result<FeatureRequest>> UpdateStatusAsync(
        Guid id,
        FeatureRequestStatus status,
        string? response,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var request = await _requests.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return Fail("Không tìm thấy đề xuất.");
        }

        try
        {
            request.UpdateStatus(status, response, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _requests.UpdateAsync(request, cancellationToken).ConfigureAwait(false);
        await AuditAsync("feature.status", request.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<FeatureRequest>.Ok(request);
    }

    public async Task<Result<FeatureRequest>> UpdateContentAsync(
        Guid id,
        string title,
        string content,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var request = await _requests.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return Fail("Không tìm thấy đề xuất.");
        }

        if (!string.Equals(request.CreatedBy, actor, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("Bạn không phải người tạo đề xuất này.");
        }

        try
        {
            request.UpdateContent(title, content, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _requests.UpdateAsync(request, cancellationToken).ConfigureAwait(false);
        await AuditAsync("feature.update", request.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<FeatureRequest>.Ok(request);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var request = await _requests.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy đề xuất."));
        }

        await _requests.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditAsync("feature.delete", id, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<IReadOnlyList<FeatureRequest>> ListForStaffAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        var requests = clubId is null
            ? await _requests.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _requests.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);

        return requests
            .OrderByDescending(request => request.CreatedAtUtc)
            .ToArray();
    }

    public async Task<IReadOnlyList<FeatureRequest>> ListMineAsync(
        string actor,
        CancellationToken cancellationToken = default)
        => (await _requests.ListByCreatorAsync(actor, cancellationToken).ConfigureAwait(false))
            .OrderByDescending(request => request.CreatedAtUtc)
            .ToArray();

    public Task<FeatureRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _requests.FindByIdAsync(id, cancellationToken);

    private Task AuditAsync(string action, Guid entityId, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(_clock.UtcNow, actor, action, "feature_request", entityId.ToString("D"), string.Empty),
            cancellationToken);

    private static Result<FeatureRequest> Fail(string message)
        => Result<FeatureRequest>.Fail(new ValidationError(string.Empty, message));
}
