using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Security;

namespace SIMS.Application.Security;

public interface IUserService
{
    Task<Result<User>> CreateAsync(UserInput input, string actor, CancellationToken cancellationToken = default);
}

/// <summary>Tạo tài khoản (thao tác quản trị). Mật khẩu băm bằng IPasswordHasher, có audit.</summary>
public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IValidator<UserInput> _validator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public UserService(
        IUserRepository users,
        IValidator<UserInput> validator,
        IPasswordHasher passwordHasher,
        IAuditLog audit,
        IClock clock)
    {
        _users = users;
        _validator = validator;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<User>> CreateAsync(
        UserInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return Result<User>.Fail(validation.Errors);
        }

        var username = (input.Username ?? string.Empty).Trim().ToLowerInvariant();
        if (await _users.FindByUsernameAsync(username, cancellationToken) is not null)
        {
            return Result<User>.Fail(new ValidationError(
                nameof(UserInput.Username),
                $"Tên đăng nhập '{username}' đã tồn tại."));
        }

        var user = User.Create(
            username,
            input.DisplayName,
            input.Role,
            _passwordHasher.Hash(input.Password),
            _clock.UtcNow);

        await _users.AddAsync(user, cancellationToken);
        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "user.create",
                "user",
                user.Id.ToString("D"),
                $"{user.Username} · {user.Role}"),
            cancellationToken);

        return Result<User>.Ok(user);
    }
}
