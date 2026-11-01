using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Abstractions;
using SIMS.Application.Finance;
using SIMS.Domain.Finance;

namespace SIMS.Tests.Web;

/// <summary>Batch 8: hoá đơn, thanh toán từng phần, chi phí và luồng duyệt.</summary>
public sealed class FinanceIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public FinanceIntegrationTests(SimsWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClientWithoutRedirect();
        var response = await SimsWebApplicationFactory.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    private async Task<(Guid ClubId, Guid StudentUserId)> GetClubAndStudentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var club = await clubs.FindByCodeAsync("CLB01");
        var student = await users.FindByUsernameAsync("student1");

        Assert.NotNull(club);
        Assert.NotNull(student);

        return (club!.Id, student!.Id);
    }

    private async Task<Invoice> FindInvoiceAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();

        var match = (await invoices.ListAsync())
            .FirstOrDefault(invoice => invoice.Title == title);
        Assert.NotNull(match);

        return match!;
    }

    private async Task<Expense> FindExpenseAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var expenses = scope.ServiceProvider.GetRequiredService<IExpenseRepository>();

        var match = (await expenses.ListAsync())
            .FirstOrDefault(expense => expense.Title == title);
        Assert.NotNull(match);

        return match!;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string url,
        string formUrl,
        Dictionary<string, string> fields)
    {
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, formUrl);
        fields["__RequestVerificationToken"] = token;

        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task Faculty_sees_seeded_invoice_details_and_receipt()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var index = await client.GetAsync("/Invoices");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        var html = await index.Content.ReadAsStringAsync();
        Assert.Contains("Hoá đơn", html);
        Assert.Contains("INV-", html);
        Assert.Contains("Phí sinh hoạt học kỳ", html);

        var invoice = await FindInvoiceAsync("Phí sinh hoạt học kỳ");
        var details = await client.GetStringAsync($"/Invoices/Details/{invoice.Id}");
        Assert.Contains("Giao dịch", details);
        Assert.Contains("Đã thu", details);

        var receipt = await client.GetAsync($"/Invoices/Receipt/{invoice.Id}");
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Contains("Phiếu thu", await receipt.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Faculty_creates_invoice_for_member_and_collects_payments()
    {
        var (clubId, studentUserId) = await GetClubAndStudentAsync();
        using var client = await LoginAsync("faculty1", "Faculty@12345");
        var title = "Hoá đơn web " + Guid.NewGuid().ToString("N")[..6];

        var createPage = await client.GetAsync("/Invoices/Create");
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        Assert.Contains("Thành viên nhận hoá đơn", await createPage.Content.ReadAsStringAsync());

        var create = await PostFormAsync(client, "/Invoices/Create", "/Invoices/Create", new()
        {
            ["ClubId"] = clubId.ToString(),
            ["Title"] = title,
            ["TotalAmount"] = "120000",
            ["DueDate"] = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd"),
            ["Period"] = "HK1 2026-2027",
            ["Note"] = "Tạo qua HTTP",
            ["MemberIds"] = studentUserId.ToString(),
        });

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var invoice = await FindInvoiceAsync(title);
        Assert.Equal(120_000m, invoice.TotalAmount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);

        var firstPayment = await PostFormAsync(
            client, $"/Invoices/MarkPaid/{invoice.Id}", $"/Invoices/Details/{invoice.Id}", new()
            {
                ["amount"] = "20000",
                ["paymentDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
                ["method"] = "Cash",
                ["note"] = "Đợt 1",
            });

        Assert.Equal(HttpStatusCode.Redirect, firstPayment.StatusCode);
        var afterPartial = await FindInvoiceAsync(title);
        Assert.Equal(InvoiceStatus.Pending, afterPartial.Status);

        var finalPayment = await PostFormAsync(
            client, $"/Invoices/MarkPaid/{invoice.Id}", $"/Invoices/Details/{invoice.Id}", new()
            {
                ["amount"] = "100000",
                ["paymentDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
                ["method"] = "Sepay",
                ["note"] = "Đợt 2",
            });

        Assert.Equal(HttpStatusCode.Redirect, finalPayment.StatusCode);
        var afterFull = await FindInvoiceAsync(title);
        Assert.Equal(InvoiceStatus.Paid, afterFull.Status);

        var details = await client.GetStringAsync($"/Invoices/Details/{invoice.Id}");
        Assert.Contains("Đã thanh toán", details);
    }

    [Fact]
    public async Task Student_sees_own_invoices_but_not_staff_invoice_list()
    {
        var (clubId, _) = await GetClubAndStudentAsync();
        var facultyInvoiceTitle = "Hoá đơn giảng viên " + Guid.NewGuid().ToString("N")[..6];

        using (var strategyScope = _factory.Services.CreateScope())
        {
            var invoices = strategyScope.ServiceProvider.GetRequiredService<IInvoiceService>();
            var users = strategyScope.ServiceProvider.GetRequiredService<IUserRepository>();
            var faculty = await users.FindByUsernameAsync("faculty1");

            var created = await invoices.CreateAsync(
                new SIMS.Application.Finance.InvoiceBatchInput(
                    clubId, facultyInvoiceTitle, 90_000m, DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
                    "HK1", null),
                new[] { faculty!.Id },
                "tests");

            Assert.True(created.IsSuccess);
        }

        using var studentClient = await LoginAsync("student1", "Student@12345");

        var denied = await studentClient.GetAsync("/Invoices");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);

        var mine = await studentClient.GetStringAsync("/Invoices/MyInvoices");
        Assert.Contains("Hoá đơn của tôi", mine);
        Assert.Contains("Phí sinh hoạt học kỳ", mine);

        var ownInvoice = (await FindInvoiceAsync("Phí sinh hoạt học kỳ"));
        var ownReceipt = await studentClient.GetAsync($"/Invoices/Receipt/{ownInvoice.Id}");
        Assert.Equal(HttpStatusCode.OK, ownReceipt.StatusCode);

        var foreignInvoice = await FindInvoiceAsync(facultyInvoiceTitle);
        var foreignReceipt = await studentClient.GetAsync($"/Invoices/Receipt/{foreignInvoice.Id}");
        Assert.Equal(HttpStatusCode.Redirect, foreignReceipt.StatusCode);
        Assert.Equal("/Account/AccessDenied", foreignReceipt.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Student_cannot_create_invoice_or_record_payment()
    {
        var invoice = await FindInvoiceAsync("Phí sinh hoạt học kỳ");
        using var client = await LoginAsync("student1", "Student@12345");

        var create = await client.GetAsync("/Invoices/Create");
        var pay = await PostFormAsync(client, $"/Invoices/MarkPaid/{invoice.Id}", "/Invoices/MyInvoices", new()
        {
            ["amount"] = "1000",
            ["paymentDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["method"] = "Cash",
        });

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal("/Account/AccessDenied", create.Headers.Location?.AbsolutePath);
        Assert.Equal(HttpStatusCode.Redirect, pay.StatusCode);
        Assert.Equal("/Account/AccessDenied", pay.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Faculty_creates_expense_and_marks_it_paid()
    {
        var (clubId, _) = await GetClubAndStudentAsync();
        using var client = await LoginAsync("faculty1", "Faculty@12345");
        var title = "Chi phí web " + Guid.NewGuid().ToString("N")[..6];

        var create = await PostFormAsync(client, "/Expenses/Create", "/Expenses/Create", new()
        {
            ["ClubId"] = clubId.ToString(),
            ["Title"] = title,
            ["Amount"] = "150000",
            ["Period"] = "HK1 2026-2027",
            ["Description"] = "Tạo qua HTTP",
        });

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var expense = await FindExpenseAsync(title);
        Assert.Equal(ExpenseStatus.Approved, expense.Status);

        var pay = await PostFormAsync(client, $"/Expenses/MarkPaid/{expense.Id}", "/Expenses/Create", new());

        Assert.Equal(HttpStatusCode.Redirect, pay.StatusCode);
        var paid = await FindExpenseAsync(title);
        Assert.Equal(ExpenseStatus.Paid, paid.Status);

        using var scope = _factory.Services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var payments = await transactions.ListByExpenseAsync(expense.Id);
        Assert.Single(payments);
        Assert.Equal(TransactionType.Outcome, payments[0].Type);
    }

    [Fact]
    public async Task Student_submits_expense_request_and_faculty_approves_it()
    {
        var title = "Đề nghị chi " + Guid.NewGuid().ToString("N")[..6];
        using var studentClient = await LoginAsync("student1", "Student@12345");

        var denied = await studentClient.GetAsync("/Expenses");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);

        var myPage = await studentClient.GetAsync("/Expenses/MyExpenses");
        Assert.Equal(HttpStatusCode.OK, myPage.StatusCode);

        var submit = await PostFormAsync(studentClient, "/Expenses/MyCreate", "/Expenses/MyExpenses", new()
        {
            ["Title"] = title,
            ["Amount"] = "45000",
            ["Period"] = "HK1 2026-2027",
            ["Description"] = "Đề nghị từ sinh viên",
        });

        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);
        var pending = await FindExpenseAsync(title);
        Assert.Equal(ExpenseStatus.Pending, pending.Status);
        Assert.Equal("student1", pending.RequestedBy);

        var myPageAfter = await studentClient.GetStringAsync("/Expenses/MyExpenses");
        Assert.Contains(title, myPageAfter);

        using var facultyClient = await LoginAsync("faculty1", "Faculty@12345");
        var approve = await PostFormAsync(
            facultyClient, $"/Expenses/Approve/{pending.Id}", "/Expenses", new());

        Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);
        var approved = await FindExpenseAsync(title);
        Assert.Equal(ExpenseStatus.Approved, approved.Status);
        Assert.Equal("faculty1", approved.ApprovedBy);
    }

    [Fact]
    public async Task Faculty_cannot_open_receipt_of_another_club()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var clubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceService>();

            var otherClub = await clubs.FindByCodeAsync("CLB02");
            var student = await users.FindByUsernameAsync("student1");
            Assert.NotNull(otherClub);
            Assert.NotNull(student);

            var created = await invoices.CreateAsync(
                new InvoiceBatchInput(
                    otherClub!.Id, "Hoá đơn CLB khác", 10_000m,
                    DateOnly.FromDateTime(DateTime.Today.AddDays(3)), "HK1", null),
                new[] { student!.Id },
                "tests");
            Assert.True(created.IsSuccess);
        }

        var invoice = await FindInvoiceAsync("Hoá đơn CLB khác");
        using var client = await LoginAsync("faculty1", "Faculty@12345");
        var response = await client.GetAsync($"/Invoices/Receipt/{invoice.Id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Anonymous_is_redirected_and_unknown_invoice_is_404()
    {
        using var anonymous = _factory.CreateClientWithoutRedirect();
        var redirect = await anonymous.GetAsync("/Invoices");

        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/Account/Login", redirect.Headers.Location?.AbsolutePath);

        using var client = await LoginAsync("faculty1", "Faculty@12345");
        var missing = await client.GetAsync($"/Invoices/Details/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
