using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Awards;
using SIMS.Application.Common;
using SIMS.Application.Security;
using SIMS.Domain.Awards;
using SIMS.Domain.Clubs;
using SIMS.Domain.Security;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module AWARDS (khen thưởng): giải thưởng + người nhận + chứng nhận đính kèm.</summary>
[Authorize]
public class AwardsController : Controller
{
    private const long MaxAttachmentBytes = 10 * 1024 * 1024;

    private readonly IAwardService _awards;
    private readonly IClubRepository _clubs;
    private readonly IClubMembershipRepository _memberships;
    private readonly IUserRepository _users;

    public AwardsController(
        IAwardService awards,
        IClubRepository clubs,
        IClubMembershipRepository memberships,
        IUserRepository users)
    {
        _awards = awards;
        _clubs = clubs;
        _memberships = memberships;
        _users = users;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private bool IsAdmin => User.IsInRole("Admin");

    private bool CanAccess(Guid clubId) => IsAdmin || CurrentClubId == clubId;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Awards, "view")]
    public async Task<IActionResult> Index(Guid? clubId, CancellationToken cancellationToken)
    {
        var (selected, clubs) = await ResolveClubAsync(clubId, cancellationToken);
        var items = await _awards.ListAsync(
            selected == Guid.Empty ? null : selected, cancellationToken);

        ViewData["Clubs"] = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray();
        ViewData["SelectedClubId"] = selected;
        ViewData["IsAdmin"] = IsAdmin;

        return View(new AwardPageModel
        {
            Items = items,
            UserNames = await UserNamesAsync(cancellationToken),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Awards, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid clubId,
        AwardFormModel model,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(clubId) || !ModelState.IsValid)
        {
            TempData["Error"] = ModelState.IsValid
                ? "Bạn không có quyền với CLB này."
                : "Dữ liệu giải thưởng không hợp lệ.";
            return RedirectToAction(nameof(Index), new { clubId });
        }

        var result = await _awards.CreateAsync(
            clubId, model.Title, model.Period, model.AwardDate, model.Status,
            model.Notes, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? $"Đã tạo giải thưởng {result.Value!.Code}." : ErrorOf(result);

        return RedirectToAction(nameof(Index), new { clubId });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Awards, "view")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var award = await _awards.GetAsync(id, cancellationToken);
        if (award is null)
        {
            return NotFound();
        }

        if (!CanAccess(award.ClubId))
        {
            return Forbid();
        }

        var recipients = await _awards.ListRecipientsAsync(id, cancellationToken);
        var members = await ListMembersAsync(award.ClubId, cancellationToken);
        var taken = recipients.Select(recipient => recipient.UserId).ToHashSet();

        return View(new AwardDetailsPageModel
        {
            Award = award,
            Recipients = recipients,
            AvailableMembers = members.Where(member => !taken.Contains(member.Id)).ToArray(),
            UserNames = await UserNamesAsync(cancellationToken),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Awards, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        AwardFormModel model,
        CancellationToken cancellationToken)
    {
        var award = await _awards.GetAsync(id, cancellationToken);
        if (award is null)
        {
            return NotFound();
        }

        if (!CanAccess(award.ClubId))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu giải thưởng không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _awards.UpdateAsync(
            id, model.Title, model.Period, model.AwardDate, model.Status,
            model.Notes, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật giải thưởng." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Awards, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var award = await _awards.GetAsync(id, cancellationToken);
        if (award is null)
        {
            return NotFound();
        }

        if (!CanAccess(award.ClubId))
        {
            return Forbid();
        }

        var result = await _awards.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá giải thưởng." : ErrorOf(result);

        return RedirectToAction(nameof(Index), new { clubId = award.ClubId });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Awards, "create")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAttachmentBytes + 4096)]
    public async Task<IActionResult> AddRecipient(
        Guid id,
        Guid userId,
        IFormFile? attachment,
        CancellationToken cancellationToken)
    {
        var award = await _awards.GetAsync(id, cancellationToken);
        if (award is null)
        {
            return NotFound();
        }

        if (!CanAccess(award.ClubId))
        {
            return Forbid();
        }

        if (attachment is not null && attachment.Length > MaxAttachmentBytes)
        {
            TempData["Error"] = "Tệp chứng nhận tối đa 10 MB.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var members = await ListMembersAsync(award.ClubId, cancellationToken);
        if (members.All(member => member.Id != userId))
        {
            TempData["Error"] = "Thành viên không thuộc CLB của giải thưởng.";
            return RedirectToAction(nameof(Details), new { id });
        }

        Stream? stream = attachment is { Length: > 0 } ? attachment.OpenReadStream() : null;
        Result<AwardRecipient> result;

        try
        {
            result = await _awards.AddRecipientAsync(
                id,
                userId,
                stream,
                attachment is { Length: > 0 } ? attachment.FileName : null,
                CurrentActor,
                cancellationToken);
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }
        }

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã thêm người nhận." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Awards, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRecipient(
        Guid id,
        Guid recipientId,
        CancellationToken cancellationToken)
    {
        var award = await _awards.GetAsync(id, cancellationToken);
        if (award is null)
        {
            return NotFound();
        }

        if (!CanAccess(award.ClubId))
        {
            return Forbid();
        }

        var result = await _awards.RemoveRecipientAsync(id, recipientId, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá người nhận." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Awards, "view")]
    public async Task<IActionResult> DownloadAttachment(
        Guid id,
        Guid recipientId,
        CancellationToken cancellationToken)
    {
        var award = await _awards.GetAsync(id, cancellationToken);
        if (award is null)
        {
            return NotFound();
        }

        if (!CanAccess(award.ClubId))
        {
            return Forbid();
        }

        var recipients = await _awards.ListRecipientsAsync(id, cancellationToken);
        var recipient = recipients.FirstOrDefault(item => item.Id == recipientId);
        if (recipient is null || string.IsNullOrWhiteSpace(recipient.Attachment))
        {
            return NotFound();
        }

        var stream = _awards.OpenAttachment(recipient);
        if (stream is null)
        {
            return NotFound();
        }

        var name = $"chung-nhan-{award.Code}{Path.GetExtension(recipient.Attachment)}";

        return File(stream, "application/octet-stream", name);
    }

    private async Task<(Guid Selected, IReadOnlyList<Club> Clubs)> ResolveClubAsync(
        Guid? clubId,
        CancellationToken cancellationToken)
    {
        var clubs = await _clubs.ListAsync(cancellationToken);
        var available = IsAdmin
            ? clubs
            : clubs.Where(club => club.Id == CurrentClubId).ToArray();

        var selected = clubId is not null && available.Any(club => club.Id == clubId)
            ? clubId.Value
            : (CurrentClubId ?? available.FirstOrDefault()?.Id ?? Guid.Empty);

        return (selected, available);
    }

    private async Task<IReadOnlyList<User>> ListMembersAsync(
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var memberships = await _memberships.ListByClubAsync(clubId, cancellationToken);
        var users = new List<User>();

        foreach (var membership in memberships.Where(item => item.IsActive))
        {
            var user = await _users.FindByIdAsync(membership.UserId, cancellationToken);
            if (user is not null)
            {
                users.Add(user);
            }
        }

        return users
            .OrderBy(user => user.DisplayName, StringComparer.CurrentCulture)
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(CancellationToken cancellationToken)
        => (await _users.ListAsync(cancellationToken))
            .ToDictionary(user => user.Id, user => user.DisplayName);

    private static string ErrorOf(Result result)
        => result.Errors.FirstOrDefault()?.Message ?? "Thao tác thất bại.";
}
