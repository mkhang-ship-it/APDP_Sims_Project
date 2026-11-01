using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Events;

namespace SIMS.Infrastructure.Persistence;

/// <summary>event_plans.csv — một kế hoạch cho mỗi sự kiện.</summary>
public sealed class CsvEventPlanRepository : CsvRepository<EventPlan>, IEventPlanRepository
{
    private const int ColumnCount = 5;
    private const int ColumnCountWithDetails = 12;

    public CsvEventPlanRepository(string filePath)
        : base(
            filePath,
            "id",
            "event_id",
            "summary",
            "created_at_utc",
            "updated_at_utc",
            "prepared_by",
            "implementation_date",
            "approved_by",
            "participants",
            "description",
            "responsible_organizer",
            "advisor_coordinator")
    {
    }

    public CsvEventPlanRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<EventPlan?> FindByEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.FirstOrDefault(p => p.EventId == eventId);
    }

    protected override string[] ToCells(EventPlan plan) =>
    [
        plan.Id.ToString("D"),
        plan.EventId.ToString("D"),
        plan.Summary,
        plan.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        plan.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        plan.PreparedBy,
        plan.ImplementationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        plan.ApprovedBy,
        plan.Participants,
        plan.Description,
        plan.ResponsibleOrganizer,
        plan.AdvisorCoordinator,
    ];

    protected override EventPlan? Map(string[] cells)
    {
        if ((cells.Length != ColumnCount && cells.Length != ColumnCountWithDetails) ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var eventId) ||
            !DateTimeOffset.TryParse(cells[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt) ||
            !DateTimeOffset.TryParse(cells[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            return null;
        }

        try
        {
            if (cells.Length == ColumnCount)
            {
                return EventPlan.Restore(id, eventId, cells[2], createdAt, updatedAt);
            }

            DateOnly? implementationDate = DateOnly.TryParseExact(
                cells[6], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                ? parsedDate
                : null;

            return EventPlan.Restore(
                id,
                eventId,
                cells[2],
                createdAt,
                updatedAt,
                cells[5],
                implementationDate,
                cells[7],
                cells[8],
                cells[9],
                cells[10],
                cells[11]);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

/// <summary>Base cho 5 bảng mục chi tiết của kế hoạch.</summary>
public abstract class CsvPlanItemRepository<T> : CsvRepository<T>, IPlanItemRepository<T>
    where T : Entity
{
    protected CsvPlanItemRepository(string filePath, params string[] header)
        : base(filePath, header)
    {
    }

    protected CsvPlanItemRepository(IRowStore store)
        : base(store)
    {
    }

    protected abstract Guid GetPlanId(T entity);

    public async Task<IReadOnlyList<T>> ListByPlanAsync(Guid eventPlanId, CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.Where(item => GetPlanId(item) == eventPlanId).ToArray();
    }
}

public sealed class CsvPlanObjectiveRepository : CsvPlanItemRepository<PlanObjective>
{
    public CsvPlanObjectiveRepository(string filePath)
        : base(filePath, "id", "event_plan_id", "content", "order")
    {
    }

    public CsvPlanObjectiveRepository(IRowStore store)
        : base(store)
    {
    }

    protected override Guid GetPlanId(PlanObjective entity) => entity.EventPlanId;

    protected override string[] ToCells(PlanObjective item) =>
    [
        item.Id.ToString("D"),
        item.EventPlanId.ToString("D"),
        item.Content,
        item.Order.ToString(CultureInfo.InvariantCulture),
    ];

    protected override PlanObjective? Map(string[] cells)
    {
        if (cells.Length != 4 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var planId) ||
            !int.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            return null;
        }

        try
        {
            return PlanObjective.Restore(id, planId, cells[2], order);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvPlanActionRepository : CsvPlanItemRepository<PlanAction>
{
    public CsvPlanActionRepository(string filePath)
        : base(filePath, "id", "event_plan_id", "content", "owner", "due_date", "order")
    {
    }

    public CsvPlanActionRepository(IRowStore store)
        : base(store)
    {
    }

    protected override Guid GetPlanId(PlanAction entity) => entity.EventPlanId;

    protected override string[] ToCells(PlanAction item) =>
    [
        item.Id.ToString("D"),
        item.EventPlanId.ToString("D"),
        item.Content,
        item.Owner,
        item.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        item.Order.ToString(CultureInfo.InvariantCulture),
    ];

    protected override PlanAction? Map(string[] cells)
    {
        if (cells.Length != 6 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var planId) ||
            !int.TryParse(cells[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            return null;
        }

        DateOnly? dueDate = DateOnly.TryParseExact(
            cells[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDueDate)
            ? parsedDueDate
            : null;

        try
        {
            return PlanAction.Restore(id, planId, cells[2], cells[3], dueDate, order);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvPlanBudgetItemRepository : CsvPlanItemRepository<PlanBudgetItem>
{
    public CsvPlanBudgetItemRepository(string filePath)
        : base(filePath, "id", "event_plan_id", "item_name", "quantity", "unit_price", "order")
    {
    }

    public CsvPlanBudgetItemRepository(IRowStore store)
        : base(store)
    {
    }

    protected override Guid GetPlanId(PlanBudgetItem entity) => entity.EventPlanId;

    protected override string[] ToCells(PlanBudgetItem item) =>
    [
        item.Id.ToString("D"),
        item.EventPlanId.ToString("D"),
        item.ItemName,
        item.Quantity.ToString(CultureInfo.InvariantCulture),
        item.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture),
        item.Order.ToString(CultureInfo.InvariantCulture),
    ];

    protected override PlanBudgetItem? Map(string[] cells)
    {
        if (cells.Length != 6 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var planId) ||
            !int.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) ||
            !decimal.TryParse(cells[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var unitPrice) ||
            !int.TryParse(cells[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            return null;
        }

        try
        {
            return PlanBudgetItem.Restore(id, planId, cells[2], quantity, unitPrice, order);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvPlanEquipmentRepository : CsvPlanItemRepository<PlanEquipment>
{
    public CsvPlanEquipmentRepository(string filePath)
        : base(filePath, "id", "event_plan_id", "name", "quantity", "note", "order")
    {
    }

    public CsvPlanEquipmentRepository(IRowStore store)
        : base(store)
    {
    }

    protected override Guid GetPlanId(PlanEquipment entity) => entity.EventPlanId;

    protected override string[] ToCells(PlanEquipment item) =>
    [
        item.Id.ToString("D"),
        item.EventPlanId.ToString("D"),
        item.Name,
        item.Quantity.ToString(CultureInfo.InvariantCulture),
        item.Note,
        item.Order.ToString(CultureInfo.InvariantCulture),
    ];

    protected override PlanEquipment? Map(string[] cells)
    {
        if (cells.Length != 6 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var planId) ||
            !int.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) ||
            !int.TryParse(cells[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            return null;
        }

        try
        {
            return PlanEquipment.Restore(id, planId, cells[2], quantity, cells[4], order);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvPlanTimelineRepository : CsvPlanItemRepository<PlanTimelineItem>
{
    public CsvPlanTimelineRepository(string filePath)
        : base(filePath, "id", "event_plan_id", "time_label", "activity", "order")
    {
    }

    public CsvPlanTimelineRepository(IRowStore store)
        : base(store)
    {
    }

    protected override Guid GetPlanId(PlanTimelineItem entity) => entity.EventPlanId;

    protected override string[] ToCells(PlanTimelineItem item) =>
    [
        item.Id.ToString("D"),
        item.EventPlanId.ToString("D"),
        item.TimeLabel,
        item.Activity,
        item.Order.ToString(CultureInfo.InvariantCulture),
    ];

    protected override PlanTimelineItem? Map(string[] cells)
    {
        if (cells.Length != 5 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var planId) ||
            !int.TryParse(cells[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            return null;
        }

        try
        {
            return PlanTimelineItem.Restore(id, planId, cells[2], cells[3], order);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
