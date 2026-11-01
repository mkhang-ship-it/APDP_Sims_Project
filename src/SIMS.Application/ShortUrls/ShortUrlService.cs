using System.Security.Cryptography;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.ShortUrls;

namespace SIMS.Application.ShortUrls;

public sealed record ShortUrlRow(ShortUrl ShortUrl, int VisitCount);

public interface IShortUrlService
{
    Task<Result<ShortUrl>> CreateAsync(
        Guid clubId,
        string targetUrl,
        string? title,
        DateTimeOffset? expiresAtUtc,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<ShortUrl>> UpdateAsync(
        Guid id,
        string targetUrl,
        string? title,
        DateTimeOffset? expiresAtUtc,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<ShortUrl>> SetActiveAsync(
        Guid id, bool isActive, string actor, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShortUrlRow>> ListAsync(Guid? clubId, CancellationToken cancellationToken = default);

    Task<ShortUrl?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<ShortUrl>> ResolveAsync(string code, CancellationToken cancellationToken = default);

    Task RecordVisitAsync(
        Guid shortUrlId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);
}

public sealed class ShortUrlService : IShortUrlService
{
    private const string Alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly IShortUrlRepository _urls;
    private readonly IShortUrlVisitRepository _visits;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public ShortUrlService(
        IShortUrlRepository urls,
        IShortUrlVisitRepository visits,
        IAuditLog audit,
        IClock clock)
    {
        _urls = urls;
        _visits = visits;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<ShortUrl>> CreateAsync(
        Guid clubId,
        string targetUrl,
        string? title,
        DateTimeOffset? expiresAtUtc,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ShortUrl url;
        try
        {
            var code = await NextCodeAsync(cancellationToken).ConfigureAwait(false);
            url = ShortUrl.Create(clubId, code, targetUrl, title, expiresAtUtc, actor, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _urls.AddAsync(url, cancellationToken).ConfigureAwait(false);
        await AuditAsync("shorturl.create", url.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<ShortUrl>.Ok(url);
    }

    public async Task<Result<ShortUrl>> UpdateAsync(
        Guid id,
        string targetUrl,
        string? title,
        DateTimeOffset? expiresAtUtc,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var url = await _urls.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (url is null)
        {
            return Fail("Không tìm thấy liên kết.");
        }

        try
        {
            url.Update(targetUrl, title, expiresAtUtc);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _urls.UpdateAsync(url, cancellationToken).ConfigureAwait(false);
        await AuditAsync("shorturl.update", url.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<ShortUrl>.Ok(url);
    }

    public async Task<Result<ShortUrl>> SetActiveAsync(
        Guid id,
        bool isActive,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var url = await _urls.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (url is null)
        {
            return Fail("Không tìm thấy liên kết.");
        }

        if (isActive)
        {
            url.Activate();
        }
        else
        {
            url.Deactivate();
        }

        await _urls.UpdateAsync(url, cancellationToken).ConfigureAwait(false);
        await AuditAsync("shorturl.toggle", url.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<ShortUrl>.Ok(url);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var url = await _urls.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (url is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy liên kết."));
        }

        foreach (var visit in await _visits
                     .ListByShortUrlAsync(id, cancellationToken)
                     .ConfigureAwait(false))
        {
            await _visits.DeleteAsync(visit.Id, cancellationToken).ConfigureAwait(false);
        }

        await _urls.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditAsync("shorturl.delete", id, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<IReadOnlyList<ShortUrlRow>> ListAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        var urls = clubId is null
            ? await _urls.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _urls.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);

        var rows = new List<ShortUrlRow>(urls.Count);
        foreach (var url in urls.OrderByDescending(item => item.CreatedAtUtc))
        {
            var count = await _visits.CountByShortUrlAsync(url.Id, cancellationToken).ConfigureAwait(false);
            rows.Add(new ShortUrlRow(url, count));
        }

        return rows;
    }

    public Task<ShortUrl?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _urls.FindByIdAsync(id, cancellationToken);

    public async Task<Result<ShortUrl>> ResolveAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var url = await _urls.FindByCodeAsync(code ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (url is null)
        {
            return Result<ShortUrl>.Fail(new ValidationError(string.Empty, "Liên kết không tồn tại."));
        }

        if (!url.IsActive)
        {
            return Result<ShortUrl>.Fail(new ValidationError(string.Empty, "Liên kết đã bị tạm dừng."));
        }

        if (url.IsExpired(_clock.UtcNow))
        {
            return Result<ShortUrl>.Fail(new ValidationError(string.Empty, "Liên kết đã hết hạn."));
        }

        return Result<ShortUrl>.Ok(url);
    }

    public async Task RecordVisitAsync(
        Guid shortUrlId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (await _urls.FindByIdAsync(shortUrlId, cancellationToken).ConfigureAwait(false) is null)
        {
            return;
        }

        var visit = ShortUrlVisit.Create(shortUrlId, _clock.UtcNow, ipAddress, userAgent);
        await _visits.AddAsync(visit, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> NextCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var buffer = new char[12];
            for (var index = 0; index < buffer.Length; index++)
            {
                buffer[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            var code = new string(buffer);
            if (!await _urls.ExistsByCodeAsync(code, cancellationToken).ConfigureAwait(false))
            {
                return code;
            }
        }

        throw new DomainException("Không sinh được mã rút gọn duy nhất.");
    }

    private Task AuditAsync(string action, Guid entityId, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(_clock.UtcNow, actor, action, "short_url", entityId.ToString("D"), string.Empty),
            cancellationToken);

    private static Result<ShortUrl> Fail(string message)
        => Result<ShortUrl>.Fail(new ValidationError(string.Empty, message));
}
