using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Clubs;
using SIMS.Application.Security;
using SIMS.Web.Models;

namespace SIMS.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IClubService _clubService;

    public AccountController(IAuthService authService, IClubService clubService)
    {
        _authService = authService;
        _clubService = clubService;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _authService.LoginAsync(model.Username, model.Password, cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Message);
            }

            return View(model);
        }

        var user = result.Value;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        // Multi-club: đặt CLB hiện tại theo membership đầu tiên (đổi được bằng /Clubs/Switch).
        var memberships = await _clubService.ListMembershipsAsync(user.Id, cancellationToken);
        var currentMembership = memberships.FirstOrDefault(m => m.IsActive);
        if (currentMembership is not null)
        {
            claims.Add(new Claim("club_id", currentMembership.ClubId.ToString("D")));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = model.RememberMe });

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return LocalRedirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }
}
