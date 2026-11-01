namespace SIMS.Application.Validation;

/// <summary>Kết quả kiểm tra dữ liệu đầu vào — tách khỏi controller và entity.</summary>
public sealed class ValidationResult
{
    private ValidationResult(IReadOnlyList<ValidationError> errors)
    {
        Errors = errors;
    }

    public static ValidationResult Ok { get; } = new(Array.Empty<ValidationError>());

    public IReadOnlyList<ValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public static ValidationResult Fail(IEnumerable<ValidationError> errors)
        => new(errors.ToArray());
}
