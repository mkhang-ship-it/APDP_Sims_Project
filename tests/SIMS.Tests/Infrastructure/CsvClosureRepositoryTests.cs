using SIMS.Application.Abstractions;
using SIMS.Domain.Events;
using SIMS.Domain.FeatureRequests;
using SIMS.Domain.ShortUrls;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvClosureRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-closure-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task Short_url_and_visit_round_trip()
    {
        var urls = new CsvShortUrlRepository(PathFor("short_urls.csv"));
        var visits = new CsvShortUrlVisitRepository(PathFor("short_url_visits.csv"));
        var clubId = Guid.NewGuid();

        var url = ShortUrl.Create(
            clubId, "Abc123XyZ", "https://example.com/a,b", "Tiêu đề", Now.AddDays(1), "faculty1", Now);
        await urls.AddAsync(url);
        await visits.AddAsync(ShortUrlVisit.Create(url.Id, Now, "127.0.0.1", "agent"));

        var loaded = await urls.FindByCodeAsync("abc123xyz");
        var loadedVisits = await visits.ListByShortUrlAsync(url.Id);

        Assert.NotNull(loaded);
        Assert.Equal("https://example.com/a,b", loaded!.TargetUrl);
        Assert.Equal(1, await visits.CountByShortUrlAsync(url.Id));
        Assert.Equal("127.0.0.1", loadedVisits[0].IpAddress);
    }

    [Fact]
    public async Task Event_image_round_trip()
    {
        var repository = new CsvEventImageRepository(PathFor("event_images.csv"));
        var eventId = Guid.NewGuid();
        var image = EventImage.Create(eventId, "events/a.png", "Khai mạc", "faculty1", Now);

        await repository.AddAsync(image);
        var loaded = await repository.ListByEventAsync(eventId);

        Assert.Single(loaded);
        Assert.Equal("Khai mạc", loaded[0].Caption);
        Assert.Equal("events/a.png", loaded[0].FilePath);
    }

    [Fact]
    public async Task Feature_request_round_trip()
    {
        var repository = new CsvFeatureRequestRepository(PathFor("feature_requests.csv"));
        var clubId = Guid.NewGuid();
        var request = FeatureRequest.Create(clubId, "Đề xuất, có phẩy", "Nội dung", "student1", Now);
        request.UpdateStatus(FeatureRequestStatus.InProgress, "Đang xử lý", Now.AddHours(1));

        await repository.AddAsync(request);
        var loaded = await repository.FindByIdAsync(request.Id);
        var byCreator = await repository.ListByCreatorAsync("STUDENT1");
        var byClub = await repository.ListByClubAsync(clubId);

        Assert.NotNull(loaded);
        Assert.Equal(FeatureRequestStatus.InProgress, loaded!.Status);
        Assert.Equal("Đang xử lý", loaded.Response);
        Assert.Single(byCreator);
        Assert.Single(byClub);
    }

    [Fact]
    public async Task Audit_log_read_returns_latest_entries()
    {
        var log = new CsvAuditLog(PathFor("audit.csv"));
        for (var index = 0; index < 5; index++)
        {
            await log.WriteAsync(new AuditEntry(
                Now.AddMinutes(index), "admin", $"action.{index}", "test", index.ToString(), string.Empty));
        }

        var entries = await log.ReadAsync(3);

        Assert.Equal(3, entries.Count);
        Assert.Equal("action.2", entries[0].Action);
        Assert.Equal("action.4", entries[^1].Action);
    }
}
