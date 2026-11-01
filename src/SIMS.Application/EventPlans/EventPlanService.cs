using System.Globalization;
using System.Text;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Events;

namespace SIMS.Application.EventPlans;

public sealed record EventPlanView(
    EventPlan Plan,
    IReadOnlyList<PlanObjective> Objectives,
    IReadOnlyList<PlanAction> Actions,
    IReadOnlyList<PlanBudgetItem> BudgetItems,
    IReadOnlyList<PlanEquipment> Equipment,
    IReadOnlyList<PlanTimelineItem> Timeline)
{
    public decimal BudgetTotal => BudgetItems.Sum(item => item.Total);
}

public sealed record EventPlanDetailsInput(
    string Summary,
    string? PreparedBy,
    DateOnly? ImplementationDate,
    string? ApprovedBy,
    string? Participants,
    string? Description,
    string? ResponsibleOrganizer,
    string? AdvisorCoordinator);

public interface IEventPlanService
{
    Task<Result<EventPlanView>> GetOrCreateAsync(Guid eventId, string actor, CancellationToken cancellationToken = default);

    Task<Result<EventPlan>> UpdateSummaryAsync(
        Guid eventId,
        string summary,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<EventPlan>> UpdateDetailsAsync(
        Guid eventId,
        EventPlanDetailsInput input,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<PlanObjective>> AddObjectiveAsync(
        Guid eventId, string content, string actor, CancellationToken cancellationToken = default);

    Task<Result> RemoveObjectiveAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default);

    Task<Result<PlanAction>> AddActionAsync(
        Guid eventId, string content, string owner, DateOnly? dueDate, string actor,
        CancellationToken cancellationToken = default);

    Task<Result> RemoveActionAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default);

    Task<Result<PlanBudgetItem>> AddBudgetItemAsync(
        Guid eventId, string itemName, int quantity, decimal unitPrice, string actor,
        CancellationToken cancellationToken = default);

    Task<Result> RemoveBudgetItemAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default);

    Task<Result<PlanEquipment>> AddEquipmentAsync(
        Guid eventId, string name, int quantity, string? note, string actor,
        CancellationToken cancellationToken = default);

    Task<Result> RemoveEquipmentAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default);

    Task<Result<PlanTimelineItem>> AddTimelineAsync(
        Guid eventId, string timeLabel, string activity, string actor,
        CancellationToken cancellationToken = default);

    Task<Result> RemoveTimelineAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default);

    /// <summary>Xuất kế hoạch dạng CSV (BCL).</summary>
    Task<Result<string>> ExportCsvAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Xuất kế hoạch dạng XLSX (tự sinh bằng BCL, không cần package Excel).</summary>
    Task<Result<byte[]>> ExportXlsxAsync(Guid eventId, CancellationToken cancellationToken = default);
}

/// <summary>Use case EVENT PLANS: 1 kế hoạch/sự kiện + 5 mục chi tiết, audit mọi ghi.</summary>
public sealed class EventPlanService : IEventPlanService
{
    private readonly IEventPlanRepository _plans;
    private readonly IPlanItemRepository<PlanObjective> _objectives;
    private readonly IPlanItemRepository<PlanAction> _actions;
    private readonly IPlanItemRepository<PlanBudgetItem> _budget;
    private readonly IPlanItemRepository<PlanEquipment> _equipment;
    private readonly IPlanItemRepository<PlanTimelineItem> _timeline;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public EventPlanService(
        IEventPlanRepository plans,
        IPlanItemRepository<PlanObjective> objectives,
        IPlanItemRepository<PlanAction> actions,
        IPlanItemRepository<PlanBudgetItem> budget,
        IPlanItemRepository<PlanEquipment> equipment,
        IPlanItemRepository<PlanTimelineItem> timeline,
        IAuditLog audit,
        IClock clock)
    {
        _plans = plans;
        _objectives = objectives;
        _actions = actions;
        _budget = budget;
        _equipment = equipment;
        _timeline = timeline;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<EventPlanView>> GetOrCreateAsync(
        Guid eventId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
        {
            return Result<EventPlanView>.Fail(new ValidationError(string.Empty, "Sự kiện không hợp lệ."));
        }

        var plan = await _plans.FindByEventAsync(eventId, cancellationToken);

        if (plan is null)
        {
            plan = EventPlan.Create(eventId, string.Empty, _clock.UtcNow);
            await _plans.AddAsync(plan, cancellationToken);
            await AuditAsync("eventplan.create", eventId, plan.Id, actor, cancellationToken);
        }

        return Result<EventPlanView>.Ok(await BuildViewAsync(plan, cancellationToken));
    }

    public async Task<Result<EventPlan>> UpdateSummaryAsync(
        Guid eventId,
        string summary,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var plan = await _plans.FindByEventAsync(eventId, cancellationToken);
        if (plan is null)
        {
            return Result<EventPlan>.Fail(new ValidationError(string.Empty, "Chưa có kế hoạch cho sự kiện."));
        }

        try
        {
            plan.UpdateSummary(summary, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result<EventPlan>.Fail(new ValidationError(nameof(summary), ex.Message));
        }

        await _plans.UpdateAsync(plan, cancellationToken);
        await AuditAsync("eventplan.update", eventId, plan.Id, actor, cancellationToken);

        return Result<EventPlan>.Ok(plan);
    }

    public async Task<Result<EventPlan>> UpdateDetailsAsync(
        Guid eventId,
        EventPlanDetailsInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var plan = await _plans.FindByEventAsync(eventId, cancellationToken);
        if (plan is null)
        {
            return Result<EventPlan>.Fail(new ValidationError(string.Empty, "Chưa có kế hoạch cho sự kiện."));
        }

        try
        {
            plan.UpdateDetails(
                input.Summary,
                input.PreparedBy,
                input.ImplementationDate,
                input.ApprovedBy,
                input.Participants,
                input.Description,
                input.ResponsibleOrganizer,
                input.AdvisorCoordinator,
                _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result<EventPlan>.Fail(new ValidationError(string.Empty, ex.Message));
        }

        await _plans.UpdateAsync(plan, cancellationToken);
        await AuditAsync("eventplan.update", eventId, plan.Id, actor, cancellationToken);

        return Result<EventPlan>.Ok(plan);
    }

    public Task<Result<PlanObjective>> AddObjectiveAsync(
        Guid eventId, string content, string actor, CancellationToken cancellationToken = default)
        => AddItemAsync(
            eventId,
            _objectives,
            (planId, order) => PlanObjective.Create(planId, content, order),
            "eventplan.objective.add",
            actor,
            cancellationToken);

    public Task<Result> RemoveObjectiveAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default)
        => RemoveItemAsync(eventId, itemId, _objectives, "eventplan.objective.remove", actor, cancellationToken);

    public Task<Result<PlanAction>> AddActionAsync(
        Guid eventId, string content, string owner, DateOnly? dueDate, string actor,
        CancellationToken cancellationToken = default)
        => AddItemAsync(
            eventId,
            _actions,
            (planId, order) => PlanAction.Create(planId, content, owner, dueDate, order),
            "eventplan.action.add",
            actor,
            cancellationToken);

    public Task<Result> RemoveActionAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default)
        => RemoveItemAsync(eventId, itemId, _actions, "eventplan.action.remove", actor, cancellationToken);

    public Task<Result<PlanBudgetItem>> AddBudgetItemAsync(
        Guid eventId, string itemName, int quantity, decimal unitPrice, string actor,
        CancellationToken cancellationToken = default)
        => AddItemAsync(
            eventId,
            _budget,
            (planId, order) => PlanBudgetItem.Create(planId, itemName, quantity, unitPrice, order),
            "eventplan.budget.add",
            actor,
            cancellationToken);

    public Task<Result> RemoveBudgetItemAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default)
        => RemoveItemAsync(eventId, itemId, _budget, "eventplan.budget.remove", actor, cancellationToken);

    public Task<Result<PlanEquipment>> AddEquipmentAsync(
        Guid eventId, string name, int quantity, string? note, string actor,
        CancellationToken cancellationToken = default)
        => AddItemAsync(
            eventId,
            _equipment,
            (planId, order) => PlanEquipment.Create(planId, name, quantity, note, order),
            "eventplan.equipment.add",
            actor,
            cancellationToken);

    public Task<Result> RemoveEquipmentAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default)
        => RemoveItemAsync(eventId, itemId, _equipment, "eventplan.equipment.remove", actor, cancellationToken);

    public Task<Result<PlanTimelineItem>> AddTimelineAsync(
        Guid eventId, string timeLabel, string activity, string actor,
        CancellationToken cancellationToken = default)
        => AddItemAsync(
            eventId,
            _timeline,
            (planId, order) => PlanTimelineItem.Create(planId, timeLabel, activity, order),
            "eventplan.timeline.add",
            actor,
            cancellationToken);

    public Task<Result> RemoveTimelineAsync(
        Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken = default)
        => RemoveItemAsync(eventId, itemId, _timeline, "eventplan.timeline.remove", actor, cancellationToken);

    public async Task<Result<string>> ExportCsvAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var plan = await _plans.FindByEventAsync(eventId, cancellationToken);
        if (plan is null)
        {
            return Result<string>.Fail(new ValidationError(string.Empty, "Chưa có kế hoạch cho sự kiện."));
        }

        var view = await BuildViewAsync(plan, cancellationToken);
        var builder = new StringBuilder();

        foreach (var row in BuildExportRows(view))
        {
            builder.AppendLine(string.Join(',', row.Select(Escape)));
        }

        return Result<string>.Ok(builder.ToString());
    }

    public async Task<Result<byte[]>> ExportXlsxAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var plan = await _plans.FindByEventAsync(eventId, cancellationToken);
        if (plan is null)
        {
            return Result<byte[]>.Fail(new ValidationError(string.Empty, "Chưa có kế hoạch cho sự kiện."));
        }

        var view = await BuildViewAsync(plan, cancellationToken);
        var workbook = Xlsx.Write($"Kế hoạch {plan.EventId.ToString("N")[..8]}", BuildExportRows(view));

        return Result<byte[]>.Ok(workbook);
    }

    private static List<IReadOnlyList<string>> BuildExportRows(EventPlanView view)
    {
        var plan = view.Plan;
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "section", "field1", "field2", "field3", "field4" },
            new[] { "summary", plan.Summary, string.Empty, string.Empty, string.Empty },
            new[] { "prepared_by", plan.PreparedBy, string.Empty, string.Empty, string.Empty },
            new[]
            {
                "implementation_date",
                plan.ImplementationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
            },
            new[] { "approved_by", plan.ApprovedBy, string.Empty, string.Empty, string.Empty },
            new[] { "participants", plan.Participants, string.Empty, string.Empty, string.Empty },
            new[] { "plan_description", plan.Description, string.Empty, string.Empty, string.Empty },
            new[] { "responsible_organizer", plan.ResponsibleOrganizer, string.Empty, string.Empty, string.Empty },
            new[] { "advisor_coordinator", plan.AdvisorCoordinator, string.Empty, string.Empty, string.Empty },
            new[] { "objectives", "content", string.Empty, string.Empty, string.Empty },
        };

        foreach (var item in view.Objectives)
        {
            rows.Add(new[] { "objective", item.Content, string.Empty, string.Empty, string.Empty });
        }

        rows.Add(new[] { "actions", "content", "owner", "due_date", string.Empty });

        foreach (var item in view.Actions)
        {
            rows.Add(
            [
                "action",
                item.Content,
                item.Owner,
                item.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                string.Empty,
            ]);
        }

        rows.Add(new[] { "budget", "item", "quantity", "unit_price", "total" });

        foreach (var item in view.BudgetItems)
        {
            rows.Add(
            [
                "budget",
                item.ItemName,
                item.Quantity.ToString(CultureInfo.InvariantCulture),
                item.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture),
                item.Total.ToString("0.##", CultureInfo.InvariantCulture),
            ]);
        }

        rows.Add(new[] { "equipment", "name", "quantity", "note", string.Empty });

        foreach (var item in view.Equipment)
        {
            rows.Add(
                ["equipment", item.Name, item.Quantity.ToString(CultureInfo.InvariantCulture), item.Note, string.Empty]);
        }

        rows.Add(new[] { "timeline", "time", "activity", string.Empty, string.Empty });

        foreach (var item in view.Timeline)
        {
            rows.Add(new[] { "timeline", item.TimeLabel, item.Activity, string.Empty, string.Empty });
        }

        return rows;
    }

    private async Task<Result<T>> AddItemAsync<T>(
        Guid eventId,
        IPlanItemRepository<T> repository,
        Func<Guid, int, T> factory,
        string action,
        string actor,
        CancellationToken cancellationToken)
        where T : Entity
    {
        var plan = await _plans.FindByEventAsync(eventId, cancellationToken);
        if (plan is null)
        {
            return Result<T>.Fail(new ValidationError(string.Empty, "Chưa có kế hoạch cho sự kiện."));
        }

        var order = (await repository.ListByPlanAsync(plan.Id, cancellationToken)).Count + 1;

        T item;
        try
        {
            item = factory(plan.Id, order);
        }
        catch (DomainException ex)
        {
            return Result<T>.Fail(new ValidationError(string.Empty, ex.Message));
        }

        await repository.AddAsync(item, cancellationToken);
        await AuditAsync(action, eventId, item.Id, actor, cancellationToken);

        return Result<T>.Ok(item);
    }

    private async Task<Result> RemoveItemAsync<T>(
        Guid eventId,
        Guid itemId,
        IPlanItemRepository<T> repository,
        string action,
        string actor,
        CancellationToken cancellationToken)
        where T : Entity
    {
        var plan = await _plans.FindByEventAsync(eventId, cancellationToken);
        if (plan is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Chưa có kế hoạch cho sự kiện."));
        }

        var items = await repository.ListByPlanAsync(plan.Id, cancellationToken);
        if (items.All(item => item.Id != itemId))
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy mục cần xoá."));
        }

        await repository.DeleteAsync(itemId, cancellationToken);
        await AuditAsync(action, eventId, itemId, actor, cancellationToken);

        return Result.Ok();
    }

    private async Task<EventPlanView> BuildViewAsync(EventPlan plan, CancellationToken cancellationToken)
    {
        var objectives = (await _objectives.ListByPlanAsync(plan.Id, cancellationToken)).OrderBy(x => x.Order).ToArray();
        var actions = (await _actions.ListByPlanAsync(plan.Id, cancellationToken)).OrderBy(x => x.Order).ToArray();
        var budget = (await _budget.ListByPlanAsync(plan.Id, cancellationToken)).OrderBy(x => x.Order).ToArray();
        var equipment = (await _equipment.ListByPlanAsync(plan.Id, cancellationToken)).OrderBy(x => x.Order).ToArray();
        var timeline = (await _timeline.ListByPlanAsync(plan.Id, cancellationToken)).OrderBy(x => x.Order).ToArray();

        return new EventPlanView(plan, objectives, actions, budget, equipment, timeline);
    }

    private Task AuditAsync(string action, Guid eventId, Guid itemId, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(_clock.UtcNow, actor, action, "event_plan", itemId.ToString("D"), $"event={eventId}"),
            cancellationToken);

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? '"' + text.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : text;
    }
}
