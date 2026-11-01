using SIMS.Domain.Common;

namespace SIMS.Domain.Clubs;

/// <summary>Câu lạc bộ (multi-club: người dùng thuộc nhiều CLB với vai trò riêng).</summary>
public sealed class Club : Entity
{
    private Club()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Club Create(string code, string name, DateTimeOffset createdAtUtc) => new()
    {
        Code = NormalizeCode(code),
        Name = NormalizeName(name),
        IsActive = true,
        CreatedAtUtc = createdAtUtc,
    };

    public static Club Restore(Guid id, string code, string name, bool isActive, DateTimeOffset createdAtUtc)
    {
        var club = Create(code, name, createdAtUtc);
        club.Id = id;
        club.IsActive = isActive;

        return club;
    }

    public void UpdateDetails(string name)
    {
        Name = NormalizeName(name);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private static string NormalizeCode(string code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (value.Length is < 2 or > 20)
        {
            throw new DomainException("Mã CLB phải có từ 2 đến 20 ký tự.");
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                throw new DomainException("Mã CLB chỉ được chứa chữ và số.");
            }
        }

        return value;
    }

    private static string NormalizeName(string name)
    {
        var value = (name ?? string.Empty).Trim();

        if (value.Length is < 2 or > 120)
        {
            throw new DomainException("Tên CLB phải có từ 2 đến 120 ký tự.");
        }

        return value;
    }
}
