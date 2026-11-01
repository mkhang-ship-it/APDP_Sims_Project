using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Security;
using SIMS.Application.Settings;
using SIMS.Domain.Clubs;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module SETTINGS: cấu hình toàn hệ thống (Admin) và cấu hình theo CLB.</summary>
[Authorize]
public class SettingsController : Controller
{
    private readonly ISettingsService _settings;
    private readonly IClubRepository _clubs;

    public SettingsController(ISettingsService settings, IClubRepository clubs)
    {
        _settings = settings;
        _clubs = clubs;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.IsInRole("Admin"))
        {
            return await Global(cancellationToken);
        }

        return RedirectToAction(nameof(Club));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Settings, "admin")]
    public async Task<IActionResult> Global(CancellationToken cancellationToken)
    {
        var values = await _settings.GetManyAsync(null, SettingKeys.Global, cancellationToken);

        return View("Global", new SettingsPageModel
        {
            IsAdmin = true,
            Values = values,
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Settings, "admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Global(IFormCollection form, CancellationToken cancellationToken)
    {
        var theme = form[SettingKeys.SiteTheme].ToString();
        var primary = form[SettingKeys.SitePrimaryColor].ToString();

        await _settings.SetAsync(
            null, SettingKeys.SiteTitle, form[SettingKeys.SiteTitle].ToString(), CurrentActor, cancellationToken);
        await _settings.SetAsync(
            null, SettingKeys.SiteDescription, form[SettingKeys.SiteDescription].ToString(),
            CurrentActor, cancellationToken);
        await _settings.SetAsync(
            null, SettingKeys.SiteTheme, theme is "dark" ? "dark" : "light", CurrentActor, cancellationToken);
        await _settings.SetAsync(
            null, SettingKeys.SitePrimaryColor,
            System.Text.RegularExpressions.Regex.IsMatch(primary, "^#[0-9a-fA-F]{6}$") ? primary : "#0d6efd",
            CurrentActor, cancellationToken);
        await _settings.SetAsync(
            null, SettingKeys.SiteSupportEmail, form[SettingKeys.SiteSupportEmail].ToString(),
            CurrentActor, cancellationToken);

        var sepayKey = form[SettingKeys.PaymentsSePayApiKey].ToString();
        if (!string.IsNullOrWhiteSpace(sepayKey))
        {
            await _settings.SetAsync(
                null, SettingKeys.PaymentsSePayApiKey, sepayKey.Trim(), CurrentActor, cancellationToken);
        }

        var maintenance = string.Equals(
            form[SettingKeys.SystemMaintenance].ToString(), "on", StringComparison.OrdinalIgnoreCase)
            ? "on"
            : "off";

        await _settings.SetAsync(
            null, SettingKeys.SystemMaintenance, maintenance, CurrentActor, cancellationToken);
        await _settings.SetAsync(
            null, SettingKeys.SystemMaintenanceMessage,
            form[SettingKeys.SystemMaintenanceMessage].ToString(), CurrentActor, cancellationToken);

        TempData["Status"] = "Đã lưu cấu hình hệ thống.";
        return RedirectToAction(nameof(Global));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Settings, "club")]
    public async Task<IActionResult> Club(Guid? clubId, CancellationToken cancellationToken)
    {
        var clubs = await AvailableClubsAsync(cancellationToken);
        if (clubs.Count == 0)
        {
            TempData["Error"] = "Bạn chưa thuộc CLB nào để cấu hình.";
            return RedirectToAction("Index", "Home");
        }

        var selected = clubId is not null && clubs.Any(club => club.Id == clubId)
            ? clubId.Value
            : clubs[0].Id;

        return View(new SettingsPageModel
        {
            IsAdmin = IsAdmin,
            ClubId = selected,
            ClubName = clubs.First(club => club.Id == selected).Name,
            Clubs = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray(),
            Values = await _settings.GetManyAsync(selected, SettingKeys.Club, cancellationToken),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Settings, "club")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Club(Guid clubId, IFormCollection form, CancellationToken cancellationToken)
    {
        if (!IsAdmin && CurrentClubId != clubId)
        {
            return Forbid();
        }

        foreach (var key in SettingKeys.Club)
        {
            await _settings.SetAsync(clubId, key, form[key].ToString(), CurrentActor, cancellationToken);
        }

        TempData["Status"] = "Đã lưu cấu hình CLB.";
        return RedirectToAction(nameof(Club), new { clubId });
    }

    private async Task<IReadOnlyList<Club>> AvailableClubsAsync(CancellationToken cancellationToken)
    {
        var clubs = await _clubs.ListAsync(cancellationToken);

        return IsAdmin
            ? clubs
            : clubs.Where(club => club.Id == CurrentClubId).ToArray();
    }
}
