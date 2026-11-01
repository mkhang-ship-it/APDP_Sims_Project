using System.Text.RegularExpressions;
using SIMS.Domain.Common;

namespace SIMS.Domain.Finance;

public enum InvoiceStatus
{
    Pending,
    Paid,
    Overdue,
}

/// <summary>Hoá đơn thu của một thành viên trong CLB (mã INV-{năm}-{6 số}).</summary>
public sealed class Invoice : Entity
{
    private static readonly Regex CodePattern = new(@"^INV-\d{4}-\d{6}$", RegexOptions.Compiled);

    private Invoice()
    {
        Code = string.Empty;
        Title = string.Empty;
        Period = string.Empty;
        Note = string.Empty;
    }

    public string Code { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ClubId { get; private set; }

    public string Title { get; private set; }

    public decimal TotalAmount { get; private set; }

    public DateOnly DueDate { get; private set; }

    public InvoiceStatus Status { get; private set; }

    /// <summary>Kỳ thu (thay cho semester_id của PHP — module học kỳ chưa port).</summary>
    public string Period { get; private set; }

    public string Note { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Invoice Create(
        string code,
        Guid userId,
        Guid clubId,
        string title,
        decimal totalAmount,
        DateOnly dueDate,
        string? period,
        string? note,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thành viên không hợp lệ.");
        }

        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        return new Invoice
        {
            Code = RequireCode(code),
            UserId = userId,
            ClubId = clubId,
            Title = RequireText(title, 255, "Tên hoá đơn"),
            TotalAmount = RequireAmount(totalAmount),
            DueDate = RequireDueDate(dueDate),
            Status = InvoiceStatus.Pending,
            Period = OptionalText(period, 40, "Kỳ thu"),
            Note = OptionalText(note, 1000, "Ghi chú"),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public static Invoice Restore(
        Guid id,
        string code,
        Guid userId,
        Guid clubId,
        string title,
        decimal totalAmount,
        DateOnly dueDate,
        InvoiceStatus status,
        string? period,
        string? note,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var invoice = Create(code, userId, clubId, title, totalAmount, dueDate, period, note, createdAtUtc);
        invoice.Id = id;
        invoice.Status = status;
        invoice.UpdatedAtUtc = updatedAtUtc;

        return invoice;
    }

    public bool IsEditable => Status == InvoiceStatus.Pending;

    public bool IsPastDue(DateOnly today) => Status == InvoiceStatus.Pending && DueDate < today;

    public void Update(
        string title,
        decimal totalAmount,
        DateOnly dueDate,
        string? period,
        string? note,
        DateTimeOffset now)
    {
        if (!IsEditable)
        {
            throw new DomainException("Chỉ có thể sửa hoá đơn đang chờ thanh toán.");
        }

        Title = RequireText(title, 255, "Tên hoá đơn");
        TotalAmount = RequireAmount(totalAmount);
        DueDate = RequireDueDate(dueDate);
        Period = OptionalText(period, 40, "Kỳ thu");
        Note = OptionalText(note, 1000, "Ghi chú");
        UpdatedAtUtc = now;
    }

    public void MarkPaid(DateTimeOffset now)
    {
        if (Status == InvoiceStatus.Paid)
        {
            return;
        }

        Status = InvoiceStatus.Paid;
        UpdatedAtUtc = now;
    }

    /// <summary>Chuyển quá hạn; trả về true nếu trạng thái thay đổi.</summary>
    public bool MarkOverdue(DateOnly today, DateTimeOffset now)
    {
        if (!IsPastDue(today))
        {
            return false;
        }

        Status = InvoiceStatus.Overdue;
        UpdatedAtUtc = now;

        return true;
    }

    private static string RequireCode(string code)
    {
        var value = (code ?? string.Empty).Trim();

        if (!CodePattern.IsMatch(value))
        {
            throw new DomainException("Mã hoá đơn phải có dạng INV-YYYY-NNNNNN.");
        }

        return value;
    }

    private static decimal RequireAmount(decimal amount)
    {
        if (amount < 0m || amount >= 10_000_000_000m)
        {
            throw new DomainException("Số tiền hoá đơn phải từ 0 đến dưới 10 tỷ.");
        }

        return decimal.Round(amount, 2);
    }

    private static DateOnly RequireDueDate(DateOnly dueDate)
        => dueDate == default ? throw new DomainException("Hạn thanh toán là bắt buộc.") : dueDate;

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

        // CSV đọc theo dòng: không lưu xuống dòng trong ghi chú.
        return text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }
}
