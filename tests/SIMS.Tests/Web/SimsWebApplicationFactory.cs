using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Security;

namespace SIMS.Tests.Web;

/// <summary>
/// Dựng app thật trong bộ nhớ với thư mục data/ tạm — không đụng dữ liệu thật.
/// </summary>
public sealed class SimsWebApplicationFactory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "sims-it-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = DataDirectory,
                ["Storage:Provider"] = "Csv",
            });
        });
    }

    public HttpClient CreateClientWithoutRedirect()
        => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    public async Task<(string Username, string Password)> CreateUserAsync(
        string username,
        string password,
        SIMS.Domain.Security.Role role)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await users.CreateAsync(
            new UserInput(username, $"Người dùng {username}", role, password),
            "tests");

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                "Không tạo được user test: " + string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        return (username, password);
    }

    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var input = Regex.Match(html, "<input[^>]*__RequestVerificationToken[^>]*>", RegexOptions.IgnoreCase);
        var value = Regex.Match(input.Value, "value=\"([^\"]+)\"");

        return value.Groups[1].Value;
    }

    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");

        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(DataDirectory))
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Thư mục tạm có thể còn file đang mở trên Windows — bỏ qua.
        }
    }
}
