using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Checkin;
using SIMS.Application.Security;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Check-in sự kiện bằng token (nội dung QR) hoặc nhập tay; hỗ trợ quét QR bằng BarcodeDetector.</summary>
[Authorize]
public class CheckinController : Controller
{
    private readonly ICheckinService _checkinService;
    private readonly IEventRepository _events;
    private readonly IStudentReader _students;

    public CheckinController(
        ICheckinService checkinService,
        IEventRepository events,
        IStudentReader students)
    {
        _checkinService = checkinService;
        _events = events;
        _students = students;
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Checkin, "create")]
    public async Task<IActionResult> Index(string? code, CancellationToken cancellationToken)
    {
        ViewData["Code"] = code ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(code))
        {
            var @event = await _events.FindByCheckinTokenAsync(code, cancellationToken);
            ViewData["EventTitle"] = @event?.Title;
            ViewData["EventStatus"] = @event?.Status.ToString();
            ViewData["EventWindow"] = @event is null
                ? null
                : $"{@event.StartsAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} – {@event.EndsAtUtc.ToLocalTime():HH:mm}";
            ViewData["EventVenue"] = @event?.Venue is { } venue
                ? $"{venue.Latitude:0.#####}, {venue.Longitude:0.#####}"
                : null;
            ViewData["EventRequiresLocation"] = @event?.Venue is not null;
        }

        return View();
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Checkin, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        string token,
        string? studentCode,
        string method,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken)
    {
        var actor = User.Identity?.Name ?? "anonymous";
        var resolvedStudentCode = (studentCode ?? string.Empty).Trim();

        if (resolvedStudentCode.Length == 0)
        {
            // Vai trò Student: lấy hồ sơ liên kết với tài khoản đang đăng nhập.
            if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var student = await _students.FindByUserIdAsync(userId, cancellationToken);
                resolvedStudentCode = student?.StudentCode ?? string.Empty;
            }
        }

        if (resolvedStudentCode.Length == 0)
        {
            TempData["Error"] = "Không xác định được sinh viên. Hãy nhập MSSV.";
            return RedirectToAction(nameof(Index), new { code = token });
        }

        var result = await _checkinService.CheckInAsync(
            token,
            resolvedStudentCode,
            string.IsNullOrWhiteSpace(method) ? "Manual" : method,
            actor,
            latitude,
            longitude,
            cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không thể check-in.";
            return RedirectToAction(nameof(Index), new { code = token });
        }

        TempData["Status"] =
            $"Check-in thành công: {result.Value.Student.StudentCode} — {result.Value.Event.Title}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Checkin, "create")]
    public IActionResult Scanner() => View();
}
