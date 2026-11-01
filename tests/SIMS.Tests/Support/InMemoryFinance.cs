using SIMS.Application.Abstractions;
using SIMS.Domain.Finance;

namespace SIMS.Tests.Support;

public sealed class InMemoryInvoiceRepository : InMemoryRepository<Invoice>, IInvoiceRepository
{
    public Task<Invoice?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(
            invoice => string.Equals(invoice.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(
            invoice => string.Equals(invoice.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Invoice>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Invoice>>(Items.Where(invoice => invoice.ClubId == clubId).ToArray());

    public Task<IReadOnlyList<Invoice>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Invoice>>(Items.Where(invoice => invoice.UserId == userId).ToArray());
}

public sealed class InMemoryTransactionRepository : InMemoryRepository<Transaction>, ITransactionRepository
{
    public Task<IReadOnlyList<Transaction>> ListByInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Transaction>>(
            Items.Where(item => item.InvoiceId == invoiceId).ToArray());

    public Task<IReadOnlyList<Transaction>> ListByExpenseAsync(
        Guid expenseId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Transaction>>(
            Items.Where(item => item.ExpenseId == expenseId).ToArray());
}

public sealed class InMemoryExpenseRepository : InMemoryRepository<Expense>, IExpenseRepository
{
    public Task<IReadOnlyList<Expense>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Expense>>(Items.Where(expense => expense.ClubId == clubId).ToArray());

    public Task<IReadOnlyList<Expense>> ListByRequesterAsync(
        string requester,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Expense>>(Items
            .Where(expense => string.Equals(expense.RequestedBy, requester, StringComparison.OrdinalIgnoreCase))
            .ToArray());
}

/// <summary>Generator xác định để test: trả lần lượt các mã đã đặt, sau đó tăng dần.</summary>
public sealed class SequenceInvoiceCodeGenerator : IInvoiceCodeGenerator
{
    private readonly Queue<string> _codes;
    private int _counter;

    public SequenceInvoiceCodeGenerator(params string[] codes) => _codes = new Queue<string>(codes);

    public string Next(int year)
        => _codes.Count > 0 ? _codes.Dequeue() : $"INV-{year:D4}-{++_counter:D6}";
}
