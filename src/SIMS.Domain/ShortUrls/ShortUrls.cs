using System.Text.RegularExpressions;
using SIMS.Domain.Common;

namespace SIMS.Domain.ShortUrls;

/// <summary>Liên kết rút gọn theo CLB (mã 12 ký tự), có thể hết hạn hoặc tạm dừng.</summary>
public sealed class ShortUrl : Entity
{
    private static readonly Regex CodePattern = new(@"^[A-Za-z0-9]{6,16}$", RegexOptions.Compiled);

    private ShortUrl()
    {
        Code = string.Empty;
        TargetUrl = string.Empty;
        Title = string.Empty;
        CreatedBy = string.Empty;
    }

    public Guid ClubId { get; private set; }

    public string Code { get; private set; }

    public string TargetUrl { get; private set; }

    public string Title { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ShortUrl Create(
        Guid clubId,
        string code,
        string targetUrl,
        string? title,
        DateTimeOffset? expiresAtUtc,
        string createdBy,
        DateTimeOffset now)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        var actor = (createdBy ?? string.Empty).Trim();

        return new ShortUrl
        {
            ClubId = clubId,
            Code = RequireCode(code),
            TargetUrl = RequireUrl(targetUrl),
            Title = OptionalText(title, 255, "Tiêu đề"),
            CreatedBy = actor.Length == 0 ? "system" : actor,
            ExpiresAtUtc = expiresAtUtc,
            IsActive = true,
            CreatedAtUtc = now,
        };
    }

    public static ShortUrl Restore(
        Guid id,
        Guid clubId,
        string code,
        string targetUrl,
        string? title,
        string createdBy,
        DateTimeOffset? expiresAtUtc,
        bool isActive,
        DateTimeOffset createdAtUtc)
    {
        var url = Create(clubId, code, targetUrl, title, expiresAtUtc, createdBy, createdAtUtc);
        url.Id = id;
        url.IsActive = isActive;

        return url;
    }

    public void Update(string targetUrl, string? title, DateTimeOffset? expiresAtUtc)
    {
        TargetUrl = RequireUrl(targetUrl);
        Title = OptionalText(title, 255, "Tiêu đề");
        ExpiresAtUtc = expiresAtUtc;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc is not null && ExpiresAtUtc <= now;

    private static string RequireCode(string code)
    {
        var value = (code ?? string.Empty).Trim();

        return CodePattern.IsMatch(value)
            ? value
            : throw new DomainException("Mã rút gọn phải gồm 6–16 chữ cái hoặc số.");
    }

    private static string RequireUrl(string targetUrl)
    {
        var value = (targetUrl ?? string.Empty).Trim();

        if (value.Length is < 8 or > 2000 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException("Đích đến phải là URL http/https hợp lệ.");
        }

        return value;
    }

    private static string OptionalText(string? value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text;
    }
}

/// <summary>Một lượt truy cập liên kết rút gọn (phục vụ thống kê).</summary>
public sealed class ShortUrlVisit : Entity
{
    private ShortUrlVisit()
    {
        IpAddress = string.Empty;
        UserAgent = string.Empty;
    }

    public Guid ShortUrlId { get; private set; }

    public DateTimeOffset VisitedAtUtc { get; private set; }

    public string IpAddress { get; private set; }

    public string UserAgent { get; private set; }

    public static ShortUrlVisit Create(
        Guid shortUrlId,
        DateTimeOffset visitedAtUtc,
        string? ipAddress,
        string? userAgent)
    {
        if (shortUrlId == Guid.Empty)
        {
            throw new DomainException("Liên kết không hợp lệ.");
        }

        var ip = (ipAddress ?? string.Empty).Trim();
        var agent = (userAgent ?? string.Empty).Trim();

        return new ShortUrlVisit
        {
            ShortUrlId = shortUrlId,
            VisitedAtUtc = visitedAtUtc,
            IpAddress = ip.Length > 64 ? ip[..64] : ip,
            UserAgent = agent.Length > 300 ? agent[..300] : agent,
        };
    }

    public static ShortUrlVisit Restore(
        Guid id,
        Guid shortUrlId,
        DateTimeOffset visitedAtUtc,
        string? ipAddress,
        string? userAgent)
    {
        var visit = Create(shortUrlId, visitedAtUtc, ipAddress, userAgent);
        visit.Id = id;

        return visit;
    }
}
