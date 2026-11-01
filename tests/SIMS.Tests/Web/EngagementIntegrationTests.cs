using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Abstractions;
using SIMS.Application.LuckyDraws;

namespace SIMS.Tests.Web;

/// <summary>Batch 10: bốc thăm, trang công khai, thông báo và cài đặt.</summary>
public sealed class EngagementIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public EngagementIntegrationTests(SimsWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClientWithoutRedirect();
        var response = await SimsWebApplicationFactory.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    private async Task<(Guid ClubId, string Slug, Guid DrawId, Guid PrizeId)> SeedDrawAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var draws = scope.ServiceProvider.GetRequiredService<ILuckyDrawService>();
        var clubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
        var club = await clubs.FindByCodeAsync("CLB01");
        Assert.NotNull(club);

        var created = await draws.CreateAsync(
            club!.Id,
            title,
            "123456",
            new[] { "An", "Bình", "Cường" },
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(6),
            true,
            "tests");
        Assert.True(created.IsSuccess);

        var prize = await draws.AddPrizeAsync(created.Value!.Id, "Giải nhất", 1, true, "tests");
        Assert.True(prize.IsSuccess);

        return (club.Id, created.Value.Slug, created.Value.Id, prize.Value!.Id);
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
    public async Task Anonymous_home_shows_public_content()
    {
        using var client = _factory.CreateClientWithoutRedirect();

        var home = await client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Hoạt động sắp tới", html);
        Assert.Contains("Câu lạc bộ", html);
        Assert.Contains("Ngày hội công nghệ", html);
        Assert.Contains("CLB01", html);
        Assert.Contains("Đăng nhập", html);
    }

    [Fact]
    public async Task Public_draw_requires_security_code_and_unknown_slug_is_404()
    {
        using var scope = _factory.Services.CreateScope();
        var draws = scope.ServiceProvider.GetRequiredService<ILuckyDrawService>();
        var rows = await draws.ListAsync(null);
        var seeded = rows.First(row => row.Draw.SecurityCode == "123456");
        using var client = _factory.CreateClientWithoutRedirect();

        var page = await client.GetAsync($"/lucky/{seeded.Draw.Slug}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Nhập mã bảo mật", await page.Content.ReadAsStringAsync());

        var wrong = await PostFormAsync(
            client,
            $"/lucky/{seeded.Draw.Slug}/verify",
            $"/lucky/{seeded.Draw.Slug}",
            new Dictionary<string, string> { ["securityCode"] = "sai" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Mã bảo mật không đúng.", await wrong.Content.ReadAsStringAsync());

        var correct = await PostFormAsync(
            client,
            $"/lucky/{seeded.Draw.Slug}/verify",
            $"/lucky/{seeded.Draw.Slug}",
            new Dictionary<string, string> { ["securityCode"] = "123456" });
        Assert.Equal(HttpStatusCode.Redirect, correct.StatusCode);

        var verified = await client.GetStringAsync($"/lucky/{seeded.Draw.Slug}");
        Assert.DoesNotContain("Nhập mã bảo mật", verified);
        Assert.Contains("Người trúng thưởng", verified);

        var missing = await client.GetAsync($"/lucky/khong-ton-tai");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Faculty_draws_winner_and_public_page_shows_result()
    {
        var title = "Bốc thăm web " + Guid.NewGuid().ToString("N")[..6];
        var (_, slug, drawId, prizeId) = await SeedDrawAsync(title);
        using var faculty = await LoginAsync("faculty1", "Faculty@12345");

        var details = await faculty.GetAsync($"/LuckyDraws/Details/{drawId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        var detailsHtml = await details.Content.ReadAsStringAsync();
        Assert.Contains(title, detailsHtml);

        var draw = await PostFormAsync(
            faculty,
            $"/LuckyDraws/Draw/{drawId}",
            $"/LuckyDraws/Details/{drawId}",
            new Dictionary<string, string> { ["prizeId"] = prizeId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, draw.StatusCode);

        var after = await faculty.GetStringAsync($"/LuckyDraws/Details/{drawId}");
        Assert.Contains("Người trúng thưởng (1)", after);
        Assert.Contains("Giải nhất", after);

        using var anonymous = _factory.CreateClientWithoutRedirect();
        var page = await anonymous.GetStringAsync($"/lucky/{slug}");
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(anonymous, $"/lucky/{slug}");
        await anonymous.PostAsync($"/lucky/{slug}/verify", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["securityCode"] = "123456",
            ["__RequestVerificationToken"] = token,
        }));

        var verified = await anonymous.GetStringAsync($"/lucky/{slug}");
        Assert.Contains("Giải nhất", verified);
    }

    [Fact]
    public async Task Faculty_manages_notifications_and_student_reads_inbox()
    {
        var title = "Thông báo web " + Guid.NewGuid().ToString("N")[..6];
        using var faculty = await LoginAsync("faculty1", "Faculty@12345");

        var page = await faculty.GetAsync("/Notifications");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Sinh hoạt CLB tháng 11", await page.Content.ReadAsStringAsync());

        var create = await PostFormAsync(faculty, "/Notifications/Create", "/Notifications", new()
        {
            ["Title"] = title,
            ["Content"] = "Nội dung thông báo kiểm thử.",
        });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        using var student = await LoginAsync("student1", "Student@12345");
        var denied = await student.GetAsync("/Notifications");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);

        var inbox = await student.GetStringAsync("/Notifications/Inbox");
        Assert.Contains("Chào mừng đến với SIMS", inbox);
        Assert.Contains(title, inbox);

        var markAll = await PostFormAsync(
            student, "/Notifications/MarkAllRead", "/Notifications/Inbox", new());
        Assert.Equal(HttpStatusCode.Redirect, markAll.StatusCode);

        var after = await student.GetStringAsync("/Notifications/Inbox");
        Assert.Contains("0 chưa đọc", after);
    }

    [Fact]
    public async Task Admin_updates_site_settings_and_public_home_reflects_them()
    {
        using var admin = await LoginAsync("admin", "Admin@12345");

        var page = await admin.GetAsync("/Settings");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var title = "SIMS cấu hình " + Guid.NewGuid().ToString("N")[..6];
        var save = await PostFormAsync(admin, "/Settings/Global", "/Settings", new()
        {
            ["site.title"] = title,
            ["site.description"] = "Mô tả công khai mới",
            ["site.theme"] = "dark",
            ["site.primary_color"] = "#112233",
            ["site.support_email"] = "help@example.com",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        using var anonymous = _factory.CreateClientWithoutRedirect();
        var home = await anonymous.GetStringAsync("/");
        Assert.Contains(title, home);
        Assert.Contains("Mô tả công khai mới", home);
        Assert.Contains("data-bs-theme=\"dark\"", home);
        Assert.Contains("#112233", home);
    }

    [Fact]
    public async Task Faculty_updates_club_settings_and_student_is_denied()
    {
        Guid clubId;
        using (var scope = _factory.Services.CreateScope())
        {
            var clubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
            clubId = (await clubs.FindByCodeAsync("CLB01"))!.Id;
        }

        using var faculty = await LoginAsync("faculty1", "Faculty@12345");
        var page = await faculty.GetAsync("/Settings/Club");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var email = $"clb-{Guid.NewGuid():N}@example.com";
        var save = await PostFormAsync(faculty, "/Settings/Club", "/Settings/Club", new()
        {
            ["clubId"] = clubId.ToString(),
            ["club.contact_email"] = email,
            ["club.contact_phone"] = "0911111111",
            ["club.address"] = "Địa chỉ mới",
            ["club.invoice_bank"] = "VCB",
            ["club.invoice_bank_account"] = "0123456789",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var after = await faculty.GetStringAsync($"/Settings/Club?clubId={clubId}");
        Assert.Contains(email, after);

        using var student = await LoginAsync("student1", "Student@12345");
        var denied = await student.GetAsync("/Settings/Club");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Anonymous_is_redirected_from_management_pages()
    {
        using var client = _factory.CreateClientWithoutRedirect();

        foreach (var url in new[] { "/LuckyDraws", "/Notifications", "/Settings" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        }
    }
}
