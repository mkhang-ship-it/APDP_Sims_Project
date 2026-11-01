using System.ComponentModel.DataAnnotations;
using SIMS.Application.Events;
using SIMS.Domain.Events;

namespace SIMS.Web.Models;

public sealed class EventFormModel : IValidatableObject
{
    [Required(ErrorMessage = "Mã sự kiện là bắt buộc.")]
    [StringLength(24, MinimumLength = 3, ErrorMessage = "Mã sự kiện phải có từ 3 đến 24 ký tự.")]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Mã sự kiện chỉ được chứa chữ, số, '-' hoặc '_'.")]
    [Display(Name = "Mã sự kiện")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên sự kiện là bắt buộc.")]
    [StringLength(160, MinimumLength = 3, ErrorMessage = "Tên sự kiện phải có từ 3 đến 160 ký tự.")]
    [Display(Name = "Tên sự kiện")]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Mô tả tối đa 2000 ký tự.")]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Thời gian bắt đầu là bắt buộc.")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Bắt đầu")]
    public DateTime StartsAt { get; set; } = DateTime.Today.AddHours(8);

    [Required(ErrorMessage = "Thời gian kết thúc là bắt buộc.")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Kết thúc")]
    public DateTime EndsAt { get; set; } = DateTime.Today.AddHours(11);

    [Required(ErrorMessage = "Địa điểm là bắt buộc.")]
    [StringLength(160, MinimumLength = 2, ErrorMessage = "Địa điểm phải có từ 2 đến 160 ký tự.")]
    [Display(Name = "Địa điểm")]
    public string Location { get; set; } = string.Empty;

    [Range(1, 1000, ErrorMessage = "Sức chứa phải từ 1 đến 1000.")]
    [Display(Name = "Sức chứa")]
    public int Capacity { get; set; } = 50;

    [Range(-90d, 90d, ErrorMessage = "Vĩ độ phải nằm trong khoảng -90 đến 90.")]
    [Display(Name = "Vĩ độ địa điểm")]
    public double? VenueLatitude { get; set; }

    [Range(-180d, 180d, ErrorMessage = "Kinh độ phải nằm trong khoảng -180 đến 180.")]
    [Display(Name = "Kinh độ địa điểm")]
    public double? VenueLongitude { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (VenueLatitude.HasValue != VenueLongitude.HasValue)
        {
            yield return new ValidationResult(
                "Cần nhập đủ cả vĩ độ và kinh độ, hoặc để trống cả hai.",
                [nameof(VenueLatitude), nameof(VenueLongitude)]);
        }
    }

    public EventInput ToInput() => new(
        Code,
        Title,
        Description ?? string.Empty,
        ToOffset(StartsAt),
        ToOffset(EndsAt),
        Location,
        Capacity,
        VenueLatitude is not null && VenueLongitude is not null
            ? new GeoPoint(VenueLatitude.Value, VenueLongitude.Value)
            : null);

    private static DateTimeOffset ToOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Local));
}

public sealed record AttendanceRow(SIMS.Domain.Events.Attendance Attendance, string StudentCode, string StudentName);

public sealed class EventDetailsViewModel
{
    public SIMS.Domain.Events.Event Event { get; init; } = null!;

    public IReadOnlyList<AttendanceRow> Attendance { get; init; } = Array.Empty<AttendanceRow>();

    public IReadOnlyList<SIMS.Domain.Events.EventImage> Images { get; init; } =
        Array.Empty<SIMS.Domain.Events.EventImage>();

    public string CheckinUrl { get; init; } = string.Empty;
}
