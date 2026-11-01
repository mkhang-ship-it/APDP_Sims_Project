using System.Text.RegularExpressions;
using SIMS.Domain.Common;

namespace SIMS.Domain.Settings;

/// <summary>Một cặp khoá/giá trị cấu hình; ClubId null nghĩa là cấu hình toàn hệ thống.</summary>
public sealed class Setting : Entity
{
    private static readonly Regex KeyPattern =
        new(@"^[a-z0-9][a-z0-9_.-]{1,59}$", RegexOptions.Compiled);

    private Setting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    public Guid? ClubId { get; private set; }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Setting Create(Guid? clubId, string key, string? value, DateTimeOffset now)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        return new Setting
        {
            ClubId = clubId,
            Key = RequireKey(key),
            Value = RequireValue(value),
            UpdatedAtUtc = now,
        };
    }

    public static Setting Restore(Guid id, Guid? clubId, string key, string? value, DateTimeOffset updatedAtUtc)
    {
        var setting = Create(clubId, key, value, updatedAtUtc);
        setting.Id = id;

        return setting;
    }

    public void SetValue(string? value, DateTimeOffset now)
    {
        Value = RequireValue(value);
        UpdatedAtUtc = now;
    }

    private static string RequireKey(string key)
    {
        var value = (key ?? string.Empty).Trim().ToLowerInvariant();

        return KeyPattern.IsMatch(value)
            ? value
            : throw new DomainException("Khoá cấu hình chỉ gồm chữ thường, số, '.', '_' hoặc '-'.");
    }

    private static string RequireValue(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 2000)
        {
            throw new DomainException("Giá trị cấu hình tối đa 2000 ký tự.");
        }

        return text;
    }
}
