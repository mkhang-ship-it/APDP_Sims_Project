using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Clubs;
using SIMS.Application.Common;
using SIMS.Application.Security;
using SIMS.Domain.Security;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module CLUBS + multi-club switch.</summary>
[Authorize]
public class ClubsController : Controller
{
    private readonly IClubService _clubService;
    private readonly IUserRepository _users;

    public ClubsController(IClubService clubService, IUserRepository users)
    {
        _clubService = clubService;
        _users = users;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Clubs, "view")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var clubs = await _clubService.ListAsync(cancellationToken);
        var model = new List<ClubListItem>();

        foreach (var club in clubs)
        {
            var members = await _clubService.ListMembersAsync(club.Id, cancellationToken);
            model.Add(new ClubListItem(club, members.Count(m => m.IsActive)));
        }

        ViewData["CurrentClubId"] = CurrentClubId;
        return View(model);
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Clubs, "create")]
    public IActionResult Create() => View(new ClubFormModel());

    [HttpPost]
    [RequirePermission(PermissionCatalog.Clubs, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ClubFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _clubService.CreateAsync(model.ToInput(), CurrentActor, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Status"] = $"Đã thêm CLB {result.Value.Code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Clubs, "edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var club = await _clubService.GetAsync(id, cancellationToken);
        if (club is null)
        {
            return NotFound();
        }

        var memberships = await _clubService.ListMembersAsync(id, cancellationToken);
        var users = new List<User>();
        foreach (var membership in memberships.Where(m => m.IsActive))
        {
            var user = await _users.FindByIdAsync(membership.UserId, cancellationToken);
            if (user is not null)
            {
                users.Add(user);
            }
        }

        return View(new ClubEditViewModel
        {
            Club = club,
            Form = ClubFormModel.FromClub(club),
            Members = users,
            AllUsers = await _users.ListAsync(cancellationToken),
            AvailableRoles = new[] { Role.Admin, Role.Faculty, Role.Student },
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Clubs, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ClubFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToAction(nameof(Edit), new { id });
        }

        var result = await _clubService.UpdateAsync(id, model.ToInput(), CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không cập nhật được CLB.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        TempData["Status"] = $"Đã cập nhật CLB {result.Value!.Code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Clubs, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(Guid clubId, Guid userId, Role role, CancellationToken cancellationToken)
    {
        var result = await _clubService.AssignMemberAsync(clubId, userId, role, CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không thêm được thành viên.";
            return RedirectToAction(nameof(Edit), new { id = clubId });
        }

        TempData["Status"] = "Đã thêm thành viên vào CLB.";
        return RedirectToAction(nameof(Edit), new { id = clubId });
    }

    /// <summary>Đổi CLB hiện tại: cấp lại cookie với claim club_id mới (multi-club).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(Guid clubId, CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var memberships = await _clubService.ListMembershipsAsync(userId, cancellationToken);
        var target = memberships.FirstOrDefault(m => m.ClubId == clubId && m.IsActive);
        if (target is null)
        {
            TempData["Error"] = "Bạn không phải thành viên của CLB này.";
            return RedirectToAction(nameof(Index));
        }

        var claims = User.Claims
            .Where(c => c.Type != "club_id")
            .Select(c => new Claim(c.Type, c.Value))
            .ToList();
        claims.Add(new Claim("club_id", clubId.ToString("D")));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        TempData["Status"] = "Đã chuyển CLB hiện tại.";
        return RedirectToAction("Index", "Home");
    }

    private void AddErrors(Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Field, error.Message);
        }
    }
}
