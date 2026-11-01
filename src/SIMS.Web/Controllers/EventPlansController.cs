using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.EventPlans;
using SIMS.Application.Events;
using SIMS.Application.Security;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module EVENT PLANS: mục tiêu, hành động, ngân sách, thiết bị, timeline + export CSV.</summary>
[Authorize]
public class EventPlansController : Controller
{
    private readonly IEventPlanService _planService;
    private readonly IEventService _eventService;
    private readonly IAttendanceRepository _attendance;

    public EventPlansController(
        IEventPlanService planService,
        IEventService eventService,
        IAttendanceRepository attendance)
    {
        _planService = planService;
        _eventService = eventService;
        _attendance = attendance;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    [HttpGet]
    [RequirePermission(PermissionCatalog.EventPlans, "view")]
    public async Task<IActionResult> Index(Guid eventId, CancellationToken cancellationToken)
    {
        var @event = await _eventService.GetAsync(eventId, cancellationToken);
        if (@event is null)
        {
            return NotFound();
        }

        var plan = await _planService.GetOrCreateAsync(eventId, CurrentActor, cancellationToken);
        if (!plan.IsSuccess || plan.Value is null)
        {
            TempData["Error"] = plan.Errors.FirstOrDefault()?.Message ?? "Không tải được kế hoạch.";
            return View(new EventPlanPageViewModel { Event = @event });
        }

        return View(new EventPlanPageViewModel { Event = @event, Plan = plan.Value });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.EventPlans, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Details(
        Guid eventId,
        string? summary,
        string? preparedBy,
        DateOnly? implementationDate,
        string? approvedBy,
        string? participants,
        string? description,
        string? responsibleOrganizer,
        string? advisorCoordinator,
        CancellationToken cancellationToken)
    {
        var input = new EventPlanDetailsInput(
            summary ?? string.Empty,
            preparedBy,
            implementationDate,
            approvedBy,
            participants,
            description,
            responsibleOrganizer,
            advisorCoordinator);

        var result = await _planService.UpdateDetailsAsync(eventId, input, CurrentActor, cancellationToken);
        return RedirectWithResult(result, "Đã lưu thông tin kế hoạch.", eventId);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.EventPlans, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Summary(Guid eventId, string? summary, CancellationToken cancellationToken)
    {
        var result = await _planService.UpdateSummaryAsync(eventId, summary ?? string.Empty, CurrentActor, cancellationToken);
        return RedirectWithResult(result, "Đã lưu tóm tắt kế hoạch.", eventId);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.EventPlans, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddObjective(Guid eventId, string? content, CancellationToken cancellationToken)
    {
        var result = await _planService.AddObjectiveAsync(eventId, content ?? string.Empty, CurrentActor, cancellationToken);
        return RedirectWithResult(result, "Đã thêm mục tiêu.", eventId);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.EventPlans, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAction(
        Guid eventId, string? content, string? owner, DateOnly? dueDate, CancellationToken cancellationToken)
    {
        var result = await _planService.AddActionAsync(
            eventId, content ?? string.Empty, owner ?? string.Empty, dueDate, CurrentActor, cancellationToken);

        return RedirectWithResult(result, "Đã thêm hành động.", eventId);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.EventPlans, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBudget(
        Guid eventId, string? itemName, int quantity, decimal unitPrice, CancellationToken cancellationToken)
    {
        var result = await _planService.AddBudgetItemAsync(
            eventId, itemName ?? string.Empty, quantity, unitPrice, CurrentActor, cancellationToken);

        return RedirectWithResult(result, "Đã thêm dòng ngân sách.", eventId);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.EventPlans, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEquipment(
        Guid eventId, string? name, int quantity, string? note, CancellationToken cancellationToken)
    {
        var result = await _planService.AddEquipmentAsync(
            eventId, name ?? string.Empty, quantity, note, CurrentActor, cancellationToken);

        return RedirectWithResult(result, "Đã thêm thiết bị.", eventId);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.EventPlans, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTimeline(
        Guid eventId, string? timeLabel, string? activity, CancellationToken cancellationToken)
    {
        var result = await _planService.AddTimelineAsync(
            eventId, timeLabel ?? string.Empty, activity ?? string.Empty, CurrentActor, cancellationToken);

        return RedirectWithResult(result, "Đã thêm mốc thời gian.", eventId);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.EventPlans, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(
        Guid eventId, string section, Guid itemId, CancellationToken cancellationToken)
    {
        var result = section switch
        {
            "objective" => await _planService.RemoveObjectiveAsync(eventId, itemId, CurrentActor, cancellationToken),
            "action" => await _planService.RemoveActionAsync(eventId, itemId, CurrentActor, cancellationToken),
            "budget" => await _planService.RemoveBudgetItemAsync(eventId, itemId, CurrentActor, cancellationToken),
            "equipment" => await _planService.RemoveEquipmentAsync(eventId, itemId, CurrentActor, cancellationToken),
            "timeline" => await _planService.RemoveTimelineAsync(eventId, itemId, CurrentActor, cancellationToken),
            _ => SIMS.Application.Common.Result.Fail(
                new SIMS.Application.Validation.ValidationError(string.Empty, "Mục không hợp lệ.")),
        };

        return RedirectWithResult(result, "Đã xoá mục.", eventId);
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.EventPlans, "view")]
    public async Task<IActionResult> Export(Guid eventId, string? format, CancellationToken cancellationToken)
    {
        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            var workbook = await _planService.ExportXlsxAsync(eventId, cancellationToken);
            if (!workbook.IsSuccess || workbook.Value is null)
            {
                TempData["Error"] = workbook.Errors.FirstOrDefault()?.Message ?? "Không xuất được kế hoạch.";
                return RedirectToAction(nameof(Index), new { eventId });
            }

            return File(
                workbook.Value,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"event-plan-{eventId:N}.xlsx");
        }

        var result = await _planService.ExportCsvAsync(eventId, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không xuất được kế hoạch.";
            return RedirectToAction(nameof(Index), new { eventId });
        }

        // CSV UTF-8 có BOM để Excel mở đúng tiếng Việt.
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = System.Text.Encoding.UTF8.GetBytes(result.Value);

        return File(bom.Concat(body).ToArray(), "text/csv", $"event-plan-{eventId:N}.csv");
    }

    private IActionResult RedirectWithResult(SIMS.Application.Common.Result result, string success, Guid eventId)
    {
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? success : result.Errors.FirstOrDefault()?.Message ?? "Thao tác thất bại.";

        return RedirectToAction(nameof(Index), new { eventId });
    }
}
