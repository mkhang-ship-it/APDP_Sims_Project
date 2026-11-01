using System.ComponentModel.DataAnnotations;
using SIMS.Application.LuckyDraws;
using SIMS.Application.Notifications;
using SIMS.Domain.Settings;

namespace SIMS.Web.Models;

public sealed class LuckyDrawFormModel
{
    public Guid ClubId { get; set; }

    [Required(ErrorMessage = "Tên chương trình là bắt buộc.")]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [StringLength(20)]
    public string? SecurityCode { get; set; }

    /// <summary>Mỗi dòng một tên người tham gia.</summary>
    public string? Participants { get; set; }

    public DateTimeOffset StartAt { get; set; } = DateTimeOffset.Now.AddHours(-1);

    public DateTimeOffset EndAt { get; set; } = DateTimeOffset.Now.AddDays(30);

    public bool IsActive { get; set; } = true;

    public IReadOnlyList<string> ParticipantNames
        => (Participants ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static LuckyDrawFormModel FromRow(LuckyDrawRow row)
        => new()
        {
            ClubId = row.Draw.ClubId,
            Title = row.Draw.Title,
            SecurityCode = row.Draw.SecurityCode,
            Participants = string.Join('\n', row.Draw.ParticipantList),
            StartAt = row.Draw.StartAtUtc.ToLocalTime(),
            EndAt = row.Draw.EndAtUtc.ToLocalTime(),
            IsActive = row.Draw.IsActive,
        };
}

public sealed class LuckyDrawPageModel
{
    public IReadOnlyList<LuckyDrawRow> Items { get; init; } = Array.Empty<LuckyDrawRow>();

    public IReadOnlyDictionary<Guid, string> ClubNames { get; init; } = new Dictionary<Guid, string>();
}

public sealed class LuckyDrawCreatePageModel
{
    public LuckyDrawFormModel Form { get; init; } = new();

    public IReadOnlyList<ClubOption> Clubs { get; init; } = Array.Empty<ClubOption>();

    public bool IsAdmin { get; init; }
}

public sealed class LuckyDrawDetailsPageModel
{
    public LuckyDrawRow Row { get; init; } = null!;

    public LuckyDrawFormModel Form { get; init; } = new();

    public IReadOnlyList<ClubOption> Clubs { get; init; } = Array.Empty<ClubOption>();

    public bool IsAdmin { get; init; }
}

public sealed class LuckyPrizeFormModel
{
    [Required(ErrorMessage = "Tên giải là bắt buộc.")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 1000, ErrorMessage = "Số lượng từ 1 đến 1000.")]
    public int Quantity { get; set; } = 1;

    public bool IsSpecial { get; set; }
}

public sealed class PublicDrawPageModel
{
    public PublicDrawView Draw { get; init; } = null!;

    public bool Verified { get; init; }

    public string? SecurityError { get; init; }
}

public sealed class NotificationFormModel
{
    [Required(ErrorMessage = "Tiêu đề là bắt buộc.")]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nội dung là bắt buộc.")]
    [StringLength(5000)]
    public string Content { get; set; } = string.Empty;

    public bool TargetAllClubs { get; set; }

    public List<Guid> ClubIds { get; set; } = new();

    public bool IsActive { get; set; } = true;
}

public sealed class NotificationPageModel
{
    public IReadOnlyList<NotificationRow> Items { get; init; } = Array.Empty<NotificationRow>();

    public IReadOnlyList<ClubOption> Clubs { get; init; } = Array.Empty<ClubOption>();

    public IReadOnlyDictionary<Guid, string> ClubNames { get; init; } = new Dictionary<Guid, string>();

    public NotificationFormModel Form { get; init; } = new();

    public bool IsAdmin { get; init; }
}

public sealed class NotificationInboxPageModel
{
    public IReadOnlyList<NotificationRow> Items { get; init; } = Array.Empty<NotificationRow>();

    public int UnreadCount { get; init; }
}

public sealed class SettingsPageModel
{
    public bool IsAdmin { get; init; }

    public Guid? ClubId { get; init; }

    public string ClubName { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyList<ClubOption> Clubs { get; init; } = Array.Empty<ClubOption>();

    public string Value(string key)
        => Values.TryGetValue(key, out var value) ? value : string.Empty;
}
