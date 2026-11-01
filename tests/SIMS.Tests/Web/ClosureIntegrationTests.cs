using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Abstractions;
using SIMS.Application.Events;
using SIMS.Application.FeatureRequests;
using SIMS.Application.Finance;
using SIMS.Application.Settings;
using SIMS.Application.ShortUrls;
using SIMS.Domain.FeatureRequests;
using SIMS.Domain.Finance;
using SIMS.Domain.Security;

namespace SIMS.Tests.Web;

/// <summary>Parity closure: hồ sơ, link rút gọn, ảnh sự kiện, webhook SePay, nhật ký, export, đề xuất.</summary>
public sealed class ClosureIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public ClosureIntegrationTests(SimsWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClientWithoutRedirect();
        var response = await SimsWebApplicationFactory.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    private async Task<(Guid ClubId, Guid StudentId, Guid FacultyId)> GetContextAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var club = await clubs.FindByCodeAsync("CLB01");
        var student = await users.FindByUsernameAsync("student1");
        var faculty = await users.FindByUsernameAsync("faculty1");
        Assert.NotNull(club);
        Assert.NotNull(student);
        Assert.NotNull(faculty);

        return (club!.Id, student!.Id, faculty!.Id);
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

    private static async Task<HttpResponseMessage> PostFileAsync(
        HttpClient client,
        string url,
        string formUrl,
        Dictionary<string, string> fields,
        string fileField,
        string fileName,
        string contentType,
        byte[] bytes)
    {
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, formUrl);
        using var content = new MultipartFormDataContent();

        foreach (var field in fields)
        {
            content.Add(new StringContent(field.Value, Encoding.UTF8), field.Key);
        }

        content.Add(new StringContent(token, Encoding.UTF8), "__RequestVerificationToken");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, fileField, fileName);

        return await client.PostAsync(url, content);
    }

    [Fact]
    public async Task Profile_update_and_password_change()
    {
        await _factory.CreateUserAsync("profile1", "Profile@123", Role.Student);
        using var client = await LoginAsync("profile1", "Profile@123");

        var page = await client.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var update = await PostFormAsync(client, "/Profile/Update", "/Profile", new()
        {
            ["displayName"] = "Hồ sơ mới",
            ["email"] = "profile1@example.com",
        });
        Assert.Equal(HttpStatusCode.Redirect, update.StatusCode);
        Assert.Contains("Hồ sơ mới", await client.GetStringAsync("/Profile"));

        var wrong = await PostFormAsync(client, "/Profile/ChangePassword", "/Profile", new()
        {
            ["currentPassword"] = "sai-mat-khau",
            ["newPassword"] = "NewProfile@123",
            ["confirmPassword"] = "NewProfile@123",
        });
        Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        Assert.Contains("Mật khẩu hiện tại không đúng", await client.GetStringAsync("/Profile"));

        var change = await PostFormAsync(client, "/Profile/ChangePassword", "/Profile", new()
        {
            ["currentPassword"] = "Profile@123",
            ["newPassword"] = "NewProfile@123",
            ["confirmPassword"] = "NewProfile@123",
        });
        Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);

        using var relogin = _factory.CreateClientWithoutRedirect();
        var login = await SimsWebApplicationFactory.LoginAsync(relogin, "profile1", "NewProfile@123");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    [Fact]
    public async Task Short_url_create_redirect_and_visits()
    {
        var (clubId, _, _) = await GetContextAsync();
        using var faculty = await LoginAsync("faculty1", "Faculty@12345");
        var title = "Link web " + Guid.NewGuid().ToString("N")[..6];

        var create = await PostFormAsync(faculty, "/ShortUrls/Create", "/ShortUrls", new()
        {
            ["ClubId"] = clubId.ToString(),
            ["TargetUrl"] = "https://example.com/target",
            ["Title"] = title,
        });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        string code;
        int visits;
        using (var scope = _factory.Services.CreateScope())
        {
            var urls = scope.ServiceProvider.GetRequiredService<IShortUrlService>();
            var rows = await urls.ListAsync(clubId);
            code = rows.Single(row => row.ShortUrl.Title == title).ShortUrl.Code;
            visits = rows.Single(row => row.ShortUrl.Title == title).VisitCount;
        }

        Assert.Equal(0, visits);

        using var anonymous = _factory.CreateClientWithoutRedirect();
        var redirect = await anonymous.GetAsync($"/s/{code}");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com/target", redirect.Headers.Location?.OriginalString);

        using (var scope = _factory.Services.CreateScope())
        {
            var urls = scope.ServiceProvider.GetRequiredService<IShortUrlService>();
            var rows = await urls.ListAsync(clubId);
            Assert.Equal(1, rows.Single(row => row.ShortUrl.Title == title).VisitCount);
        }

        var missing = await anonymous.GetAsync("/s/khongtontai");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        string expiredCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var urls = scope.ServiceProvider.GetRequiredService<IShortUrlService>();
            var expired = await urls.CreateAsync(
                clubId, "https://example.com/expired", "Hết hạn", DateTimeOffset.UtcNow.AddDays(-1), "tests");
            expiredCode = expired.Value!.Code;
        }

        var expiredResponse = await anonymous.GetAsync($"/s/{expiredCode}");
        Assert.Equal(HttpStatusCode.Gone, expiredResponse.StatusCode);

        using var student = await LoginAsync("student1", "Student@12345");
        var denied = await student.GetAsync("/ShortUrls");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Event_image_upload_download_and_delete()
    {
        Guid eventId;
        using (var scope = _factory.Services.CreateScope())
        {
            var events = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            eventId = (await events.FindByCodeAsync("EVT001"))!.Id;
        }

        using var faculty = await LoginAsync("faculty1", "Faculty@12345");
        var caption = "Ảnh web " + Guid.NewGuid().ToString("N")[..6];
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var upload = await PostFileAsync(
            faculty,
            $"/Events/UploadImage/{eventId}",
            $"/Events/Details/{eventId}",
            new Dictionary<string, string> { ["caption"] = caption },
            "file",
            "anh.png",
            "image/png",
            png);
        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);

        Guid imageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var images = scope.ServiceProvider.GetRequiredService<IEventImageService>();
            imageId = (await images.ListAsync(eventId)).Single(item => item.Caption == caption).Id;
        }

        Assert.Contains(caption, await faculty.GetStringAsync($"/Events/Details/{eventId}"));

        var download = await faculty.GetAsync($"/Events/Image/{imageId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);

        using var student = await LoginAsync("student1", "Student@12345");
        var studentDownload = await student.GetAsync($"/Events/Image/{imageId}");
        Assert.Equal(HttpStatusCode.OK, studentDownload.StatusCode);

        var studentUpload = await PostFileAsync(
            student,
            $"/Events/UploadImage/{eventId}",
            $"/Events/Details/{eventId}",
            new Dictionary<string, string> { ["caption"] = "Sinh viên" },
            "file",
            "anh.png",
            "image/png",
            png);
        Assert.Equal(HttpStatusCode.Redirect, studentUpload.StatusCode);
        Assert.Equal("/Account/AccessDenied", studentUpload.Headers.Location?.AbsolutePath);

        var delete = await PostFormAsync(
            faculty,
            $"/Events/DeleteImage/{eventId}",
            $"/Events/Details/{eventId}",
            new Dictionary<string, string> { ["imageId"] = imageId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);

        var afterDelete = await faculty.GetAsync($"/Events/Image/{imageId}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Sepay_webhook_marks_invoice_paid()
    {
        var (_, studentId, _) = await GetContextAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            await settings.SetAsync(null, SettingKeys.PaymentsSePayApiKey, "test-key-12345", "tests");
        }

        string code;
        Guid invoiceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
            var created = await invoices.CreateAsync(
                new InvoiceBatchInput(
                    Guid.NewGuid(), "Hoá đơn SePay", 100_000m,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), "HK1", null),
                new[] { studentId },
                "tests");
            code = created.Value!.Single().Code;
            invoiceId = created.Value!.Single().Id;
        }

        using var anonymous = _factory.CreateClientWithoutRedirect();
        var payload = JsonSerializer.Serialize(new
        {
            transferType = "in",
            code = code.Replace("-", string.Empty, StringComparison.Ordinal),
            transferAmount = 120_000,
            transactionDate = DateTime.UtcNow.ToString("O"),
        });

        var wrongKey = await SendWebhookAsync(anonymous, payload, "Apikey wrong-key");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);

        var accepted = await SendWebhookAsync(anonymous, payload, "Apikey test-key-12345");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
            var transactions = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
            var invoice = await invoices.FindByIdAsync(invoiceId);
            var payments = await transactions.ListByInvoiceAsync(invoiceId);
            Assert.Equal(InvoiceStatus.Paid, invoice!.Status);
            Assert.Single(payments);
            Assert.Equal(100_000m, payments[0].PaidAmount);
            Assert.Equal(PaymentMethod.Sepay, payments[0].Method);
        }

        var duplicate = await SendWebhookAsync(anonymous, payload, "Apikey test-key-12345");
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);

        var unknown = await SendWebhookAsync(
            anonymous,
            JsonSerializer.Serialize(new { transferType = "in", code = "INV-2099-999999", transferAmount = 1000 }),
            "Apikey test-key-12345");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    private static Task<HttpResponseMessage> SendWebhookAsync(
        HttpClient client,
        string json,
        string authorization)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/sepay/webhook")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        return client.SendAsync(request);
    }

    [Fact]
    public async Task Logs_export_and_feature_request_flow()
    {
        using var admin = await LoginAsync("admin", "Admin@12345");
        using (var auditScope = _factory.Services.CreateScope())
        {
            var reader = auditScope.ServiceProvider.GetRequiredService<IAuditLogReader>();
            var entries = await reader.ReadAsync(50);
            Assert.NotEmpty(entries);
            Assert.Contains(entries, entry => entry.Action.StartsWith("auth.login", StringComparison.Ordinal));
        }
        var logs = await admin.GetAsync("/Logs");
        Assert.Equal(HttpStatusCode.OK, logs.StatusCode);
        var logsHtml = await logs.Content.ReadAsStringAsync();
        Assert.Contains("Nhật ký hệ thống", logsHtml);
        Assert.Contains("auth.login", logsHtml);

        using var faculty = await LoginAsync("faculty1", "Faculty@12345");
        var deniedLogs = await faculty.GetAsync("/Logs");
        Assert.Equal(HttpStatusCode.Redirect, deniedLogs.StatusCode);
        Assert.Equal("/Account/AccessDenied", deniedLogs.Headers.Location?.AbsolutePath);

        var export = await faculty.GetAsync("/Students/Export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var csv = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains("SV0001", csv);

        var title = "Đề xuất web " + Guid.NewGuid().ToString("N")[..6];
        using var student = await LoginAsync("student1", "Student@12345");
        var studentPage = await student.GetAsync("/FeatureRequests");
        Assert.Equal(HttpStatusCode.OK, studentPage.StatusCode);

        var create = await PostFormAsync(student, "/FeatureRequests/Create", "/FeatureRequests", new()
        {
            ["Title"] = title,
            ["Content"] = "Nội dung đề xuất kiểm thử.",
        });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        Guid requestId;
        using (var scope = _factory.Services.CreateScope())
        {
            var requests = scope.ServiceProvider.GetRequiredService<IFeatureRequestService>();
            requestId = (await requests.ListMineAsync("student1")).Single(item => item.Title == title).Id;
        }

        var facultyPage = await faculty.GetAsync("/FeatureRequests");
        Assert.Equal(HttpStatusCode.OK, facultyPage.StatusCode);
        Assert.Contains(title, await facultyPage.Content.ReadAsStringAsync());

        var status = await PostFormAsync(
            faculty,
            $"/FeatureRequests/Status/{requestId}",
            "/FeatureRequests",
            new Dictionary<string, string>
            {
                ["status"] = nameof(FeatureRequestStatus.Closed),
                ["response"] = "Đã xử lý xong.",
            });
        Assert.Equal(HttpStatusCode.Redirect, status.StatusCode);

        var studentAfter = await student.GetStringAsync("/FeatureRequests");
        Assert.Contains("Đã xử lý xong.", studentAfter);

        var deniedStatus = await PostFormAsync(
            student,
            $"/FeatureRequests/Status/{requestId}",
            "/FeatureRequests",
            new Dictionary<string, string>
            {
                ["status"] = nameof(FeatureRequestStatus.Open),
                ["response"] = "Tự mở lại",
            });
        Assert.Equal(HttpStatusCode.Redirect, deniedStatus.StatusCode);
        Assert.Equal("/Account/AccessDenied", deniedStatus.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Anonymous_is_redirected_from_closure_pages()
    {
        using var client = _factory.CreateClientWithoutRedirect();

        foreach (var url in new[] { "/ShortUrls", "/FeatureRequests", "/Logs", "/Profile" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        }
    }
}

/// <summary>Chế độ bảo trì dùng factory riêng để không ảnh hưởng test khác.</summary>
public sealed class MaintenanceIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public MaintenanceIntegrationTests(SimsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Maintenance_blocks_guests_but_allows_admin()
    {
        using var scope = _factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settings.SetAsync(null, SettingKeys.SystemMaintenance, "on", "tests");
        await settings.SetAsync(null, SettingKeys.SystemMaintenanceMessage, "Bảo trì thử nghiệm", "tests");
        Assert.Equal("on", await settings.GetAsync(null, SettingKeys.SystemMaintenance));
        Assert.Equal("Bảo trì thử nghiệm", await settings.GetAsync(null, SettingKeys.SystemMaintenanceMessage));

        try
        {
            using var anonymous = _factory.CreateClientWithoutRedirect();
            var blocked = await anonymous.GetAsync("/");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
            var blockedHtml = await blocked.Content.ReadAsStringAsync();
            Assert.Contains("Bảo trì thử nghiệm", blockedHtml);

            using var admin = _factory.CreateClientWithoutRedirect();
            var login = await SimsWebApplicationFactory.LoginAsync(admin, "admin", "Admin@12345");
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

            var allowed = await admin.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        finally
        {
            await settings.SetAsync(null, SettingKeys.SystemMaintenance, "off", "tests");
        }

        using var after = _factory.CreateClientWithoutRedirect();
        var restored = await after.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }
}
