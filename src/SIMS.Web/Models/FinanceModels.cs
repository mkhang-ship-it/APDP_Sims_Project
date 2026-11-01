using System.ComponentModel.DataAnnotations;
using SIMS.Application.Finance;
using SIMS.Domain.Clubs;
using SIMS.Domain.Finance;
using SIMS.Domain.Security;

namespace SIMS.Web.Models;

public sealed class InvoiceFormModel
{
    [Required]
    public Guid ClubId { get; set; }

    [Required(ErrorMessage = "Tên hoá đơn là bắt buộc.")]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [Range(0, 9_999_999_999, ErrorMessage = "Số tiền không hợp lệ.")]
    public decimal TotalAmount { get; set; }

    [Required(ErrorMessage = "Hạn thanh toán là bắt buộc.")]
    [DataType(DataType.Date)]
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(7));

    [StringLength(40)]
    public string? Period { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }

    public List<Guid> MemberIds { get; set; } = new();

    public InvoiceBatchInput ToInput()
        => new(ClubId, Title, TotalAmount, DueDate, Period, Note);

    public static InvoiceFormModel FromInvoice(Invoice invoice)
        => new()
        {
            ClubId = invoice.ClubId,
            Title = invoice.Title,
            TotalAmount = invoice.TotalAmount,
            DueDate = invoice.DueDate,
            Period = invoice.Period,
            Note = invoice.Note,
        };
}

public sealed record InvoiceListItem(InvoiceRow Row, string MemberName, string ClubName);

public sealed class InvoiceListPageModel
{
    public IReadOnlyList<InvoiceListItem> Items { get; init; } = Array.Empty<InvoiceListItem>();

    public InvoiceSummary Summary { get; init; } = new(0, 0, 0, 0, 0m, 0m);

    public string? Status { get; init; }

    public int? Month { get; init; }

    public int? Year { get; init; }
}

public sealed class InvoiceCreatePageModel
{
    public InvoiceFormModel Form { get; init; } = new();

    public IReadOnlyList<Club> Clubs { get; init; } = Array.Empty<Club>();

    public IReadOnlyList<User> Members { get; init; } = Array.Empty<User>();

    public bool IsAdmin { get; init; }
}

public sealed class InvoiceDetailsPageModel
{
    public InvoiceDetail Detail { get; init; } = null!;

    public string MemberName { get; init; } = "?";

    public string ClubName { get; init; } = "?";
}

public sealed class InvoiceEditPageModel
{
    public Invoice Invoice { get; init; } = null!;

    public InvoiceFormModel Form { get; init; } = new();

    public string MemberName { get; init; } = "?";
}

public sealed class MyInvoicesPageModel
{
    public IReadOnlyList<InvoiceListItem> Items { get; init; } = Array.Empty<InvoiceListItem>();

    public InvoiceSummary Summary { get; init; } = new(0, 0, 0, 0, 0m, 0m);
}

public sealed class InvoiceReceiptPageModel
{
    public Invoice Invoice { get; init; } = null!;

    public string MemberName { get; init; } = "?";

    public string ClubName { get; init; } = "?";

    public IReadOnlyList<Transaction> Transactions { get; init; } = Array.Empty<Transaction>();

    public decimal PaidAmount { get; init; }

    public decimal Remaining => Math.Max(0m, Invoice.TotalAmount - PaidAmount);
}

public sealed class ExpenseFormModel
{
    public Guid ClubId { get; set; }

    [Required(ErrorMessage = "Tên chi phí là bắt buộc.")]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [Range(0, 9_999_999_999, ErrorMessage = "Số tiền không hợp lệ.")]
    public decimal Amount { get; set; }

    [StringLength(40)]
    public string? Period { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    public static ExpenseFormModel FromExpense(Expense expense)
        => new()
        {
            ClubId = expense.ClubId,
            Title = expense.Title,
            Amount = expense.Amount,
            Period = expense.Period,
            Description = expense.Description,
        };
}

public sealed record ExpenseSummary(
    int Total,
    int Pending,
    int Approved,
    int Rejected,
    int Paid,
    decimal TotalAmount,
    decimal PendingAmount,
    decimal ApprovedAmount,
    decimal PaidAmount)
{
    public static ExpenseSummary From(IEnumerable<Expense> expenses)
    {
        var items = expenses.ToArray();

        return new ExpenseSummary(
            items.Length,
            items.Count(expense => expense.Status == ExpenseStatus.Pending),
            items.Count(expense => expense.Status == ExpenseStatus.Approved),
            items.Count(expense => expense.Status == ExpenseStatus.Rejected),
            items.Count(expense => expense.Status == ExpenseStatus.Paid),
            items.Sum(expense => expense.Amount),
            items.Where(expense => expense.Status == ExpenseStatus.Pending).Sum(expense => expense.Amount),
            items.Where(expense => expense.Status == ExpenseStatus.Approved).Sum(expense => expense.Amount),
            items.Where(expense => expense.Status == ExpenseStatus.Paid).Sum(expense => expense.Amount));
    }
}

public sealed record ExpenseListItem(Expense Expense, string ClubName);

public sealed class ExpenseListPageModel
{
    public IReadOnlyList<ExpenseListItem> Items { get; init; } = Array.Empty<ExpenseListItem>();

    public ExpenseSummary Summary { get; init; } = new(0, 0, 0, 0, 0, 0m, 0m, 0m, 0m);

    public string? Status { get; init; }
}

public sealed class ExpenseCreatePageModel
{
    public ExpenseFormModel Form { get; init; } = new();

    public IReadOnlyList<Club> Clubs { get; init; } = Array.Empty<Club>();

    public bool IsAdmin { get; init; }
}

public sealed class ExpenseEditPageModel
{
    public Expense Expense { get; init; } = null!;

    public ExpenseFormModel Form { get; init; } = new();
}

public sealed class MyExpensesPageModel
{
    public IReadOnlyList<ExpenseListItem> Items { get; init; } = Array.Empty<ExpenseListItem>();

    public ExpenseSummary Summary { get; init; } = new(0, 0, 0, 0, 0, 0m, 0m, 0m, 0m);

    public ExpenseFormModel NewRequest { get; init; } = new();
}
