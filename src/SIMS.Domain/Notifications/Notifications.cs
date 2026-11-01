using SIMS.Domain.Common;

namespace SIMS.Domain.Notifications;

public enum NotificationTarget
{
    AllClubs,
    SpecificClubs,
}

/// <summary>Thông báo gửi tới toàn bộ CLB hoặc danh sách CLB cụ thể.</summary>
public sealed class Notification : Entity
{
    private Notification()
    {
        Title = string.Empty;
        Content = string.Empty;
        Sender = string.Empty;
    }

    public string Title { get; private set; }

    public string Content { get; private set; }

    public NotificationTarget TargetType { get; private set; }

    public string Sender { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Notification Create(
        string title,
        string content,
        NotificationTarget targetType,
        string sender,
        DateTimeOffset now)
    {
        var actor = (sender ?? string.Empty).Trim();

        return new Notification
        {
            Title = RequireText(title, 255, "Tiêu đề"),
            Content = RequireContent(content),
            TargetType = targetType,
            Sender = actor.Length == 0 ? "system" : actor,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public static Notification Restore(
        Guid id,
        string title,
        string content,
        NotificationTarget targetType,
        string sender,
        bool isActive,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var notification = Create(title, content, targetType, sender, createdAtUtc);
        notification.Id = id;
        notification.IsActive = isActive;
        notification.UpdatedAtUtc = updatedAtUtc;

        return notification;
    }

    public void Update(
        string title,
        string content,
        NotificationTarget targetType,
        bool isActive,
        DateTimeOffset now)
    {
        Title = RequireText(title, 255, "Tiêu đề");
        Content = RequireContent(content);
        TargetType = targetType;
        IsActive = isActive;
        UpdatedAtUtc = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
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
            throw new DomainException("Nội dung thông báo là bắt buộc.");
        }

        if (text.Length > 5000)
        {
            throw new DomainException("Nội dung tối đa 5000 ký tự.");
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }
}

/// <summary>CLB nhận thông báo (khi target là SpecificClubs).</summary>
public sealed class NotificationTargetClub : Entity
{
    private NotificationTargetClub()
    {
    }

    public Guid NotificationId { get; private set; }

    public Guid ClubId { get; private set; }

    public static NotificationTargetClub Create(Guid notificationId, Guid clubId)
    {
        if (notificationId == Guid.Empty || clubId == Guid.Empty)
        {
            throw new DomainException("CLB nhận thông báo không hợp lệ.");
        }

        return new NotificationTargetClub { NotificationId = notificationId, ClubId = clubId };
    }

    public static NotificationTargetClub Restore(Guid id, Guid notificationId, Guid clubId)
    {
        var target = Create(notificationId, clubId);
        target.Id = id;

        return target;
    }
}

/// <summary>Đánh dấu người dùng đã đọc thông báo.</summary>
public sealed class NotificationRead : Entity
{
    private NotificationRead()
    {
    }

    public Guid NotificationId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset ReadAtUtc { get; private set; }

    public static NotificationRead Create(Guid notificationId, Guid userId, DateTimeOffset now)
    {
        if (notificationId == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainException("Lượt đọc thông báo không hợp lệ.");
        }

        return new NotificationRead
        {
            NotificationId = notificationId,
            UserId = userId,
            ReadAtUtc = now,
        };
    }

    public static NotificationRead Restore(
        Guid id,
        Guid notificationId,
        Guid userId,
        DateTimeOffset readAtUtc)
    {
        var read = Create(notificationId, userId, readAtUtc);
        read.Id = id;

        return read;
    }
}
