using SIMS.Domain.Common;

namespace SIMS.Domain.Courses;

/// <summary>
/// Học phần. Bất biến: mã duy nhất (kiểm tra ở repository/service), sức chứa 1..200,
/// mã học phần tiên quyết (nếu có) khác chính nó.
/// </summary>
public sealed class Course : Entity
{
    private Course()
    {
        Code = string.Empty;
        Title = string.Empty;
    }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public int Capacity { get; private set; }

    public string? PrerequisiteCode { get; private set; }

    public CourseStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool HasPrerequisite => !string.IsNullOrWhiteSpace(PrerequisiteCode);

    public static Course Create(
        string code,
        string title,
        int capacity,
        string? prerequisiteCode,
        DateTimeOffset createdAtUtc)
    {
        var normalizedCode = NormalizeCode(code);

        return new Course
        {
            Code = normalizedCode,
            Title = NormalizeTitle(title),
            Capacity = NormalizeCapacity(capacity),
            PrerequisiteCode = NormalizePrerequisite(prerequisiteCode, normalizedCode),
            Status = CourseStatus.Active,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public static Course Restore(
        Guid id,
        string code,
        string title,
        int capacity,
        string? prerequisiteCode,
        CourseStatus status,
        DateTimeOffset createdAtUtc)
    {
        var course = Create(code, title, capacity, prerequisiteCode, createdAtUtc);
        course.Id = id;
        course.Status = status;

        return course;
    }

    public void UpdateDetails(string title, int capacity, string? prerequisiteCode)
    {
        Title = NormalizeTitle(title);
        Capacity = NormalizeCapacity(capacity);
        PrerequisiteCode = NormalizePrerequisite(prerequisiteCode, Code);
    }

    public void Archive()
    {
        Status = CourseStatus.Archived;
    }

    private static string NormalizeCode(string code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (value.Length is < 2 or > 20)
        {
            throw new DomainException("Mã học phần phải có từ 2 đến 20 ký tự.");
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                throw new DomainException("Mã học phần chỉ được chứa chữ và số.");
            }
        }

        return value;
    }

    private static string NormalizeTitle(string title)
    {
        var value = (title ?? string.Empty).Trim();

        if (value.Length is < 2 or > 160)
        {
            throw new DomainException("Tên học phần phải có từ 2 đến 160 ký tự.");
        }

        return value;
    }

    private static int NormalizeCapacity(int capacity)
    {
        if (capacity is < 1 or > 200)
        {
            throw new DomainException("Sức chứa học phần phải từ 1 đến 200.");
        }

        return capacity;
    }

    private static string? NormalizePrerequisite(string? prerequisiteCode, string courseCode)
    {
        var value = (prerequisiteCode ?? string.Empty).Trim().ToUpperInvariant();

        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length is < 2 or > 20)
        {
            throw new DomainException("Mã học phần tiên quyết phải có từ 2 đến 20 ký tự.");
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                throw new DomainException("Mã học phần tiên quyết chỉ được chứa chữ và số.");
            }
        }

        if (string.Equals(value, courseCode, StringComparison.Ordinal))
        {
            throw new DomainException("Học phần không thể là tiên quyết của chính nó.");
        }

        return value;
    }
}
