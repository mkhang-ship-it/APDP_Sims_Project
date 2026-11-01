using System.ComponentModel.DataAnnotations;
using SIMS.Application.Students;
using SIMS.Domain.Students;

namespace SIMS.Web.Models;

public sealed class StudentFormModel
{
    [Required(ErrorMessage = "MSSV là bắt buộc.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "MSSV phải có từ 3 đến 20 ký tự.")]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "MSSV chỉ được chứa chữ và số.")]
    [Display(Name = "MSSV")]
    public string StudentCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ tên là bắt buộc.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Họ tên phải có từ 2 đến 120 ký tự.")]
    [Display(Name = "Họ tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(254)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Ngày sinh")]
    public DateOnly DateOfBirth { get; set; }

    [Required(ErrorMessage = "Chương trình học là bắt buộc.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Chương trình học phải có từ 2 đến 120 ký tự.")]
    [Display(Name = "Chương trình học")]
    public string Program { get; set; } = string.Empty;

    public StudentInput ToInput() => new(StudentCode, FullName, Email, DateOfBirth, Program);

    public static StudentFormModel FromStudent(Student student) => new()
    {
        StudentCode = student.StudentCode,
        FullName = student.FullName,
        Email = student.Email,
        DateOfBirth = student.DateOfBirth,
        Program = student.Program,
    };
}
