using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Students;

namespace SIMS.Application.Students;

public interface IStudentService
{
    Task<IReadOnlyList<Student>> ListAsync(CancellationToken cancellationToken = default);

    Task<Student?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Student>> CreateAsync(
        StudentInput input,
        string actor,
        Guid? clubId = null,
        CancellationToken cancellationToken = default);

    Task<Result<Student>> UpdateAsync(Guid id, StudentInput input, string actor, CancellationToken cancellationToken = default);

    Task<Result<Student>> LinkUserAsync(
        Guid studentId,
        Guid userId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Use case STUDENTS: CRUD + kiểm tra MSSV duy nhất + audit cho mọi thao tác ghi.
/// </summary>
public sealed class StudentService : IStudentService
{
    private readonly IStudentRepository _repository;
    private readonly IValidator<StudentInput> _validator;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public StudentService(
        IStudentRepository repository,
        IValidator<StudentInput> validator,
        IAuditLog audit,
        IClock clock)
    {
        _repository = repository;
        _validator = validator;
        _audit = audit;
        _clock = clock;
    }

    public Task<IReadOnlyList<Student>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.ListAsync(cancellationToken);

    public Task<Student?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.FindByIdAsync(id, cancellationToken);

    public async Task<Result<Student>> CreateAsync(
        StudentInput input,
        string actor,
        Guid? clubId = null,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return Result<Student>.Fail(validation.Errors);
        }

        var normalizedCode = (input.StudentCode ?? string.Empty).Trim().ToUpperInvariant();
        if (await _repository.ExistsByCodeAsync(normalizedCode, null, cancellationToken))
        {
            return Result<Student>.Fail(new ValidationError(
                nameof(StudentInput.StudentCode),
                $"MSSV '{normalizedCode}' đã tồn tại."));
        }

        var student = Student.Create(
            input.StudentCode ?? string.Empty,
            input.FullName,
            input.Email,
            input.DateOfBirth,
            input.Program,
            _clock.UtcNow,
            userId: null,
            clubId: clubId);

        await _repository.AddAsync(student, cancellationToken);
        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "student.create",
                "student",
                student.Id.ToString("D"),
                $"{student.StudentCode} · {student.FullName}"),
            cancellationToken);

        return Result<Student>.Ok(student);
    }

    public async Task<Result<Student>> UpdateAsync(
        Guid id,
        StudentInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return Result<Student>.Fail(validation.Errors);
        }

        var student = await _repository.FindByIdAsync(id, cancellationToken);
        if (student is null)
        {
            return Result<Student>.Fail(new ValidationError(string.Empty, "Không tìm thấy sinh viên."));
        }

        var normalizedCode = (input.StudentCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.Equals(student.StudentCode, normalizedCode, StringComparison.Ordinal))
        {
            return Result<Student>.Fail(new ValidationError(
                nameof(StudentInput.StudentCode),
                "Không được đổi MSSV sau khi đã tạo."));
        }

        student.UpdateProfile(input.FullName, input.Email, input.DateOfBirth, input.Program, _clock.UtcNow);
        await _repository.UpdateAsync(student, cancellationToken);
        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "student.update",
                "student",
                student.Id.ToString("D"),
                $"{student.StudentCode} · {student.FullName}"),
            cancellationToken);

        return Result<Student>.Ok(student);
    }

    public async Task<Result<Student>> LinkUserAsync(
        Guid studentId,
        Guid userId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var student = await _repository.FindByIdAsync(studentId, cancellationToken);
        if (student is null)
        {
            return Result<Student>.Fail(new ValidationError(string.Empty, "Không tìm thấy sinh viên."));
        }

        var existing = await _repository.FindByUserIdAsync(userId, cancellationToken);
        if (existing is not null && existing.Id != studentId)
        {
            return Result<Student>.Fail(new ValidationError(string.Empty, "Tài khoản đã liên kết với sinh viên khác."));
        }

        student.LinkUser(userId);
        await _repository.UpdateAsync(student, cancellationToken);

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "student.link_user",
                "student",
                student.Id.ToString("D"),
                $"userId={userId}"),
            cancellationToken);

        return Result<Student>.Ok(student);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var student = await _repository.FindByIdAsync(id, cancellationToken);
        if (student is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy sinh viên."));
        }

        var deleted = await _repository.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không xoá được sinh viên."));
        }

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "student.delete",
                "student",
                id.ToString("D"),
                $"{student.StudentCode} · {student.FullName}"),
            cancellationToken);

        return Result.Ok();
    }
}
