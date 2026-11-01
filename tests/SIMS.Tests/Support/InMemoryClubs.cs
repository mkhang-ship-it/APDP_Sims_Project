using SIMS.Application.Abstractions;
using SIMS.Domain.Clubs;
using SIMS.Domain.Security;

namespace SIMS.Tests.Support;

/// <summary>In-memory cho CLB và membership (LSP với bản CSV).</summary>
public sealed class InMemoryClubRepository : InMemoryRepository<Club>, IClubRepository
{
    public Task<Club?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(
            c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsByCodeAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(c =>
            string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase) &&
            (excludeId is null || c.Id != excludeId)));
}

public sealed class InMemoryClubMembershipRepository
    : InMemoryRepository<ClubMembership>, IClubMembershipRepository
{
    public Task<IReadOnlyList<ClubMembership>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ClubMembership>>(Items.Where(m => m.UserId == userId).ToArray());

    public Task<IReadOnlyList<ClubMembership>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ClubMembership>>(Items.Where(m => m.ClubId == clubId).ToArray());

    public Task<ClubMembership?> FindByPairAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(m => m.UserId == userId && m.ClubId == clubId));
}

/// <summary>In-memory user repository (dùng cho test CLB/permission).</summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly List<User> _users = new();

    public IReadOnlyList<User> All => _users;

    public void Seed(User user) => _users.Add(user);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_users.Count > 0);

    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<User>>(_users.ToArray());

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

    public Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => Task.FromResult(_users.FirstOrDefault(
            u => string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(_users.FirstOrDefault(
            u => u.Email is not null && string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        var index = _users.FindIndex(item => item.Id == user.Id);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        _users[index] = user;
        return Task.FromResult(true);
    }
}
