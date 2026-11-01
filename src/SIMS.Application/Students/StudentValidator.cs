using SIMS.Application.Abstractions;
using SIMS.Application.Validation;

namespace SIMS.Application.Students;

public sealed record StudentInput(
    string StudentCode,
    string FullName,
    string Email,
    DateOnly DateOfBirth,
    string Program);

/// <summary>Validation tách riêng khỏi entity và controller.</summary>
public sealed class StudentValidator : IValidator<StudentInput>
{
    private readonly IClock _clock;

    public StudentValidator(IClock clock)
    {
        _clock = clock;
    }

    public ValidationResult Validate(StudentInput input)
    {
        var errors = new List<ValidationError>();
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var code = (input.StudentCode ?? string.Empty).Trim();
        if (code.Length is < 3 or > 20)
        {
            errors.Add(new ValidationError(nameof(StudentInput.StudentCode), "MSSV phải có từ 3 đến 20 ký tự."));
        }
        else if (!code.All(char.IsLetterOrDigit))
        {
            errors.Add(new ValidationError(nameof(StudentInput.StudentCode), "MSSV chỉ được chứa chữ và số."));
        }

        var fullName = (input.FullName ?? string.Empty).Trim();
        if (fullName.Length is < 2 or > 120)
        {
            errors.Add(new ValidationError(nameof(StudentInput.FullName), "Họ tên phải có từ 2 đến 120 ký tự."));
        }

        var email = (input.Email ?? string.Empty).Trim();
        var atIndex = email.IndexOf('@');
        var isEmailValid =
            atIndex > 0 &&
            atIndex == email.LastIndexOf('@') &&
            email.Length > atIndex + 3 &&
            email[(atIndex + 1)..].Contains('.') &&
            !email.Contains(' ', StringComparison.Ordinal) &&
            email.Length <= 254;

        if (!isEmailValid)
        {
            errors.Add(new ValidationError(nameof(StudentInput.Email), "Email không hợp lệ."));
        }

        if (input.DateOfBirth == default)
        {
            errors.Add(new ValidationError(nameof(StudentInput.DateOfBirth), "Ngày sinh là bắt buộc."));
        }
        else if (input.DateOfBirth >= today)
        {
            errors.Add(new ValidationError(nameof(StudentInput.DateOfBirth), "Ngày sinh phải trước ngày hiện tại."));
        }
        else if (input.DateOfBirth.Year < 1900)
        {
            errors.Add(new ValidationError(nameof(StudentInput.DateOfBirth), "Ngày sinh không hợp lệ (trước năm 1900)."));
        }

        var program = (input.Program ?? string.Empty).Trim();
        if (program.Length is < 2 or > 120)
        {
            errors.Add(new ValidationError(nameof(StudentInput.Program), "Chương trình học phải có từ 2 đến 120 ký tự."));
        }

        return errors.Count == 0 ? ValidationResult.Ok : ValidationResult.Fail(errors);
    }
}
