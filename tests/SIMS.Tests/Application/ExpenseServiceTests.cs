using SIMS.Application.Finance;
using SIMS.Domain.Finance;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class ExpenseServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ExpenseService Service,
        InMemoryExpenseRepository Expenses,
        InMemoryTransactionRepository Transactions,
        RecordingAuditLog Audit);

    private static Harness Create()
    {
        var expenses = new InMemoryExpenseRepository();
        var transactions = new InMemoryTransactionRepository();
        var audit = new RecordingAuditLog();
        var service = new ExpenseService(expenses, transactions, audit, new FakeClock(Now));

        return new Harness(service, expenses, transactions, audit);
    }

    [Fact]
    public async Task Staff_creation_is_approved_and_member_creation_is_pending()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();

        var staff = await harness.Service.CreateAsync(
            clubId, "Mua văn phòng phẩm", 150_000m, "HK1", "Mô tả",
            autoApprove: true, requestedBy: "faculty1", actor: "faculty1");
        var member = await harness.Service.CreateAsync(
            clubId, "In ấn", 80_000m, "HK1", null,
            autoApprove: false, requestedBy: "student1", actor: "student1");

        Assert.Equal(ExpenseStatus.Approved, staff.Value!.Status);
        Assert.Equal(ExpenseStatus.Pending, member.Value!.Status);
        Assert.Equal("student1", member.Value.RequestedBy);
        Assert.Equal(2, harness.Expenses.All.Count);
        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "expense.create");
    }

    [Fact]
    public async Task Creation_rejects_invalid_input_without_writing()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();

        var blank = await harness.Service.CreateAsync(
            clubId, " ", 1000m, null, null, false, "student1", "student1");
        var negative = await harness.Service.CreateAsync(
            clubId, "Hợp lệ", -1m, null, null, false, "student1", "student1");

        Assert.False(blank.IsSuccess);
        Assert.False(negative.IsSuccess);
        Assert.Empty(harness.Expenses.All);
    }

    [Fact]
    public async Task Approve_then_mark_paid_writes_outcome_transaction()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var created = await harness.Service.CreateAsync(
            clubId, "Mua giấy", 100_000m, "HK1", null, false, "student1", "student1");
        var expenseId = created.Value!.Id;

        var approved = await harness.Service.ApproveAsync(expenseId, "faculty1");
        var paid = await harness.Service.MarkPaidAsync(expenseId, "faculty1");
        var payments = await harness.Service.ListPaymentsAsync(expenseId);

        Assert.True(approved.IsSuccess);
        Assert.Equal(ExpenseStatus.Paid, paid.Value!.Status);
        Assert.Single(payments);
        Assert.Equal(TransactionType.Outcome, payments[0].Type);
        Assert.Equal(100_000m, payments[0].PaidAmount);
        Assert.Equal(expenseId, payments[0].ExpenseId);
        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "expense.approve");
        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "expense.pay");

        var secondPayment = await harness.Service.MarkPaidAsync(expenseId, "faculty1");
        Assert.False(secondPayment.IsSuccess);
        Assert.Single(payments);
    }

    [Fact]
    public async Task Mark_paid_requires_approved_status()
    {
        var harness = Create();
        var created = await harness.Service.CreateAsync(
            Guid.NewGuid(), "Mua giấy", 10_000m, null, null, false, "student1", "student1");

        var paid = await harness.Service.MarkPaidAsync(created.Value!.Id, "faculty1");

        Assert.False(paid.IsSuccess);
        Assert.Equal(ExpenseStatus.Pending, created.Value.Status);
        Assert.Empty(harness.Transactions.All);
    }

    [Fact]
    public async Task Reject_only_works_from_pending()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var first = await harness.Service.CreateAsync(
            clubId, "Yêu cầu A", 10_000m, null, null, false, "student1", "student1");
        var second = await harness.Service.CreateAsync(
            clubId, "Yêu cầu B", 20_000m, null, null, false, "student1", "student1");

        var rejected = await harness.Service.RejectAsync(first.Value!.Id, "faculty1");
        await harness.Service.ApproveAsync(second.Value!.Id, "faculty1");
        var rejectApproved = await harness.Service.RejectAsync(second.Value.Id, "faculty1");

        Assert.True(rejected.IsSuccess);
        Assert.Equal(ExpenseStatus.Rejected, rejected.Value!.Status);
        Assert.False(rejectApproved.IsSuccess);
    }

    [Fact]
    public async Task Update_and_delete_only_work_while_pending()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var pending = await harness.Service.CreateAsync(
            clubId, "Chờ duyệt", 10_000m, null, null, false, "student1", "student1");
        var approved = await harness.Service.CreateAsync(
            clubId, "Đã duyệt", 20_000m, null, null, true, "faculty1", "faculty1");

        var updated = await harness.Service.UpdateAsync(
            pending.Value!.Id, "Đã sửa", 15_000m, "HK2", "Mô tả mới", "student1");
        var deleted = await harness.Service.DeleteAsync(pending.Value.Id, "student1");
        var updateApproved = await harness.Service.UpdateAsync(
            approved.Value!.Id, "Sửa", 30_000m, null, null, "faculty1");
        var deleteApproved = await harness.Service.DeleteAsync(approved.Value.Id, "faculty1");

        Assert.True(updated.IsSuccess);
        Assert.True(deleted.IsSuccess);
        Assert.False(updateApproved.IsSuccess);
        Assert.False(deleteApproved.IsSuccess);
        Assert.Single(harness.Expenses.All);
    }

    [Fact]
    public async Task Lists_filter_by_club_requester_and_status()
    {
        var harness = Create();
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        await harness.Service.CreateAsync(
            clubA, "Của sinh viên", 10_000m, null, null, false, "student1", "student1");
        await harness.Service.CreateAsync(
            clubA, "Của giảng viên", 20_000m, null, null, true, "faculty1", "faculty1");
        await harness.Service.CreateAsync(
            clubB, "CLB khác", 30_000m, null, null, false, "student1", "student1");

        var byClub = await harness.Service.ListAsync(clubA);
        var mine = await harness.Service.ListMineAsync(clubA, "STUDENT1");
        var approvedOnly = await harness.Service.ListAsync(clubA, ExpenseStatus.Approved);

        Assert.Equal(2, byClub.Count);
        Assert.Single(mine);
        Assert.Equal("student1", mine[0].RequestedBy);
        Assert.Single(approvedOnly);
        Assert.Equal("Của giảng viên", approvedOnly[0].Title);
    }
}
