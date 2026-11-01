using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Learning;
using SIMS.Application.Security;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Vai trò Student xem hồ sơ + điểm của chính mình (liên kết Student↔User).</summary>
[Authorize]
public class MyLearningController : Controller
{
    private readonly IMyLearningService _learning;

    public MyLearningController(IMyLearningService learning)
    {
        _learning = learning;
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Learning, "view_own")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Challenge();
        }

        var model = await _learning.GetAsync(userId, cancellationToken);
        return View(model);
    }
}
