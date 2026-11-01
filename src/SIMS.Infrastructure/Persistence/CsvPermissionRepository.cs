using SIMS.Application.Abstractions;
using SIMS.Domain.Security;

namespace SIMS.Infrastructure.Persistence;

/// <summary>permissions.csv — ma trận quyền theo module/action (role,module,action).</summary>
public sealed class CsvPermissionRepository : IPermissionRepository
{
    private readonly IRowStore _store;

    public CsvPermissionRepository(string filePath)
        : this(new CsvFileStore(filePath, "role", "module", "action"))
    {
    }

    public CsvPermissionRepository(IRowStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<RolePermission>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var permissions = new List<RolePermission>(rows.Count);

        foreach (var row in rows)
        {
            if (row.Length != 3 || !Enum.TryParse<Role>(row[0], ignoreCase: true, out var role))
            {
                continue;
            }

            try
            {
                permissions.Add(RolePermission.Create(role, row[1], row[2]));
            }
            catch (Domain.Common.DomainException)
            {
                // Bỏ qua dòng hỏng.
            }
        }

        return permissions;
    }

    public async Task AddRangeAsync(
        IEnumerable<RolePermission> permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        foreach (var permission in permissions)
        {
            var cells = new[] { permission.Role.ToString(), permission.Module, permission.Action };
            await _store.AppendAsync(cells, cancellationToken).ConfigureAwait(false);
        }
    }
}
