using SIMS.Domain.Events;

namespace SIMS.Application.Abstractions;

public interface IEventRepository : IRepository<Event>
{
    Task<Event?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<Event?> FindByCheckinTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Event>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
}

public interface IAttendanceRepository : IRepository<Attendance>
{
    Task<IReadOnlyList<Attendance>> ListByEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid eventId, Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra trùng + sức chứa và ghi trong cùng một khoá file (atomic).
    /// Vi phạm thì không ghi gì.
    /// </summary>
    Task<TryCheckinResult> TryCheckInAsync(
        Attendance attendance,
        int capacity,
        CancellationToken cancellationToken = default);
}

public sealed record TryCheckinResult(bool Success, string? Error)
{
    public static TryCheckinResult Ok() => new(true, null);

    public static TryCheckinResult Fail(string error) => new(false, error);
}
