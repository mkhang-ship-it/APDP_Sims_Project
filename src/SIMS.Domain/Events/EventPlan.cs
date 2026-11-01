using SIMS.Domain.Common;

namespace SIMS.Domain.Events;

/// <summary>
/// Kế hoạch sự kiện (1 kế hoạch / sự kiện) — đầu mối cho 5 mục chi tiết.
/// Thông tin chung bám theo bảng <c>event_plans</c> của bản PHP.
/// </summary>
public sealed class EventPlan : Entity
{
    private EventPlan()
    {
        Summary = string.Empty;
        PreparedBy = string.Empty;
        ApprovedBy = string.Empty;
        Participants = string.Empty;
        Description = string.Empty;
        ResponsibleOrganizer = string.Empty;
        AdvisorCoordinator = string.Empty;
    }

    public Guid EventId { get; private set; }

    public string Summary { get; private set; }

    /// <summary>Người lập kế hoạch (tự do nhập, không ràng buộc tài khoản như bản PHP).</summary>
    public string PreparedBy { get; private set; }

    /// <summary>Ngày triển khai.</summary>
    public DateOnly? ImplementationDate { get; private set; }

    /// <summary>Người/bộ phận phê duyệt.</summary>
    public string ApprovedBy { get; private set; }

    /// <summary>Đối tượng tham gia.</summary>
    public string Participants { get; private set; }

    /// <summary>Mô tả/hình thức tổ chức.</summary>
    public string Description { get; private set; }

    /// <summary>Chủ trì tổ chức.</summary>
    public string ResponsibleOrganizer { get; private set; }

    /// <summary>Cố vấn và phối hợp tổ chức.</summary>
    public string AdvisorCoordinator { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static EventPlan Create(
        Guid eventId,
        string summary,
        DateTimeOffset createdAtUtc,
        string? preparedBy = null,
        DateOnly? implementationDate = null,
        string? approvedBy = null,
        string? participants = null,
        string? description = null,
        string? responsibleOrganizer = null,
        string? advisorCoordinator = null)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("Sự kiện không hợp lệ.");
        }

        return new EventPlan
        {
            EventId = eventId,
            Summary = Normalize(summary, 2000, "Tóm tắt kế hoạch"),
            PreparedBy = Normalize(preparedBy, 160, "Người lập kế hoạch"),
            ImplementationDate = implementationDate,
            ApprovedBy = Normalize(approvedBy, 200, "Người phê duyệt"),
            Participants = Normalize(participants, 500, "Đối tượng tham gia"),
            Description = Normalize(description, 2000, "Mô tả kế hoạch"),
            ResponsibleOrganizer = Normalize(responsibleOrganizer, 200, "Chủ trì tổ chức"),
            AdvisorCoordinator = Normalize(advisorCoordinator, 200, "Cố vấn/phối hợp"),
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    public static EventPlan Restore(
        Guid id,
        Guid eventId,
        string summary,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        string? preparedBy = null,
        DateOnly? implementationDate = null,
        string? approvedBy = null,
        string? participants = null,
        string? description = null,
        string? responsibleOrganizer = null,
        string? advisorCoordinator = null)
    {
        var plan = Create(
            eventId,
            summary,
            createdAtUtc,
            preparedBy,
            implementationDate,
            approvedBy,
            participants,
            description,
            responsibleOrganizer,
            advisorCoordinator);
        plan.Id = id;
        plan.UpdatedAtUtc = updatedAtUtc;

        return plan;
    }

    public void UpdateSummary(string summary, DateTimeOffset updatedAtUtc)
    {
        Summary = Normalize(summary, 2000, "Tóm tắt kế hoạch");
        UpdatedAtUtc = updatedAtUtc;
    }

    public void UpdateDetails(
        string summary,
        string? preparedBy,
        DateOnly? implementationDate,
        string? approvedBy,
        string? participants,
        string? description,
        string? responsibleOrganizer,
        string? advisorCoordinator,
        DateTimeOffset updatedAtUtc)
    {
        Summary = Normalize(summary, 2000, "Tóm tắt kế hoạch");
        PreparedBy = Normalize(preparedBy, 160, "Người lập kế hoạch");
        ImplementationDate = implementationDate;
        ApprovedBy = Normalize(approvedBy, 200, "Người phê duyệt");
        Participants = Normalize(participants, 500, "Đối tượng tham gia");
        Description = Normalize(description, 2000, "Mô tả kế hoạch");
        ResponsibleOrganizer = Normalize(responsibleOrganizer, 200, "Chủ trì tổ chức");
        AdvisorCoordinator = Normalize(advisorCoordinator, 200, "Cố vấn/phối hợp");
        UpdatedAtUtc = updatedAtUtc;
    }

    private static string Normalize(string? value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text;
    }
}
