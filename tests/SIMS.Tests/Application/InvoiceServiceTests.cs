using SIMS.Application.Finance;
using SIMS.Domain.Finance;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class InvoiceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 1);

    private sealed record Harness(
        InvoiceService Service,
        InMemoryInvoiceRepository Invoices,
        InMemoryTransactionRepository Transactions,
        RecordingAuditLog Audit);

    private static Harness Create(params string[] codes)
    {
        var invoices = new InMemoryInvoiceRepository();
        var transactions = new InMemoryTransactionRepository();
        var audit = new RecordingAuditLog();
        var service = new InvoiceService(
            invoices,
            transactions,
            new SequenceInvoiceCodeGenerator(codes),
            audit,
            new FakeClock(Now));

        return new Harness(service, invoices, transactions, audit);
    }

    private static InvoiceBatchInput Input(
        Guid clubId,
        decimal amount = 200_000m,
        DateOnly? dueDate = null)
        => new(clubId, "Phí sinh hoạt", amount, dueDate ?? Today.AddDays(7), "HK1", "Ghi chú");

    [Fact]
    public async Task Create_makes_one_invoice_per_member_with_unique_codes()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();

        var result = await harness.Service.CreateAsync(
            Input(clubId), new[] { firstUser, secondUser }, "faculty1");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(2, result.Value.Select(invoice => invoice.Code).Distinct().Count());
        Assert.All(result.Value, invoice => Assert.Equal(InvoiceStatus.Pending, invoice.Status));
        Assert.Equal(2, harness.Invoices.All.Count);
        Assert.Equal("invoice.create", harness.Audit.Entries[0].Action);
    }

    [Fact]
    public async Task Create_requires_members_and_valid_input()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();

        var noMembers = await harness.Service.CreateAsync(Input(clubId), Array.Empty<Guid>(), "faculty1");
        var negative = await harness.Service.CreateAsync(
            Input(clubId, -5m), new[] { Guid.NewGuid() }, "faculty1");
        var blankTitle = await harness.Service.CreateAsync(
            new InvoiceBatchInput(clubId, "   ", 1000m, Today, null, null),
            new[] { Guid.NewGuid() },
            "faculty1");

        Assert.False(noMembers.IsSuccess);
        Assert.False(negative.IsSuccess);
        Assert.False(blankTitle.IsSuccess);
        Assert.Empty(harness.Invoices.All);
    }

    [Fact]
    public async Task Partial_payment_then_remaining_marks_invoice_paid()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var created = await harness.Service.CreateAsync(Input(clubId), new[] { Guid.NewGuid() }, "faculty1");
        var invoiceId = created.Value![0].Id;

        var partial = await harness.Service.RecordPaymentAsync(
            invoiceId, 50_000m, Today, PaymentMethod.Cash, "Đóng một phần", "faculty1");
        var detailAfterPartial = await harness.Service.GetAsync(invoiceId);

        Assert.True(partial.IsSuccess);
        Assert.Equal(50_000m, detailAfterPartial!.PaidAmount);
        Assert.Equal(150_000m, detailAfterPartial.Remaining);
        Assert.Equal(InvoiceStatus.Pending, detailAfterPartial.Invoice.Status);

        var rest = await harness.Service.RecordPaymentAsync(
            invoiceId, 150_000m, Today, PaymentMethod.Sepay, "Đóng phần còn lại", "faculty1");
        var detailAfterFull = await harness.Service.GetAsync(invoiceId);

        Assert.True(rest.IsSuccess);
        Assert.Equal(0m, detailAfterFull!.Remaining);
        Assert.Equal(InvoiceStatus.Paid, detailAfterFull.Invoice.Status);
        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "invoice.payment");
    }

    [Fact]
    public async Task Payment_rejects_overpay_zero_and_date_before_creation()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var created = await harness.Service.CreateAsync(Input(clubId), new[] { Guid.NewGuid() }, "faculty1");
        var invoiceId = created.Value![0].Id;

        var overpay = await harness.Service.RecordPaymentAsync(
            invoiceId, 250_000m, Today, PaymentMethod.Cash, null, "faculty1");
        var zero = await harness.Service.RecordPaymentAsync(
            invoiceId, 0m, Today, PaymentMethod.Cash, null, "faculty1");
        var beforeCreation = await harness.Service.RecordPaymentAsync(
            invoiceId, 10_000m, Today.AddDays(-1), PaymentMethod.Cash, null, "faculty1");

        Assert.False(overpay.IsSuccess);
        Assert.False(zero.IsSuccess);
        Assert.False(beforeCreation.IsSuccess);
        Assert.Empty(harness.Transactions.All);
    }

    [Fact]
    public async Task Payment_on_paid_invoice_is_rejected()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var created = await harness.Service.CreateAsync(
            Input(clubId, 100_000m), new[] { Guid.NewGuid() }, "faculty1");
        var invoiceId = created.Value![0].Id;
        await harness.Service.RecordPaymentAsync(
            invoiceId, 100_000m, Today, PaymentMethod.Cash, null, "faculty1");

        var again = await harness.Service.RecordPaymentAsync(
            invoiceId, 1_000m, Today, PaymentMethod.Cash, null, "faculty1");

        Assert.False(again.IsSuccess);
        Assert.Contains("đã thanh toán", again.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_and_delete_follow_pending_status()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var created = await harness.Service.CreateAsync(Input(clubId), new[] { Guid.NewGuid() }, "faculty1");
        var invoiceId = created.Value![0].Id;

        var updated = await harness.Service.UpdateAsync(
            invoiceId, "Phí mới", 250_000m, Today.AddDays(10), "HK2", "Sửa", "faculty1");
        Assert.True(updated.IsSuccess);
        Assert.Equal("Phí mới", updated.Value!.Title);

        await harness.Service.RecordPaymentAsync(
            invoiceId, 10_000m, Today, PaymentMethod.Cash, null, "faculty1");
        var deleted = await harness.Service.DeleteAsync(invoiceId, "faculty1");

        Assert.True(deleted.IsSuccess);
        Assert.Empty(harness.Invoices.All);
        Assert.Empty(harness.Transactions.All);
        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "invoice.delete");

        var second = await harness.Service.CreateAsync(Input(clubId, 50_000m), new[] { Guid.NewGuid() }, "faculty1");
        var secondId = second.Value![0].Id;
        await harness.Service.RecordPaymentAsync(secondId, 50_000m, Today, PaymentMethod.Cash, null, "faculty1");

        var updatePaid = await harness.Service.UpdateAsync(
            secondId, "Sửa", 60_000m, Today.AddDays(3), null, null, "faculty1");
        var deletePaid = await harness.Service.DeleteAsync(secondId, "faculty1");

        Assert.False(updatePaid.IsSuccess);
        Assert.False(deletePaid.IsSuccess);
    }

    [Fact]
    public async Task Mark_overdue_updates_only_past_due_pending_invoices()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var user = Guid.NewGuid();
        var dueToday = await harness.Service.CreateAsync(
            Input(clubId, 100_000m, Today), new[] { user }, "faculty1");
        var paid = await harness.Service.CreateAsync(
            Input(clubId, 100_000m, Today), new[] { user }, "faculty1");
        await harness.Service.RecordPaymentAsync(
            paid.Value![0].Id, 100_000m, Today, PaymentMethod.Cash, null, "faculty1");

        var firstRun = await harness.Service.MarkOverdueAsync(clubId, Today.AddDays(1), "faculty1");
        var secondRun = await harness.Service.MarkOverdueAsync(clubId, Today.AddDays(1), "faculty1");

        Assert.Equal(1, firstRun.Value);
        Assert.Equal(0, secondRun.Value);
        Assert.Equal(InvoiceStatus.Overdue, dueToday.Value![0].Status);
        Assert.Equal(InvoiceStatus.Paid, paid.Value![0].Status);
    }

    [Fact]
    public async Task Lists_scope_by_club_and_user_and_compute_paid_amounts()
    {
        var harness = Create();
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var first = await harness.Service.CreateAsync(Input(clubA, 100_000m), new[] { userA }, "faculty1");
        await harness.Service.CreateAsync(Input(clubB, 200_000m), new[] { userB }, "faculty1");
        await harness.Service.RecordPaymentAsync(
            first.Value![0].Id, 40_000m, Today, PaymentMethod.Cash, null, "faculty1");

        var byClub = await harness.Service.ListAsync(clubA);
        var byUser = await harness.Service.ListForUserAsync(clubA, userA);
        var paidOnly = await harness.Service.ListAsync(clubA, InvoiceStatus.Paid);

        Assert.Single(byClub);
        Assert.Equal(40_000m, byClub[0].PaidAmount);
        Assert.Equal(60_000m, byClub[0].Remaining);
        Assert.Single(byUser);
        Assert.Empty(paidOnly);
    }

    [Fact]
    public async Task Duplicate_generated_code_is_retried()
    {
        var harness = Create("INV-2026-000001", "INV-2026-000777");
        var clubId = Guid.NewGuid();

        var first = await harness.Service.CreateAsync(
            Input(clubId), new[] { Guid.NewGuid() }, "faculty1");
        var second = await harness.Service.CreateAsync(
            Input(clubId), new[] { Guid.NewGuid() }, "faculty1");

        Assert.Equal("INV-2026-000001", first.Value![0].Code);
        Assert.Equal("INV-2026-000777", second.Value![0].Code);
    }
}
