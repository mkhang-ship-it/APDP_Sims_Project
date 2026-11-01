using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Security;

namespace SIMS.Application.Security;

public sealed record AuthenticatedUser(Guid Id, string Username, string DisplayName, Role Role);

public interface IAuthService
{
    Task<Result<AuthenticatedUser>> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Xác thực bằng users.csv + PBKDF2. Không dùng Identity/JWT/OAuth theo spec.
/// Audit cả đăng nhập thành công và thất bại.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IAuditLog audit,
        IClock clock)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<AuthenticatedUser>> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalized = (username ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.Length == 0 || string.IsNullOrEmpty(password))
        {
            return Result<AuthenticatedUser>.Fail(
                new ValidationError(string.Empty, "Tên đăng nhập và mật khẩu là bắt buộc."));
        }

        var user = await _users.FindByUsernameAsync(normalized, cancellationToken);

        var isValid = user is not null &&
                      user.IsActive &&
                      _passwordHasher.Verify(password, user.PasswordHash);

        if (!isValid)
        {
            await _audit.WriteAsync(
                new AuditEntry(
                    _clock.UtcNow,
                    normalized,
                    "auth.login.failed",
                    "user",
                    user?.Id.ToString("D") ?? normalized,
                    "Sai thông tin đăng nhập hoặc tài khoản bị khoá"),
                cancellationToken);

            return Result<AuthenticatedUser>.Fail(
                new ValidationError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng."));
        }

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                user!.Username,
                "auth.login.success",
                "user",
                user.Id.ToString("D"),
                $"Vai trò: {user.Role}"),
            cancellationToken);

        return Result<AuthenticatedUser>.Ok(
            new AuthenticatedUser(user.Id, user.Username, user.DisplayName, user.Role));
    }
}
