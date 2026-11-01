using SIMS.Domain.Common;

namespace SIMS.Domain.Security;

/// <summary>Một quyền theo module/action gán cho vai trò (thay cho phân quyền chỉ theo role).</summary>
public sealed record RolePermission(Role Role, string Module, string Action)
{
    public static RolePermission Create(Role role, string module, string action)
    {
        var normalizedModule = (module ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();

        if (normalizedModule.Length is < 2 or > 40)
        {
            throw new DomainException("Mã module không hợp lệ.");
        }

        if (normalizedAction.Length is < 2 or > 40)
        {
            throw new DomainException("Mã hành động không hợp lệ.");
        }

        return new RolePermission(role, normalizedModule, normalizedAction);
    }
}
