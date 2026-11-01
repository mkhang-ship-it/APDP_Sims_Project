using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Finance;

namespace SIMS.Infrastructure.Persistence;

/// <summary>invoices.csv — hoá đơn thu theo thành viên/CLB.</summary>
public sealed class CsvInvoiceRepository : CsvRepository<Invoice>, IInvoiceRepository
{
    private const int ColumnCount = 12;

    public CsvInvoiceRepository(string filePath)
        : base(
            filePath,
            "id",
            "invoice_code",
            "user_id",
            "club_id",
            "name",
            "total_amount",
            "due_date",
            "status",
            "period",
            "note",
            "created_at_utc",
            "updated_at_utc")
    {
    }

    public CsvInvoiceRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<Invoice?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(
            invoice => string.Equals(invoice.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await FindByCodeAsync(code, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<Invoice>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(invoice => invoice.ClubId == clubId).ToArray();
    }

    public async Task<IReadOnlyList<Invoice>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(invoice => invoice.UserId == userId).ToArray();
    }

    protected override string[] ToCells(Invoice invoice) =>
    [
        invoice.Id.ToString("D"),
        invoice.Code,
        invoice.UserId.ToString("D"),
        invoice.ClubId.ToString("D"),
        invoice.Title,
        invoice.TotalAmount.ToString("0.##", CultureInfo.InvariantCulture),
        invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        invoice.Status.ToString(),
        invoice.Period,
        invoice.Note,
        invoice.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        invoice.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Invoice? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[2], out var userId) ||
            !Guid.TryParse(cells[3], out var clubId) ||
            !decimal.TryParse(cells[5], NumberStyles.Number, CultureInfo.InvariantCulture, out var totalAmount) ||
            !DateOnly.TryParseExact(cells[6], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate) ||
            !Enum.TryParse<InvoiceStatus>(cells[7], ignoreCase: true, out var status) ||
            !DateTimeOffset.TryParse(cells[10], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt) ||
            !DateTimeOffset.TryParse(cells[11], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            return null;
        }

        try
        {
            return Invoice.Restore(
                id, cells[1], userId, clubId, cells[4], totalAmount, dueDate, status,
                cells[8], cells[9], createdAt, updatedAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

/// <summary>transactions.csv — sổ thu/chi gắn hoá đơn hoặc chi phí.</summary>
public sealed class CsvTransactionRepository : CsvRepository<Transaction>, ITransactionRepository
{
    private const int ColumnCount = 10;

    public CsvTransactionRepository(string filePath)
        : base(
            filePath,
            "id",
            "invoice_id",
            "expense_id",
            "type",
            "paid_amount",
            "payment_date",
            "payment_method",
            "note",
            "club_id",
            "created_at_utc")
    {
    }

    public CsvTransactionRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<Transaction>> ListByInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(item => item.InvoiceId == invoiceId).ToArray();
    }

    public async Task<IReadOnlyList<Transaction>> ListByExpenseAsync(
        Guid expenseId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(item => item.ExpenseId == expenseId).ToArray();
    }

    protected override string[] ToCells(Transaction transaction) =>
    [
        transaction.Id.ToString("D"),
        transaction.InvoiceId?.ToString("D") ?? string.Empty,
        transaction.ExpenseId?.ToString("D") ?? string.Empty,
        transaction.Type.ToString(),
        transaction.PaidAmount.ToString("0.##", CultureInfo.InvariantCulture),
        transaction.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        transaction.Method.ToString(),
        transaction.Note,
        transaction.ClubId.ToString("D"),
        transaction.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Transaction? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !TryGuid(cells[1], out var invoiceId) ||
            !TryGuid(cells[2], out var expenseId) ||
            !Enum.TryParse<TransactionType>(cells[3], ignoreCase: true, out var type) ||
            !decimal.TryParse(cells[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            !DateOnly.TryParseExact(cells[5], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var paymentDate) ||
            !Enum.TryParse<PaymentMethod>(cells[6], ignoreCase: true, out var method) ||
            !Guid.TryParse(cells[8], out var clubId) ||
            !DateTimeOffset.TryParse(cells[9], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return Transaction.Restore(
                id, invoiceId, expenseId, type, clubId, amount, paymentDate, method, cells[7], createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }

    private static bool TryGuid(string value, out Guid? id)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            id = null;
            return true;
        }

        if (Guid.TryParse(value, out var parsed))
        {
            id = parsed;
            return true;
        }

        id = null;
        return false;
    }
}

/// <summary>expenses.csv — đề nghị chi của CLB với luồng duyệt.</summary>
public sealed class CsvExpenseRepository : CsvRepository<Expense>, IExpenseRepository
{
    private const int ColumnCount = 13;

    public CsvExpenseRepository(string filePath)
        : base(
            filePath,
            "id",
            "name",
            "amount",
            "requested_by",
            "approved_by",
            "approved_at_utc",
            "requested_at_utc",
            "status",
            "club_id",
            "period",
            "description",
            "created_at_utc",
            "updated_at_utc")
    {
    }

    public CsvExpenseRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<Expense>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(expense => expense.ClubId == clubId).ToArray();
    }

    public async Task<IReadOnlyList<Expense>> ListByRequesterAsync(
        string requester,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items
            .Where(expense => string.Equals(expense.RequestedBy, requester, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    protected override string[] ToCells(Expense expense) =>
    [
        expense.Id.ToString("D"),
        expense.Title,
        expense.Amount.ToString("0.##", CultureInfo.InvariantCulture),
        expense.RequestedBy,
        expense.ApprovedBy,
        expense.ApprovedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        expense.RequestedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        expense.Status.ToString(),
        expense.ClubId.ToString("D"),
        expense.Period,
        expense.Description,
        expense.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        expense.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Expense? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !decimal.TryParse(cells[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            !TryDate(cells[5], out var approvedAt) ||
            !DateTimeOffset.TryParse(cells[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var requestedAt) ||
            !Enum.TryParse<ExpenseStatus>(cells[7], ignoreCase: true, out var status) ||
            !Guid.TryParse(cells[8], out var clubId) ||
            !DateTimeOffset.TryParse(cells[11], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt) ||
            !DateTimeOffset.TryParse(cells[12], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            return null;
        }

        try
        {
            return Expense.Restore(
                id, clubId, cells[1], amount, cells[3], cells[4], approvedAt, requestedAt, status,
                cells[9], cells[10], createdAt, updatedAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }

    private static bool TryDate(string value, out DateTimeOffset? date)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            date = null;
            return true;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            date = parsed;
            return true;
        }

        date = null;
        return false;
    }
}
