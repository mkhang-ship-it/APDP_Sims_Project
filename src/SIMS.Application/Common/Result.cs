using SIMS.Application.Validation;

namespace SIMS.Application.Common;

/// <summary>Kết quả thao tác không trả dữ liệu.</summary>
public class Result
{
    protected Result(bool isSuccess, IReadOnlyList<ValidationError> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public IReadOnlyList<ValidationError> Errors { get; }

    public static Result Ok() => new(true, Array.Empty<ValidationError>());

    public static Result Fail(params ValidationError[] errors) => new(false, errors);

    public static Result Fail(IEnumerable<ValidationError> errors) => new(false, errors.ToArray());
}

/// <summary>Kết quả thao tác có dữ liệu trả về (Result pattern, không ném exception cho lỗi nghiệp vụ).</summary>
public sealed class Result<T> : Result
{
    private Result(bool isSuccess, T? value, IReadOnlyList<ValidationError> errors)
        : base(isSuccess, errors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Ok(T value) => new(true, value, Array.Empty<ValidationError>());

    public static new Result<T> Fail(params ValidationError[] errors) => new(false, default, errors);

    public static new Result<T> Fail(IEnumerable<ValidationError> errors) => new(false, default, errors.ToArray());
}
