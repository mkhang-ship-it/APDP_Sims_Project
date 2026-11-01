using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Abstractions;
using SIMS.Application.Events;

namespace SIMS.Tests.Web;

/// <summary>Batch 7: kế hoạch sự kiện (mục tiêu, hành động, ngân sách, thiết bị, timeline) + export CSV.</summary>
public sealed class EventPlansIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public EventPlansIntegrationTests(SimsWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClientWithoutRedirect();
        var response = await SimsWebApplicationFactory.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    private async Task<Guid> GetEventIdAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<IEventRepository>();
        var @event = await events.FindByCodeAsync(code);

        Assert.NotNull(@event);
        return @event!.Id;
    }

    private async Task<Guid> CreateEventAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();

        var created = await service.CreateAsync(
            new EventInput(
                code,
                "Sự kiện kế hoạch " + code,
                "Mô tả",
                DateTimeOffset.UtcNow.AddDays(3),
                DateTimeOffset.UtcNow.AddDays(3).AddHours(4),
                "Hội trường B",
                30),
            null,
            "tests");

        Assert.True(created.IsSuccess);
        return created.Value!.Id;
    }

    private static async Task<HttpResponseMessage> PostAsync(
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
    public async Task Faculty_sees_seeded_plan_and_link_from_event_details()
    {
        var eventId = await GetEventIdAsync("EVT001");
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var planPage = await client.GetAsync($"/EventPlans?eventId={eventId}");

        Assert.Equal(HttpStatusCode.OK, planPage.StatusCode);
        var html = await planPage.Content.ReadAsStringAsync();
        Assert.Contains("Kế hoạch:", html);
        Assert.Contains("Tổ chức ngày hội công nghệ", html);
        Assert.Contains("Nước uống", html);
        Assert.Contains("Xuất CSV", html);

        var details = await client.GetStringAsync($"/Events/Details/{eventId}");
        Assert.Contains($"/EventPlans?eventId={eventId}", details.Replace("&amp;", "&"));
    }

    [Fact]
    public async Task Faculty_adds_items_and_exports_csv()
    {
        var eventId = await CreateEventAsync("EVTPL01");
        using var client = await LoginAsync("faculty1", "Faculty@12345");
        var pageUrl = $"/EventPlans?eventId={eventId}";

        var objective = await PostAsync(client, "/EventPlans/AddObjective", pageUrl, new()
        {
            ["eventId"] = eventId.ToString(),
            ["content"] = "Mục tiêu từ web",
        });
        var budget = await PostAsync(client, "/EventPlans/AddBudget", pageUrl, new()
        {
            ["eventId"] = eventId.ToString(),
            ["itemName"] = "Áo đồng phục",
            ["quantity"] = "4",
            ["unitPrice"] = "125000",
        });

        Assert.Equal(HttpStatusCode.Redirect, objective.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, budget.StatusCode);

        var html = await client.GetStringAsync(pageUrl);
        Assert.Contains("Mục tiêu từ web", html);
        Assert.Contains("Áo đồng phục", html);

        var export = await client.GetAsync($"/EventPlans/Export?eventId={eventId}");

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains("event-plan-", export.Content.Headers.ContentDisposition?.FileName);

        var bytes = await export.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

        var csv = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("objective,Mục tiêu từ web", csv);
        Assert.Contains("budget,Áo đồng phục,4,125000,500000", csv);
    }

    [Fact]
    public async Task Faculty_removes_item_from_plan()
    {
        var eventId = await CreateEventAsync("EVTPL02");
        using var client = await LoginAsync("faculty1", "Faculty@12345");
        var pageUrl = $"/EventPlans?eventId={eventId}";

        await PostAsync(client, "/EventPlans/AddTimeline", pageUrl, new()
        {
            ["eventId"] = eventId.ToString(),
            ["timeLabel"] = "07:30",
            ["activity"] = "Đón khách",
        });

        var before = await client.GetStringAsync(pageUrl);
        Assert.Contains("Đón khách", before);
        var itemId = ExtractItemId(before, "timeline", "Đón khách");

        var remove = await PostAsync(client, "/EventPlans/Remove", pageUrl, new()
        {
            ["eventId"] = eventId.ToString(),
            ["section"] = "timeline",
            ["itemId"] = itemId,
        });

        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.DoesNotContain("Đón khách", await client.GetStringAsync(pageUrl));
    }

    [Fact]
    public async Task Student_is_denied_on_plan_module()
    {
        var eventId = await GetEventIdAsync("EVT001");
        using var client = await LoginAsync("student1", "Student@12345");

        var response = await client.GetAsync($"/EventPlans?eventId={eventId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Unknown_event_returns_404_and_anonymous_is_redirected()
    {
        using var anonymous = _factory.CreateClientWithoutRedirect();
        var redirect = await anonymous.GetAsync($"/EventPlans?eventId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/Account/Login", redirect.Headers.Location?.AbsolutePath);

        using var client = await LoginAsync("faculty1", "Faculty@12345");
        var missing = await client.GetAsync($"/EventPlans?eventId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static string ExtractItemId(string html, string section, string activity)
    {
        var index = html.IndexOf(activity, StringComparison.Ordinal);
        Assert.True(index >= 0, "Không tìm thấy mục vừa thêm trong HTML.");

        var sectionMarker = html.IndexOf($"value=\"{section}\"", index, StringComparison.Ordinal);
        Assert.True(sectionMarker >= 0, "Không tìm thấy section trong HTML.");

        var start = html.IndexOf("name=\"itemId\" value=\"", sectionMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Không tìm thấy itemId trong HTML.");

        start += "name=\"itemId\" value=\"".Length;
        return html.Substring(start, html.IndexOf('"', start) - start);
    }
}
