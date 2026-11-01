using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Events;

namespace SIMS.Tests.Support;

public sealed class InMemoryEventPlanRepository : InMemoryRepository<EventPlan>, IEventPlanRepository
{
    public Task<EventPlan?> FindByEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(plan => plan.EventId == eventId));
}

/// <summary>Bản bộ nhớ của bảng mục kế hoạch — cùng hợp đồng ListByPlanAsync với bản CSV.</summary>
public sealed class InMemoryPlanItemRepository<T> : InMemoryRepository<T>, IPlanItemRepository<T>
    where T : Entity
{
    private readonly Func<T, Guid> _planIdSelector;

    public InMemoryPlanItemRepository(Func<T, Guid> planIdSelector) => _planIdSelector = planIdSelector;

    public Task<IReadOnlyList<T>> ListByPlanAsync(Guid eventPlanId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<T>>(
            Items.Where(item => _planIdSelector(item) == eventPlanId).ToArray());
}
