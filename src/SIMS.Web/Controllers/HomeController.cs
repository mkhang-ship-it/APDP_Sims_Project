using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Dashboard;
using SIMS.Application.LuckyDraws;
using SIMS.Application.Notifications;
using SIMS.Application.Settings;
using SIMS.Domain.Events;
using SIMS.Infrastructure.Storage;
using SIMS.Web.Models;

namespace SIMS.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IDashboardService _dashboardService;
    private readonly IClubRepository _clubs;
    private readonly IEventRepository _events;
    private readonly ILuckyDrawService _draws;
    private readonly ISettingsService _settings;
    private readonly StoragePaths _storagePaths;
    private readonly INotificationService _notifications;
    private readonly IUserRepository _users;

    public HomeController(
        ILogger<HomeController> logger,
        IDashboardService dashboardService,
        IClubRepository clubs,
        IEventRepository events,
        ILuckyDrawService draws,
        ISettingsService settings,
        StoragePaths storagePaths,
        INotificationService notifications,
        IUserRepository users)
    {
        _logger = logger;
        _dashboardService = dashboardService;
        _clubs = clubs;
        _events = events;
        _draws = draws;
        _settings = settings;
        _storagePaths = storagePaths;
        _notifications = notifications;
        _users = users;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var isAuthenticated = User.Identity?.IsAuthenticated == true;

        if (!isAuthenticated)
        {
            return View("PublicHome", await BuildPublicHomeAsync(cancellationToken));
        }

        var canSeeDashboard = User.IsInRole("Admin") || User.IsInRole("Faculty");

        var summary = await _dashboardService.GetAsync(cancellationToken);

        // Student không được xem số liệu toàn trường; vẫn giữ sự kiện sắp tới.
        if (!canSeeDashboard)
        {
            summary = summary with { Students = 0, Courses = 0, ActiveEnrolments = 0, GradedEnrolments = 0 };
        }

        var currentUser = await _users.FindByUsernameAsync(User.Identity?.Name ?? string.Empty, cancellationToken);
        var clubId = Guid.TryParse(User.FindFirstValue("club_id"), out var parsedClubId) ? parsedClubId : (Guid?)null;

        var model = new HomeViewModel
        {
            IsAuthenticated = true,
            CanSeeDashboard = canSeeDashboard,
            DisplayName = User.FindFirstValue(ClaimTypes.GivenName) ?? User.Identity?.Name ?? string.Empty,
            RoleName = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            StudentCount = summary.Students,
            CourseCount = summary.Courses,
            ActiveEnrolments = summary.ActiveEnrolments,
            GradedEnrolments = summary.GradedEnrolments,
            ClubCount = summary.Clubs,
            UpcomingEventCount = summary.UpcomingEvents,
            NextEvents = summary.NextEvents,
            UnreadNotifications = currentUser is null
                ? 0
                : await _notifications.UnreadCountAsync(currentUser.Id, clubId, cancellationToken),
            DataDirectory = _storagePaths.DataDirectory,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
        };

        return View(model);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    private async Task<HomeViewModel> BuildPublicHomeAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var clubs = (await _clubs.ListAsync(cancellationToken))
            .Where(club => club.IsActive)
            .OrderBy(club => club.Name, StringComparer.CurrentCulture)
            .ToArray();
        var clubNames = clubs.ToDictionary(club => club.Id, club => club.Name);

        var events = (await _events.ListAsync(cancellationToken))
            .Where(@event => @event.Status == EventStatus.Published && @event.EndsAtUtc >= now)
            .OrderBy(@event => @event.StartsAtUtc)
            .Take(6)
            .Select(@event => new PublicEventCard(
                @event.Title,
                @event.StartsAtUtc,
                @event.Location,
                clubNames.TryGetValue(@event.ClubId ?? Guid.Empty, out var name) ? name : "CLB"))
            .ToArray();

        var draws = (await _draws.ListAsync(null, cancellationToken))
            .Where(row => row.Draw.IsOpen(now))
            .OrderBy(row => row.Draw.StartAtUtc)
            .Take(6)
            .Select(row => new PublicDrawCard(
                row.Draw.Title,
                row.Draw.Slug,
                row.Draw.EndAtUtc,
                clubNames.TryGetValue(row.Draw.ClubId, out var name) ? name : "CLB"))
            .ToArray();

        return new HomeViewModel
        {
            IsAuthenticated = false,
            SiteTitle = await _settings.GetAsync(null, SettingKeys.SiteTitle, cancellationToken) ?? "SIMS",
            SiteDescription = await _settings.GetAsync(null, SettingKeys.SiteDescription, cancellationToken)
                ?? "Hệ thống quản lý sinh viên và câu lạc bộ.",
            PublicClubs = clubs
                .Take(8)
                .Select(club => new PublicClubCard(club.Code, club.Name))
                .ToArray(),
            PublicEvents = events,
            PublicDraws = draws,
        };
    }
}
