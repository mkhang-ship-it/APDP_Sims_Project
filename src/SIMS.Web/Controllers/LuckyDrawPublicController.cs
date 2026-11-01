using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.LuckyDraws;
using SIMS.Web.Models;

namespace SIMS.Web.Controllers;

/// <summary>Trang bốc thăm công khai (không cần đăng nhập), có mã bảo mật tuỳ chọn.</summary>
[AllowAnonymous]
[Route("lucky")]
public class LuckyDrawPublicController : Controller
{
    private const string SessionPrefix = "lucky.verified.";

    private readonly ILuckyDrawService _draws;

    public LuckyDrawPublicController(ILuckyDrawService draws) => _draws = draws;

    [HttpGet("{slug}")]
    public async Task<IActionResult> Show(string slug, CancellationToken cancellationToken)
    {
        var draw = await _draws.GetPublicAsync(slug, cancellationToken);
        if (draw is null)
        {
            return NotFound();
        }

        return View(new PublicDrawPageModel
        {
            Draw = draw,
            Verified = IsVerified(slug, draw.RequiresSecurityCode),
        });
    }

    [HttpPost("{slug}/verify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(
        string slug,
        string? securityCode,
        CancellationToken cancellationToken)
    {
        var draw = await _draws.GetPublicAsync(slug, cancellationToken);
        if (draw is null)
        {
            return NotFound();
        }

        if (!draw.RequiresSecurityCode ||
            string.Equals(draw.Draw.SecurityCode, (securityCode ?? string.Empty).Trim(), StringComparison.Ordinal))
        {
            HttpContext.Session.SetString(SessionPrefix + slug, "1");
            return RedirectToAction(nameof(Show), new { slug });
        }

        return View("Show", new PublicDrawPageModel
        {
            Draw = draw,
            Verified = false,
            SecurityError = "Mã bảo mật không đúng.",
        });
    }

    private bool IsVerified(string slug, bool requiresCode)
        => !requiresCode || HttpContext.Session.GetString(SessionPrefix + slug) == "1";
}
