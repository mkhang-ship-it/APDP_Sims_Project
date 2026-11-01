using SIMS.Application.Abstractions;
using SIMS.Domain.Security;

namespace SIMS.Application.Security;

public interface IPermissionService
{
    Task<IReadOnlyList<RolePermission>> ListAsync(CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(
        Role role,
        string module,
        string action,
        CancellationToken cancellationToken = default);
}

/// <summary>Ma trận quyền theo module/action đọc từ permissions.csv (seed từ PermissionCatalog).</summary>
public sealed class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _repository;

    public PermissionService(IPermissionRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<RolePermission>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.ListAsync(cancellationToken);

    public async Task<bool> HasPermissionAsync(
        Role role,
        string module,
        string action,
        CancellationToken cancellationToken = default)
    {
        var normalizedModule = (module ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();

        var permissions = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);

        return permissions.Any(p =>
            p.Role == role &&
            string.Equals(p.Module, normalizedModule, StringComparison.Ordinal) &&
            string.Equals(p.Action, normalizedAction, StringComparison.Ordinal));
    }
}
