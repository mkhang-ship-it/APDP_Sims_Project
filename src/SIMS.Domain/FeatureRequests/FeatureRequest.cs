using SIMS.Domain.Common;

namespace SIMS.Domain.FeatureRequests;

public enum FeatureRequestStatus
{
    Open,
    InProgress,
    Closed,
}

/// <summary>Đề xuất tính năng/góp ý của thành viên (có thể gắn CLB hoặc toàn hệ thống).</summary>
public sealed class FeatureRequest : Entity
{
    private FeatureRequest()
    {
        Title = string.Empty;
        Content = string.Empty;
        CreatedBy = string.Empty;
        Response = string.Empty;
    }

    public Guid? ClubId { get; private set; }

    public string Title { get; private set; }

    public string Content { get; private set; }

    public string CreatedBy { get; private set; }

    public FeatureRequestStatus Status { get; private set; }

    public string Response { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static FeatureRequest Create(
        Guid? clubId,
        string title,
        string content,
        string createdBy,
        DateTimeOffset now)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        var actor = (createdBy ?? string.Empty).Trim();

        return new FeatureRequest
        {
            ClubId = clubId,
            Title = RequireText(title, 200, "Tiêu đề"),
            Content = RequireContent(content),
            CreatedBy = actor.Length == 0 ? "system" : actor,
            Status = FeatureRequestStatus.Open,
            Response = string.Empty,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public static FeatureRequest Restore(
        Guid id,
        Guid? clubId,
        string title,
        string content,
        string createdBy,
        FeatureRequestStatus status,
        string? response,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var request = Create(clubId, title, content, createdBy, createdAtUtc);
        request.Id = id;
        request.Status = status;
        request.Response = OptionalText(response, 2000, "Phản hồi");
        request.UpdatedAtUtc = updatedAtUtc;

        return request;
    }

    public void UpdateStatus(FeatureRequestStatus status, string? response, DateTimeOffset now)
    {
        Status = status;
        Response = OptionalText(response, 2000, "Phản hồi");
        UpdatedAtUtc = now;
    }

    public void UpdateContent(string title, string content, DateTimeOffset now)
    {
        if (Status != FeatureRequestStatus.Open)
        {
            throw new DomainException("Chỉ có thể sửa đề xuất đang mở.");
        }

        Title = RequireText(title, 200, "Tiêu đề");
        Content = RequireContent(content);
        UpdatedAtUtc = now;
    }

    private static string RequireText(string value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new DomainException($"{fieldName} là bắt buộc.");
        }

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text;
    }

    private static string RequireContent(string value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new DomainException("Nội dung là bắt buộc.");
        }

        if (text.Length > 4000)
        {
            throw new DomainException("Nội dung tối đa 4000 ký tự.");
        }

        return text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    private static string OptionalText(string? value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }
}
