using SIMS.Domain.Events;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvGeofenceAndPlanTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-gap2-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Event_venue_round_trips()
    {
        var repository = new CsvEventRepository(Path.Combine(_directory, "events.csv"));
        var venue = new GeoPoint(10.7769, 106.7009);
        var @event = Event.Create("EVT001", "Ngày hội", "Mô tả", Now, Now.AddHours(3), "A1", 30, null, Now, venue);

        await repository.AddAsync(@event);
        var loaded = await repository.FindByCodeAsync("EVT001");

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Venue);
        Assert.Equal(10.7769, loaded.Venue!.Value.Latitude, 5);
        Assert.Equal(106.7009, loaded.Venue!.Value.Longitude, 5);
    }

    [Fact]
    public async Task Old_event_rows_without_venue_are_still_loaded()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "events.csv");
        await File.WriteAllTextAsync(
            path,
            "id,code,title,description,starts_at_utc,ends_at_utc,location,capacity,status,club_id,checkin_token,created_at_utc\n" +
            $"{Guid.NewGuid():D},EVTOLD,Ten cu,,{Now:O},{Now.AddHours(2):O},A1,10,Draft,,ABC123456789,{Now:O}\n",
            new System.Text.UTF8Encoding(false));

        var repository = new CsvEventRepository(path);
        var items = await repository.ListAsync();

        var loaded = Assert.Single(items);
        Assert.Equal("EVTOLD", loaded.Code);
        Assert.Null(loaded.Venue);
    }

    [Fact]
    public async Task Attendance_location_and_distance_round_trip()
    {
        var repository = new CsvAttendanceRepository(Path.Combine(_directory, "attendance.csv"));
        var eventId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var attendance = Attendance.Create(
            eventId, studentId, "Qr", Now, new GeoPoint(10.7769, 106.7009), 2.5);

        await repository.TryCheckInAsync(attendance, 10);
        var loaded = Assert.Single(await repository.ListByEventAsync(eventId));

        Assert.NotNull(loaded.CheckinLocation);
        Assert.Equal(2.5, loaded.DistanceKm);
        Assert.Equal(studentId, loaded.StudentId);
    }

    [Fact]
    public async Task Old_attendance_rows_without_location_are_still_loaded()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "attendance.csv");
        var eventId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        await File.WriteAllTextAsync(
            path,
            "id,event_id,student_id,method,checked_in_at_utc\n" +
            $"{Guid.NewGuid():D},{eventId:D},{studentId:D},Qr,{Now:O}\n",
            new System.Text.UTF8Encoding(false));

        var repository = new CsvAttendanceRepository(path);
        var items = await repository.ListByEventAsync(eventId);

        var loaded = Assert.Single(items);
        Assert.Null(loaded.CheckinLocation);
        Assert.Null(loaded.DistanceKm);
    }

    [Fact]
    public async Task Plan_details_round_trip()
    {
        var repository = new CsvEventPlanRepository(Path.Combine(_directory, "event_plans.csv"));
        var eventId = Guid.NewGuid();
        var plan = EventPlan.Create(
            eventId,
            "Kế hoạch",
            Now,
            "Ban truyền thông",
            new DateOnly(2026, 11, 20),
            "Ban giám hiệu",
            "Sinh viên",
            "Mô tả chi tiết",
            "CLB Công nghệ",
            "Cố vấn");

        await repository.AddAsync(plan);
        var loaded = await repository.FindByEventAsync(eventId);

        Assert.NotNull(loaded);
        Assert.Equal("Ban truyền thông", loaded!.PreparedBy);
        Assert.Equal(new DateOnly(2026, 11, 20), loaded.ImplementationDate);
        Assert.Equal("Ban giám hiệu", loaded.ApprovedBy);
        Assert.Equal("Mô tả chi tiết", loaded.Description);
        Assert.Equal("CLB Công nghệ", loaded.ResponsibleOrganizer);
        Assert.Equal("Cố vấn", loaded.AdvisorCoordinator);
    }

    [Fact]
    public async Task Old_plan_rows_without_details_are_still_loaded()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "event_plans.csv");
        var eventId = Guid.NewGuid();
        await File.WriteAllTextAsync(
            path,
            "id,event_id,summary,created_at_utc,updated_at_utc\n" +
            $"{Guid.NewGuid():D},{eventId:D},Tom tat,{Now:O},{Now:O}\n",
            new System.Text.UTF8Encoding(false));

        var repository = new CsvEventPlanRepository(path);
        var loaded = await repository.FindByEventAsync(eventId);

        Assert.NotNull(loaded);
        Assert.Equal("Tom tat", loaded!.Summary);
        Assert.Empty(loaded.PreparedBy);
    }
}
