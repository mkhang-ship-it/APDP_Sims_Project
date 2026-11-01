using SIMS.Application.EventPlans;
using SIMS.Domain.Events;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class EventPlanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        EventPlanService Service,
        InMemoryEventPlanRepository Plans,
        InMemoryPlanItemRepository<PlanObjective> Objectives,
        InMemoryPlanItemRepository<PlanAction> Actions,
        InMemoryPlanItemRepository<PlanBudgetItem> Budget,
        InMemoryPlanItemRepository<PlanEquipment> Equipment,
        InMemoryPlanItemRepository<PlanTimelineItem> Timeline,
        RecordingAuditLog Audit);

    private static Harness Create()
    {
        var plans = new InMemoryEventPlanRepository();
        var objectives = new InMemoryPlanItemRepository<PlanObjective>(x => x.EventPlanId);
        var actions = new InMemoryPlanItemRepository<PlanAction>(x => x.EventPlanId);
        var budget = new InMemoryPlanItemRepository<PlanBudgetItem>(x => x.EventPlanId);
        var equipment = new InMemoryPlanItemRepository<PlanEquipment>(x => x.EventPlanId);
        var timeline = new InMemoryPlanItemRepository<PlanTimelineItem>(x => x.EventPlanId);
        var audit = new RecordingAuditLog();

        var service = new EventPlanService(
            plans, objectives, actions, budget, equipment, timeline, audit, new FakeClock(Now));

        return new Harness(service, plans, objectives, actions, budget, equipment, timeline, audit);
    }

    [Fact]
    public async Task Get_or_create_persists_single_plan_per_event()
    {
        var harness = Create();
        var eventId = Guid.NewGuid();

        var first = await harness.Service.GetOrCreateAsync(eventId, "faculty1");
        var second = await harness.Service.GetOrCreateAsync(eventId, "faculty1");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(harness.Plans.All);
        Assert.Equal(first.Value!.Plan.Id, second.Value!.Plan.Id);
        Assert.Equal("eventplan.create", harness.Audit.Entries[0].Action);
    }

    [Fact]
    public async Task Adding_items_across_sections_returns_view_with_budget_total()
    {
        var harness = Create();
        var eventId = Guid.NewGuid();
        await harness.Service.GetOrCreateAsync(eventId, "faculty1");

        await harness.Service.UpdateSummaryAsync(eventId, "Kế hoạch ngày hội", "faculty1");
        await harness.Service.AddObjectiveAsync(eventId, "Thu hút sinh viên", "faculty1");
        await harness.Service.AddActionAsync(
            eventId, "Đặt hội trường", "Ban tổ chức", new DateOnly(2026, 11, 5), "faculty1");
        await harness.Service.AddBudgetItemAsync(eventId, "Nước uống", 10, 5000m, "faculty1");
        await harness.Service.AddBudgetItemAsync(eventId, "Băng rôn", 2, 150000m, "faculty1");
        await harness.Service.AddEquipmentAsync(eventId, "Máy chiếu", 1, "Phòng A", "faculty1");
        await harness.Service.AddTimelineAsync(eventId, "08:00", "Khai mạc", "faculty1");
        await harness.Service.AddTimelineAsync(eventId, "09:30", "Talkshow", "faculty1");

        var view = await harness.Service.GetOrCreateAsync(eventId, "faculty1");

        Assert.True(view.IsSuccess);
        Assert.Equal("Kế hoạch ngày hội", view.Value!.Plan.Summary);
        Assert.Single(view.Value.Objectives);
        Assert.Single(view.Value.Actions);
        Assert.Single(view.Value.Equipment);
        Assert.Equal(2, view.Value.BudgetItems.Count);
        Assert.Equal(350000m, view.Value.BudgetTotal);
        Assert.Equal(new[] { "08:00", "09:30" }, view.Value.Timeline.Select(x => x.TimeLabel).ToArray());
        Assert.Contains(harness.Audit.Entries, e => e.Action == "eventplan.budget.add");
    }

    [Fact]
    public async Task Invalid_items_fail_without_writing()
    {
        var harness = Create();
        var eventId = Guid.NewGuid();
        await harness.Service.GetOrCreateAsync(eventId, "faculty1");

        var blankObjective = await harness.Service.AddObjectiveAsync(eventId, "   ", "faculty1");
        var badQuantity = await harness.Service.AddBudgetItemAsync(eventId, "Nước", 0, 1000m, "faculty1");
        var missingOwner = await harness.Service.AddActionAsync(eventId, "Việc", " ", null, "faculty1");

        Assert.False(blankObjective.IsSuccess);
        Assert.False(badQuantity.IsSuccess);
        Assert.False(missingOwner.IsSuccess);
        Assert.Empty(harness.Objectives.All);
        Assert.Empty(harness.Budget.All);
        Assert.Empty(harness.Actions.All);
    }

    [Fact]
    public async Task Items_require_an_existing_plan()
    {
        var harness = Create();

        var result = await harness.Service.AddObjectiveAsync(Guid.NewGuid(), "Mục tiêu", "faculty1");

        Assert.False(result.IsSuccess);
        Assert.Empty(harness.Objectives.All);
    }

    [Fact]
    public async Task Remove_deletes_known_item_and_rejects_unknown()
    {
        var harness = Create();
        var eventId = Guid.NewGuid();
        await harness.Service.GetOrCreateAsync(eventId, "faculty1");
        var added = await harness.Service.AddEquipmentAsync(eventId, "Loa", 2, null, "faculty1");

        var removed = await harness.Service.RemoveEquipmentAsync(eventId, added.Value!.Id, "faculty1");
        var again = await harness.Service.RemoveEquipmentAsync(eventId, added.Value.Id, "faculty1");

        Assert.True(removed.IsSuccess);
        Assert.False(again.IsSuccess);
        Assert.Empty(harness.Equipment.All);
        Assert.Contains(harness.Audit.Entries, e => e.Action == "eventplan.equipment.remove");
    }

    [Fact]
    public async Task Export_csv_contains_all_sections_and_escapes_commas()
    {
        var harness = Create();
        var eventId = Guid.NewGuid();
        await harness.Service.GetOrCreateAsync(eventId, "faculty1");
        await harness.Service.UpdateSummaryAsync(eventId, "Ngày hội, quy mô lớn", "faculty1");
        await harness.Service.AddObjectiveAsync(eventId, "Mục tiêu A", "faculty1");
        await harness.Service.AddActionAsync(eventId, "Việc B", "Ban tổ chức", null, "faculty1");
        await harness.Service.AddBudgetItemAsync(eventId, "Nước uống", 3, 5000m, "faculty1");
        await harness.Service.AddEquipmentAsync(eventId, "Máy chiếu", 1, "Phòng A", "faculty1");
        await harness.Service.AddTimelineAsync(eventId, "08:00", "Khai mạc", "faculty1");

        var csv = await harness.Service.ExportCsvAsync(eventId);

        Assert.True(csv.IsSuccess);
        Assert.StartsWith("section,field1,field2,field3,field4", csv.Value, StringComparison.Ordinal);
        Assert.Contains("\"Ngày hội, quy mô lớn\"", csv.Value);
        Assert.Contains("objective,Mục tiêu A", csv.Value);
        Assert.Contains("action,Việc B,Ban tổ chức,,", csv.Value);
        Assert.Contains("budget,Nước uống,3,5000,15000", csv.Value);
        Assert.Contains("equipment,Máy chiếu,1,Phòng A", csv.Value);
        Assert.Contains("timeline,08:00,Khai mạc", csv.Value);
    }

    [Fact]
    public async Task Export_fails_without_plan()
    {
        var harness = Create();

        var result = await harness.Service.ExportCsvAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("Chưa có kế hoạch", result.Errors[0].Message);
    }
}
