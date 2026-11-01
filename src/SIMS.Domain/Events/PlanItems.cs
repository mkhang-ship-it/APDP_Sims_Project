using SIMS.Domain.Common;

namespace SIMS.Domain.Events;

/// <summary>Mục tiêu của kế hoạch sự kiện.</summary>
public sealed class PlanObjective : Entity
{
    private PlanObjective() => Content = string.Empty;

    public Guid EventPlanId { get; private set; }

    public string Content { get; private set; }

    public int Order { get; private set; }

    public static PlanObjective Create(Guid planId, string content, int order)
        => new()
        {
            EventPlanId = RequirePlan(planId),
            Content = RequireText(content, 500, "Nội dung mục tiêu"),
            Order = RequireOrder(order),
        };

    public static PlanObjective Restore(Guid id, Guid planId, string content, int order)
    {
        var item = Create(planId, content, order);
        item.Id = id;

        return item;
    }

    internal static Guid RequirePlan(Guid planId)
        => planId == Guid.Empty ? throw new DomainException("Kế hoạch không hợp lệ.") : planId;

    internal static int RequireOrder(int order)
        => order < 0 ? throw new DomainException("Thứ tự không hợp lệ.") : order;

    internal static string RequireText(string value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new DomainException($"{fieldName} là bắt buộc.");
        }

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text;
    }
}

/// <summary>Hành động cần làm (ai làm, hạn chót).</summary>
public sealed class PlanAction : Entity
{
    private PlanAction()
    {
        Content = string.Empty;
        Owner = string.Empty;
    }

    public Guid EventPlanId { get; private set; }

    public string Content { get; private set; }

    public string Owner { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public int Order { get; private set; }

    public static PlanAction Create(Guid planId, string content, string owner, DateOnly? dueDate, int order)
        => new()
        {
            EventPlanId = PlanObjective.RequirePlan(planId),
            Content = PlanObjective.RequireText(content, 500, "Nội dung hành động"),
            Owner = PlanObjective.RequireText(owner, 120, "Người phụ trách"),
            DueDate = dueDate,
            Order = PlanObjective.RequireOrder(order),
        };

    public static PlanAction Restore(Guid id, Guid planId, string content, string owner, DateOnly? dueDate, int order)
    {
        var item = Create(planId, content, owner, dueDate, order);
        item.Id = id;

        return item;
    }
}

/// <summary>Dòng ngân sách: số lượng × đơn giá.</summary>
public sealed class PlanBudgetItem : Entity
{
    private PlanBudgetItem() => ItemName = string.Empty;

    public Guid EventPlanId { get; private set; }

    public string ItemName { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Order { get; private set; }

    public decimal Total => Quantity * UnitPrice;

    public static PlanBudgetItem Create(Guid planId, string itemName, int quantity, decimal unitPrice, int order)
        => new()
        {
            EventPlanId = PlanObjective.RequirePlan(planId),
            ItemName = PlanObjective.RequireText(itemName, 160, "Tên hạng mục"),
            Quantity = quantity is < 1 or > 100_000
                ? throw new DomainException("Số lượng phải từ 1 đến 100000.")
                : quantity,
            UnitPrice = unitPrice is < 0 or > 1_000_000_000m
                ? throw new DomainException("Đơn giá không hợp lệ.")
                : decimal.Round(unitPrice, 2),
            Order = PlanObjective.RequireOrder(order),
        };

    public static PlanBudgetItem Restore(
        Guid id, Guid planId, string itemName, int quantity, decimal unitPrice, int order)
    {
        var item = Create(planId, itemName, quantity, unitPrice, order);
        item.Id = id;

        return item;
    }
}

/// <summary>Thiết bị/vật dụng cần chuẩn bị.</summary>
public sealed class PlanEquipment : Entity
{
    private PlanEquipment()
    {
        Name = string.Empty;
        Note = string.Empty;
    }

    public Guid EventPlanId { get; private set; }

    public string Name { get; private set; }

    public int Quantity { get; private set; }

    public string Note { get; private set; }

    public int Order { get; private set; }

    public static PlanEquipment Create(Guid planId, string name, int quantity, string? note, int order)
        => new()
        {
            EventPlanId = PlanObjective.RequirePlan(planId),
            Name = PlanObjective.RequireText(name, 160, "Tên thiết bị"),
            Quantity = quantity is < 1 or > 100_000
                ? throw new DomainException("Số lượng phải từ 1 đến 100000.")
                : quantity,
            Note = (note ?? string.Empty).Trim() is var n && n.Length > 500
                ? throw new DomainException("Ghi chú tối đa 500 ký tự.")
                : n,
            Order = PlanObjective.RequireOrder(order),
        };

    public static PlanEquipment Restore(Guid id, Guid planId, string name, int quantity, string? note, int order)
    {
        var item = Create(planId, name, quantity, note, order);
        item.Id = id;

        return item;
    }
}

/// <summary>Mốc thời gian của kế hoạch.</summary>
public sealed class PlanTimelineItem : Entity
{
    private PlanTimelineItem()
    {
        TimeLabel = string.Empty;
        Activity = string.Empty;
    }

    public Guid EventPlanId { get; private set; }

    public string TimeLabel { get; private set; }

    public string Activity { get; private set; }

    public int Order { get; private set; }

    public static PlanTimelineItem Create(Guid planId, string timeLabel, string activity, int order)
        => new()
        {
            EventPlanId = PlanObjective.RequirePlan(planId),
            TimeLabel = PlanObjective.RequireText(timeLabel, 60, "Mốc thời gian"),
            Activity = PlanObjective.RequireText(activity, 500, "Hoạt động"),
            Order = PlanObjective.RequireOrder(order),
        };

    public static PlanTimelineItem Restore(Guid id, Guid planId, string timeLabel, string activity, int order)
    {
        var item = Create(planId, timeLabel, activity, order);
        item.Id = id;

        return item;
    }
}
