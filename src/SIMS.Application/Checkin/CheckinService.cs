using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Events;
using SIMS.Domain.Students;

namespace SIMS.Application.Checkin;

public sealed record CheckinOutcome(Event Event, Student Student, Attendance Attendance);

public interface ICheckinService
{
    Task<Result<CheckinOutcome>> CheckInAsync(
        string token,
        string studentCode,
        string method,
        string actor,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Check-in sự kiện bằng token QR/link: kiểm tra token, trạng thái + thời gian sự kiện,
/// sinh viên tồn tại/đang học, chống trùng và sức chứa (atomic trong repository).
/// </summary>
public sealed class CheckinService : ICheckinService
{
    private readonly IEventRepository _events;
    private readonly IStudentReader _students;
    private readonly IAttendanceRepository _attendance;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public CheckinService(
        IEventRepository events,
        IStudentReader students,
        IAttendanceRepository attendance,
        IAuditLog audit,
        IClock clock)
    {
        _events = events;
        _students = students;
        _attendance = attendance;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<CheckinOutcome>> CheckInAsync(
        string token,
        string studentCode,
        string method,
        string actor,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedToken = (token ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedToken.Length == 0)
        {
            return Result<CheckinOutcome>.Fail(new ValidationError(nameof(token), "Mã check-in là bắt buộc."));
        }

        var @event = await _events.FindByCheckinTokenAsync(normalizedToken, cancellationToken);
        if (@event is null)
        {
            return Result<CheckinOutcome>.Fail(new ValidationError(nameof(token), "Mã check-in không đúng."));
        }

        if (@event.Status != EventStatus.Published)
        {
            return Result<CheckinOutcome>.Fail(
                new ValidationError(string.Empty, "Sự kiện chưa được công bố hoặc đã kết thúc."));
        }

        if (!@event.IsOpenForCheckin(_clock.UtcNow))
        {
            return Result<CheckinOutcome>.Fail(
                new ValidationError(string.Empty, "Ngoài thời gian check-in của sự kiện."));
        }

        var student = await _students.FindByCodeAsync((studentCode ?? string.Empty).Trim(), cancellationToken);
        if (student is null)
        {
            return Result<CheckinOutcome>.Fail(new ValidationError(nameof(studentCode), "Không tìm thấy MSSV."));
        }

        if (student.Status != StudentStatus.Active)
        {
            return Result<CheckinOutcome>.Fail(
                new ValidationError(string.Empty, "Sinh viên đã ngừng học, không thể check-in."));
        }

        GeoPoint? checkinLocation = null;
        double? distanceKm = null;

        if (latitude is not null || longitude is not null)
        {
            if (latitude is null || longitude is null)
            {
                return Result<CheckinOutcome>.Fail(
                    new ValidationError(string.Empty, "Cần cung cấp đầy đủ vĩ độ và kinh độ khi check-in."));
            }

            try
            {
                checkinLocation = new GeoPoint(latitude.Value, longitude.Value);
            }
            catch (DomainException ex)
            {
                return Result<CheckinOutcome>.Fail(new ValidationError(nameof(latitude), ex.Message));
            }
        }

        // Geofence: giống bản PHP, chỉ kiểm tra khi sự kiện có toạ độ địa điểm.
        if (@event.Venue is { } venue)
        {
            if (checkinLocation is null)
            {
                return Result<CheckinOutcome>.Fail(
                    new ValidationError(string.Empty, "Sự kiện yêu cầu vị trí. Hãy bật định vị trình duyệt và thử lại."));
            }

            distanceKm = Math.Round(venue.DistanceKmTo(checkinLocation.Value), 2, MidpointRounding.AwayFromZero);

            if (distanceKm > GeoPoint.CheckinRadiusKm)
            {
                return Result<CheckinOutcome>.Fail(new ValidationError(
                    string.Empty,
                    $"Bạn đang ở quá xa địa điểm sự kiện ({distanceKm:0.00} km). Vui lòng di chuyển gần hơn (trong bán kính 5 km)."));
            }
        }

        var attendance = Attendance.Create(
            @event.Id, student.Id, method, _clock.UtcNow, checkinLocation, distanceKm);
        var attempt = await _attendance.TryCheckInAsync(attendance, @event.Capacity, cancellationToken);

        if (!attempt.Success)
        {
            return Result<CheckinOutcome>.Fail(new ValidationError(string.Empty, attempt.Error ?? "Không thể check-in."));
        }

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "checkin.create",
                "attendance",
                attendance.Id.ToString("D"),
                $"event={@event.Code} student={student.StudentCode} method={attendance.Method}"),
            cancellationToken);

        return Result<CheckinOutcome>.Ok(new CheckinOutcome(@event, student, attendance));
    }
}
