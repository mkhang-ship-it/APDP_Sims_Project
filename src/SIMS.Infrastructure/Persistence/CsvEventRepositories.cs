using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Events;

namespace SIMS.Infrastructure.Persistence;

/// <summary>events.csv — sự kiện CLB + token check-in.</summary>
public sealed class CsvEventRepository : CsvRepository<Event>, IEventRepository
{
    private const int ColumnCount = 12;
    private const int ColumnCountWithVenue = 14;

    public CsvEventRepository(string filePath)
        : base(
            filePath,
            "id",
            "code",
            "title",
            "description",
            "starts_at_utc",
            "ends_at_utc",
            "location",
            "capacity",
            "status",
            "club_id",
            "checkin_token",
            "created_at_utc",
            "venue_latitude",
            "venue_longitude")
    {
    }

    public CsvEventRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<Event?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var target = (code ?? string.Empty).Trim();
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(e => string.Equals(e.Code, target, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ExistsByCodeAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var target = (code ?? string.Empty).Trim();
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Any(e =>
            string.Equals(e.Code, target, StringComparison.OrdinalIgnoreCase) &&
            (excludeId is null || e.Id != excludeId));
    }

    public async Task<Event?> FindByCheckinTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var target = (token ?? string.Empty).Trim();
        if (target.Length == 0)
        {
            return null;
        }

        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(e => string.Equals(e.CheckinToken, target, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<Event>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.Where(e => e.ClubId == clubId).OrderBy(e => e.StartsAtUtc).ToArray();
    }

    protected override string[] ToCells(Event @event) =>
    [
        @event.Id.ToString("D"),
        @event.Code,
        @event.Title,
        @event.Description,
        @event.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
        @event.EndsAtUtc.ToString("O", CultureInfo.InvariantCulture),
        @event.Location,
        @event.Capacity.ToString(CultureInfo.InvariantCulture),
        @event.Status.ToString(),
        @event.ClubId?.ToString("D") ?? string.Empty,
        @event.CheckinToken,
        @event.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        @event.Venue?.Latitude.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
        @event.Venue?.Longitude.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
    ];

    protected override Event? Map(string[] cells)
    {
        if ((cells.Length != ColumnCount && cells.Length != ColumnCountWithVenue) ||
            !Guid.TryParse(cells[0], out var id) ||
            !DateTimeOffset.TryParse(cells[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startsAt) ||
            !DateTimeOffset.TryParse(cells[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var endsAt) ||
            !int.TryParse(cells[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var capacity) ||
            !Enum.TryParse<EventStatus>(cells[8], ignoreCase: true, out var status) ||
            !DateTimeOffset.TryParse(cells[11], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        Guid? clubId = Guid.TryParse(cells[9], out var parsedClubId) ? parsedClubId : null;

        GeoPoint? venue = null;
        if (cells.Length == ColumnCountWithVenue &&
            double.TryParse(cells[12], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) &&
            double.TryParse(cells[13], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            try
            {
                venue = new GeoPoint(latitude, longitude);
            }
            catch (DomainException)
            {
                return null;
            }
        }

        try
        {
            return Event.Restore(
                id,
                cells[1],
                cells[2],
                cells[3],
                startsAt,
                endsAt,
                cells[6],
                capacity,
                status,
                clubId,
                cells[10],
                createdAt,
                venue);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

/// <summary>attendance.csv — check-in, duy nhất theo (event, student), atomic.</summary>
public sealed class CsvAttendanceRepository : CsvRepository<Attendance>, IAttendanceRepository
{
    private const int ColumnCount = 5;
    private const int ColumnCountWithLocation = 8;

    public CsvAttendanceRepository(string filePath)
        : base(
            filePath,
            "id",
            "event_id",
            "student_id",
            "method",
            "checked_in_at_utc",
            "latitude",
            "longitude",
            "distance_km")
    {
    }

    public CsvAttendanceRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<Attendance>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.Where(a => a.EventId == eventId).OrderBy(a => a.CheckedInAtUtc).ToArray();
    }

    public async Task<bool> ExistsAsync(Guid eventId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.Any(a => a.EventId == eventId && a.StudentId == studentId);
    }

    public async Task<TryCheckinResult> TryCheckInAsync(
        Attendance attendance,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attendance);

        var outcome = TryCheckinResult.Fail("Không thể check-in.");

        await Store.MutateAsync(rows =>
        {
            var mapped = rows.Select(Map).Where(a => a is not null).ToArray();

            if (mapped.Any(a => a!.EventId == attendance.EventId && a.StudentId == attendance.StudentId))
            {
                outcome = TryCheckinResult.Fail("Sinh viên đã check-in sự kiện này.");
                return null;
            }

            if (mapped.Count(a => a!.EventId == attendance.EventId) >= capacity)
            {
                outcome = TryCheckinResult.Fail($"Sự kiện đã đủ sức chứa ({capacity}).");
                return null;
            }

            var updated = rows.ToList();
            updated.Add(ToCells(attendance));
            outcome = TryCheckinResult.Ok();

            return updated;
        }, cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    protected override string[] ToCells(Attendance attendance) =>
    [
        attendance.Id.ToString("D"),
        attendance.EventId.ToString("D"),
        attendance.StudentId.ToString("D"),
        attendance.Method,
        attendance.CheckedInAtUtc.ToString("O", CultureInfo.InvariantCulture),
        attendance.CheckinLocation?.Latitude.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
        attendance.CheckinLocation?.Longitude.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
        attendance.DistanceKm?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
    ];

    protected override Attendance? Map(string[] cells)
    {
        if ((cells.Length != ColumnCount && cells.Length != ColumnCountWithLocation) ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var eventId) ||
            !Guid.TryParse(cells[2], out var studentId) ||
            !DateTimeOffset.TryParse(cells[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var checkedInAt))
        {
            return null;
        }

        GeoPoint? location = null;
        double? distance = null;

        if (cells.Length == ColumnCountWithLocation)
        {
            if (double.TryParse(cells[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) &&
                double.TryParse(cells[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                try
                {
                    location = new GeoPoint(latitude, longitude);
                }
                catch (DomainException)
                {
                    return null;
                }
            }

            if (double.TryParse(cells[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDistance))
            {
                distance = parsedDistance;
            }
        }

        try
        {
            return Attendance.Restore(id, eventId, studentId, cells[3], checkedInAt, location, distance);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
