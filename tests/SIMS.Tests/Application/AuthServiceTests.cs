using SIMS.Application.Abstractions;
using SIMS.Application.Security;
using SIMS.Domain.Security;

namespace SIMS.Tests.Application;

public sealed class AuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Login_success_returns_authenticated_user_and_audits()
    {
        var users = new InMemoryUserRepository();
        var hasher = new FakeHasher();
        users.Add(User.Create("admin", "Quản trị", Role.Admin, hasher.Hash("Admin@12345"), Now));
        var audit = new InMemoryAuditLog();
        var service = new AuthService(users, hasher, audit, new FixedClock());

        var result = await service.LoginAsync("ADMIN", "Admin@12345");

        Assert.True(result.IsSuccess);
        Assert.Equal("admin", result.Value!.Username);
        Assert.Equal(Role.Admin, result.Value.Role);
        Assert.Contains(audit.Entries, e => e.Action == "auth.login.success");
    }

    [Fact]
    public async Task Login_wrong_password_fails_and_audits_failure()
    {
        var users = new InMemoryUserRepository();
        var hasher = new FakeHasher();
        users.Add(User.Create("admin", "Quản trị", Role.Admin, hasher.Hash("Admin@12345"), Now));
        var audit = new InMemoryAuditLog();
        var service = new AuthService(users, hasher, audit, new FixedClock());

        var result = await service.LoginAsync("admin", "sai-mat-khau");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains(audit.Entries, e => e.Action == "auth.login.failed");
    }

    [Fact]
    public async Task Login_inactive_user_fails()
    {
        var users = new InMemoryUserRepository();
        var hasher = new FakeHasher();
        var user = User.Create("admin", "Quản trị", Role.Admin, hasher.Hash("Admin@12345"), Now);
        user.Deactivate();
        users.Add(user);
        var service = new AuthService(users, hasher, new InMemoryAuditLog(), new FixedClock());

        var result = await service.LoginAsync("admin", "Admin@12345");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Login_unknown_user_fails()
    {
        var service = new AuthService(
            new InMemoryUserRepository(),
            new FakeHasher(),
            new InMemoryAuditLog(),
            new FixedClock());

        var result = await service.LoginAsync("khong-ton-tai", "whatever1");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Login_empty_input_fails_without_touching_repository()
    {
        var service = new AuthService(
            new InMemoryUserRepository(),
            new FakeHasher(),
            new InMemoryAuditLog(),
            new FixedClock());

        var result = await service.LoginAsync("  ", "");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password)
            => "fake$" + password + "$" + new string('a', 40);

        public bool Verify(string password, string encodedHash) => encodedHash == Hash(password);
    }

    private sealed class InMemoryAuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly List<User> _users = new();

        public void Add(User user) => _users.Add(user);

        public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_users.Count > 0);

        public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<User>>(_users.ToArray());

        public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

        public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Email is not null && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(
                u => string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)));

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
}
