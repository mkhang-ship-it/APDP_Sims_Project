using SIMS.Domain.Events;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvEventPlanRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-plans-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task Plan_round_trip_finds_by_event()
    {
        var repository = new CsvEventPlanRepository(PathFor("event_plans.csv"));
        var eventId = Guid.NewGuid();
        var plan = EventPlan.Create(eventId, "Tóm tắt", Now);
        plan.UpdateSummary("Tóm tắt mới", Now.AddMinutes(5));

        await repository.AddAsync(plan);
        var loaded = await repository.FindByEventAsync(eventId);

        Assert.NotNull(loaded);
        Assert.Equal(plan.Id, loaded!.Id);
        Assert.Equal("Tóm tắt mới", loaded.Summary);
        Assert.Equal(Now.AddMinutes(5), loaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task Plan_items_round_trip_and_filter_by_plan()
    {
        var planId = Guid.NewGuid();
        var otherPlanId = Guid.NewGuid();

        var objectives = new CsvPlanObjectiveRepository(PathFor("event_plan_objectives.csv"));
        await objectives.AddAsync(PlanObjective.Create(planId, "Mục tiêu 1", 1));
        await objectives.AddAsync(PlanObjective.Create(otherPlanId, "Mục tiêu khác", 1));

        var actions = new CsvPlanActionRepository(PathFor("event_plan_actions.csv"));
        await actions.AddAsync(PlanAction.Create(planId, "Việc", "Ban tổ chức", new DateOnly(2026, 11, 9), 1));
        await actions.AddAsync(PlanAction.Create(planId, "Việc không hạn", "Truyền thông", null, 2));

        var budget = new CsvPlanBudgetItemRepository(PathFor("event_plan_budget_items.csv"));
        await budget.AddAsync(PlanBudgetItem.Create(planId, "Nước uống", 5, 5000.5m, 1));

        var equipment = new CsvPlanEquipmentRepository(PathFor("event_plan_equipment.csv"));
        await equipment.AddAsync(PlanEquipment.Create(planId, "Loa", 2, "Phòng B", 1));

        var timeline = new CsvPlanTimelineRepository(PathFor("event_plan_timeline.csv"));
        await timeline.AddAsync(PlanTimelineItem.Create(planId, "08:00", "Khai mạc", 1));

        var loadedObjectives = await objectives.ListByPlanAsync(planId);
        var loadedActions = await actions.ListByPlanAsync(planId);
        var loadedBudget = await budget.ListByPlanAsync(planId);
        var loadedEquipment = await equipment.ListByPlanAsync(planId);
        var loadedTimeline = await timeline.ListByPlanAsync(planId);

        Assert.Single(loadedObjectives);
        Assert.Equal("Mục tiêu 1", loadedObjectives[0].Content);
        Assert.Equal(2, loadedActions.Count);
        Assert.Equal(new DateOnly(2026, 11, 9), loadedActions.Single(x => x.Order == 1).DueDate);
        Assert.Null(loadedActions.Single(x => x.Order == 2).DueDate);
        Assert.Equal(25002.5m, loadedBudget[0].Total);
        Assert.Equal("Phòng B", loadedEquipment[0].Note);
        Assert.Equal("Khai mạc", loadedTimeline[0].Activity);
    }

    [Fact]
    public async Task Item_can_be_deleted()
    {
        var repository = new CsvPlanTimelineRepository(PathFor("event_plan_timeline.csv"));
        var planId = Guid.NewGuid();
        var item = PlanTimelineItem.Create(planId, "08:00", "Khai mạc", 1);
        await repository.AddAsync(item);

        var deleted = await repository.DeleteAsync(item.Id);

        Assert.True(deleted);
        Assert.Empty(await repository.ListByPlanAsync(planId));
    }
}
