using SIMS.Domain.Common;

namespace SIMS.Domain.Enrolments;

/// <summary>
/// Đăng ký học phần của một sinh viên. Mỗi cặp (StudentId, CourseId) chỉ có một bản ghi;
/// điểm 0..10 và xếp loại chữ do Strategy ở tầng Application quyết định.
/// </summary>
public sealed class Enrolment : Entity
{
    private Enrolment()
    {
    }

    public Guid StudentId { get; private set; }

    public Guid CourseId { get; private set; }

    public EnrolmentStatus Status { get; private set; }

    public DateTimeOffset EnrolledAtUtc { get; private set; }

    public decimal? Score { get; private set; }

    public string? GradeLetter { get; private set; }

    public bool IsActive => Status == EnrolmentStatus.Active;

    public bool HasGrade => Score.HasValue;

    public static Enrolment Create(Guid studentId, Guid courseId, DateTimeOffset enrolledAtUtc)
    {
        if (studentId == Guid.Empty)
        {
            throw new DomainException("Sinh viên không hợp lệ.");
        }

        if (courseId == Guid.Empty)
        {
            throw new DomainException("Học phần không hợp lệ.");
        }

        return new Enrolment
        {
            StudentId = studentId,
            CourseId = courseId,
            Status = EnrolmentStatus.Active,
            EnrolledAtUtc = enrolledAtUtc,
        };
    }

    public static Enrolment Restore(
        Guid id,
        Guid studentId,
        Guid courseId,
        EnrolmentStatus status,
        DateTimeOffset enrolledAtUtc,
        decimal? score,
        string? gradeLetter)
    {
        var enrolment = Create(studentId, courseId, enrolledAtUtc);
        enrolment.Id = id;
        enrolment.Status = status;
        enrolment.Score = score;
        enrolment.GradeLetter = gradeLetter;

        return enrolment;
    }

    public void AssignGrade(decimal score, string gradeLetter)
    {
        if (Status == EnrolmentStatus.Dropped)
        {
            throw new DomainException("Không thể nhập điểm cho đăng ký đã huỷ.");
        }

        if (score is < 0 or > 10)
        {
            throw new DomainException("Điểm phải trong khoảng 0..10.");
        }

        var letter = (gradeLetter ?? string.Empty).Trim();
        if (letter.Length is < 1 or > 2)
        {
            throw new DomainException("Xếp loại không hợp lệ.");
        }

        Score = decimal.Round(score, 2);
        GradeLetter = letter.ToUpperInvariant();
        Status = EnrolmentStatus.Completed;
    }

    public void Drop()
    {
        if (Status == EnrolmentStatus.Dropped)
        {
            return;
        }

        Status = EnrolmentStatus.Dropped;
    }

    /// <summary>Đã hoàn thành học phần với điểm đạt (dùng cho kiểm tra tiên quyết).</summary>
    public bool IsPassed(decimal passThreshold)
        => Status == EnrolmentStatus.Completed && Score.HasValue && Score.Value >= passThreshold;
}
