using System.ComponentModel.DataAnnotations;
using SIMS.Application.Clubs;
using SIMS.Domain.Clubs;
using SIMS.Domain.Security;

namespace SIMS.Web.Models;

public sealed class ClubFormModel
{
    [Required(ErrorMessage = "Mã CLB là bắt buộc.")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Mã CLB phải có từ 2 đến 20 ký tự.")]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Mã CLB chỉ được chứa chữ và số.")]
    [Display(Name = "Mã CLB")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên CLB là bắt buộc.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Tên CLB phải có từ 2 đến 120 ký tự.")]
    [Display(Name = "Tên CLB")]
    public string Name { get; set; } = string.Empty;

    public ClubInput ToInput() => new(Code, Name);

    public static ClubFormModel FromClub(Club club) => new() { Code = club.Code, Name = club.Name };
}

public sealed record ClubListItem(Club Club, int MemberCount);

public sealed class ClubEditViewModel
{
    public Club Club { get; init; } = null!;

    public ClubFormModel Form { get; init; } = new();

    public IReadOnlyList<User> Members { get; init; } = Array.Empty<User>();

    public IReadOnlyList<User> AllUsers { get; init; } = Array.Empty<User>();

    public IReadOnlyList<Role> AvailableRoles { get; init; } = Array.Empty<Role>();
}
