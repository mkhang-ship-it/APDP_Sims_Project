using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Security;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Nhật ký hệ thống: đọc audit.csv (chỉ Admin) với bộ lọc cơ bản.</summary>
[Authorize]
public class LogsController : Controller
{
    private readonly IAuditLogReader _reader;

    public LogsController(IAuditLogReader reader) => _reader = reader;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Logs, "view")]
    public async Task<IActionResult> Index(
        string? actor,
        string? actionFilter,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 20, 500);
        var entries = await _reader.ReadAsync(1000, cancellationToken);

        var filtered = entries
            .Where(entry => string.IsNullOrWhiteSpace(actor) ||
                entry.Actor.Contains(actor.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrWhiteSpace(actionFilter) ||
                entry.Action.Contains(actionFilter.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        filtered.Reverse();

        ViewData["ActorFilter"] = actor;
        ViewData["ActionFilter"] = actionFilter;
        ViewData["Take"] = take;

        return View(filtered.Take(take).ToArray());
    }
}
