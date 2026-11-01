using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Courses;

namespace SIMS.Application.Courses;

public sealed record CourseInput(string Code, string Title, int Capacity, string? PrerequisiteCode);

/// <summary>Validation tách riêng khỏi entity/controller.</summary>
public sealed class CourseValidator : IValidator<CourseInput>
{
    public ValidationResult Validate(CourseInput input)
    {
        var errors = new List<ValidationError>();
        var code = (input.Code ?? string.Empty).Trim();

        if (code.Length is < 2 or > 20)
        {
            errors.Add(new ValidationError(nameof(CourseInput.Code), "Mã học phần phải có từ 2 đến 20 ký tự."));
        }
        else if (!code.All(char.IsLetterOrDigit))
        {
            errors.Add(new ValidationError(nameof(CourseInput.Code), "Mã học phần chỉ được chứa chữ và số."));
        }

        var title = (input.Title ?? string.Empty).Trim();
        if (title.Length is < 2 or > 160)
        {
            errors.Add(new ValidationError(nameof(CourseInput.Title), "Tên học phần phải có từ 2 đến 160 ký tự."));
        }

        if (input.Capacity is < 1 or > 200)
        {
            errors.Add(new ValidationError(nameof(CourseInput.Capacity), "Sức chứa phải từ 1 đến 200."));
        }

        var prerequisite = (input.PrerequisiteCode ?? string.Empty).Trim();
        if (prerequisite.Length > 0)
        {
            if (prerequisite.Length is < 2 or > 20)
            {
                errors.Add(new ValidationError(nameof(CourseInput.PrerequisiteCode), "Mã tiên quyết phải có từ 2 đến 20 ký tự."));
            }
            else if (!prerequisite.All(char.IsLetterOrDigit))
            {
                errors.Add(new ValidationError(nameof(CourseInput.PrerequisiteCode), "Mã tiên quyết chỉ được chứa chữ và số."));
            }
            else if (string.Equals(prerequisite, code, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationError(nameof(CourseInput.PrerequisiteCode), "Học phần không thể là tiên quyết của chính nó."));
            }
        }

        return errors.Count == 0 ? ValidationResult.Ok : ValidationResult.Fail(errors);
    }
}

public interface ICourseService
{
    Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default);

    Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Course>> CreateAsync(CourseInput input, string actor, CancellationToken cancellationToken = default);

    Task<Result<Course>> UpdateAsync(Guid id, CourseInput input, string actor, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);
}

/// <summary>Use case Courses — thuần nghiệp vụ, không tự ghi audit (decorator lo).</summary>
public sealed class CourseService : ICourseService
{
    private readonly ICourseRepository _courses;
    private readonly IValidator<CourseInput> _validator;
    private readonly IClock _clock;

    public CourseService(ICourseRepository courses, IValidator<CourseInput> validator, IClock clock)
    {
        _courses = courses;
        _validator = validator;
        _clock = clock;
    }

    public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default)
        => _courses.ListAsync(cancellationToken);

    public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _courses.FindByIdAsync(id, cancellationToken);

    public async Task<Result<Course>> CreateAsync(
        CourseInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return Result<Course>.Fail(validation.Errors);
        }

        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await _courses.ExistsByCodeAsync(code, null, cancellationToken))
        {
            return Result<Course>.Fail(new ValidationError(nameof(CourseInput.Code), $"Mã học phần '{code}' đã tồn tại."));
        }

        var prerequisiteError = await ValidatePrerequisiteAsync(input.PrerequisiteCode, null, cancellationToken);
        if (prerequisiteError is not null)
        {
            return Result<Course>.Fail(new ValidationError(nameof(CourseInput.PrerequisiteCode), prerequisiteError));
        }

        var course = Course.Create(input.Code ?? string.Empty, input.Title, input.Capacity, input.PrerequisiteCode, _clock.UtcNow);

        await _courses.AddAsync(course, cancellationToken);
        return Result<Course>.Ok(course);
    }

    public async Task<Result<Course>> UpdateAsync(
        Guid id,
        CourseInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return Result<Course>.Fail(validation.Errors);
        }

        var course = await _courses.FindByIdAsync(id, cancellationToken);
        if (course is null)
        {
            return Result<Course>.Fail(new ValidationError(string.Empty, "Không tìm thấy học phần."));
        }

        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.Equals(course.Code, code, StringComparison.Ordinal))
        {
            return Result<Course>.Fail(new ValidationError(nameof(CourseInput.Code), "Không được đổi mã học phần sau khi tạo."));
        }

        var prerequisiteError = await ValidatePrerequisiteAsync(input.PrerequisiteCode, id, cancellationToken);
        if (prerequisiteError is not null)
        {
            return Result<Course>.Fail(new ValidationError(nameof(CourseInput.PrerequisiteCode), prerequisiteError));
        }

        course.UpdateDetails(input.Title, input.Capacity, input.PrerequisiteCode);
        await _courses.UpdateAsync(course, cancellationToken);

        return Result<Course>.Ok(course);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var course = await _courses.FindByIdAsync(id, cancellationToken);
        if (course is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy học phần."));
        }

        var deleted = await _courses.DeleteAsync(id, cancellationToken);
        return deleted
            ? Result.Ok()
            : Result.Fail(new ValidationError(string.Empty, "Không xoá được học phần."));
    }

    private async Task<string?> ValidatePrerequisiteAsync(
        string? prerequisiteCode,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var code = (prerequisiteCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            return null;
        }

        var prerequisite = await _courses.FindByCodeAsync(code, cancellationToken);
        if (prerequisite is null)
        {
            return $"Học phần tiên quyết '{code}' không tồn tại.";
        }

        if (excludeId is not null && prerequisite.Id == excludeId)
        {
            return "Học phần không thể là tiên quyết của chính nó.";
        }

        return null;
    }
}
