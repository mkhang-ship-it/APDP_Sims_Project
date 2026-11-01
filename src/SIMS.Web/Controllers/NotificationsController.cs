using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Notifications;
using SIMS.Application.Security;
using SIMS.Domain.Clubs;
using SIMS.Domain.Notifications;
using SIMS.Domain.Security;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module NOTIFICATIONS: quản lý thông báo theo CLB + hộp thư/đánh dấu đã đọc của người dùng.</summary>
[Authorize]
public class NotificationsController : Controller
{
    private readonly INotificationService _notifications;
    private readonly IClubRepository _clubs;
    private readonly IUserRepository _users;

    public NotificationsController(
        INotificationService notifications,
        IClubRepository clubs,
        IUserRepository users)
    {
        _notifications = notifications;
        _clubs = clubs;
        _users = users;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet]
    [RequirePermission(PermissionCatalog.Notifications, "view")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var rows = await _notifications.ListForManagementAsync(
            IsAdmin ? null : CurrentClubId, cancellationToken);
        var clubs = await _clubs.ListAsync(cancellationToken);

        return View(new NotificationPageModel
        {
            Items = rows,
            Clubs = ToOptions(clubs),
            ClubNames = clubs.ToDictionary(club => club.Id, club => club.Name),
            Form = new NotificationFormModel
            {
                ClubIds = CurrentClubId is null ? new List<Guid>() : new List<Guid> { CurrentClubId.Value },
                TargetAllClubs = false,
            },
            IsAdmin = IsAdmin,
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Notifications, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NotificationFormModel model, CancellationToken cancellationToken)
    {
        var (targetAll, clubIds) = ResolveTargets(model);

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu thông báo không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _notifications.CreateAsync(
            model.Title,
            model.Content,
            targetAll,
            clubIds,
            CurrentActor,
            CurrentActor,
            cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã tạo thông báo." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Notifications, "edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetAsync(id, cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        if (!await CanManageAsync(notification, cancellationToken))
        {
            return Forbid();
        }

        var rows = await _notifications.ListForManagementAsync(
            IsAdmin ? null : CurrentClubId, cancellationToken);
        var row = rows.FirstOrDefault(item => item.Notification.Id == id);
        var clubs = await _clubs.ListAsync(cancellationToken);

        return View(new NotificationPageModel
        {
            Items = rows,
            Clubs = ToOptions(clubs),
            ClubNames = clubs.ToDictionary(club => club.Id, club => club.Name),
            IsAdmin = IsAdmin,
            Form = new NotificationFormModel
            {
                Title = notification.Title,
                Content = notification.Content,
                TargetAllClubs = notification.TargetType == NotificationTarget.AllClubs,
                ClubIds = row?.TargetClubIds.ToList() ?? new List<Guid>(),
                IsActive = notification.IsActive,
            },
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Notifications, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        NotificationFormModel model,
        CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetAsync(id, cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        if (!await CanManageAsync(notification, cancellationToken))
        {
            return Forbid();
        }

        var (targetAll, clubIds) = ResolveTargets(model);
        var result = await _notifications.UpdateAsync(
            id,
            model.Title,
            model.Content,
            targetAll,
            clubIds,
            model.IsActive,
            CurrentActor,
            cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật thông báo." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Notifications, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetAsync(id, cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        if (!await CanManageAsync(notification, cancellationToken))
        {
            return Forbid();
        }

        var result = await _notifications.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá thông báo." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Notifications, "inbox")]
    public async Task<IActionResult> Inbox(CancellationToken cancellationToken)
    {
        var user = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        var rows = await _notifications.InboxAsync(user.Id, CurrentClubId, cancellationToken);

        return View(new NotificationInboxPageModel
        {
            Items = rows,
            UnreadCount = rows.Count(row => !row.IsRead),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Notifications, "inbox")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var user = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        await _notifications.MarkReadAsync(id, user.Id, cancellationToken);

        return RedirectToAction(nameof(Inbox));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Notifications, "inbox")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var user = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        var result = await _notifications.MarkAllReadAsync(user.Id, CurrentClubId, cancellationToken);
        TempData["Status"] = $"Đã đánh dấu {result.Value} thông báo là đã đọc.";

        return RedirectToAction(nameof(Inbox));
    }

    private (bool TargetAll, IReadOnlyCollection<Guid> ClubIds) ResolveTargets(NotificationFormModel model)
    {
        if (IsAdmin)
        {
            return model.TargetAllClubs
                ? (true, Array.Empty<Guid>())
                : (false, model.ClubIds);
        }

        // Nhân viên CLB chỉ gửi cho CLB hiện tại.
        return CurrentClubId is null
            ? (false, Array.Empty<Guid>())
            : (false, new[] { CurrentClubId.Value });
    }

    private async Task<bool> CanManageAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (IsAdmin)
        {
            return true;
        }

        if (CurrentClubId is null)
        {
            return false;
        }

        var rows = await _notifications.ListForManagementAsync(CurrentClubId, cancellationToken);
        var row = rows.FirstOrDefault(item => item.Notification.Id == notification.Id);

        return row is not null &&
               row.Notification.TargetType == NotificationTarget.SpecificClubs &&
               row.TargetClubIds.Contains(CurrentClubId.Value);
    }

    private static IReadOnlyList<ClubOption> ToOptions(IReadOnlyList<Club> clubs)
        => clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray();

    private static string ErrorOf(Result result)
        => result.Errors.FirstOrDefault()?.Message ?? "Thao tác thất bại.";
}
