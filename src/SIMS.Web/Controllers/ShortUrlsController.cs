using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Security;
using SIMS.Application.ShortUrls;
using SIMS.Domain.Clubs;
using SIMS.Domain.ShortUrls;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module SHORT URLS: rút gọn liên kết theo CLB + chuyển hướng công khai `/s/{code}`.</summary>
[Authorize]
public class ShortUrlsController : Controller
{
    private readonly IShortUrlService _urls;
    private readonly IClubRepository _clubs;

    public ShortUrlsController(IShortUrlService urls, IClubRepository clubs)
    {
        _urls = urls;
        _clubs = clubs;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private bool IsAdmin => User.IsInRole("Admin");

    private Guid? ScopeClubId => IsAdmin ? null : CurrentClubId;

    private bool CanAccess(Guid clubId) => IsAdmin || CurrentClubId == clubId;

    [HttpGet]
    [RequirePermission(PermissionCatalog.ShortUrls, "view")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var clubs = await AvailableClubsAsync(cancellationToken);
        var rows = await _urls.ListAsync(ScopeClubId, cancellationToken);

        return View(new ShortUrlPageModel
        {
            Items = rows,
            ClubNames = (await _clubs.ListAsync(cancellationToken))
                .ToDictionary(club => club.Id, club => club.Name),
            Form = new ShortUrlFormModel
            {
                ClubId = CurrentClubId ?? clubs.FirstOrDefault()?.Id ?? Guid.Empty,
            },
            Clubs = ToOptions(clubs),
            IsAdmin = IsAdmin,
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.ShortUrls, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShortUrlFormModel model, CancellationToken cancellationToken)
    {
        if (!CanAccess(model.ClubId))
        {
            return Forbid();
        }

        var result = await _urls.CreateAsync(
            model.ClubId,
            model.TargetUrl,
            model.Title,
            model.ExpiresAt,
            CurrentActor,
            cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] = result.IsSuccess
            ? $"Đã tạo liên kết /s/{result.Value!.Code}."
            : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.ShortUrls, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        Guid id,
        string? targetUrl,
        string? title,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        var url = await _urls.GetAsync(id, cancellationToken);
        if (url is null)
        {
            return NotFound();
        }

        if (!CanAccess(url.ClubId))
        {
            return Forbid();
        }

        var result = await _urls.UpdateAsync(
            id, targetUrl ?? string.Empty, title, expiresAt, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật liên kết." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.ShortUrls, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var url = await _urls.GetAsync(id, cancellationToken);
        if (url is null)
        {
            return NotFound();
        }

        if (!CanAccess(url.ClubId))
        {
            return Forbid();
        }

        var result = await _urls.SetActiveAsync(id, isActive, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? (isActive ? "Đã bật liên kết." : "Đã tạm dừng liên kết.") : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.ShortUrls, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var url = await _urls.GetAsync(id, cancellationToken);
        if (url is null)
        {
            return NotFound();
        }

        if (!CanAccess(url.ClubId))
        {
            return Forbid();
        }

        var result = await _urls.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá liên kết." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/s/{code}")]
    [AllowAnonymous]
    public async Task<IActionResult> Go(string code, CancellationToken cancellationToken)
    {
        var result = await _urls.ResolveAsync(code, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            var message = result.Errors.FirstOrDefault()?.Message ?? "Liên kết không tồn tại.";
            return message.Contains("hết hạn", StringComparison.OrdinalIgnoreCase)
                ? StatusCode(410, message)
                : NotFound(message);
        }

        await _urls.RecordVisitAsync(
            result.Value.Id,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);

        return Redirect(result.Value.TargetUrl);
    }

    private async Task<IReadOnlyList<Club>> AvailableClubsAsync(CancellationToken cancellationToken)
    {
        var clubs = await _clubs.ListAsync(cancellationToken);

        return IsAdmin
            ? clubs
            : clubs.Where(club => club.Id == CurrentClubId).ToArray();
    }

    private static IReadOnlyList<ClubOption> ToOptions(IReadOnlyList<Club> clubs)
        => clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray();

    private static string ErrorOf(Result result)
        => result.Errors.FirstOrDefault()?.Message ?? "Thao tác thất bại.";
}
