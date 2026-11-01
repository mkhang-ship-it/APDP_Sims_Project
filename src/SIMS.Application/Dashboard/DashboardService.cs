using SIMS.Application.Abstractions;
using SIMS.Domain.Events;

namespace SIMS.Application.Dashboard;

/// <summary>
/// Số liệu tổng quan cho dashboard. Dữ liệu lấy từ các danh sách service đã nạp sẵn —
/// không thêm truy vấn riêng cho từng con số.
/// </summary>
public sealed record DashboardSummary(
    int Students,
    int Courses,
    int ActiveEnrolments,
    int GradedEnrolments,
    int Clubs = 0,
    int UpcomingEvents = 0)
{
    public IReadOnlyList<DashboardEventRow> NextEvents { get; init; } = Array.Empty<DashboardEventRow>();
}

/// <summary>Một sự kiện sắp diễn ra, đủ để vẽ dòng trong dashboard.</summary>
public sealed record DashboardEventRow(string Title, DateTimeOffset StartsAtUtc, string Location);

public interface IDashboardService
{
    Task<DashboardSummary> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Số liệu tổng quan — chỉ đọc, không phụ thuộc phần ghi (ISP).</summary>
public sealed class DashboardService : IDashboardService
{
    private readonly IStudentReader _students;
    private readonly ICourseRepository _courses;
    private readonly IEnrolmentRepository _enrolments;
    private readonly IClubRepository _clubs;
    private readonly IEventRepository _events;

    public DashboardService(
        IStudentReader students,
        ICourseRepository courses,
        IEnrolmentRepository enrolments,
        IClubRepository clubs,
        IEventRepository events)
    {
        _students = students;
        _courses = courses;
        _enrolments = enrolments;
        _clubs = clubs;
        _events = events;
    }

    public async Task<DashboardSummary> GetAsync(CancellationToken cancellationToken = default)
    {
        var students = await _students.ListAsync(cancellationToken);
        var courses = await _courses.ListAsync(cancellationToken);
        var enrolments = await _enrolments.ListAsync(cancellationToken);
        var clubs = await _clubs.ListAsync(cancellationToken);
        var events = await _events.ListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // "Sắp tới" = đã công bố và chưa kết thúc.
        var upcoming = events
            .Where(e => e.Status == EventStatus.Published && e.EndsAtUtc >= now)
            .OrderBy(e => e.StartsAtUtc)
            .ToArray();

        return new DashboardSummary(
            Students: students.Count,
            Courses: courses.Count,
            ActiveEnrolments: enrolments.Count(e => e.IsActive),
            GradedEnrolments: enrolments.Count(e => e.HasGrade),
            Clubs: clubs.Count(c => c.IsActive),
            UpcomingEvents: upcoming.Length)
        {
            NextEvents = upcoming
                .Take(5)
                .Select(e => new DashboardEventRow(e.Title, e.StartsAtUtc, e.Location))
                .ToArray(),
        };
    }
}
