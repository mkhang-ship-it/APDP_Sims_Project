using System.Net;
using SIMS.Domain.Security;

namespace SIMS.Tests.Web;

public sealed class AuthIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public AuthIntegrationTests(SimsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_page_renders_with_antiforgery_token()
    {
        using var client = _factory.CreateClientWithoutRedirect();

        var response = await client.GetAsync("/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Đăng nhập SIMS", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task Seeded_admin_can_login_and_logout()
    {
        using var client = _factory.CreateClientWithoutRedirect();

        var login = await SimsWebApplicationFactory.LoginAsync(client, "admin", "Admin@12345");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location?.OriginalString);

        var home = await client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        // Dashboard chào theo buổi (Chào buổi sáng/trưa/chiều/tối) + tên hiển thị.
        Assert.Matches("Chào buổi (sáng|trưa|chiều|tối)", html);
        Assert.Contains("Dashboard", html);
        Assert.Contains("Admin", html);

        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/");
        var logout = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.StartsWith("/Account/Login", logout.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_password_shows_error_and_does_not_sign_in()
    {
        using var client = _factory.CreateClientWithoutRedirect();

        var login = await SimsWebApplicationFactory.LoginAsync(client, "admin", "sai-mat-khau");
        var html = await login.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Tên đăng nhập hoặc mật khẩu không đúng", html);

        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var anonHtml = await home.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Chào buổi", anonHtml);
        Assert.Contains("Đăng nhập", anonHtml);
    }

    [Fact]
    public async Task Anonymous_request_to_students_is_redirected_to_login()
    {
        using var client = _factory.CreateClientWithoutRedirect();

        var response = await client.GetAsync("/Students");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Login_failure_is_written_to_audit_log()
    {
        using var client = _factory.CreateClientWithoutRedirect();
        await SimsWebApplicationFactory.LoginAsync(client, "admin", "sai-mat-khau");

        var auditFile = Path.Combine(_factory.DataDirectory, "audit.csv");
        Assert.True(File.Exists(auditFile));

        var audit = await File.ReadAllTextAsync(auditFile);
        Assert.Contains("auth.login.failed", audit);
    }
}
