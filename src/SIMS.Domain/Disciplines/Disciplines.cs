using System.Text.RegularExpressions;
using SIMS.Domain.Common;

namespace SIMS.Domain.Disciplines;

public enum DisciplineRecordStatus
{
    Active,
    Revoked,
}

/// <summary>Danh mục kỷ luật/điểm rèn luyện: điểm dương là cộng, điểm âm là trừ.</summary>
public sealed class Discipline : Entity
{
    private static readonly Regex CodePattern = new(@"^KL-\d{6}$", RegexOptions.Compiled);

    private Discipline()
    {
        Code = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
    }

    public Guid ClubId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public decimal Points { get; private set; }

    public string Description { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Discipline Create(
        Guid clubId,
        string code,
        string name,
        decimal points,
        string? description,
        DateTimeOffset now)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        return new Discipline
        {
            ClubId = clubId,
            Code = RequireCode(code),
            Name = RequireText(name, 255, "Tên danh mục"),
            Points = RequirePoints(points),
            Description = OptionalText(description, 1000, "Mô tả"),
            IsActive = true,
            CreatedAtUtc = now,
        };
    }

    public static Discipline Restore(
        Guid id,
        Guid clubId,
        string code,
        string name,
        decimal points,
        string? description,
        bool isActive,
        DateTimeOffset createdAtUtc)
    {
        var discipline = Create(clubId, code, name, points, description, createdAtUtc);
        discipline.Id = id;
        discipline.IsActive = isActive;

        return discipline;
    }

    public void Update(string name, decimal points, string? description)
    {
        Name = RequireText(name, 255, "Tên danh mục");
        Points = RequirePoints(points);
        Description = OptionalText(description, 1000, "Mô tả");
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private static string RequireCode(string code)
    {
        var value = (code ?? string.Empty).Trim();

        return CodePattern.IsMatch(value)
            ? value
            : throw new DomainException("Mã danh mục phải có dạng KL-NNNNNN.");
    }

    private static decimal RequirePoints(decimal points)
    {
        if (points is < -1000m or > 1000m)
        {
            throw new DomainException("Điểm phải từ -1000 đến 1000.");
        }

        return decimal.Round(points, 1);
    }

    private static string RequireText(string value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new DomainException($"{fieldName} là bắt buộc.");
        }

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text;
    }

    private static string OptionalText(string? value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }
}

/// <summary>Bản ghi áp dụng một danh mục kỷ luật cho thành viên (có thể thu hồi).</summary>
public sealed class DisciplineRecord : Entity
{
    private DisciplineRecord()
    {
        AppliedBy = string.Empty;
        Notes = string.Empty;
        Period = string.Empty;
    }

    public Guid UserId { get; private set; }

    public Guid ClubId { get; private set; }

    public Guid DisciplineId { get; private set; }

    public DateOnly AppliedDate { get; private set; }

    /// <summary>Kỳ ghi nhận (thay cho semester_id của PHP — module học kỳ chưa port).</summary>
    public string Period { get; private set; }

    public DisciplineRecordStatus Status { get; private set; }

    public string AppliedBy { get; private set; }

    public string Notes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool IsActive => Status == DisciplineRecordStatus.Active;

    public static DisciplineRecord Create(
        Guid userId,
        Guid clubId,
        Guid disciplineId,
        DateOnly appliedDate,
        string? period,
        string appliedBy,
        string? notes,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty || clubId == Guid.Empty || disciplineId == Guid.Empty)
        {
            throw new DomainException("Thành viên, CLB hoặc danh mục không hợp lệ.");
        }

        if (appliedDate == default)
        {
            throw new DomainException("Ngày áp dụng là bắt buộc.");
        }

        var actor = (appliedBy ?? string.Empty).Trim();
        var note = (notes ?? string.Empty).Trim();

        if (note.Length > 500)
        {
            throw new DomainException("Ghi chú tối đa 500 ký tự.");
        }

        var periodValue = (period ?? string.Empty).Trim();
        if (periodValue.Length > 40)
        {
            throw new DomainException("Kỳ tối đa 40 ký tự.");
        }

        return new DisciplineRecord
        {
            UserId = userId,
            ClubId = clubId,
            DisciplineId = disciplineId,
            AppliedDate = appliedDate,
            Period = periodValue,
            Status = DisciplineRecordStatus.Active,
            AppliedBy = actor.Length == 0 ? "system" : actor,
            Notes = note.Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' '),
            CreatedAtUtc = now,
        };
    }

    public static DisciplineRecord Restore(
        Guid id,
        Guid userId,
        Guid clubId,
        Guid disciplineId,
        DateOnly appliedDate,
        string? period,
        DisciplineRecordStatus status,
        string appliedBy,
        string? notes,
        DateTimeOffset createdAtUtc)
    {
        var record = Create(userId, clubId, disciplineId, appliedDate, period, appliedBy, notes, createdAtUtc);
        record.Id = id;
        record.Status = status;

        return record;
    }

    public void Update(Guid disciplineId, DateOnly appliedDate, string? period, string? notes)
    {
        if (!IsActive)
        {
            throw new DomainException("Không thể sửa bản ghi đã thu hồi.");
        }

        if (disciplineId == Guid.Empty)
        {
            throw new DomainException("Danh mục không hợp lệ.");
        }

        if (appliedDate == default)
        {
            throw new DomainException("Ngày áp dụng là bắt buộc.");
        }

        var periodValue = (period ?? string.Empty).Trim();
        if (periodValue.Length > 40)
        {
            throw new DomainException("Kỳ tối đa 40 ký tự.");
        }

        var note = (notes ?? string.Empty).Trim();
        if (note.Length > 500)
        {
            throw new DomainException("Ghi chú tối đa 500 ký tự.");
        }

        DisciplineId = disciplineId;
        AppliedDate = appliedDate;
        Period = periodValue;
        Notes = note.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    public void Revoke()
    {
        if (!IsActive)
        {
            return;
        }

        Status = DisciplineRecordStatus.Revoked;
    }
}
