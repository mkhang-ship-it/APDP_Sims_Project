using SIMS.Domain.Common;

namespace SIMS.Domain.Events;

/// <summary>Check-in của một sinh viên cho một sự kiện (duy nhất theo cặp event+student).</summary>
public sealed class Attendance : Entity
{
    public static readonly string[] AllowedMethods = ["Qr", "Manual", "Link"];

    private Attendance()
    {
        Method = "Manual";
    }

    public Guid EventId { get; private set; }

    public Guid StudentId { get; private set; }

    public DateTimeOffset CheckedInAtUtc { get; private set; }

    /// <summary>Qr = quét mã, Link = mở link có token, Manual = nhập mã tay.</summary>
    public string Method { get; private set; }

    /// <summary>Toạ độ thiết bị lúc check-in (nếu sự kiện có geofence).</summary>
    public GeoPoint? CheckinLocation { get; private set; }

    /// <summary>Khoảng cách tới địa điểm sự kiện (km), null khi không kiểm tra.</summary>
    public double? DistanceKm { get; private set; }

    public static Attendance Create(
        Guid eventId,
        Guid studentId,
        string method,
        DateTimeOffset checkedInAtUtc,
        GeoPoint? checkinLocation = null,
        double? distanceKm = null)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("Sự kiện không hợp lệ.");
        }

        if (studentId == Guid.Empty)
        {
            throw new DomainException("Sinh viên không hợp lệ.");
        }

        if (distanceKm is < 0d or > 20_000d)
        {
            throw new DomainException("Khoảng cách check-in không hợp lệ.");
        }

        var normalizedMethod = NormalizeMethod(method);

        return new Attendance
        {
            EventId = eventId,
            StudentId = studentId,
            Method = normalizedMethod,
            CheckedInAtUtc = checkedInAtUtc,
            CheckinLocation = checkinLocation,
            DistanceKm = distanceKm,
        };
    }

    public static Attendance Restore(
        Guid id,
        Guid eventId,
        Guid studentId,
        string method,
        DateTimeOffset checkedInAtUtc,
        GeoPoint? checkinLocation = null,
        double? distanceKm = null)
    {
        var attendance = Create(eventId, studentId, method, checkedInAtUtc, checkinLocation, distanceKm);
        attendance.Id = id;

        return attendance;
    }

    private static string NormalizeMethod(string method)
    {
        var value = (method ?? string.Empty).Trim();

        var matched = AllowedMethods.FirstOrDefault(
            m => string.Equals(m, value, StringComparison.OrdinalIgnoreCase));

        if (matched is null)
        {
            throw new DomainException("Phương thức check-in không hợp lệ.");
        }

        return matched;
    }
}
