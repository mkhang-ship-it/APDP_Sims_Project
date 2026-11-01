using SIMS.Domain.Events;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvEventRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-events-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Event_round_trip_keeps_status_token_and_club()
    {
        var repository = new CsvEventRepository(Path.Combine(_directory, "events.csv"));
        var clubId = Guid.NewGuid();
        var @event = Event.Create("EVT001", "Ngày hội", "Mô tả", Now.AddHours(1), Now.AddHours(4), "A1", 30, clubId, Now);

        await repository.AddAsync(@event);

        var loaded = await repository.FindByCodeAsync("evt001");

        Assert.NotNull(loaded);
        Assert.Equal(@event.Id, loaded!.Id);
        Assert.Equal(@event.CheckinToken, loaded.CheckinToken);
        Assert.Equal(clubId, loaded.ClubId);
        Assert.Equal(EventStatus.Draft, loaded.Status);
    }

    [Fact]
    public async Task Find_by_token_and_list_by_club()
    {
        var repository = new CsvEventRepository(Path.Combine(_directory, "events.csv"));
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        var first = Event.Create("EVT001", "Sự kiện A", string.Empty, Now, Now.AddHours(2), "A1", 10, clubA, Now);
        var second = Event.Create("EVT002", "Sự kiện B", string.Empty, Now, Now.AddHours(2), "B1", 10, clubB, Now);
        await repository.AddAsync(first);
        await repository.AddAsync(second);

        var byToken = await repository.FindByCheckinTokenAsync(first.CheckinToken);
        var byClub = await repository.ListByClubAsync(clubB);

        Assert.Equal("EVT001", byToken!.Code);
        Assert.Single(byClub);
        Assert.Equal("EVT002", byClub[0].Code);
    }

    [Fact]
    public async Task Publish_is_persisted_after_update()
    {
        var repository = new CsvEventRepository(Path.Combine(_directory, "events.csv"));
        var @event = Event.Create("EVT001", "Sự kiện", string.Empty, Now, Now.AddHours(2), "A1", 10, null, Now);
        await repository.AddAsync(@event);

        @event.Publish();
        await repository.UpdateAsync(@event);

        var loaded = await repository.FindByIdAsync(@event.Id);
        Assert.Equal(EventStatus.Published, loaded!.Status);
    }
}

public sealed class CsvAttendanceRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-attendance-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private CsvAttendanceRepository CreateRepository()
        => new(Path.Combine(_directory, "attendance.csv"));

    [Fact]
    public async Task Try_checkin_writes_once()
    {
        var repository = CreateRepository();
        var eventId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        var outcome = await repository.TryCheckInAsync(Attendance.Create(eventId, studentId, "Qr", Now), capacity: 10);

        Assert.True(outcome.Success);
        Assert.True(await repository.ExistsAsync(eventId, studentId));
        Assert.Single(await repository.ListByEventAsync(eventId));
    }

    [Fact]
    public async Task Try_checkin_rejects_duplicate_without_writing()
    {
        var repository = CreateRepository();
        var eventId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        await repository.TryCheckInAsync(Attendance.Create(eventId, studentId, "Qr", Now), 10);

        var duplicate = await repository.TryCheckInAsync(Attendance.Create(eventId, studentId, "Qr", Now), 10);

        Assert.False(duplicate.Success);
        Assert.Contains("đã check-in", duplicate.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await repository.ListByEventAsync(eventId));
    }

    [Fact]
    public async Task Try_checkin_rejects_when_capacity_reached()
    {
        var repository = CreateRepository();
        var eventId = Guid.NewGuid();
        await repository.TryCheckInAsync(Attendance.Create(eventId, Guid.NewGuid(), "Qr", Now), capacity: 1);

        var overflow = await repository.TryCheckInAsync(Attendance.Create(eventId, Guid.NewGuid(), "Qr", Now), capacity: 1);

        Assert.False(overflow.Success);
        Assert.Contains("sức chứa", overflow.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await repository.ListByEventAsync(eventId));
    }
}
