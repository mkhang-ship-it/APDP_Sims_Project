using SIMS.Application.Abstractions;
using SIMS.Domain.Events;

namespace SIMS.Tests.Support;

public sealed class InMemoryEventRepository : InMemoryRepository<Event>, IEventRepository
{
    public Task<Event?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(
            e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsByCodeAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(e =>
            string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase) &&
            (excludeId is null || e.Id != excludeId)));

    public Task<Event?> FindByCheckinTokenAsync(string token, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(
            e => string.Equals(e.CheckinToken, token, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Event>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Event>>(Items.Where(e => e.ClubId == clubId).ToArray());
}

public sealed class InMemoryAttendanceRepository : InMemoryRepository<Attendance>, IAttendanceRepository
{
    public Task<IReadOnlyList<Attendance>> ListByEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Attendance>>(Items.Where(a => a.EventId == eventId).ToArray());

    public Task<bool> ExistsAsync(Guid eventId, Guid studentId, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(a => a.EventId == eventId && a.StudentId == studentId));

    public Task<TryCheckinResult> TryCheckInAsync(
        Attendance attendance,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        if (Items.Any(a => a.EventId == attendance.EventId && a.StudentId == attendance.StudentId))
        {
            return Task.FromResult(TryCheckinResult.Fail("Sinh viên đã check-in sự kiện này."));
        }

        if (Items.Count(a => a.EventId == attendance.EventId) >= capacity)
        {
            return Task.FromResult(TryCheckinResult.Fail($"Sự kiện đã đủ sức chứa ({capacity})."));
        }

        Items.Add(attendance);
        return Task.FromResult(TryCheckinResult.Ok());
    }
}
