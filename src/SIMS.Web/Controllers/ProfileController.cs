using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Common;
using SIMS.Application.Security;
using SIMS.Web.Models;

namespace SIMS.Web.Controllers;

/// <summary>Hồ sơ cá nhân: sửa tên/email và đổi mật khẩu tự phục vụ.</summary>
[Authorize]
public class ProfileController : Controller
{
    private readonly IProfileService _profile;

    public ProfileController(IProfileService profile) => _profile = profile;

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = await _profile.GetAsync(CurrentActor, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return View(new ProfilePageModel
        {
            Username = user.Username,
            DisplayName = user.DisplayName,
            Email = user.Email,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        string? displayName,
        string? email,
        CancellationToken cancellationToken)
    {
        var result = await _profile.UpdateProfileAsync(
            CurrentActor, displayName ?? string.Empty, email, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] = result.IsSuccess
            ? "Đã cập nhật hồ sơ."
            : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(
        string? currentPassword,
        string? newPassword,
        string? confirmPassword,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            TempData["Error"] = "Mật khẩu xác nhận không khớp.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _profile.ChangePasswordAsync(
            CurrentActor,
            currentPassword ?? string.Empty,
            newPassword ?? string.Empty,
            CurrentActor,
            cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] = result.IsSuccess
            ? "Đã đổi mật khẩu."
            : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    private static string ErrorOf(Result result)
        => result.Errors.FirstOrDefault()?.Message ?? "Thao tác thất bại.";
}
