using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Finance;

namespace SIMS.Application.Finance;

public interface IExpenseService
{
    /// <summary>
    /// Tạo chi phí. <paramref name="autoApprove"/> = true cho quản trị CLB (giống store() của PHP),
    /// false cho thành viên đề nghị (chờ duyệt).
    /// </summary>
    Task<Result<Expense>> CreateAsync(
        Guid clubId,
        string title,
        decimal amount,
        string? period,
        string? description,
        bool autoApprove,
        string requestedBy,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<Expense>> UpdateAsync(
        Guid id,
        string title,
        decimal amount,
        string? period,
        string? description,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<Result<Expense>> ApproveAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<Result<Expense>> RejectAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    /// <summary>Đánh dấu đã chi và ghi một giao dịch chi (tiền mặt) tương ứng.</summary>
    Task<Result<Expense>> MarkPaidAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Expense>> ListAsync(
        Guid? clubId,
        ExpenseStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Expense>> ListMineAsync(
        Guid? clubId,
        string requester,
        ExpenseStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transaction>> ListPaymentsAsync(
        Guid expenseId,
        CancellationToken cancellationToken = default);
}

public sealed class ExpenseService : IExpenseService
{
    private readonly IExpenseRepository _expenses;
    private readonly ITransactionRepository _transactions;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public ExpenseService(
        IExpenseRepository expenses,
        ITransactionRepository transactions,
        IAuditLog audit,
        IClock clock)
    {
        _expenses = expenses;
        _transactions = transactions;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<Expense>> CreateAsync(
        Guid clubId,
        string title,
        decimal amount,
        string? period,
        string? description,
        bool autoApprove,
        string requestedBy,
        string actor,
        CancellationToken cancellationToken = default)
    {
        Expense expense;
        try
        {
            expense = autoApprove
                ? Expense.CreateApproved(clubId, title, amount, requestedBy, period, description, _clock.UtcNow)
                : Expense.CreatePending(clubId, title, amount, requestedBy, period, description, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result<Expense>.Fail(new ValidationError(string.Empty, ex.Message));
        }

        await _expenses.AddAsync(expense, cancellationToken).ConfigureAwait(false);
        await AuditAsync("expense.create", expense, actor, cancellationToken).ConfigureAwait(false);

        return Result<Expense>.Ok(expense);
    }

    public async Task<Result<Expense>> UpdateAsync(
        Guid id,
        string title,
        decimal amount,
        string? period,
        string? description,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var expense = await _expenses.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (expense is null)
        {
            return Fail("Không tìm thấy chi phí.");
        }

        try
        {
            expense.Update(title, amount, period, description, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _expenses.UpdateAsync(expense, cancellationToken).ConfigureAwait(false);
        await AuditAsync("expense.update", expense, actor, cancellationToken).ConfigureAwait(false);

        return Result<Expense>.Ok(expense);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var expense = await _expenses.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (expense is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy chi phí."));
        }

        if (!expense.IsEditable)
        {
            return Result.Fail(new ValidationError(string.Empty, "Chỉ có thể xoá chi phí đang chờ duyệt."));
        }

        await _expenses.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditAsync("expense.delete", expense, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<Result<Expense>> ApproveAsync(
        Guid id,
        string actor,
        CancellationToken cancellationToken = default)
        => await ChangeStatusAsync(id, actor, "expense.approve", expense => expense.Approve(actor, _clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);

    public async Task<Result<Expense>> RejectAsync(
        Guid id,
        string actor,
        CancellationToken cancellationToken = default)
        => await ChangeStatusAsync(id, actor, "expense.reject", expense => expense.Reject(actor, _clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);

    public async Task<Result<Expense>> MarkPaidAsync(
        Guid id,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var expense = await _expenses.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (expense is null)
        {
            return Fail("Không tìm thấy chi phí.");
        }

        try
        {
            expense.MarkPaid(_clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _expenses.UpdateAsync(expense, cancellationToken).ConfigureAwait(false);

        var payment = Transaction.CreateForExpense(
            expense.Id,
            expense.ClubId,
            expense.Amount,
            DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            PaymentMethod.Cash,
            $"Thanh toán chi phí {expense.Title}",
            _clock.UtcNow);

        await _transactions.AddAsync(payment, cancellationToken).ConfigureAwait(false);
        await AuditAsync("expense.pay", expense, actor, cancellationToken).ConfigureAwait(false);

        return Result<Expense>.Ok(expense);
    }

    public async Task<IReadOnlyList<Expense>> ListAsync(
        Guid? clubId,
        ExpenseStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var expenses = clubId is null
            ? await _expenses.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _expenses.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);

        return expenses
            .Where(expense => status is null || expense.Status == status)
            .OrderByDescending(expense => expense.RequestedAtUtc)
            .ToArray();
    }

    public async Task<IReadOnlyList<Expense>> ListMineAsync(
        Guid? clubId,
        string requester,
        ExpenseStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var expenses = (await _expenses.ListByRequesterAsync(requester, cancellationToken).ConfigureAwait(false))
            .Where(expense => clubId is null || expense.ClubId == clubId)
            .Where(expense => status is null || expense.Status == status)
            .OrderByDescending(expense => expense.RequestedAtUtc)
            .ToArray();

        return expenses;
    }

    public Task<IReadOnlyList<Transaction>> ListPaymentsAsync(
        Guid expenseId,
        CancellationToken cancellationToken = default)
        => _transactions.ListByExpenseAsync(expenseId, cancellationToken);

    private async Task<Result<Expense>> ChangeStatusAsync(
        Guid id,
        string actor,
        string action,
        Action<Expense> change,
        CancellationToken cancellationToken)
    {
        var expense = await _expenses.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (expense is null)
        {
            return Fail("Không tìm thấy chi phí.");
        }

        try
        {
            change(expense);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _expenses.UpdateAsync(expense, cancellationToken).ConfigureAwait(false);
        await AuditAsync(action, expense, actor, cancellationToken).ConfigureAwait(false);

        return Result<Expense>.Ok(expense);
    }

    private Task AuditAsync(string action, Expense expense, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                action,
                "expense",
                expense.Id.ToString("D"),
                $"amount={expense.Amount}; status={expense.Status}"),
            cancellationToken);

    private static Result<Expense> Fail(string message)
        => Result<Expense>.Fail(new ValidationError(string.Empty, message));
}
