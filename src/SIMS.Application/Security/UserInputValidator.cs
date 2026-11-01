using SIMS.Application.Validation;
using SIMS.Domain.Security;

namespace SIMS.Application.Security;

public sealed record UserInput(string Username, string DisplayName, Role Role, string Password);

/// <summary>Chính sách tài khoản: tên đăng nhập hợp lệ + mật khẩu tối thiểu 8 ký tự có chữ và số.</summary>
public sealed class UserInputValidator : IValidator<UserInput>
{
    public ValidationResult Validate(UserInput input)
    {
        var errors = new List<ValidationError>();

        var username = (input.Username ?? string.Empty).Trim();
        if (username.Length is < 3 or > 64)
        {
            errors.Add(new ValidationError(nameof(UserInput.Username), "Tên đăng nhập phải có từ 3 đến 64 ký tự."));
        }
        else if (!username.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-'))
        {
            errors.Add(new ValidationError(nameof(UserInput.Username), "Tên đăng nhập chỉ được chứa chữ, số, '.', '_' hoặc '-'."));
        }

        var displayName = (input.DisplayName ?? string.Empty).Trim();
        if (displayName.Length is < 2 or > 120)
        {
            errors.Add(new ValidationError(nameof(UserInput.DisplayName), "Tên hiển thị phải có từ 2 đến 120 ký tự."));
        }

        var password = input.Password ?? string.Empty;
        if (password.Length < 8)
        {
            errors.Add(new ValidationError(nameof(UserInput.Password), "Mật khẩu phải có ít nhất 8 ký tự."));
        }
        else if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            errors.Add(new ValidationError(nameof(UserInput.Password), "Mật khẩu phải có cả chữ và số."));
        }

        if (!Enum.IsDefined(input.Role))
        {
            errors.Add(new ValidationError(nameof(UserInput.Role), "Vai trò không hợp lệ."));
        }

        return errors.Count == 0 ? ValidationResult.Ok : ValidationResult.Fail(errors);
    }
}
