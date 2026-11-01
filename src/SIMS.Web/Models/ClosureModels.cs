using System.ComponentModel.DataAnnotations;
using SIMS.Application.ShortUrls;
using SIMS.Domain.FeatureRequests;

namespace SIMS.Web.Models;

public sealed class ProfilePageModel
{
    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? Email { get; init; }
}

public sealed class ShortUrlFormModel
{
    public Guid ClubId { get; set; }

    [Required(ErrorMessage = "Đích đến là bắt buộc.")]
    [StringLength(2000)]
    public string TargetUrl { get; set; } = string.Empty;

    [StringLength(255)]
    public string? Title { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class ShortUrlPageModel
{
    public IReadOnlyList<ShortUrlRow> Items { get; init; } = Array.Empty<ShortUrlRow>();

    public IReadOnlyDictionary<Guid, string> ClubNames { get; init; } = new Dictionary<Guid, string>();

    public ShortUrlFormModel Form { get; init; } = new();

    public IReadOnlyList<ClubOption> Clubs { get; init; } = Array.Empty<ClubOption>();

    public bool IsAdmin { get; init; }
}

public sealed class FeatureRequestFormModel
{
    public Guid? ClubId { get; set; }

    [Required(ErrorMessage = "Tiêu đề là bắt buộc.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nội dung là bắt buộc.")]
    [StringLength(4000)]
    public string Content { get; set; } = string.Empty;
}

public sealed class FeatureRequestPageModel
{
    public IReadOnlyList<FeatureRequest> Items { get; init; } = Array.Empty<FeatureRequest>();

    public bool CanManage { get; init; }

    public bool IsAdmin { get; init; }

    public FeatureRequestFormModel Form { get; init; } = new();

    public IReadOnlyList<ClubOption> Clubs { get; init; } = Array.Empty<ClubOption>();

    public IReadOnlyDictionary<Guid, string> ClubNames { get; init; } = new Dictionary<Guid, string>();
}
