using SIMS.Application.Security;
using SIMS.Domain.Security;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class PermissionServiceTests
{
    private static PermissionService Create(out InMemoryPermissionRepository repository)
    {
        repository = new InMemoryPermissionRepository(PermissionCatalog.Defaults);
        return new PermissionService(repository);
    }

    [Theory]
    [InlineData("students", "view")]
    [InlineData("students", "delete")]
    [InlineData("clubs", "create")]
    [InlineData("learning", "view_own")]
    public async Task Admin_has_every_permission(string module, string action)
    {
        var service = Create(out _);

        Assert.True(await service.HasPermissionAsync(Role.Admin, module, action));
    }

    [Theory]
    [InlineData("students", "view", true)]
    [InlineData("students", "create", true)]
    [InlineData("students", "delete", false)]
    [InlineData("clubs", "create", false)]
    [InlineData("learning", "view_own", false)]
    public async Task Faculty_matches_expected_matrix(string module, string action, bool expected)
    {
        var service = Create(out _);

        Assert.Equal(expected, await service.HasPermissionAsync(Role.Faculty, module, action));
    }

    [Theory]
    [InlineData("learning", "view_own", true)]
    [InlineData("dashboard", "view", true)]
    [InlineData("clubs", "view", true)]
    [InlineData("students", "view", false)]
    [InlineData("courses", "view", false)]
    public async Task Student_only_has_own_learning(string module, string action, bool expected)
    {
        var service = Create(out _);

        Assert.Equal(expected, await service.HasPermissionAsync(Role.Student, module, action));
    }

    [Fact]
    public async Task Unknown_module_is_denied()
    {
        var service = Create(out _);

        Assert.False(await service.HasPermissionAsync(Role.Admin, "khong_ton_tai", "view"));
    }

    [Fact]
    public void Missing_defaults_returns_only_absent_rows()
    {
        var existing = PermissionCatalog.Defaults
            .Where(permission => permission.Module != PermissionCatalog.EventPlans)
            .ToArray();

        var missing = PermissionCatalog.MissingDefaults(existing);

        Assert.NotEmpty(missing);
        Assert.All(missing, permission => Assert.Equal(PermissionCatalog.EventPlans, permission.Module));
        Assert.Empty(PermissionCatalog.MissingDefaults(PermissionCatalog.Defaults));
    }
}

public sealed class InMemoryPermissionRepository : SIMS.Application.Abstractions.IPermissionRepository
{
    private readonly List<RolePermission> _items;

    public InMemoryPermissionRepository(IEnumerable<RolePermission> items)
    {
        _items = items.ToList();
    }

    public Task<IReadOnlyList<RolePermission>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RolePermission>>(_items.ToArray());

    public Task AddRangeAsync(IEnumerable<RolePermission> permissions, CancellationToken cancellationToken = default)
    {
        _items.AddRange(permissions);
        return Task.CompletedTask;
    }
}
