using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.FeatureRequests;
using SIMS.Application.Security;
using SIMS.Domain.Clubs;
using SIMS.Domain.FeatureRequests;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module FEATURE REQUESTS: thành viên gửi đề xuất, nhân viên cập nhật trạng thái/phản hồi.</summary>
[Authorize]
public class FeatureRequestsController : Controller
{
    private readonly IFeatureRequestService _requests;
    private readonly IClubRepository _clubs;
    private readonly IAuthorizationService _authorization;

    public FeatureRequestsController(
        IFeatureRequestService requests,
        IClubRepository clubs,
        IAuthorizationService authorization)
    {
        _requests = requests;
        _clubs = clubs;
        _authorization = authorization;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet]
    [RequirePermission(PermissionCatalog.FeatureRequests, "view")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var canManage = (await _authorization
            .AuthorizeAsync(User, HttpContext, "perm:feature_requests:manage")).Succeeded;
        var clubs = await _clubs.ListAsync(cancellationToken);

        var items = canManage
            ? await _requests.ListForStaffAsync(IsAdmin ? null : CurrentClubId, cancellationToken)
            : await _requests.ListMineAsync(CurrentActor, cancellationToken);

        return View(new FeatureRequestPageModel
        {
            Items = items,
            CanManage = canManage,
            IsAdmin = IsAdmin,
            Form = new FeatureRequestFormModel { ClubId = CurrentClubId },
            Clubs = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray(),
            ClubNames = clubs.ToDictionary(club => club.Id, club => club.Name),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.FeatureRequests, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FeatureRequestFormModel model, CancellationToken cancellationToken)
    {
        var clubId = IsAdmin ? model.ClubId : CurrentClubId;

        var result = await _requests.CreateAsync(
            clubId, model.Title, model.Content, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] = result.IsSuccess
            ? "Đã gửi đề xuất."
            : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.FeatureRequests, "manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(
        Guid id,
        FeatureRequestStatus status,
        string? response,
        CancellationToken cancellationToken)
    {
        var request = await _requests.GetAsync(id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        if (!CanManage(request))
        {
            return Forbid();
        }

        var result = await _requests.UpdateStatusAsync(
            id, status, response, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật đề xuất." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.FeatureRequests, "view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var request = await _requests.GetAsync(id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        var isOwner = string.Equals(request.CreatedBy, CurrentActor, StringComparison.OrdinalIgnoreCase);
        if (!CanManage(request) && !(isOwner && request.Status == FeatureRequestStatus.Open))
        {
            return Forbid();
        }

        var result = await _requests.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá đề xuất." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    private bool CanManage(FeatureRequest request)
    {
        if (IsAdmin)
        {
            return true;
        }

        return CurrentClubId is not null && request.ClubId == CurrentClubId;
    }

    private static string ErrorOf(Result result)
        => result.Errors.FirstOrDefault()?.Message ?? "Thao tác thất bại.";
}
