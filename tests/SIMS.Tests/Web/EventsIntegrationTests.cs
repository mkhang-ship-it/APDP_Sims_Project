using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Events;
using SIMS.Domain.Events;

namespace SIMS.Tests.Web;

/// <summary>Batch 6: sự kiện, lịch tháng và check-in bằng token (nội dung QR).</summary>
public sealed class EventsIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public EventsIntegrationTests(SimsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClientWithoutRedirect();
        var response = await SimsWebApplicationFactory.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task<Event> CreatePublishedEventAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();

        var created = await service.CreateAsync(
            new EventInput(
                code,
                "Sự kiện kiểm thử " + code,
                "Mô tả",
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddHours(4),
                "Hội trường A",
                10),
            null,
            "tests");

        Assert.True(created.IsSuccess);
        var published = await service.PublishAsync(created.Value!.Id, "tests");
        Assert.True(published.IsSuccess);

        return (await service.GetAsync(created.Value.Id))!;
    }

    [Fact]
    public async Task Faculty_can_create_publish_and_see_checkin_panel()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Events/Create");
        var created = await client.PostAsync("/Events/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Code"] = "EVTWEB1",
            ["Title"] = "Sự kiện web",
            ["Description"] = "Tạo qua HTTP",
            ["StartsAt"] = DateTime.Today.AddHours(9).ToString("yyyy-MM-ddTHH:mm"),
            ["EndsAt"] = DateTime.Today.AddHours(12).ToString("yyyy-MM-ddTHH:mm"),
            ["Location"] = "Phòng A1",
            ["Capacity"] = "20",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.StartsWith("/Events/Details/", created.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var detailsUrl = created.Headers.Location!.OriginalString;
        var details = await client.GetStringAsync(detailsUrl);
        Assert.Contains("EVTWEB1", details);
        Assert.Contains("Check-in", details);
    }

    [Fact]
    public async Task Student_checks_in_with_token_and_appears_in_attendance()
    {
        var @event = await CreatePublishedEventAsync("EVTWEB2");

        using var studentClient = await LoginAsync("student1", "Student@12345");
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(studentClient, "/Checkin");

        var response = await studentClient.PostAsync("/Checkin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = @event.CheckinToken,
            ["studentCode"] = string.Empty,
            ["method"] = "Qr",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var facultyClient = await LoginAsync("faculty1", "Faculty@12345");
        var details = await facultyClient.GetStringAsync($"/Events/Details/{@event.Id}");

        Assert.Contains("SV0001", details);
        Assert.Contains("Qr", details);
    }

    [Fact]
    public async Task Duplicate_checkin_is_rejected()
    {
        var @event = await CreatePublishedEventAsync("EVTWEB3");
        using var client = await LoginAsync("student1", "Student@12345");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Checkin");
            await client.PostAsync("/Checkin", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = @event.CheckinToken,
                ["studentCode"] = string.Empty,
                ["method"] = "Qr",
                ["__RequestVerificationToken"] = token,
            }));
        }

        var page = await client.GetStringAsync("/Checkin?code=" + @event.CheckinToken);
        Assert.Contains("đã check-in", page);
    }

    [Fact]
    public async Task Student_can_view_events_but_cannot_create_them()
    {
        using var client = await LoginAsync("student1", "Student@12345");

        var list = await client.GetAsync("/Events");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var create = await client.GetAsync("/Events/Create");
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal("/Account/AccessDenied", create.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Calendar_page_renders_for_faculty()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var response = await client.GetAsync("/Events/Calendar");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Lịch tháng", html);
    }

    [Fact]
    public async Task Checkin_rejects_invalid_token()
    {
        using var client = await LoginAsync("student1", "Student@12345");
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Checkin");

        await client.PostAsync("/Checkin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "KHONGCO12345",
            ["studentCode"] = string.Empty,
            ["method"] = "Manual",
            ["__RequestVerificationToken"] = token,
        }));

        var page = await client.GetStringAsync("/Checkin?code=KHONGCO12345");
        Assert.Contains("không đúng", page, StringComparison.OrdinalIgnoreCase);
    }
}
