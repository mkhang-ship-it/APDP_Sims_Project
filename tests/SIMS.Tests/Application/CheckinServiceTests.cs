using SIMS.Application.Checkin;
using SIMS.Domain.Events;
using SIMS.Domain.Students;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class CheckinServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static (CheckinService Service, InMemoryEventRepository Events, InMemoryAttendanceRepository Attendance, InMemoryStudentRepository Students, RecordingAuditLog Audit) Create()
    {
        var events = new InMemoryEventRepository();
        var students = new InMemoryStudentRepository();
        var attendance = new InMemoryAttendanceRepository();
        var audit = new RecordingAuditLog();

        var service = new CheckinService(events, students, attendance, audit, new FakeClock(Now));
        return (service, events, attendance, students, audit);
    }

    private static async Task<Event> AddPublishedEventAsync(
        InMemoryEventRepository events,
        int capacity = 10,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null)
    {
        var @event = Event.Create(
            "EVT001",
            "Ngày hội công nghệ",
            string.Empty,
            startsAt ?? Now.AddHours(-1),
            endsAt ?? Now.AddHours(4),
            "Hội trường A",
            capacity,
            null,
            Now);
        @event.Publish();
        await events.AddAsync(@event);

        return @event;
    }

    private static async Task<Student> AddStudentAsync(InMemoryStudentRepository students, string code = "SV001")
    {
        var student = Student.Create(code, $"Sinh viên {code}", $"{code.ToLowerInvariant()}@example.com", new DateOnly(2004, 1, 1), "CNTT", Now);
        await students.AddAsync(student);

        return student;
    }

    [Fact]
    public async Task Checkin_succeeds_and_writes_audit()
    {
        var (service, events, attendance, students, audit) = Create();
        var @event = await AddPublishedEventAsync(events);
        var student = await AddStudentAsync(students);

        var result = await service.CheckInAsync(@event.CheckinToken, student.StudentCode, "Qr", "student1");

        Assert.True(result.IsSuccess);
        Assert.Equal(@event.Id, result.Value!.Event.Id);
        Assert.Equal("Qr", result.Value.Attendance.Method);
        Assert.Single(attendance.All);
        Assert.Equal("checkin.create", audit.Entries[0].Action);
    }

    [Fact]
    public async Task Checkin_rejects_duplicate()
    {
        var (service, events, attendance, students, _) = Create();
        var @event = await AddPublishedEventAsync(events);
        var student = await AddStudentAsync(students);
        await service.CheckInAsync(@event.CheckinToken, student.StudentCode, "Qr", "student1");

        var duplicate = await service.CheckInAsync(@event.CheckinToken, student.StudentCode, "Qr", "student1");

        Assert.False(duplicate.IsSuccess);
        Assert.Single(attendance.All);
    }

    [Fact]
    public async Task Checkin_rejects_unknown_token()
    {
        var (service, _, _, students, _) = Create();
        var student = await AddStudentAsync(students);

        var result = await service.CheckInAsync("KHONGCO", student.StudentCode, "Qr", "student1");

        Assert.False(result.IsSuccess);
        Assert.Contains("không đúng", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checkin_rejects_draft_or_outside_window()
    {
        var (service, events, _, students, _) = Create();
        var student = await AddStudentAsync(students);

        var draft = Event.Create("EVT002", "Nháp", string.Empty, Now.AddHours(-1), Now.AddHours(2), "A1", 10, null, Now);
        await events.AddAsync(draft);

        var draftResult = await service.CheckInAsync(draft.CheckinToken, student.StudentCode, "Qr", "student1");

        var future = await AddPublishedEventAsync(events, startsAt: Now.AddHours(5), endsAt: Now.AddHours(8));
        var outside = await service.CheckInAsync(future.CheckinToken, student.StudentCode, "Qr", "student1");

        Assert.False(draftResult.IsSuccess);
        Assert.False(outside.IsSuccess);
        Assert.Contains("thời gian", outside.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checkin_rejects_when_full()
    {
        var (service, events, _, students, _) = Create();
        var @event = await AddPublishedEventAsync(events, capacity: 1);
        var first = await AddStudentAsync(students, "SV001");
        var second = await AddStudentAsync(students, "SV002");

        await service.CheckInAsync(@event.CheckinToken, first.StudentCode, "Qr", "faculty1");
        var overflow = await service.CheckInAsync(@event.CheckinToken, second.StudentCode, "Qr", "faculty1");

        Assert.False(overflow.IsSuccess);
        Assert.Contains("sức chứa", overflow.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checkin_rejects_unknown_or_inactive_student()
    {
        var (service, events, _, students, _) = Create();
        var @event = await AddPublishedEventAsync(events);
        var student = await AddStudentAsync(students);
        student.Deactivate();
        await students.UpdateAsync(student);

        var unknown = await service.CheckInAsync(@event.CheckinToken, "SV999", "Qr", "faculty1");
        var inactive = await service.CheckInAsync(@event.CheckinToken, student.StudentCode, "Qr", "faculty1");

        Assert.False(unknown.IsSuccess);
        Assert.False(inactive.IsSuccess);
    }
}

public sealed class EventCalendarServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Calendar_filters_events_by_month()
    {
        var events = new InMemoryEventRepository();
        var thisMonth = Event.Create("EVT001", "Sự kiện tháng 10", string.Empty, Now, Now.AddHours(2), "A1", 10, null, Now);
        var nextMonth = Event.Create("EVT002", "Sự kiện tháng 11", string.Empty, Now.AddDays(10), Now.AddDays(10).AddHours(2), "A1", 10, null, Now);
        await events.AddAsync(thisMonth);
        await events.AddAsync(nextMonth);

        var service = new SIMS.Application.Events.EventCalendarService(events);
        var result = await service.GetByMonthAsync(2026, 10, null);

        Assert.Single(result);
        Assert.Equal("EVT001", result[0].Code);
    }
}
