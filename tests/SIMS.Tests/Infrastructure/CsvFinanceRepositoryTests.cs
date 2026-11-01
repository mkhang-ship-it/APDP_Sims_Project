using SIMS.Domain.Finance;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvFinanceRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 1);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-finance-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task Invoice_round_trip_keeps_code_status_and_scope()
    {
        var repository = new CsvInvoiceRepository(PathFor("invoices.csv"));
        var clubId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var invoice = Invoice.Create(
            "INV-2026-000123", userId, clubId, "Phí sinh hoạt, học kỳ 1",
            200_000.5m, Today.AddDays(7), "HK1", "Ghi chú có, dấu phẩy", Now);

        await repository.AddAsync(invoice);

        var byCode = await repository.FindByCodeAsync("inv-2026-000123");
        var byClub = await repository.ListByClubAsync(clubId);
        var byUser = await repository.ListByUserAsync(userId);

        Assert.NotNull(byCode);
        Assert.Equal("Phí sinh hoạt, học kỳ 1", byCode!.Title);
        Assert.Equal("Ghi chú có, dấu phẩy", byCode.Note);
        Assert.Equal(200_000.5m, byCode.TotalAmount);
        Assert.Equal(Today.AddDays(7), byCode.DueDate);
        Assert.Single(byClub);
        Assert.Single(byUser);
        Assert.True(await repository.ExistsByCodeAsync("INV-2026-000123"));
        Assert.False(await repository.ExistsByCodeAsync("INV-2026-999999"));
    }

    [Fact]
    public async Task Invoice_update_persists_paid_status()
    {
        var repository = new CsvInvoiceRepository(PathFor("invoices.csv"));
        var invoice = Invoice.Create(
            "INV-2026-000001", Guid.NewGuid(), Guid.NewGuid(), "Hoá đơn", 1000m, Today, null, null, Now);
        await repository.AddAsync(invoice);

        invoice.MarkPaid(Now.AddMinutes(5));
        await repository.UpdateAsync(invoice);

        var loaded = await repository.FindByIdAsync(invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, loaded!.Status);
    }

    [Fact]
    public async Task Transaction_round_trip_keeps_optional_owner_and_method()
    {
        var repository = new CsvTransactionRepository(PathFor("transactions.csv"));
        var invoiceId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        var clubId = Guid.NewGuid();

        var income = Transaction.CreateForInvoice(
            invoiceId, clubId, 50_000m, Today, PaymentMethod.Sepay, "Chuyển khoản", Now);
        var outcome = Transaction.CreateForExpense(
            expenseId, clubId, 20_000m, Today.AddDays(1), PaymentMethod.Cash, null, Now);
        await repository.AddAsync(income);
        await repository.AddAsync(outcome);

        var forInvoice = await repository.ListByInvoiceAsync(invoiceId);
        var forExpense = await repository.ListByExpenseAsync(expenseId);

        Assert.Single(forInvoice);
        Assert.Equal(TransactionType.Income, forInvoice[0].Type);
        Assert.Equal(PaymentMethod.Sepay, forInvoice[0].Method);
        Assert.Null(forInvoice[0].ExpenseId);
        Assert.Single(forExpense);
        Assert.Equal(TransactionType.Outcome, forExpense[0].Type);
        Assert.Null(forExpense[0].InvoiceId);
    }

    [Fact]
    public async Task Expense_round_trip_keeps_workflow_fields()
    {
        var repository = new CsvExpenseRepository(PathFor("expenses.csv"));
        var clubId = Guid.NewGuid();
        var pending = Expense.CreatePending(
            clubId, "Mua giấy", 100_000m, "student1", "HK1", "Mô tả, có phẩy", Now);
        var approved = Expense.CreateApproved(
            clubId, "Mua mực", 250_000m, "faculty1", "HK1", null, Now);

        await repository.AddAsync(pending);
        await repository.AddAsync(approved);
        pending.Approve("faculty1", Now.AddHours(1));
        await repository.UpdateAsync(pending);

        var byClub = await repository.ListByClubAsync(clubId);
        var byRequester = await repository.ListByRequesterAsync("STUDENT1");
        var reloaded = await repository.FindByIdAsync(pending.Id);

        Assert.Equal(2, byClub.Count);
        Assert.Single(byRequester);
        Assert.Equal("Mô tả, có phẩy", reloaded!.Description);
        Assert.Equal(ExpenseStatus.Approved, reloaded.Status);
        Assert.Equal("faculty1", reloaded.ApprovedBy);
        Assert.NotNull(reloaded.ApprovedAtUtc);
    }
}
