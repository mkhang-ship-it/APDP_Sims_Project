using SIMS.Domain.Common;

namespace SIMS.Domain.Finance;

public enum TransactionType
{
    Income,
    Outcome,
}

public enum PaymentMethod
{
    Sepay,
    PayOS,
    Paykit,
    Cash,
}

/// <summary>Giao dịch thu (hoá đơn) hoặc chi (chi phí) — gắn đúng một trong hai.</summary>
public sealed class Transaction : Entity
{
    private Transaction() => Note = string.Empty;

    public Guid? InvoiceId { get; private set; }

    public Guid? ExpenseId { get; private set; }

    public TransactionType Type { get; private set; }

    public decimal PaidAmount { get; private set; }

    public DateOnly PaymentDate { get; private set; }

    public PaymentMethod Method { get; private set; }

    public string Note { get; private set; }

    public Guid ClubId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Transaction CreateForInvoice(
        Guid invoiceId,
        Guid clubId,
        decimal amount,
        DateOnly paymentDate,
        PaymentMethod method,
        string? note,
        DateTimeOffset now)
        => Create(invoiceId, null, TransactionType.Income, clubId, amount, paymentDate, method, note, now);

    public static Transaction CreateForExpense(
        Guid expenseId,
        Guid clubId,
        decimal amount,
        DateOnly paymentDate,
        PaymentMethod method,
        string? note,
        DateTimeOffset now)
        => Create(null, expenseId, TransactionType.Outcome, clubId, amount, paymentDate, method, note, now);

    public static Transaction Restore(
        Guid id,
        Guid? invoiceId,
        Guid? expenseId,
        TransactionType type,
        Guid clubId,
        decimal paidAmount,
        DateOnly paymentDate,
        PaymentMethod method,
        string? note,
        DateTimeOffset createdAtUtc)
    {
        var transaction = Create(
            invoiceId, expenseId, type, clubId, paidAmount, paymentDate, method, note, createdAtUtc);
        transaction.Id = id;

        return transaction;
    }

    private static Transaction Create(
        Guid? invoiceId,
        Guid? expenseId,
        TransactionType type,
        Guid clubId,
        decimal amount,
        DateOnly paymentDate,
        PaymentMethod method,
        string? note,
        DateTimeOffset now)
    {
        if ((invoiceId is null) == (expenseId is null))
        {
            throw new DomainException("Giao dịch phải gắn với đúng một hoá đơn hoặc chi phí.");
        }

        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        if (type == TransactionType.Income && invoiceId is null)
        {
            throw new DomainException("Giao dịch thu phải gắn với hoá đơn.");
        }

        if (type == TransactionType.Outcome && expenseId is null)
        {
            throw new DomainException("Giao dịch chi phải gắn với chi phí.");
        }

        if (amount is <= 0m or >= 10_000_000_000m)
        {
            throw new DomainException("Số tiền giao dịch phải lớn hơn 0 và dưới 10 tỷ.");
        }

        if (paymentDate == default)
        {
            throw new DomainException("Ngày thanh toán là bắt buộc.");
        }

        var normalizedNote = (note ?? string.Empty).Trim();
        if (normalizedNote.Length > 500)
        {
            throw new DomainException("Ghi chú giao dịch tối đa 500 ký tự.");
        }

        return new Transaction
        {
            InvoiceId = invoiceId,
            ExpenseId = expenseId,
            Type = type,
            PaidAmount = decimal.Round(amount, 2),
            PaymentDate = paymentDate,
            Method = method,
            Note = normalizedNote
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' '),
            ClubId = clubId,
            CreatedAtUtc = now,
        };
    }
}
