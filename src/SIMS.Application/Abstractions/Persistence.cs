using SIMS.Domain.Common;
using SIMS.Domain.Courses;
using SIMS.Domain.Enrolments;
using SIMS.Domain.Security;
using SIMS.Domain.Students;

namespace SIMS.Application.Abstractions;

/// <summary>Hợp đồng repository chung (LSP: mọi cài đặt — CSV, in-memory — thay thế được nhau).</summary>
public interface IRepository<T> where T : Entity
{
    Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default);

    Task<T?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>ISP: nơi chỉ đọc không phụ thuộc phần ghi.</summary>
public interface IStudentReader : IRepository<Student>
{
    Task<Student?> FindByCodeAsync(string studentCode, CancellationToken cancellationToken = default);

    Task<Student?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(
        string studentCode,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);
}

public interface IStudentWriter : IRepository<Student>
{
}

public interface IStudentRepository : IStudentReader, IStudentWriter
{
}

public interface ICourseRepository : IRepository<Course>
{
    Task<Course?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Kết quả thử ghi đăng ký (atomic: kiểm tra sức chứa + ghi trong cùng một khoá file).</summary>
public sealed record TryEnrolResult(bool Success, string? Error)
{
    public static TryEnrolResult Ok() => new(true, null);

    public static TryEnrolResult Fail(string error) => new(false, error);
}

public interface IEnrolmentRepository : IRepository<Enrolment>
{
    Task<IReadOnlyList<Enrolment>> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Enrolment>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default);

    Task<Enrolment?> FindByPairAsync(Guid studentId, Guid courseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra trùng + sức chứa và ghi trong cùng một giao dịch file.
    /// Nếu vi phạm: không ghi gì (rollback tự nhiên) và trả lỗi.
    /// </summary>
    Task<TryEnrolResult> TryAddAsync(
        Enrolment enrolment,
        int capacity,
        CancellationToken cancellationToken = default);
}

public interface IUserRepository
{
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(User user, CancellationToken cancellationToken = default);
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string encodedHash);
}

/// <summary>Một dòng audit ghi cho mọi thao tác ghi dữ liệu.</summary>
public sealed record AuditEntry(
    DateTimeOffset TimestampUtc,
    string Actor,
    string Action,
    string Entity,
    string EntityId,
    string Details);

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
