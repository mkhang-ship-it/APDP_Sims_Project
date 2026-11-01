using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Disciplines;
using SIMS.Domain.Security;

namespace SIMS.Application.Disciplines;

public sealed record MemberPointRow(Guid UserId, decimal ClubPoints, decimal SchoolPoints, bool IsLeader);

public sealed record DisciplineRecordItem(DisciplineRecord Record, Discipline? Discipline);

public sealed record UserPointsView(
    Guid UserId,
    decimal ClubPoints,
    decimal SchoolPoints,
    bool IsLeader,
    int? Rank,
    IReadOnlyList<DisciplineRecordItem> Records);

public interface IDisciplineService
{
    Task<Result<Discipline>> CreateDisciplineAsync(
        Guid clubId, string name, decimal points, string? description, string actor,
        CancellationToken cancellationToken = default);

    Task<Result<Discipline>> UpdateDisciplineAsync(
        Guid id, string name, decimal points, string? description, string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteDisciplineAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Discipline>> ListDisciplinesAsync(
        Guid? clubId,
        bool activeOnly = false,
        CancellationToken cancellationToken = default);

    Task<Result<DisciplineRecord>> ApplyRecordAsync(
        Guid clubId,
        Guid userId,
        Guid disciplineId,
        DateOnly appliedDate,
        string? period,
        string? notes,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<DisciplineRecord>> UpdateRecordAsync(
        Guid id,
        Guid disciplineId,
        DateOnly appliedDate,
        string? period,
        string? notes,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> RevokeRecordAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DisciplineRecordItem>> ListRecordsAsync(
        Guid? clubId,
        Guid? userId = null,
        bool activeOnly = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberPointRow>> GetMemberPointsAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);

    Task<UserPointsView?> GetUserPointsAsync(
        Guid clubId,
        Guid userId,
        CancellationToken cancellationToken = default);

}

public sealed class DisciplineService : IDisciplineService
{
    private const int MaxCodeAttempts = 50;

    /// <summary>Công thức PHP: scale điểm CLB lên tối đa 5, cộng 5 nếu là cán bộ CLB.</summary>
    public static decimal ComputeSchoolPoints(decimal clubPoints, decimal maxClubPoints, bool isLeader)
    {
        var max = maxClubPoints <= 0m ? 1m : maxClubPoints;
        var basePoints = clubPoints / max * 5m;

        if (basePoints < 0m)
        {
            basePoints = 0m;
        }

        if (isLeader)
        {
            basePoints += 5m;
        }

        return decimal.Round(basePoints, 2);
    }

    private readonly IDisciplineRepository _disciplines;
    private readonly IDisciplineRecordRepository _records;
    private readonly IClubMembershipRepository _memberships;
    private readonly IReferenceCodeGenerator _codes;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public DisciplineService(
        IDisciplineRepository disciplines,
        IDisciplineRecordRepository records,
        IClubMembershipRepository memberships,
        IReferenceCodeGenerator codes,
        IAuditLog audit,
        IClock clock)
    {
        _disciplines = disciplines;
        _records = records;
        _memberships = memberships;
        _codes = codes;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<Discipline>> CreateDisciplineAsync(
        Guid clubId,
        string name,
        decimal points,
        string? description,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var code = await NextCodeAsync(clubId, cancellationToken).ConfigureAwait(false);

        Discipline discipline;
        try
        {
            discipline = Discipline.Create(clubId, code, name, points, description, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _disciplines.AddAsync(discipline, cancellationToken).ConfigureAwait(false);
        await AuditAsync("discipline.create", discipline.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<Discipline>.Ok(discipline);
    }

    public async Task<Result<Discipline>> UpdateDisciplineAsync(
        Guid id,
        string name,
        decimal points,
        string? description,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var discipline = await _disciplines.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (discipline is null)
        {
            return Fail("Không tìm thấy danh mục.");
        }

        try
        {
            discipline.Update(name, points, description);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _disciplines.UpdateAsync(discipline, cancellationToken).ConfigureAwait(false);
        await AuditAsync("discipline.update", discipline.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<Discipline>.Ok(discipline);
    }

    public async Task<Result> DeleteDisciplineAsync(
        Guid id,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var discipline = await _disciplines.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (discipline is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy danh mục."));
        }

        var records = await _records
            .ListByClubAsync(discipline.ClubId, cancellationToken)
            .ConfigureAwait(false);

        if (records.Any(record => record.DisciplineId == id))
        {
            return Result.Fail(new ValidationError(
                string.Empty,
                "Không thể xoá danh mục đã có bản ghi."));
        }

        await _disciplines.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditAsync("discipline.delete", id, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<IReadOnlyList<Discipline>> ListDisciplinesAsync(
        Guid? clubId,
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var disciplines = clubId is null
            ? await _disciplines.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _disciplines.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);

        return disciplines
            .Where(discipline => !activeOnly || discipline.IsActive)
            .OrderBy(discipline => discipline.Name, StringComparer.CurrentCulture)
            .ToArray();
    }

    public async Task<Result<DisciplineRecord>> ApplyRecordAsync(
        Guid clubId,
        Guid userId,
        Guid disciplineId,
        DateOnly appliedDate,
        string? period,
        string? notes,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var discipline = await _disciplines.FindByIdAsync(disciplineId, cancellationToken).ConfigureAwait(false);
        if (discipline is null || discipline.ClubId != clubId)
        {
            return FailRecord("Danh mục không thuộc CLB đã chọn.");
        }

        DisciplineRecord record;
        try
        {
            record = DisciplineRecord.Create(
                userId, clubId, disciplineId, appliedDate, period, actor, notes, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return FailRecord(ex.Message);
        }

        await _records.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await AuditAsync("discipline.record.apply", record.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<DisciplineRecord>.Ok(record);
    }

    public async Task<Result<DisciplineRecord>> UpdateRecordAsync(
        Guid id,
        Guid disciplineId,
        DateOnly appliedDate,
        string? period,
        string? notes,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var record = await _records.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return FailRecord("Không tìm thấy bản ghi.");
        }

        var discipline = await _disciplines.FindByIdAsync(disciplineId, cancellationToken).ConfigureAwait(false);
        if (discipline is null || discipline.ClubId != record.ClubId)
        {
            return FailRecord("Danh mục không thuộc CLB của bản ghi.");
        }

        try
        {
            record.Update(disciplineId, appliedDate, period, notes);
        }
        catch (DomainException ex)
        {
            return FailRecord(ex.Message);
        }

        await _records.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        await AuditAsync("discipline.record.update", record.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<DisciplineRecord>.Ok(record);
    }

    public async Task<Result> RevokeRecordAsync(
        Guid id,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var record = await _records.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy bản ghi."));
        }

        record.Revoke();
        await _records.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        await AuditAsync("discipline.record.revoke", id, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<IReadOnlyList<DisciplineRecordItem>> ListRecordsAsync(
        Guid? clubId,
        Guid? userId = null,
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var records = clubId is null
            ? await _records.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _records.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);
        var disciplines = (await _disciplines.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(discipline => discipline.Id);

        return records
            .Where(record => userId is null || record.UserId == userId)
            .Where(record => !activeOnly || record.IsActive)
            .OrderByDescending(record => record.AppliedDate)
            .Select(record => new DisciplineRecordItem(
                record,
                disciplines.TryGetValue(record.DisciplineId, out var discipline) ? discipline : null))
            .ToArray();
    }

    public async Task<IReadOnlyList<MemberPointRow>> GetMemberPointsAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var memberships = (await _memberships
            .ListByClubAsync(clubId, cancellationToken)
            .ConfigureAwait(false))
            .Where(membership => membership.IsActive)
            .ToArray();
        var disciplines = (await _disciplines.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(discipline => discipline.Id);
        var records = (await _records.ListByClubAsync(clubId, cancellationToken).ConfigureAwait(false))
            .Where(record => record.IsActive)
            .ToArray();

        var clubPoints = memberships
            .Select(membership => new
            {
                membership.UserId,
                Points = records
                    .Where(record => record.UserId == membership.UserId)
                    .Sum(record => disciplines.TryGetValue(record.DisciplineId, out var discipline)
                        ? discipline.Points
                        : 0m),
                IsLeader = membership.Role is Role.Admin or Role.Faculty,
            })
            .ToArray();

        var maxPoints = clubPoints.Length == 0 ? 0m : clubPoints.Max(item => item.Points);

        return clubPoints
            .Select(item => new MemberPointRow(
                item.UserId,
                decimal.Round(item.Points, 2),
                DisciplineService.ComputeSchoolPoints(item.Points, maxPoints, item.IsLeader),
                item.IsLeader))
            .OrderByDescending(row => row.ClubPoints)
            .ThenBy(row => row.UserId)
            .ToArray();
    }

    public async Task<UserPointsView?> GetUserPointsAsync(
        Guid clubId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await GetMemberPointsAsync(clubId, cancellationToken).ConfigureAwait(false);
        var row = rows.FirstOrDefault(item => item.UserId == userId);
        var rank = row is null ? (int?)null : rows.ToList().FindIndex(item => item.UserId == userId) + 1;

        var recordItems = await ListRecordsAsync(clubId, userId, true, cancellationToken).ConfigureAwait(false);

        return row is null && recordItems.Count == 0
            ? null
            : new UserPointsView(
                userId,
                row?.ClubPoints ?? recordItems.Sum(item => item.Discipline?.Points ?? 0m),
                row?.SchoolPoints ?? 0m,
                row?.IsLeader ?? false,
                rank,
                recordItems);
    }

    private async Task<string> NextCodeAsync(Guid clubId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = _codes.Next("KL");

            if (!await _disciplines.ExistsByCodeAsync(clubId, code, cancellationToken).ConfigureAwait(false))
            {
                return code;
            }
        }

        throw new DomainException("Không sinh được mã danh mục duy nhất.");
    }

    private Task AuditAsync(string action, Guid entityId, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(_clock.UtcNow, actor, action, "discipline", entityId.ToString("D"), string.Empty),
            cancellationToken);

    private static Result<Discipline> Fail(string message)
        => Result<Discipline>.Fail(new ValidationError(string.Empty, message));

    private static Result<DisciplineRecord> FailRecord(string message)
        => Result<DisciplineRecord>.Fail(new ValidationError(string.Empty, message));
}
