using System.ComponentModel.DataAnnotations;
using SIMS.Application.Courses;
using SIMS.Domain.Courses;

namespace SIMS.Web.Models;

public sealed class CourseFormModel
{
    [Required(ErrorMessage = "Mã học phần là bắt buộc.")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Mã học phần phải có từ 2 đến 20 ký tự.")]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Mã học phần chỉ được chứa chữ và số.")]
    [Display(Name = "Mã học phần")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên học phần là bắt buộc.")]
    [StringLength(160, MinimumLength = 2, ErrorMessage = "Tên học phần phải có từ 2 đến 160 ký tự.")]
    [Display(Name = "Tên học phần")]
    public string Title { get; set; } = string.Empty;

    [Range(1, 200, ErrorMessage = "Sức chứa phải từ 1 đến 200.")]
    [Display(Name = "Sức chứa")]
    public int Capacity { get; set; } = 30;

    [StringLength(20, ErrorMessage = "Mã tiên quyết tối đa 20 ký tự.")]
    [Display(Name = "Học phần tiên quyết (nếu có)")]
    public string? PrerequisiteCode { get; set; }

    public CourseInput ToInput() => new(Code, Title, Capacity, PrerequisiteCode);

    public static CourseFormModel FromCourse(Course course) => new()
    {
        Code = course.Code,
        Title = course.Title,
        Capacity = course.Capacity,
        PrerequisiteCode = course.PrerequisiteCode,
    };
}
