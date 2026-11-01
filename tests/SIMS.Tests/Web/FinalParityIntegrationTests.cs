using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.EventPlans;
using SIMS.Domain.Events;
using SIMS.Domain.Security;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Web;

/// <summary>Final parity: geofence check-in, xuất/nhập Excel, thông tin kế hoạch mở rộng.</summary>
public sealed class FinalParityIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public FinalParityIntegrationTests(SimsWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClientWithoutRedirect();
        var response = await SimsWebApplicationFactory.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
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

    private static string Local(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm");

    [Fact]
    public async Task Students_export_xlsx_is_a_real_workbook()
    {
        using var client = await LoginAsync("admin", "Admin@12345");

        var response = await client.GetAsync("/Students/Export?format=xlsx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(Xlsx.LooksLikeXlsx(bytes.AsSpan(0, 2)));

        using var stream = new MemoryStream(bytes);
        var rows = Xlsx.Read(stream);

        Assert.NotEmpty(rows);
        Assert.Equal("student_code", rows[0][0]);
        Assert.Contains(rows, row => row.Count > 0 && row[0] == "SV0001");
    }

    [Fact]
    public async Task Xlsx_student_import_creates_student()
    {
        using var client = await LoginAsync("admin", "Admin@12345");
        var code = "SVX" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var workbook = Xlsx.Write("Sinh viên",
        [
            new[] { "student_code", "full_name", "email", "date_of_birth", "program" },
            new[] { code, "Sinh viên Excel", $"{code.ToLowerInvariant()}@example.com", "2004-05-06", "CNTT" },
        ]);

        var form = new MultipartFormDataContent();
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Students/Import");
        form.Add(new StringContent(token), "__RequestVerificationToken");
        var file = new ByteArrayContent(workbook);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "sinh-vien.xlsx");

        var response = await client.PostAsync("/Students/Import", form);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Import xong: 1/1", html);

        using var scope = _factory.Services.CreateScope();
        var students = scope.ServiceProvider.GetRequiredService<IStudentReader>();
        var created = await students.FindByCodeAsync(code);
        Assert.NotNull(created);
        Assert.Equal("Sinh viên Excel", created!.FullName);
    }

    [Fact]
    public async Task Event_plan_xlsx_export_contains_extended_fields()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var eventsHtml = await client.GetStringAsync("/Events");
        var match = System.Text.RegularExpressions.Regex.Match(
            eventsHtml, "/Events/Details/([0-9a-fA-F-]{36})");
        Assert.True(match.Success, "Không tìm thấy sự kiện mẫu trong trang /Events.");

        var eventId = match.Groups[1].Value;
        await client.GetAsync($"/EventPlans?eventId={eventId}");

        var response = await client.GetAsync($"/EventPlans/Export?eventId={eventId}&format=xlsx");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        var rows = Xlsx.Read(stream);
        var flat = string.Join("\n", rows.Select(row => string.Join("|", row)));

        Assert.Contains("prepared_by", flat);
        Assert.Contains("advisor_coordinator", flat);
    }

    [Fact]
    public async Task Geofence_checkin_rejects_far_location_and_accepts_near()
    {
        using var client = await LoginAsync("admin", "Admin@12345");
        var code = "GEO" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var create = await PostFormAsync(client, "/Events/Create", "/Events/Create", new Dictionary<string, string>
        {
            ["Code"] = code,
            ["Title"] = "Sự kiện geofence",
            ["Description"] = "Kiểm tra bán kính 5 km",
            ["StartsAt"] = Local(DateTime.Now.AddHours(-1)),
            ["EndsAt"] = Local(DateTime.Now.AddHours(2)),
            ["Location"] = "Hội trường A",
            ["Capacity"] = "50",
            ["VenueLatitude"] = "10.7769",
            ["VenueLongitude"] = "106.7009",
        });

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        Guid eventId;
        string token;

        using (var scope = _factory.Services.CreateScope())
        {
            var events = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            var created = await events.FindByCodeAsync(code);
            Assert.NotNull(created);
            eventId = created!.Id;
            token = created.CheckinToken;
        }

        var publish = await PostFormAsync(
            client, $"/Events/Publish/{eventId}", $"/Events/Details/{eventId}", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);

        var far = await PostFormAsync(client, "/Checkin", $"/Checkin?code={token}", new Dictionary<string, string>
        {
            ["token"] = token,
            ["studentCode"] = "SV0001",
            ["method"] = "Qr",
            ["latitude"] = "21.0278",
            ["longitude"] = "105.8342",
        });

        Assert.Equal(HttpStatusCode.Redirect, far.StatusCode);
        var farPage = await client.GetStringAsync("/Checkin?code=" + token);
        Assert.Contains("quá xa", farPage);

        var near = await PostFormAsync(client, "/Checkin", $"/Checkin?code={token}", new Dictionary<string, string>
        {
            ["token"] = token,
            ["studentCode"] = "SV0001",
            ["method"] = "Qr",
            ["latitude"] = "10.77691",
            ["longitude"] = "106.70091",
        });

        Assert.Equal(HttpStatusCode.Redirect, near.StatusCode);
        var nearPage = await client.GetStringAsync("/Checkin?code=" + token);
        Assert.Contains("Check-in thành công", nearPage);

        using (var scope = _factory.Services.CreateScope())
        {
            var attendance = scope.ServiceProvider.GetRequiredService<IAttendanceRepository>();
            var rows = await attendance.ListByEventAsync(eventId);
            var saved = Assert.Single(rows);
            Assert.NotNull(saved.DistanceKm);
            Assert.InRange(saved.DistanceKm!.Value, 0d, 0.2d);
        }
    }
}
