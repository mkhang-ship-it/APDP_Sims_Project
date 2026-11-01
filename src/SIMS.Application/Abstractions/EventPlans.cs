using SIMS.Domain.Common;
using SIMS.Domain.Events;

namespace SIMS.Application.Abstractions;

public interface IEventPlanRepository : IRepository<EventPlan>
{
    Task<EventPlan?> FindByEventAsync(Guid eventId, CancellationToken cancellationToken = default);
}

/// <summary>Repository cho 5 mục chi tiết của kế hoạch (dùng chung, LSP với bản CSV).</summary>
public interface IPlanItemRepository<T> : IRepository<T> where T : Entity
{
    Task<IReadOnlyList<T>> ListByPlanAsync(Guid eventPlanId, CancellationToken cancellationToken = default);
}
