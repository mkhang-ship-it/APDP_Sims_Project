using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SIMS.Application.Security;
using SIMS.Domain.Security;

namespace SIMS.Web.Security;

/// <summary>Yêu cầu quyền theo module/action: [RequirePermission("students","edit")].</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string module, string action)
    {
        Policy = PermissionPolicyProvider.PolicyPrefix + module + ":" + action;
    }
}

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string module, string action)
    {
        Module = module;
        Action = action;
    }

    public string Module { get; }

    public string Action { get; }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var roleValue = context.User.FindFirst(ClaimTypes.Role)?.Value;
        if (!Enum.TryParse<Role>(roleValue, ignoreCase: true, out var role))
        {
            return;
        }

        if (context.Resource is not HttpContext httpContext)
        {
            return;
        }

        var permissions = httpContext.RequestServices.GetRequiredService<IPermissionService>();
        if (await permissions.HasPermissionAsync(role, requirement.Module, requirement.Action))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Tạo policy động cho "perm:module:action" (không cần khai báo từng policy).</summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "perm:";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var parts = policyName[PolicyPrefix.Length..].Split(':', 2);
        if (parts.Length != 2)
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(parts[0], parts[1]))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
