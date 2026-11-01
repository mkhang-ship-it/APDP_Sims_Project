using SIMS.Domain.Clubs;
using SIMS.Domain.Security;

namespace SIMS.Application.Abstractions;

public interface IPermissionRepository
{
    Task<IReadOnlyList<RolePermission>> ListAsync(CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<RolePermission> permissions, CancellationToken cancellationToken = default);
}

public interface IClubRepository : IRepository<Club>
{
    Task<Club?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);
}

public interface IClubMembershipRepository : IRepository<ClubMembership>
{
    Task<IReadOnlyList<ClubMembership>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClubMembership>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<ClubMembership?> FindByPairAsync(Guid userId, Guid clubId, CancellationToken cancellationToken = default);
}
