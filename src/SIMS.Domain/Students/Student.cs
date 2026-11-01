using SIMS.Domain.Common;

namespace SIMS.Domain.Students;

/// <summary>
/// Sinh viên/học viên (module STUDENTS). Bất biến cốt lõi:
/// MSSV duy nhất, email hợp lệ, ngày sinh trong quá khứ, chương trình học bắt buộc.
/// </summary>
public sealed class Student : Entity
{
    private Student()
    {
        StudentCode = string.Empty;
        FullName = string.Empty;
        Email = string.Empty;
        Program = string.Empty;
    }

    /// <summary>MSSV — mã sinh viên, duy nhất toàn hệ thống.</summary>
    public string StudentCode { get; private set; }

    public string FullName { get; private set; }

    public string Email { get; private set; }

    public DateOnly DateOfBirth { get; private set; }

    public string Program { get; private set; }

    public StudentStatus Status { get; private set; }

    /// <summary>Tài khoản đăng nhập liên kết (nếu có) — cho phép vai trò Student xem hồ sơ của mình.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>CLB quản lý sinh viên (multi-club); null = chưa gán CLB.</summary>
    public Guid? ClubId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Student Create(
        string studentCode,
        string fullName,
        string email,
        DateOnly dateOfBirth,
        string program,
        DateTimeOffset createdAtUtc,
        Guid? userId = null,
        Guid? clubId = null)
    {
        return new Student
        {
            StudentCode = NormalizeCode(studentCode),
            FullName = NormalizeFullName(fullName),
            Email = NormalizeEmail(email),
            DateOfBirth = NormalizeDateOfBirth(dateOfBirth, createdAtUtc),
            Program = NormalizeProgram(program),
            Status = StudentStatus.Active,
            UserId = NormalizeOptionalId(userId),
            ClubId = NormalizeOptionalId(clubId),
            CreatedAtUtc = createdAtUtc,
        };
    }

    public static Student Restore(
        Guid id,
        string studentCode,
        string fullName,
        string email,
        DateOnly dateOfBirth,
        string program,
        StudentStatus status,
        DateTimeOffset createdAtUtc,
        Guid? userId = null,
        Guid? clubId = null)
    {
        var student = Create(studentCode, fullName, email, dateOfBirth, program, createdAtUtc, userId, clubId);
        student.Id = id;
        student.Status = status;

        return student;
    }

    public void LinkUser(Guid userId)
    {
        UserId = NormalizeOptionalId(userId) ?? throw new DomainException("Người dùng không hợp lệ.");
    }

    public void UnlinkUser() => UserId = null;

    public void AssignClub(Guid clubId)
    {
        ClubId = NormalizeOptionalId(clubId) ?? throw new DomainException("CLB không hợp lệ.");
    }

    public void ClearClub() => ClubId = null;

    public void UpdateProfile(string fullName, string email, DateOnly dateOfBirth, string program, DateTimeOffset updatedAtUtc)
    {
        FullName = NormalizeFullName(fullName);
        Email = NormalizeEmail(email);
        DateOfBirth = NormalizeDateOfBirth(dateOfBirth, updatedAtUtc);
        Program = NormalizeProgram(program);
    }

    public void Deactivate()
    {
        Status = StudentStatus.Inactive;
    }

    public void Activate()
    {
        Status = StudentStatus.Active;
    }

    private static string NormalizeCode(string studentCode)
    {
        var value = (studentCode ?? string.Empty).Trim().ToUpperInvariant();

        if (value.Length is < 3 or > 20)
        {
            throw new DomainException("MSSV phải có từ 3 đến 20 ký tự.");
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                throw new DomainException("MSSV chỉ được chứa chữ và số.");
            }
        }

        return value;
    }

    private static string NormalizeFullName(string fullName)
    {
        var value = (fullName ?? string.Empty).Trim();

        if (value.Length is < 2 or > 120)
        {
            throw new DomainException("Họ tên phải có từ 2 đến 120 ký tự.");
        }

        return value;
    }

    private static string NormalizeEmail(string email)
    {
        var value = (email ?? string.Empty).Trim().ToLowerInvariant();
        var atIndex = value.IndexOf('@');

        var isSingleAt = atIndex > 0 && atIndex == value.LastIndexOf('@');
        var hasDomainPart = isSingleAt && value.Length > atIndex + 3 && value[(atIndex + 1)..].Contains('.');
        var hasNoSpaces = !value.Contains(' ', StringComparison.Ordinal);

        if (!isSingleAt || !hasDomainPart || !hasNoSpaces || value.Length > 254)
        {
            throw new DomainException("Email không hợp lệ.");
        }

        return value;
    }

    private static DateOnly NormalizeDateOfBirth(DateOnly dateOfBirth, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        if (dateOfBirth == default)
        {
            throw new DomainException("Ngày sinh là bắt buộc.");
        }

        if (dateOfBirth >= today)
        {
            throw new DomainException("Ngày sinh phải trước ngày hiện tại.");
        }

        if (dateOfBirth.Year < 1900)
        {
            throw new DomainException("Ngày sinh không hợp lệ (trước năm 1900).");
        }

        return dateOfBirth;
    }

    private static string NormalizeProgram(string program)
    {
        var value = (program ?? string.Empty).Trim();

        if (value.Length is < 2 or > 120)
        {
            throw new DomainException("Chương trình học phải có từ 2 đến 120 ký tự.");
        }

        return value;
    }

    private static Guid? NormalizeOptionalId(Guid? id)
        => id is null || id.Value == Guid.Empty ? null : id;
}
