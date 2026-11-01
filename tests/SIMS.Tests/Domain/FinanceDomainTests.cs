using SIMS.Domain.Common;
using SIMS.Domain.Finance;

namespace SIMS.Tests.Domain;

public sealed class FinanceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Fact]
    public void Invoice_code_must_match_pattern_and_title_is_trimmed()
    {
        var invoice = Invoice.Create(
            "INV-2026-000123", Guid.NewGuid(), Guid.NewGuid(), "  Phí sinh hoạt  ",
            200_000m, Today.AddDays(7), "HK1", "Ghi chú", Now);

        Assert.Equal("Phí sinh hoạt", invoice.Title);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.True(invoice.IsEditable);
        Assert.Throws<DomainException>(() => Invoice.Create(
            "HD-2026-1", Guid.NewGuid(), Guid.NewGuid(), "Sai mã", 1000m, Today, null, null, Now));
    }

    [Fact]
    public void Invoice_rejects_invalid_amount_and_missing_ids()
    {
        Assert.Throws<DomainException>(() => Invoice.Create(
            "INV-2026-000001", Guid.Empty, Guid.NewGuid(), "Hoá đơn", 1000m, Today, null, null, Now));
        Assert.Throws<DomainException>(() => Invoice.Create(
            "INV-2026-000001", Guid.NewGuid(), Guid.Empty, "Hoá đơn", 1000m, Today, null, null, Now));
        Assert.Throws<DomainException>(() => Invoice.Create(
            "INV-2026-000001", Guid.NewGuid(), Guid.NewGuid(), "Hoá đơn", -1m, Today, null, null, Now));
        Assert.Throws<DomainException>(() => Invoice.Create(
            "INV-2026-000001", Guid.NewGuid(), Guid.NewGuid(), "Hoá đơn", 1000m, default, null, null, Now));
    }

    [Fact]
    public void Invoice_status_moves_to_paid_and_overdue_once()
    {
        var invoice = Invoice.Create(
            "INV-2026-000001", Guid.NewGuid(), Guid.NewGuid(), "Hoá đơn", 1000m, Today, null, null, Now);

        Assert.False(invoice.MarkOverdue(Today, Now));
        Assert.True(invoice.MarkOverdue(Today.AddDays(1), Now));
        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);

        invoice.MarkPaid(Now);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.False(invoice.IsEditable);
        Assert.Throws<DomainException>(() => invoice.Update("Sửa", 1m, Today, null, null, Now));
    }

    [Fact]
    public void Transaction_requires_exactly_one_owner_and_positive_amount()
    {
        var invoiceId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        var clubId = Guid.NewGuid();

        var income = Transaction.CreateForInvoice(
            invoiceId, clubId, 50_000m, Today, PaymentMethod.Cash, "Đóng tiền", Now);

        Assert.Equal(TransactionType.Income, income.Type);
        Assert.Equal(invoiceId, income.InvoiceId);
        Assert.Null(income.ExpenseId);

        var outcome = Transaction.CreateForExpense(
            expenseId, clubId, 20_000m, Today, PaymentMethod.Cash, null, Now);

        Assert.Equal(TransactionType.Outcome, outcome.Type);
        Assert.Equal(expenseId, outcome.ExpenseId);

        Assert.Throws<DomainException>(() => Transaction.CreateForInvoice(
            invoiceId, clubId, 0m, Today, PaymentMethod.Cash, null, Now));
        Assert.Throws<DomainException>(() => Transaction.Restore(
            Guid.NewGuid(), invoiceId, expenseId, TransactionType.Income, clubId,
            1000m, Today, PaymentMethod.Cash, null, Now));
    }

    [Fact]
    public void Expense_workflow_guards_status_changes()
    {
        var clubId = Guid.NewGuid();
        var pending = Expense.CreatePending(clubId, "Mua giấy", 100_000m, "sv01", "HK1", null, Now);

        Assert.Equal(ExpenseStatus.Pending, pending.Status);
        Assert.True(pending.IsEditable);

        pending.Update("Mua giấy A4", 120_000m, "HK1", "Ghi chú", Now);
        Assert.Equal("Mua giấy A4", pending.Title);
        Assert.Equal(120_000m, pending.Amount);

        pending.Approve("faculty1", Now);
        Assert.Equal(ExpenseStatus.Approved, pending.Status);
        Assert.Equal("faculty1", pending.ApprovedBy);
        Assert.False(pending.IsEditable);
        Assert.True(pending.CanBePaid);

        Assert.Throws<DomainException>(() => pending.Reject("faculty1", Now));

        pending.MarkPaid(Now);
        Assert.Equal(ExpenseStatus.Paid, pending.Status);
        Assert.Throws<DomainException>(() => pending.MarkPaid(Now));
        Assert.Throws<DomainException>(() => pending.Update("Sửa", 1m, null, null, Now));
    }

    [Fact]
    public void Expense_rejects_negative_amount_and_blank_title()
    {
        var clubId = Guid.NewGuid();

        Assert.Throws<DomainException>(() =>
            Expense.CreatePending(clubId, "  ", 1000m, "sv01", null, null, Now));
        Assert.Throws<DomainException>(() =>
            Expense.CreatePending(clubId, "Hợp lệ", -1m, "sv01", null, null, Now));
        Assert.Throws<DomainException>(() =>
            Expense.CreateApproved(Guid.Empty, "Hợp lệ", 1000m, "admin", null, null, Now));
    }
}
