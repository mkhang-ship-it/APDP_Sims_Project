using SIMS.Domain.Common;

namespace SIMS.Domain.Security;

/// <summary>
/// Tài khoản đăng nhập (lưu trong users.csv). Mật khẩu **không** nằm ở đây dưới dạng thô:
/// chỉ giữ chuỗi đã băm theo định dạng do IPasswordHasher sinh ra.
/// </summary>
public sealed class User : Entity
{
    private User()
    {
        Username = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    public string Username { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>Email liên kết (dùng để nối Student↔User); null nếu không khai báo.</summary>
    public string? Email { get; private set; }

    public Role Role { get; private set; }

    public string PasswordHash { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static User Create(
        string username,
        string displayName,
        Role role,
        string passwordHash,
        DateTimeOffset createdAtUtc,
        string? email = null)
    {
        return new User
        {
            Username = NormalizeUsername(username),
            DisplayName = NormalizeDisplayName(displayName),
            Email = NormalizeEmail(email),
            Role = role,
            PasswordHash = NormalizePasswordHash(passwordHash),
            IsActive = true,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public static User Restore(
        Guid id,
        string username,
        string displayName,
        Role role,
        string passwordHash,
        bool isActive,
        DateTimeOffset createdAtUtc,
        string? email = null)
    {
        return new User
        {
            Id = id,
            Username = NormalizeUsername(username),
            DisplayName = NormalizeDisplayName(displayName),
            Email = NormalizeEmail(email),
            Role = role,
            PasswordHash = NormalizePasswordHash(passwordHash),
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = NormalizePasswordHash(passwordHash);
    }

    public void UpdateProfile(string displayName, string? email)
    {
        DisplayName = NormalizeDisplayName(displayName);
        Email = NormalizeEmail(email);
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    private static string NormalizeUsername(string username)
    {
        var value = (username ?? string.Empty).Trim().ToLowerInvariant();

        if (value.Length is < 3 or > 64)
        {
            throw new DomainException("Tên đăng nhập phải có từ 3 đến 64 ký tự.");
        }

        foreach (var ch in value)
        {
            var allowed = char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-';
            if (!allowed)
            {
                throw new DomainException("Tên đăng nhập chỉ được chứa chữ, số, '.', '_' hoặc '-'.");
            }
        }

        return value;
    }

    private static string NormalizeDisplayName(string displayName)
    {
        var value = (displayName ?? string.Empty).Trim();

        if (value.Length is < 2 or > 120)
        {
            throw new DomainException("Tên hiển thị phải có từ 2 đến 120 ký tự.");
        }

        return value;
    }

    private static string NormalizePasswordHash(string passwordHash)
    {
        var value = (passwordHash ?? string.Empty).Trim();

        if (value.Length < 20)
        {
            throw new DomainException("Chuỗi băm mật khẩu không hợp lệ.");
        }

        return value;
    }

    private static string? NormalizeEmail(string? email)
    {
        var value = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0)
        {
            return null;
        }

        var atIndex = value.IndexOf('@');
        var isValid =
            atIndex > 0 &&
            atIndex == value.LastIndexOf('@') &&
            value.Length > atIndex + 3 &&
            value[(atIndex + 1)..].Contains('.') &&
            !value.Contains(' ', StringComparison.Ordinal) &&
            value.Length <= 254;

        if (!isValid)
        {
            throw new DomainException("Email không hợp lệ.");
        }

        return value;
    }
}
