using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Security;

namespace SIMS.Application.Security;

public interface IProfileService
{
    Task<User?> GetAsync(string username, CancellationToken cancellationToken = default);

    Task<Result<User>> UpdateProfileAsync(
        string username,
        string displayName,
        string? email,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(
        string username,
        string currentPassword,
        string newPassword,
        string actor,
        CancellationToken cancellationToken = default);
}

/// <summary>Tự phục vụ hồ sơ cá nhân: sửa tên/email và đổi mật khẩu (có xác minh mật khẩu hiện tại).</summary>
public sealed class ProfileService : IProfileService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public ProfileService(
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

    public Task<User?> GetAsync(string username, CancellationToken cancellationToken = default)
        => _users.FindByUsernameAsync(username, cancellationToken);

    public async Task<Result<User>> UpdateProfileAsync(
        string username,
        string displayName,
        string? email,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result<User>.Fail(new ValidationError(string.Empty, "Không tìm thấy tài khoản."));
        }

        try
        {
            user.UpdateProfile(displayName, email);
        }
        catch (DomainException ex)
        {
            return Result<User>.Fail(new ValidationError(string.Empty, ex.Message));
        }

        await _users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow, actor, "profile.update", "user", user.Id.ToString("D"), user.Username),
            cancellationToken).ConfigureAwait(false);

        return Result<User>.Ok(user);
    }

    public async Task<Result> ChangePasswordAsync(
        string username,
        string currentPassword,
        string newPassword,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy tài khoản."));
        }

        if (!_passwordHasher.Verify(currentPassword ?? string.Empty, user.PasswordHash))
        {
            return Result.Fail(new ValidationError(nameof(currentPassword), "Mật khẩu hiện tại không đúng."));
        }

        var password = newPassword ?? string.Empty;
        if (password.Length is < 8 or > 128)
        {
            return Result.Fail(new ValidationError(
                nameof(newPassword),
                "Mật khẩu mới phải từ 8 đến 128 ký tự."));
        }

        user.SetPasswordHash(_passwordHasher.Hash(password));
        await _users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow, actor, "profile.password", "user", user.Id.ToString("D"), user.Username),
            cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }
}
