using System.Text.RegularExpressions;
using SIMS.Domain.Common;

namespace SIMS.Domain.Awards;

public enum AwardStatus
{
    Pending,
    Awarded,
    Revoked,
}

/// <summary>Giải thưởng/khen thưởng của CLB (mã BK-######) với nhiều người nhận.</summary>
public sealed class Award : Entity
{
    private static readonly Regex CodePattern = new(@"^BK-\d{6}$", RegexOptions.Compiled);

    private Award()
    {
        Code = string.Empty;
        Title = string.Empty;
        Period = string.Empty;
        Notes = string.Empty;
    }

    public Guid ClubId { get; private set; }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public string Period { get; private set; }

    public DateOnly AwardDate { get; private set; }

    public AwardStatus Status { get; private set; }

    public string Notes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Award Create(
        Guid clubId,
        string code,
        string title,
        string? period,
        DateOnly awardDate,
        AwardStatus status,
        string? notes,
        DateTimeOffset now)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        if (awardDate == default)
        {
            throw new DomainException("Ngày trao thưởng là bắt buộc.");
        }

        return new Award
        {
            ClubId = clubId,
            Code = RequireCode(code),
            Title = RequireText(title, 255, "Tên giải thưởng"),
            Period = OptionalText(period, 40, "Kỳ"),
            AwardDate = awardDate,
            Status = status,
            Notes = OptionalText(notes, 1000, "Ghi chú"),
            CreatedAtUtc = now,
        };
    }

    public static Award Restore(
        Guid id,
        Guid clubId,
        string code,
        string title,
        string? period,
        DateOnly awardDate,
        AwardStatus status,
        string? notes,
        DateTimeOffset createdAtUtc)
    {
        var award = Create(clubId, code, title, period, awardDate, status, notes, createdAtUtc);
        award.Id = id;

        return award;
    }

    public void Update(string title, string? period, DateOnly awardDate, AwardStatus status, string? notes)
    {
        if (awardDate == default)
        {
            throw new DomainException("Ngày trao thưởng là bắt buộc.");
        }

        Title = RequireText(title, 255, "Tên giải thưởng");
        Period = OptionalText(period, 40, "Kỳ");
        AwardDate = awardDate;
        Status = status;
        Notes = OptionalText(notes, 1000, "Ghi chú");
    }

    private static string RequireCode(string code)
    {
        var value = (code ?? string.Empty).Trim();

        return CodePattern.IsMatch(value)
            ? value
            : throw new DomainException("Mã giải thưởng phải có dạng BK-NNNNNN.");
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

/// <summary>Một thành viên nhận giải thưởng, kèm file chứng nhận (tuỳ chọn).</summary>
public sealed class AwardRecipient : Entity
{
    private AwardRecipient() => Attachment = string.Empty;

    public Guid AwardId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Khoá tương đối trong kho file; rỗng nếu không đính kèm.</summary>
    public string Attachment { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static AwardRecipient Create(
        Guid awardId,
        Guid userId,
        string? attachment,
        DateTimeOffset now)
    {
        if (awardId == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainException("Giải thưởng hoặc thành viên không hợp lệ.");
        }

        var file = (attachment ?? string.Empty).Trim();
        if (file.Length > 500)
        {
            throw new DomainException("Đường dẫn tệp đính kèm quá dài.");
        }

        return new AwardRecipient
        {
            AwardId = awardId,
            UserId = userId,
            Attachment = file,
            CreatedAtUtc = now,
        };
    }

    public static AwardRecipient Restore(
        Guid id,
        Guid awardId,
        Guid userId,
        string? attachment,
        DateTimeOffset createdAtUtc)
    {
        var recipient = Create(awardId, userId, attachment, createdAtUtc);
        recipient.Id = id;

        return recipient;
    }

    public void SetAttachment(string? attachment)
    {
        var file = (attachment ?? string.Empty).Trim();

        if (file.Length > 500)
        {
            throw new DomainException("Đường dẫn tệp đính kèm quá dài.");
        }

        Attachment = file;
    }
}
