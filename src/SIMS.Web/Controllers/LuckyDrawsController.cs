using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.LuckyDraws;
using SIMS.Application.Security;
using SIMS.Domain.Clubs;
using SIMS.Domain.LuckyDraws;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module LUCKY DRAWS: chương trình bốc thăm, giải thưởng, quay số và người trúng.</summary>
[Authorize]
public class LuckyDrawsController : Controller
{
    private readonly ILuckyDrawService _draws;
    private readonly IClubRepository _clubs;

    public LuckyDrawsController(ILuckyDrawService draws, IClubRepository clubs)
    {
        _draws = draws;
        _clubs = clubs;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private bool IsAdmin => User.IsInRole("Admin");

    private Guid? ScopeClubId => IsAdmin ? null : CurrentClubId;

    private bool CanAccess(Guid clubId) => IsAdmin || CurrentClubId == clubId;

    [HttpGet]
    [RequirePermission(PermissionCatalog.LuckyDraws, "view")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var rows = await _draws.ListAsync(ScopeClubId, cancellationToken);
        var clubs = (await _clubs.ListAsync(cancellationToken))
            .ToDictionary(club => club.Id, club => club.Name);

        return View(new LuckyDrawPageModel { Items = rows, ClubNames = clubs });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.LuckyDraws, "create")]
    public async Task<IActionResult> Create(Guid? clubId, CancellationToken cancellationToken)
    {
        var clubs = await AvailableClubsAsync(cancellationToken);
        if (clubs.Count == 0)
        {
            TempData["Error"] = "Bạn chưa thuộc CLB nào để tạo chương trình.";
            return RedirectToAction(nameof(Index));
        }

        var selected = clubId is not null && clubs.Any(club => club.Id == clubId)
            ? clubId.Value
            : clubs[0].Id;

        return View(new LuckyDrawCreatePageModel
        {
            Form = new LuckyDrawFormModel { ClubId = selected },
            Clubs = ToOptions(clubs),
            IsAdmin = IsAdmin,
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.LuckyDraws, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LuckyDrawFormModel model, CancellationToken cancellationToken)
    {
        var clubs = await AvailableClubsAsync(cancellationToken);

        if (!CanAccess(model.ClubId) || !ModelState.IsValid)
        {
            TempData["Error"] = ModelState.IsValid
                ? "Bạn không có quyền với CLB này."
                : "Dữ liệu chương trình không hợp lệ.";

            return View(new LuckyDrawCreatePageModel
            {
                Form = model,
                Clubs = ToOptions(clubs),
                IsAdmin = IsAdmin,
            });
        }

        var result = await _draws.CreateAsync(
            model.ClubId,
            model.Title,
            model.SecurityCode,
            model.ParticipantNames,
            model.StartAt.ToUniversalTime(),
            model.EndAt.ToUniversalTime(),
            model.IsActive,
            CurrentActor,
            cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = ErrorOf(result);

            return View(new LuckyDrawCreatePageModel
            {
                Form = model,
                Clubs = ToOptions(clubs),
                IsAdmin = IsAdmin,
            });
        }

        TempData["Status"] = $"Đã tạo chương trình {result.Value!.Title}.";
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.LuckyDraws, "view")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var row = await _draws.GetAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        if (!CanAccess(row.Draw.ClubId))
        {
            return Forbid();
        }

        return View(new LuckyDrawDetailsPageModel
        {
            Row = row,
            Form = LuckyDrawFormModel.FromRow(row),
            Clubs = ToOptions(await AvailableClubsAsync(cancellationToken)),
            IsAdmin = IsAdmin,
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.LuckyDraws, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, LuckyDrawFormModel model, CancellationToken cancellationToken)
    {
        var row = await _draws.GetAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        if (!CanAccess(row.Draw.ClubId))
        {
            return Forbid();
        }

        var result = await _draws.UpdateAsync(
            id,
            model.Title,
            model.SecurityCode,
            model.ParticipantNames,
            model.StartAt.ToUniversalTime(),
            model.EndAt.ToUniversalTime(),
            model.IsActive,
            CurrentActor,
            cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật chương trình." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.LuckyDraws, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var row = await _draws.GetAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        if (!CanAccess(row.Draw.ClubId))
        {
            return Forbid();
        }

        var result = await _draws.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá chương trình." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.LuckyDraws, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPrize(
        Guid id,
        LuckyPrizeFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessDrawAsync(id, cancellationToken))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu giải thưởng không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _draws.AddPrizeAsync(
            id, model.Name, model.Quantity, model.IsSpecial, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã thêm giải thưởng." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.LuckyDraws, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePrize(
        Guid id,
        Guid prizeId,
        LuckyPrizeFormModel model,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessDrawAsync(id, cancellationToken))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu giải thưởng không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _draws.UpdatePrizeAsync(
            prizeId, model.Name, model.Quantity, model.IsSpecial, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật giải thưởng." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.LuckyDraws, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePrize(Guid id, Guid prizeId, CancellationToken cancellationToken)
    {
        if (!await CanAccessDrawAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var result = await _draws.DeletePrizeAsync(prizeId, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá giải thưởng." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.LuckyDraws, "draw")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Draw(Guid id, Guid prizeId, CancellationToken cancellationToken)
    {
        if (!await CanAccessDrawAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var result = await _draws.DrawAsync(id, prizeId, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] = result.IsSuccess
            ? $"🎉 {result.Value!.Participant} trúng {result.Value.PrizeName}!"
            : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.LuckyDraws, "draw")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearWinners(Guid id, CancellationToken cancellationToken)
    {
        if (!await CanAccessDrawAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var result = await _draws.ClearWinnersAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] = result.IsSuccess
            ? $"Đã xoá {result.Value} lượt trúng thưởng."
            : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> CanAccessDrawAsync(Guid drawId, CancellationToken cancellationToken)
    {
        var row = await _draws.GetAsync(drawId, cancellationToken);

        return row is not null && CanAccess(row.Draw.ClubId);
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
