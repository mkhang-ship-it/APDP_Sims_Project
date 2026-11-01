using SIMS.Domain.Finance;

namespace SIMS.Application.Abstractions;

public interface IInvoiceRepository : IRepository<Invoice>
{
    Task<Invoice?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Invoice>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Invoice>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface ITransactionRepository : IRepository<Transaction>
{
    Task<IReadOnlyList<Transaction>> ListByInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transaction>> ListByExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default);
}

public interface IExpenseRepository : IRepository<Expense>
{
    Task<IReadOnlyList<Expense>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Expense>> ListByRequesterAsync(
        string requester,
        CancellationToken cancellationToken = default);
}

/// <summary>Strategy sinh mã hoá đơn (mặc định: INV-{năm}-{6 số ngẫu nhiên}).</summary>
public interface IInvoiceCodeGenerator
{
    string Next(int year);
}
