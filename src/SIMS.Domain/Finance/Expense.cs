using SIMS.Domain.Common;

namespace SIMS.Domain.Finance;

public enum ExpenseStatus
{
    Pending,
    Approved,
    Rejected,
    Paid,
}

/// <summary>Đề nghị chi của CLB: chờ duyệt → duyệt/từ chối → đã chi.</summary>
public sealed class Expense : Entity
{
    private Expense()
    {
        Title = string.Empty;
        RequestedBy = string.Empty;
        ApprovedBy = string.Empty;
        Period = string.Empty;
        Description = string.Empty;
    }

    public string Title { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Tên đăng nhập người đề nghị (snapshot — tránh join users.csv khi hiển thị).</summary>
    public string RequestedBy { get; private set; }

    public string ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public ExpenseStatus Status { get; private set; }

    public Guid ClubId { get; private set; }

    public string Period { get; private set; }

    public string Description { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsEditable => Status == ExpenseStatus.Pending;

    public bool CanBePaid => Status == ExpenseStatus.Approved;

    /// <summary>Chi phí do quản trị CLB tạo được duyệt sẵn (giống store() của PHP).</summary>
    public static Expense CreateApproved(
        Guid clubId,
        string title,
        decimal amount,
        string requestedBy,
        string? period,
        string? description,
        DateTimeOffset now)
        => Create(clubId, title, amount, requestedBy, period, description, ExpenseStatus.Approved, now);

    public static Expense CreatePending(
        Guid clubId,
        string title,
        decimal amount,
        string requestedBy,
        string? period,
        string? description,
        DateTimeOffset now)
        => Create(clubId, title, amount, requestedBy, period, description, ExpenseStatus.Pending, now);

    public static Expense Restore(
        Guid id,
        Guid clubId,
        string title,
        decimal amount,
        string requestedBy,
        string approvedBy,
        DateTimeOffset? approvedAtUtc,
        DateTimeOffset requestedAtUtc,
        ExpenseStatus status,
        string? period,
        string? description,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var expense = Create(
            clubId, title, amount, requestedBy, period, description, status, createdAtUtc);
        expense.Id = id;
        expense.RequestedAtUtc = requestedAtUtc;
        expense.ApprovedBy = approvedBy;
        expense.ApprovedAtUtc = approvedAtUtc;
        expense.UpdatedAtUtc = updatedAtUtc;

        return expense;
    }

    public void Update(
        string title,
        decimal amount,
        string? period,
        string? description,
        DateTimeOffset now)
    {
        if (!IsEditable)
        {
            throw new DomainException("Chỉ có thể sửa chi phí đang chờ duyệt.");
        }

        Title = RequireText(title, 255, "Tên chi phí");
        Amount = RequireAmount(amount);
        Period = OptionalText(period, 40, "Kỳ");
        Description = OptionalText(description, 1000, "Mô tả");
        UpdatedAtUtc = now;
    }

    public void Approve(string actor, DateTimeOffset now)
    {
        if (Status != ExpenseStatus.Pending)
        {
            throw new DomainException("Chỉ có thể duyệt chi phí đang chờ duyệt.");
        }

        Status = ExpenseStatus.Approved;
        ApprovedBy = RequireActor(actor);
        ApprovedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void Reject(string actor, DateTimeOffset now)
    {
        if (Status != ExpenseStatus.Pending)
        {
            throw new DomainException("Chỉ có thể từ chối chi phí đang chờ duyệt.");
        }

        Status = ExpenseStatus.Rejected;
        ApprovedBy = RequireActor(actor);
        ApprovedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void MarkPaid(DateTimeOffset now)
    {
        if (!CanBePaid)
        {
            throw new DomainException("Chỉ có thể đánh dấu đã chi cho chi phí đã được duyệt.");
        }

        Status = ExpenseStatus.Paid;
        UpdatedAtUtc = now;
    }

    private static Expense Create(
        Guid clubId,
        string title,
        decimal amount,
        string requestedBy,
        string? period,
        string? description,
        ExpenseStatus status,
        DateTimeOffset now)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        var expense = new Expense
        {
            ClubId = clubId,
            Title = RequireText(title, 255, "Tên chi phí"),
            Amount = RequireAmount(amount),
            RequestedBy = RequireActor(requestedBy),
            ApprovedBy = string.Empty,
            ApprovedAtUtc = null,
            RequestedAtUtc = now,
            Status = status,
            Period = OptionalText(period, 40, "Kỳ"),
            Description = OptionalText(description, 1000, "Mô tả"),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        return expense;
    }

    private static decimal RequireAmount(decimal amount)
    {
        if (amount < 0m || amount >= 10_000_000_000m)
        {
            throw new DomainException("Số tiền chi phí phải từ 0 đến dưới 10 tỷ.");
        }

        return decimal.Round(amount, 2);
    }

    private static string RequireActor(string actor)
    {
        var value = (actor ?? string.Empty).Trim();

        return value.Length == 0 ? "system" : value;
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
