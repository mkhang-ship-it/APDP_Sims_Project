using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Awards;
using SIMS.Domain.Common;

namespace SIMS.Infrastructure.Persistence;

public sealed class CsvAwardRepository : CsvRepository<Award>, IAwardRepository
{
    private const int ColumnCount = 9;

    public CsvAwardRepository(string filePath)
        : base(
            filePath,
            "id",
            "club_id",
            "code",
            "title",
            "period",
            "award_date",
            "status",
            "notes",
            "created_at_utc")
    {
    }

    public CsvAwardRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<Award?> FindByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(award =>
            award.ClubId == clubId &&
            string.Equals(award.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ExistsByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default)
        => await FindByCodeAsync(clubId, code, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<Award>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(award => award.ClubId == clubId).ToArray();
    }

    protected override string[] ToCells(Award award) =>
    [
        award.Id.ToString("D"),
        award.ClubId.ToString("D"),
        award.Code,
        award.Title,
        award.Period,
        award.AwardDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        award.Status.ToString(),
        award.Notes,
        award.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Award? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var clubId) ||
            !DateOnly.TryParseExact(cells[5], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var awardDate) ||
            !Enum.TryParse<AwardStatus>(cells[6], ignoreCase: true, out var status) ||
            !DateTimeOffset.TryParse(cells[8], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return Award.Restore(id, clubId, cells[2], cells[3], cells[4], awardDate, status, cells[7], createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvAwardRecipientRepository
    : CsvRepository<AwardRecipient>, IAwardRecipientRepository
{
    public CsvAwardRecipientRepository(string filePath)
        : base(filePath, "id", "award_id", "user_id", "attachment", "created_at_utc")
    {
    }

    public CsvAwardRecipientRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<AwardRecipient>> ListByAwardAsync(
        Guid awardId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(recipient => recipient.AwardId == awardId).ToArray();
    }

    public async Task<IReadOnlyList<AwardRecipient>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(recipient => recipient.UserId == userId).ToArray();
    }

    protected override string[] ToCells(AwardRecipient recipient) =>
    [
        recipient.Id.ToString("D"),
        recipient.AwardId.ToString("D"),
        recipient.UserId.ToString("D"),
        recipient.Attachment,
        recipient.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override AwardRecipient? Map(string[] cells)
    {
        if (cells.Length != 5 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var awardId) ||
            !Guid.TryParse(cells[2], out var userId) ||
            !DateTimeOffset.TryParse(cells[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return AwardRecipient.Restore(id, awardId, userId, cells[3], createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
