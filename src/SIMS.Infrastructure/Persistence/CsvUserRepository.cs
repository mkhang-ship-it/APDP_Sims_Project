using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Security;

namespace SIMS.Infrastructure.Persistence;

/// <summary>users.csv — tài khoản đăng nhập; chỉ lưu chuỗi băm PBKDF2.</summary>
public sealed class CsvUserRepository : IUserRepository
{
    private const int ColumnCount = 8;

    private readonly IRowStore _store;

    public CsvUserRepository(string filePath)
        : this(new CsvFileStore(
            filePath,
            "id",
            "username",
            "display_name",
            "role",
            "password_hash",
            "is_active",
            "created_at_utc",
            "email"))
    {
    }

    public CsvUserRepository(IRowStore store)
    {
        _store = store;
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count > 0;
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var users = new List<User>(rows.Count);

        foreach (var row in rows)
        {
            var user = TryMap(row);
            if (user is not null)
            {
                users.Add(user);
            }
        }

        return users;
    }

    public async Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var target = (username ?? string.Empty).Trim();
        var rows = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            var user = TryMap(row);
            if (user is not null && string.Equals(user.Username, target, StringComparison.OrdinalIgnoreCase))
            {
                return user;
            }
        }

        return null;
    }

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (Guid.TryParse(row.Length > 0 ? row[0] : null, out var rowId) && rowId == id)
            {
                return TryMap(row);
            }
        }

        return null;
    }

    public async Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var target = (email ?? string.Empty).Trim();
        if (target.Length == 0)
        {
            return null;
        }

        var rows = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            var user = TryMap(row);
            if (user?.Email is not null && string.Equals(user.Email, target, StringComparison.OrdinalIgnoreCase))
            {
                return user;
            }
        }

        return null;
    }

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        return _store.AppendAsync(ToCells(user), cancellationToken);
    }

    public Task<bool> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        return _store.MutateAsync(
            rows =>
            {
                var result = new List<string[]>(rows.Count);
                var changed = false;

                foreach (var row in rows)
                {
                    if (Guid.TryParse(row.Length > 0 ? row[0] : null, out var rowId) && rowId == user.Id)
                    {
                        result.Add(ToCells(user));
                        changed = true;
                    }
                    else
                    {
                        result.Add(row);
                    }
                }

                return changed ? result : null;
            },
            cancellationToken);
    }

    private static string[] ToCells(User user) =>
    [
        user.Id.ToString("D"),
        user.Username,
        user.DisplayName,
        user.Role.ToString(),
        user.PasswordHash,
        user.IsActive ? "1" : "0",
        user.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        user.Email ?? string.Empty,
    ];

    private static User? TryMap(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Enum.TryParse<Role>(cells[3], ignoreCase: true, out var role) ||
            !DateTimeOffset.TryParse(cells[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return User.Restore(id, cells[1], cells[2], role, cells[4], cells[5] == "1", createdAt, cells[7]);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
