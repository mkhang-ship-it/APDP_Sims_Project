using SIMS.Domain.Awards;
using SIMS.Domain.Disciplines;

namespace SIMS.Application.Abstractions;

public interface IDisciplineRepository : IRepository<Discipline>
{
    Task<Discipline?> FindByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Discipline>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);
}

public interface IDisciplineRecordRepository : IRepository<DisciplineRecord>
{
    Task<IReadOnlyList<DisciplineRecord>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DisciplineRecord>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface IAwardRepository : IRepository<Award>
{
    Task<Award?> FindByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Award>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
}

public interface IAwardRecipientRepository : IRepository<AwardRecipient>
{
    Task<IReadOnlyList<AwardRecipient>> ListByAwardAsync(
        Guid awardId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AwardRecipient>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
