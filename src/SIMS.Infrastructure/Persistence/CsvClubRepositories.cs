using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Clubs;
using SIMS.Domain.Common;
using SIMS.Domain.Security;

namespace SIMS.Infrastructure.Persistence;

/// <summary>clubs.csv — danh mục câu lạc bộ.</summary>
public sealed class CsvClubRepository : CsvRepository<Club>, IClubRepository
{
    private const int ColumnCount = 5;

    public CsvClubRepository(string filePath)
        : base(filePath, "id", "code", "name", "is_active", "created_at_utc")
    {
    }

    public CsvClubRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<Club?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var target = (code ?? string.Empty).Trim();
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(c => string.Equals(c.Code, target, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ExistsByCodeAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var target = (code ?? string.Empty).Trim();
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Any(c =>
            string.Equals(c.Code, target, StringComparison.OrdinalIgnoreCase) &&
            (excludeId is null || c.Id != excludeId));
    }

    protected override string[] ToCells(Club club) =>
    [
        club.Id.ToString("D"),
        club.Code,
        club.Name,
        club.IsActive ? "1" : "0",
        club.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Club? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !DateTimeOffset.TryParse(cells[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return Club.Restore(id, cells[1], cells[2], cells[3] == "1", createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

/// <summary>club_memberships.csv — multi-club: một người dùng thuộc nhiều CLB với vai trò riêng.</summary>
public sealed class CsvClubMembershipRepository : CsvRepository<ClubMembership>, IClubMembershipRepository
{
    private const int ColumnCount = 6;

    public CsvClubMembershipRepository(string filePath)
        : base(filePath, "id", "user_id", "club_id", "role", "is_active", "joined_at_utc")
    {
    }

    public CsvClubMembershipRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<ClubMembership>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.Where(m => m.UserId == userId).ToArray();
    }

    public async Task<IReadOnlyList<ClubMembership>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.Where(m => m.ClubId == clubId).ToArray();
    }

    public async Task<ClubMembership?> FindByPairAsync(
        Guid userId,
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.FirstOrDefault(m => m.UserId == userId && m.ClubId == clubId);
    }

    protected override string[] ToCells(ClubMembership membership) =>
    [
        membership.Id.ToString("D"),
        membership.UserId.ToString("D"),
        membership.ClubId.ToString("D"),
        membership.Role.ToString(),
        membership.IsActive ? "1" : "0",
        membership.JoinedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override ClubMembership? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var userId) ||
            !Guid.TryParse(cells[2], out var clubId) ||
            !Enum.TryParse<Role>(cells[3], ignoreCase: true, out var role) ||
            !DateTimeOffset.TryParse(cells[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var joinedAt))
        {
            return null;
        }

        try
        {
            return ClubMembership.Restore(id, userId, clubId, role, cells[4] == "1", joinedAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
