namespace SIMS.Domain.Events;

/// <summary>Trạng thái sự kiện (tương ứng pending/active/completed/cancelled của bản PHP).</summary>
public enum EventStatus
{
    Draft = 0,
    Published = 1,
    Completed = 2,
    Cancelled = 3,
}
