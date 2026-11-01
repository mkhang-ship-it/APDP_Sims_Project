using SIMS.Application.Abstractions;
using SIMS.Domain.Events;

namespace SIMS.Application.Events;

public interface IEventCalendarService
{
    Task<IReadOnlyList<Event>> GetByMonthAsync(
        int year,
        int month,
        Guid? clubId,
        CancellationToken cancellationToken = default);
}

/// <summary>Lịch sự kiện theo tháng (dùng StartsAtUtc, quy về local date để hiển thị).</summary>
public sealed class EventCalendarService : IEventCalendarService
{
    private readonly IEventRepository _events;

    public EventCalendarService(IEventRepository events)
    {
        _events = events;
    }

    public async Task<IReadOnlyList<Event>> GetByMonthAsync(
        int year,
        int month,
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Tháng phải trong khoảng 1..12.");
        }

        var events = clubId is null || clubId == Guid.Empty
            ? await _events.ListAsync(cancellationToken)
            : await _events.ListByClubAsync(clubId.Value, cancellationToken);

        return events
            .Where(e => e.StartsAtUtc.ToLocalTime().Year == year && e.StartsAtUtc.ToLocalTime().Month == month)
            .OrderBy(e => e.StartsAtUtc)
            .ToArray();
    }
}
