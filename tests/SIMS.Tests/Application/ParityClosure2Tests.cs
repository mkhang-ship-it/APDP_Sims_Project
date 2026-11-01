using SIMS.Application.Checkin;
using SIMS.Application.Common;
using SIMS.Application.EventPlans;
using SIMS.Domain.Events;
using SIMS.Domain.Students;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class GeofenceCheckinTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static (CheckinService Service, InMemoryEventRepository Events, InMemoryAttendanceRepository Attendance, InMemoryStudentRepository Students)
        Create()
    {
        var events = new InMemoryEventRepository();
        var students = new InMemoryStudentRepository();
        var attendance = new InMemoryAttendanceRepository();
        var service = new CheckinService(events, students, attendance, new RecordingAuditLog(), new FakeClock(Now));

        return (service, events, attendance, students);
    }

    private static async Task<Event> AddEventAsync(InMemoryEventRepository events, GeoPoint? venue)
    {
        var @event = Event.Create(
            "EVTGE01", "Ngày hội", string.Empty, Now.AddHours(-1), Now.AddHours(3), "Hội trường A", 10, null, Now, venue);
        @event.Publish();
        await events.AddAsync(@event);

        return @event;
    }

    private static async Task<Student> AddStudentAsync(InMemoryStudentRepository students)
    {
        var student = Student.Create(
            "SVGEO01", "Sinh viên Geofence", "geo@example.com", new DateOnly(2004, 1, 1), "CNTT", Now);
        await students.AddAsync(student);

        return student;
    }

    [Fact]
    public async Task Checkin_succeeds_within_radius_and_persists_distance()
    {
        var (service, events, attendance, students) = Create();
        var @event = await AddEventAsync(events, new GeoPoint(10.7769, 106.7009));
        var student = await AddStudentAsync(students);

        var result = await service.CheckInAsync(
            @event.CheckinToken, student.StudentCode, "Qr", "student1", 10.77691, 106.70091);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(attendance.All);
        Assert.NotNull(saved.CheckinLocation);
        Assert.InRange(saved.DistanceKm!.Value, 0d, 0.1d);
    }

    [Fact]
    public async Task Checkin_rejects_when_far_from_venue()
    {
        var (service, events, attendance, students) = Create();
        var @event = await AddEventAsync(events, new GeoPoint(10.7769, 106.7009));
        var student = await AddStudentAsync(students);

        var result = await service.CheckInAsync(
            @event.CheckinToken, student.StudentCode, "Qr", "student1", 21.0278, 105.8342);

        Assert.False(result.IsSuccess);
        Assert.Contains("quá xa", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(attendance.All);
    }

    [Fact]
    public async Task Checkin_requires_location_when_event_has_venue()
    {
        var (service, events, _, students) = Create();
        var @event = await AddEventAsync(events, new GeoPoint(10.7769, 106.7009));
        var student = await AddStudentAsync(students);

        var result = await service.CheckInAsync(@event.CheckinToken, student.StudentCode, "Qr", "student1");

        Assert.False(result.IsSuccess);
        Assert.Contains("vị trí", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checkin_without_venue_still_accepts_missing_location()
    {
        var (service, events, attendance, students) = Create();
        var @event = await AddEventAsync(events, venue: null);
        var student = await AddStudentAsync(students);

        var result = await service.CheckInAsync(@event.CheckinToken, student.StudentCode, "Qr", "student1");

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(attendance.All).DistanceKm);
    }

    [Fact]
    public async Task Checkin_rejects_invalid_coordinates()
    {
        var (service, events, _, students) = Create();
        var @event = await AddEventAsync(events, venue: null);
        var student = await AddStudentAsync(students);

        var result = await service.CheckInAsync(
            @event.CheckinToken, student.StudentCode, "Qr", "student1", 10.5, null);

        Assert.False(result.IsSuccess);

        var outOfRange = await service.CheckInAsync(
            @event.CheckinToken, student.StudentCode, "Qr", "student1", 190, 10);

        Assert.False(outOfRange.IsSuccess);
        Assert.Contains("Vĩ độ", outOfRange.Errors[0].Message);
    }
}

public sealed class XlsxTests
{
    [Fact]
    public void Round_trip_keeps_vietnamese_and_special_characters()
    {
        var rows = new List<IReadOnlyList<string?>>
        {
            new[] { "student_code", "full_name", "note" },
            new[] { "SV0001", "Nguyễn Văn A", "Có dấu, phẩy" },
            new[] { "SV0002", "Trần Thị B", "Dòng \"trích dẫn\"" },
            new[] { string.Empty, string.Empty, string.Empty },
        };

        var bytes = Xlsx.Write("Sinh viên", rows);

        Assert.True(Xlsx.LooksLikeXlsx(bytes.AsSpan(0, 2)));

        using var stream = new MemoryStream(bytes);
        var read = Xlsx.Read(stream);

        Assert.Equal(4, read.Count);
        Assert.Equal("Nguyễn Văn A", read[1][1]);
        Assert.Equal("Có dấu, phẩy", read[1][2]);
        Assert.Equal("Dòng \"trích dẫn\"", read[2][2]);
        Assert.Empty(read[3]);
    }

    [Fact]
    public void To_csv_quotes_values_with_separators()
    {
        var rows = new List<IReadOnlyList<string?>>
        {
            new[] { "a", "b\"c", "d,e" },
        };

        var bytes = Xlsx.Write("Sheet1", rows);
        using var stream = new MemoryStream(bytes);
        var csv = Xlsx.ToCsv(stream);

        Assert.Contains("\"b\"\"c\"", csv);
        Assert.Contains("\"d,e\"", csv);
    }

    [Fact]
    public void Invalid_stream_throws_invalid_data()
    {
        using var stream = new MemoryStream("not a zip"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => Xlsx.Read(stream));
    }
}

public sealed class EventPlanDetailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static (EventPlanService Service, InMemoryEventPlanRepository Plans, RecordingAuditLog Audit) Create()
    {
        var plans = new InMemoryEventPlanRepository();
        var audit = new RecordingAuditLog();
        var service = new EventPlanService(
            plans,
            new InMemoryPlanItemRepository<PlanObjective>(x => x.EventPlanId),
            new InMemoryPlanItemRepository<PlanAction>(x => x.EventPlanId),
            new InMemoryPlanItemRepository<PlanBudgetItem>(x => x.EventPlanId),
            new InMemoryPlanItemRepository<PlanEquipment>(x => x.EventPlanId),
            new InMemoryPlanItemRepository<PlanTimelineItem>(x => x.EventPlanId),
            audit,
            new FakeClock(Now));

        return (service, plans, audit);
    }

    [Fact]
    public async Task Update_details_persists_php_event_plan_fields()
    {
        var (service, plans, audit) = Create();
        var eventId = Guid.NewGuid();
        await service.GetOrCreateAsync(eventId, "faculty1");

        var result = await service.UpdateDetailsAsync(
            eventId,
            new EventPlanDetailsInput(
                "Kế hoạch ngày hội",
                "Ban truyền thông",
                new DateOnly(2026, 11, 20),
                "Ban giám hiệu",
                "Sinh viên toàn trường",
                "Tổ chức tại sân trường",
                "CLB Công nghệ",
                "Cố vấn học tập"),
            "faculty1");

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(plans.All);
        Assert.Equal("Ban truyền thông", saved.PreparedBy);
        Assert.Equal(new DateOnly(2026, 11, 20), saved.ImplementationDate);
        Assert.Equal("Ban giám hiệu", saved.ApprovedBy);
        Assert.Equal("Sinh viên toàn trường", saved.Participants);
        Assert.Equal("Tổ chức tại sân trường", saved.Description);
        Assert.Equal("CLB Công nghệ", saved.ResponsibleOrganizer);
        Assert.Equal("Cố vấn học tập", saved.AdvisorCoordinator);
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact]
    public async Task Update_details_rejects_overlong_field()
    {
        var (service, _, _) = Create();
        var eventId = Guid.NewGuid();
        await service.GetOrCreateAsync(eventId, "faculty1");

        var result = await service.UpdateDetailsAsync(
            eventId,
            new EventPlanDetailsInput(new string('x', 2001), null, null, null, null, null, null, null),
            "faculty1");

        Assert.False(result.IsSuccess);
        Assert.Contains("2000", result.Errors[0].Message);
    }

    [Fact]
    public async Task Export_xlsx_contains_plan_details_and_items()
    {
        var (service, _, _) = Create();
        var eventId = Guid.NewGuid();
        await service.GetOrCreateAsync(eventId, "faculty1");
        await service.UpdateDetailsAsync(
            eventId,
            new EventPlanDetailsInput(
                "Kế hoạch ngày hội", "Ban truyền thông", new DateOnly(2026, 11, 20), null, null, null, null, null),
            "faculty1");
        await service.AddObjectiveAsync(eventId, "Thu hút 500 sinh viên", "faculty1");

        var result = await service.ExportXlsxAsync(eventId);

        Assert.True(result.IsSuccess);
        using var stream = new MemoryStream(result.Value!);
        var rows = Xlsx.Read(stream);
        var flat = string.Join("\n", rows.Select(row => string.Join("|", row)));

        Assert.Contains("prepared_by", flat);
        Assert.Contains("Ban truyền thông", flat);
        Assert.Contains("Thu hút 500 sinh viên", flat);
    }

    [Fact]
    public async Task Export_csv_includes_extended_fields()
    {
        var (service, _, _) = Create();
        var eventId = Guid.NewGuid();
        await service.GetOrCreateAsync(eventId, "faculty1");
        await service.UpdateDetailsAsync(
            eventId,
            new EventPlanDetailsInput(
                "Kế hoạch", "Người lập", new DateOnly(2026, 12, 1), "Người duyệt", "Sinh viên", "Mô tả",
                "Chủ trì", "Cố vấn"),
            "faculty1");

        var result = await service.ExportCsvAsync(eventId);

        Assert.True(result.IsSuccess);
        Assert.Contains("prepared_by,Người lập", result.Value);
        Assert.Contains("approved_by,Người duyệt", result.Value);
        Assert.Contains("advisor_coordinator,Cố vấn", result.Value);
    }
}
