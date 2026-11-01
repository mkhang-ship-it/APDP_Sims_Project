using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Finance;

namespace SIMS.Application.Finance;

public sealed record InvoiceBatchInput(
    Guid ClubId,
    string Title,
    decimal TotalAmount,
    DateOnly DueDate,
    string? Period,
    string? Note);

/// <summary>Một dòng danh sách hoá đơn kèm số đã thu (tính từ transactions).</summary>
public sealed record InvoiceRow(Invoice Invoice, decimal PaidAmount)
{
    public decimal Remaining => Math.Max(0m, Invoice.TotalAmount - PaidAmount);

    public bool IsFullyPaid => Remaining == 0m;
}

public sealed record InvoiceDetail(Invoice Invoice, IReadOnlyList<Transaction> Transactions)
{
    public decimal PaidAmount => Transactions
        .Where(item => item.Type == TransactionType.Income)
        .Sum(item => item.PaidAmount);

    public decimal Remaining => Math.Max(0m, Invoice.TotalAmount - PaidAmount);
}

public sealed record InvoiceSummary(
    int Total,
    int Pending,
    int Paid,
    int Overdue,
    decimal TotalAmount,
    decimal PaidAmount)
{
    public decimal OutstandingAmount => Math.Max(0m, TotalAmount - PaidAmount);

    public static InvoiceSummary From(IEnumerable<InvoiceRow> rows)
    {
        var items = rows.ToArray();

        return new InvoiceSummary(
            items.Length,
            items.Count(row => row.Invoice.Status is InvoiceStatus.Pending or InvoiceStatus.Overdue),
            items.Count(row => row.Invoice.Status == InvoiceStatus.Paid),
            items.Count(row => row.Invoice.Status == InvoiceStatus.Overdue),
            items.Sum(row => row.Invoice.TotalAmount),
            items.Sum(row => row.PaidAmount));
    }
}

public interface IInvoiceService
{
    /// <summary>Tạo hoá đơn cho nhiều thành viên CLB trong một lần (giống store() của PHP).</summary>
    Task<Result<IReadOnlyList<Invoice>>> CreateAsync(
        InvoiceBatchInput input,
        IReadOnlyCollection<Guid> userIds,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<Invoice>> UpdateAsync(
        Guid id,
        string title,
        decimal totalAmount,
        DateOnly dueDate,
        string? period,
        string? note,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    /// <summary>Ghi nhận thanh toán (có thể nhiều lần); đủ tiền thì hoá đơn tự chuyển "đã thanh toán".</summary>
    Task<Result<Transaction>> RecordPaymentAsync(
        Guid id,
        decimal amount,
        DateOnly paymentDate,
        PaymentMethod method,
        string? note,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Đánh dấu quá hạn cho các hoá đơn chờ thanh toán đã quá hạn.</summary>
    Task<Result<int>> MarkOverdueAsync(
        Guid? clubId,
        DateOnly today,
        string actor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceRow>> ListAsync(
        Guid? clubId,
        InvoiceStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceRow>> ListForUserAsync(
        Guid? clubId,
        Guid userId,
        InvoiceStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<InvoiceDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class InvoiceService : IInvoiceService
{
    private const int MaxCodeAttempts = 50;

    private readonly IInvoiceRepository _invoices;
    private readonly ITransactionRepository _transactions;
    private readonly IInvoiceCodeGenerator _codes;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public InvoiceService(
        IInvoiceRepository invoices,
        ITransactionRepository transactions,
        IInvoiceCodeGenerator codes,
        IAuditLog audit,
        IClock clock)
    {
        _invoices = invoices;
        _transactions = transactions;
        _codes = codes;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<Invoice>>> CreateAsync(
        InvoiceBatchInput input,
        IReadOnlyCollection<Guid> userIds,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return Result<IReadOnlyList<Invoice>>.Fail(
                new ValidationError(nameof(userIds), "Chọn ít nhất một thành viên."));
        }

        var now = _clock.UtcNow;
        var year = now.UtcDateTime.Year;
        var created = new List<Invoice>(userIds.Count);
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var userId in userIds.Distinct())
            {
                var code = await NextCodeAsync(year, usedCodes, cancellationToken).ConfigureAwait(false);
                usedCodes.Add(code);

                created.Add(Invoice.Create(
                    code,
                    userId,
                    input.ClubId,
                    input.Title,
                    input.TotalAmount,
                    input.DueDate,
                    input.Period,
                    input.Note,
                    now));
            }
        }
        catch (DomainException ex)
        {
            return Result<IReadOnlyList<Invoice>>.Fail(new ValidationError(string.Empty, ex.Message));
        }

        foreach (var invoice in created)
        {
            await _invoices.AddAsync(invoice, cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            new AuditEntry(
                now,
                actor,
                "invoice.create",
                "invoice",
                created[0].Id.ToString("D"),
                $"count={created.Count}; codes={string.Join('|', created.Select(invoice => invoice.Code))}"),
            cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<Invoice>>.Ok(created);
    }

    public async Task<Result<Invoice>> UpdateAsync(
        Guid id,
        string title,
        decimal totalAmount,
        DateOnly dueDate,
        string? period,
        string? note,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Fail("Không tìm thấy hoá đơn.");
        }

        try
        {
            invoice.Update(title, totalAmount, dueDate, period, note, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _invoices.UpdateAsync(invoice, cancellationToken).ConfigureAwait(false);
        await AuditAsync("invoice.update", invoice, actor, cancellationToken).ConfigureAwait(false);

        return Result<Invoice>.Ok(invoice);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy hoá đơn."));
        }

        if (!invoice.IsEditable)
        {
            return Result.Fail(new ValidationError(
                string.Empty,
                "Chỉ có thể xoá hoá đơn đang chờ thanh toán."));
        }

        foreach (var transaction in await _transactions
                     .ListByInvoiceAsync(id, cancellationToken)
                     .ConfigureAwait(false))
        {
            await _transactions.DeleteAsync(transaction.Id, cancellationToken).ConfigureAwait(false);
        }

        await _invoices.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditAsync("invoice.delete", invoice, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<Result<Transaction>> RecordPaymentAsync(
        Guid id,
        decimal amount,
        DateOnly paymentDate,
        PaymentMethod method,
        string? note,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result<Transaction>.Fail(new ValidationError(string.Empty, "Không tìm thấy hoá đơn."));
        }

        if (invoice.Status == InvoiceStatus.Paid)
        {
            return Result<Transaction>.Fail(new ValidationError(string.Empty, "Hoá đơn đã thanh toán đủ."));
        }

        var transactions = await _transactions
            .ListByInvoiceAsync(id, cancellationToken)
            .ConfigureAwait(false);
        var paid = transactions.Where(item => item.Type == TransactionType.Income).Sum(item => item.PaidAmount);
        var remaining = invoice.TotalAmount - paid;

        if (amount <= 0m)
        {
            return Result<Transaction>.Fail(new ValidationError(nameof(amount), "Số tiền phải lớn hơn 0."));
        }

        if (amount > remaining)
        {
            return Result<Transaction>.Fail(new ValidationError(
                nameof(amount),
                $"Số tiền vượt quá số còn phải thu ({remaining.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})."));
        }

        var createdOn = DateOnly.FromDateTime(invoice.CreatedAtUtc.UtcDateTime);
        if (paymentDate < createdOn)
        {
            return Result<Transaction>.Fail(new ValidationError(
                nameof(paymentDate),
                "Ngày thanh toán không được trước ngày tạo hoá đơn."));
        }

        Transaction transaction;
        try
        {
            transaction = Transaction.CreateForInvoice(
                invoice.Id, invoice.ClubId, amount, paymentDate, method, note, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result<Transaction>.Fail(new ValidationError(string.Empty, ex.Message));
        }

        await _transactions.AddAsync(transaction, cancellationToken).ConfigureAwait(false);

        if (paid + transaction.PaidAmount >= invoice.TotalAmount)
        {
            invoice.MarkPaid(_clock.UtcNow);
            await _invoices.UpdateAsync(invoice, cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "invoice.payment",
                "invoice",
                invoice.Id.ToString("D"),
                $"code={invoice.Code}; amount={transaction.PaidAmount}; method={method}"),
            cancellationToken).ConfigureAwait(false);

        return Result<Transaction>.Ok(transaction);
    }

    public async Task<Result<int>> MarkOverdueAsync(
        Guid? clubId,
        DateOnly today,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var invoices = clubId is null
            ? await _invoices.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _invoices.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);

        var updated = 0;
        foreach (var invoice in invoices)
        {
            if (!invoice.MarkOverdue(today, _clock.UtcNow))
            {
                continue;
            }

            await _invoices.UpdateAsync(invoice, cancellationToken).ConfigureAwait(false);
            updated++;
        }

        if (updated > 0)
        {
            await _audit.WriteAsync(
                new AuditEntry(
                    _clock.UtcNow,
                    actor,
                    "invoice.overdue",
                    "invoice",
                    clubId?.ToString("D") ?? "all",
                    $"count={updated}; today={today:yyyy-MM-dd}"),
                cancellationToken).ConfigureAwait(false);
        }

        return Result<int>.Ok(updated);
    }

    public async Task<IReadOnlyList<InvoiceRow>> ListAsync(
        Guid? clubId,
        InvoiceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var invoices = clubId is null
            ? await _invoices.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _invoices.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);
        var transactions = await _transactions.ListAsync(cancellationToken).ConfigureAwait(false);

        return BuildRows(invoices, transactions, status);
    }

    public async Task<IReadOnlyList<InvoiceRow>> ListForUserAsync(
        Guid? clubId,
        Guid userId,
        InvoiceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var invoices = (await _invoices.ListByUserAsync(userId, cancellationToken).ConfigureAwait(false))
            .Where(invoice => clubId is null || invoice.ClubId == clubId)
            .ToArray();
        var transactions = await _transactions.ListAsync(cancellationToken).ConfigureAwait(false);

        return BuildRows(invoices, transactions, status);
    }

    public async Task<InvoiceDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return null;
        }

        var transactions = await _transactions
            .ListByInvoiceAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return new InvoiceDetail(
            invoice,
            transactions.OrderBy(item => item.PaymentDate).ToArray());
    }

    private static IReadOnlyList<InvoiceRow> BuildRows(
        IEnumerable<Invoice> invoices,
        IReadOnlyList<Transaction> transactions,
        InvoiceStatus? status)
    {
        var paidByInvoice = transactions
            .Where(item => item.InvoiceId is not null && item.Type == TransactionType.Income)
            .GroupBy(item => item.InvoiceId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.PaidAmount));

        return invoices
            .Where(invoice => status is null || invoice.Status == status)
            .OrderByDescending(invoice => invoice.CreatedAtUtc)
            .Select(invoice => new InvoiceRow(
                invoice,
                paidByInvoice.TryGetValue(invoice.Id, out var paid) ? paid : 0m))
            .ToArray();
    }

    private async Task<string> NextCodeAsync(
        int year,
        IReadOnlySet<string> usedCodes,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = _codes.Next(year);

            if (!usedCodes.Contains(code) &&
                !await _invoices.ExistsByCodeAsync(code, cancellationToken).ConfigureAwait(false))
            {
                return code;
            }
        }

        throw new DomainException("Không sinh được mã hoá đơn duy nhất, thử lại sau.");
    }

    private Task AuditAsync(string action, Invoice invoice, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                action,
                "invoice",
                invoice.Id.ToString("D"),
                $"code={invoice.Code}; status={invoice.Status}"),
            cancellationToken);

    private static Result<Invoice> Fail(string message)
        => Result<Invoice>.Fail(new ValidationError(string.Empty, message));
}
