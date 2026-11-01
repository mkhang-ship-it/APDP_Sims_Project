using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Events;
using SIMS.Application.Security;
using SIMS.Domain.Events;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module EVENTS: danh sách, lịch tháng, chi tiết + check-in, CRUD, công bố/huỷ, xoay token.</summary>
[Authorize]
public class EventsController : Controller
{
    private readonly IEventService _eventService;
    private readonly IEventCalendarService _calendarService;
    private readonly IAttendanceRepository _attendance;
    private readonly IStudentReader _students;
    private readonly IEventImageService _images;

    public EventsController(
        IEventService eventService,
        IEventCalendarService calendarService,
        IAttendanceRepository attendance,
        IStudentReader students,
        IEventImageService images)
    {
        _eventService = eventService;
        _calendarService = calendarService;
        _attendance = attendance;
        _students = students;
        _images = images;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private Guid? ScopeClubId => User.IsInRole("Admin") ? null : CurrentClubId;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Events, "view")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await _eventService.ListAsync(ScopeClubId, cancellationToken));

    [HttpGet]
    [RequirePermission(PermissionCatalog.Events, "view")]
    public async Task<IActionResult> Calendar(int? year, int? month, CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var targetYear = year ?? today.Year;
        var targetMonth = month ?? today.Month;

        if (targetMonth is < 1 or > 12)
        {
            targetMonth = today.Month;
        }

        var events = await _calendarService.GetByMonthAsync(targetYear, targetMonth, ScopeClubId, cancellationToken);

        ViewData["Year"] = targetYear;
        ViewData["Month"] = targetMonth;

        return View(events);
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Events, "view")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var @event = await _eventService.GetAsync(id, cancellationToken);
        if (@event is null)
        {
            return NotFound();
        }

        var rows = new List<AttendanceRow>();
        foreach (var attendance in await _attendance.ListByEventAsync(id, cancellationToken))
        {
            var student = await _students.FindByIdAsync(attendance.StudentId, cancellationToken);
            rows.Add(new AttendanceRow(
                attendance,
                student?.StudentCode ?? "?",
                student?.FullName ?? "(không tìm thấy)"));
        }

        return View(new EventDetailsViewModel
        {
            Event = @event,
            Attendance = rows,
            Images = await _images.ListAsync(id, cancellationToken),
            CheckinUrl = Url.Action("Index", "Checkin", new { code = @event.CheckinToken }, Request.Scheme) ?? string.Empty,
        });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Events, "create")]
    public IActionResult Create() => View(new EventFormModel());

    [HttpPost]
    [RequirePermission(PermissionCatalog.Events, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EventFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _eventService.CreateAsync(model.ToInput(), CurrentClubId, CurrentActor, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Status"] = $"Đã tạo sự kiện {result.Value.Code}.";
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Events, "edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var @event = await _eventService.GetAsync(id, cancellationToken);
        if (@event is null)
        {
            return NotFound();
        }

        return View(new EventFormModel
        {
            Code = @event.Code,
            Title = @event.Title,
            Description = @event.Description,
            StartsAt = @event.StartsAtUtc.ToLocalTime().DateTime,
            EndsAt = @event.EndsAtUtc.ToLocalTime().DateTime,
            Location = @event.Location,
            Capacity = @event.Capacity,
            VenueLatitude = @event.Venue?.Latitude,
            VenueLongitude = @event.Venue?.Longitude,
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Events, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EventFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _eventService.UpdateAsync(id, model.ToInput(), CurrentActor, cancellationToken);
        if (!result.IsSuccess)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Status"] = "Đã cập nhật sự kiện.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Events, "publish")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
        => ChangeStatusAsync(id, _eventService.PublishAsync, "Đã công bố sự kiện.", cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCatalog.Events, "publish")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
        => ChangeStatusAsync(id, _eventService.CancelAsync, "Đã huỷ sự kiện.", cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCatalog.Events, "publish")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
        => ChangeStatusAsync(id, _eventService.CompleteAsync, "Đã đánh dấu hoàn thành.", cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCatalog.Events, "checkin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RotateToken(Guid id, CancellationToken cancellationToken)
    {
        var result = await _eventService.RotateCheckinTokenAsync(id, CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không xoay được mã check-in.";
        }
        else
        {
            TempData["Status"] = "Đã tạo mã check-in mới.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> ChangeStatusAsync(
        Guid id,
        Func<Guid, string, CancellationToken, Task<Result>> action,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await action(id, CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không cập nhật được sự kiện.";
        }
        else
        {
            TempData["Status"] = successMessage;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Events, "edit")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024 + 4096)]
    public async Task<IActionResult> UploadImage(
        Guid id,
        string? caption,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var @event = await _eventService.GetAsync(id, cancellationToken);
        if (@event is null)
        {
            return NotFound();
        }

        if (!CanAccessEvent(@event))
        {
            return Forbid();
        }

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Chọn ảnh cần tải lên.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (file.Length > 10 * 1024 * 1024)
        {
            TempData["Error"] = "Ảnh tối đa 10 MB.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var stream = file.OpenReadStream();
        var result = await _images.AddAsync(
            id, caption, stream, file.FileName, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã thêm ảnh sự kiện." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Events, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        var @event = await _eventService.GetAsync(id, cancellationToken);
        if (@event is null)
        {
            return NotFound();
        }

        if (!CanAccessEvent(@event))
        {
            return Forbid();
        }

        var result = await _images.DeleteAsync(imageId, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá ảnh." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Events, "view")]
    public async Task<IActionResult> Image(Guid id, CancellationToken cancellationToken)
    {
        var image = await _images.GetAsync(id, cancellationToken);
        if (image is null)
        {
            return NotFound();
        }

        var @event = await _eventService.GetAsync(image.EventId, cancellationToken);
        if (@event is null || !CanAccessEvent(@event))
        {
            return Forbid();
        }

        var stream = _images.OpenFile(image);
        if (stream is null)
        {
            return NotFound();
        }

        var extension = Path.GetExtension(image.FilePath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };

        return File(stream, contentType);
    }

    private bool CanAccessEvent(Event @event)
        => User.IsInRole("Admin") || @event.ClubId is null || @event.ClubId == CurrentClubId;

    private static string ErrorOf(Result result)
        => result.Errors.FirstOrDefault()?.Message ?? "Thao tác thất bại.";

    private void AddErrors(Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Field, error.Message);
        }
    }
}
