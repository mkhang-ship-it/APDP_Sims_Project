using SIMS.Domain.Common;
using SIMS.Domain.Events;

namespace SIMS.Tests.Domain;

public sealed class EventPlanTests
{
    [Fact]
    public void Plan_summary_is_trimmed_and_rejects_too_long_text()
    {
        var plan = EventPlan.Create(Guid.NewGuid(), "  Kế hoạch  ", DateTimeOffset.UtcNow);

        Assert.Equal("Kế hoạch", plan.Summary);
        Assert.Throws<DomainException>(() =>
            plan.UpdateSummary(new string('x', 2001), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Objective_requires_plan_content_and_valid_order()
    {
        var planId = Guid.NewGuid();
        var objective = PlanObjective.Create(planId, "  Tăng tương tác  ", 1);

        Assert.Equal(planId, objective.EventPlanId);
        Assert.Equal("Tăng tương tác", objective.Content);
        Assert.Throws<DomainException>(() => PlanObjective.Create(Guid.Empty, "Nội dung", 1));
        Assert.Throws<DomainException>(() => PlanObjective.Create(planId, "   ", 1));
        Assert.Throws<DomainException>(() => PlanObjective.Create(planId, "Nội dung", -1));
    }

    [Fact]
    public void Action_keeps_owner_and_optional_due_date()
    {
        var dueDate = new DateOnly(2026, 11, 10);
        var action = PlanAction.Create(Guid.NewGuid(), "Đặt phòng", "Ban tổ chức", dueDate, 1);
        var withoutDueDate = PlanAction.Create(Guid.NewGuid(), "Gửi thư mời", "Truyền thông", null, 2);

        Assert.Equal(dueDate, action.DueDate);
        Assert.Equal("Ban tổ chức", action.Owner);
        Assert.Null(withoutDueDate.DueDate);
        Assert.Throws<DomainException>(() => PlanAction.Create(Guid.NewGuid(), "Việc", "  ", null, 1));
    }

    [Fact]
    public void Budget_item_computes_total_and_rejects_bad_quantity()
    {
        var item = PlanBudgetItem.Create(Guid.NewGuid(), "Nước uống", 100, 5000.555m, 1);

        Assert.Equal(5000.56m, item.UnitPrice);
        Assert.Equal(500056m, item.Total);
        Assert.Throws<DomainException>(() => PlanBudgetItem.Create(Guid.NewGuid(), "Nước", 0, 1000m, 1));
        Assert.Throws<DomainException>(() => PlanBudgetItem.Create(Guid.NewGuid(), "Nước", 1, -1m, 1));
    }

    [Fact]
    public void Equipment_note_is_optional_but_bounded()
    {
        var equipment = PlanEquipment.Create(Guid.NewGuid(), "Máy chiếu", 2, null, 1);

        Assert.Equal(string.Empty, equipment.Note);
        Assert.Throws<DomainException>(() =>
            PlanEquipment.Create(Guid.NewGuid(), "Máy chiếu", 1, new string('x', 501), 1));
        Assert.Throws<DomainException>(() => PlanEquipment.Create(Guid.NewGuid(), "Máy chiếu", 0, null, 1));
    }

    [Fact]
    public void Timeline_requires_time_label_and_activity()
    {
        var item = PlanTimelineItem.Create(Guid.NewGuid(), "08:00", "Khai mạc", 1);

        Assert.Equal("08:00", item.TimeLabel);
        Assert.Equal("Khai mạc", item.Activity);
        Assert.Throws<DomainException>(() => PlanTimelineItem.Create(Guid.NewGuid(), " ", "Khai mạc", 1));
        Assert.Throws<DomainException>(() =>
            PlanTimelineItem.Create(Guid.NewGuid(), new string('9', 61), "Khai mạc", 1));
    }
}
