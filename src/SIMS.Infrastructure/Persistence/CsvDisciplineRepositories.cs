using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Disciplines;

namespace SIMS.Infrastructure.Persistence;

public sealed class CsvDisciplineRepository : CsvRepository<Discipline>, IDisciplineRepository
{
    private const int ColumnCount = 8;

    public CsvDisciplineRepository(string filePath)
        : base(
            filePath,
            "id",
            "club_id",
            "code",
            "name",
            "points",
            "description",
            "is_active",
            "created_at_utc")
    {
    }

    public CsvDisciplineRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<Discipline?> FindByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(discipline =>
            discipline.ClubId == clubId &&
            string.Equals(discipline.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ExistsByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default)
        => await FindByCodeAsync(clubId, code, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<Discipline>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(discipline => discipline.ClubId == clubId).ToArray();
    }

    protected override string[] ToCells(Discipline discipline) =>
    [
        discipline.Id.ToString("D"),
        discipline.ClubId.ToString("D"),
        discipline.Code,
        discipline.Name,
        discipline.Points.ToString("0.##", CultureInfo.InvariantCulture),
        discipline.Description,
        discipline.IsActive ? "true" : "false",
        discipline.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Discipline? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var clubId) ||
            !decimal.TryParse(cells[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var points) ||
            !bool.TryParse(cells[6], out var isActive) ||
            !DateTimeOffset.TryParse(cells[7], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return Discipline.Restore(id, clubId, cells[2], cells[3], points, cells[5], isActive, createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvDisciplineRecordRepository
    : CsvRepository<DisciplineRecord>, IDisciplineRecordRepository
{
    private const int ColumnCount = 10;

    public CsvDisciplineRecordRepository(string filePath)
        : base(
            filePath,
            "id",
            "user_id",
            "club_id",
            "discipline_id",
            "applied_date",
            "period",
            "status",
            "applied_by",
            "notes",
            "created_at_utc")
    {
    }

    public CsvDisciplineRecordRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<DisciplineRecord>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(record => record.ClubId == clubId).ToArray();
    }

    public async Task<IReadOnlyList<DisciplineRecord>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(record => record.UserId == userId).ToArray();
    }

    protected override string[] ToCells(DisciplineRecord record) =>
    [
        record.Id.ToString("D"),
        record.UserId.ToString("D"),
        record.ClubId.ToString("D"),
        record.DisciplineId.ToString("D"),
        record.AppliedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        record.Period,
        record.Status.ToString(),
        record.AppliedBy,
        record.Notes,
        record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override DisciplineRecord? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var userId) ||
            !Guid.TryParse(cells[2], out var clubId) ||
            !Guid.TryParse(cells[3], out var disciplineId) ||
            !DateOnly.TryParseExact(cells[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var appliedDate) ||
            !Enum.TryParse<DisciplineRecordStatus>(cells[6], ignoreCase: true, out var status) ||
            !DateTimeOffset.TryParse(cells[9], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return DisciplineRecord.Restore(
                id, userId, clubId, disciplineId, appliedDate, cells[5], status,
                cells[7], cells[8], createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
