namespace SIMS.Web.Models;

public sealed record PublicClubCard(string Code, string Name);

public sealed record PublicEventCard(
    string Title,
    DateTimeOffset StartsAtUtc,
    string Location,
    string ClubName);

public sealed record PublicDrawCard(
    string Title,
    string Slug,
    DateTimeOffset EndAtUtc,
    string ClubName);

/// <summary>Dữ liệu hiển thị cho trang chủ (dashboard counts + trang công khai).</summary>
public sealed class HomeViewModel
{
    public bool IsAuthenticated { get; init; }

    public bool CanSeeDashboard { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string RoleName { get; init; } = string.Empty;

    public int StudentCount { get; init; }

    public int CourseCount { get; init; }

    public int ActiveEnrolments { get; init; }

    public int GradedEnrolments { get; init; }

    public int ClubCount { get; init; }

    public int UpcomingEventCount { get; init; }

    public int UnreadNotifications { get; init; }

    public IReadOnlyList<SIMS.Application.Dashboard.DashboardEventRow> NextEvents { get; init; }
        = Array.Empty<SIMS.Application.Dashboard.DashboardEventRow>();

    public string DataDirectory { get; init; } = string.Empty;

    public string FrameworkDescription { get; init; } = string.Empty;

    public string EnvironmentName { get; init; } = string.Empty;

    public string SiteTitle { get; init; } = "SIMS";

    public string SiteDescription { get; init; } = string.Empty;

    public IReadOnlyList<PublicClubCard> PublicClubs { get; init; } = Array.Empty<PublicClubCard>();

    public IReadOnlyList<PublicEventCard> PublicEvents { get; init; } = Array.Empty<PublicEventCard>();

    public IReadOnlyList<PublicDrawCard> PublicDraws { get; init; } = Array.Empty<PublicDrawCard>();
}
