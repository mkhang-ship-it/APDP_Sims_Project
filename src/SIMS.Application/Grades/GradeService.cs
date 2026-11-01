using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Enrolments;

namespace SIMS.Application.Grades;

/// <summary>Strategy: quy đổi điểm số sang xếp loại và kết luận đạt/không đạt.</summary>
public sealed record GradeResult(string Letter, bool Passed, decimal Score);

public interface IGradePolicy
{
    GradeResult Evaluate(decimal score);
}

/// <summary>Thang điểm 10: A ≥ 9, B ≥ 8, C ≥ 7, D ≥ 6, E ≥ 5 (đạt), F &lt; 5 (không đạt).</summary>
public sealed class TenPointGradePolicy : IGradePolicy
{
    public const decimal PassThreshold = 5m;

    public GradeResult Evaluate(decimal score)
    {
        if (score is < 0 or > 10)
        {
            throw new DomainException("Điểm phải trong khoảng 0..10.");
        }

        var letter = score switch
        {
            >= 9m => "A",
            >= 8m => "B",
            >= 7m => "C",
            >= 6m => "D",
            >= 5m => "E",
            _ => "F",
        };

        return new GradeResult(letter, score >= PassThreshold, decimal.Round(score, 2));
    }
}

public interface IGradeService
{
    Task<Result<Enrolment>> AssignAsync(
        Guid enrolmentId,
        decimal score,
        string actor,
        CancellationToken cancellationToken = default);
}

public sealed class GradeService : IGradeService
{
    private readonly IEnrolmentRepository _enrolments;
    private readonly IGradePolicy _policy;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public GradeService(
        IEnrolmentRepository enrolments,
        IGradePolicy policy,
        IAuditLog audit,
        IClock clock)
    {
        _enrolments = enrolments;
        _policy = policy;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<Enrolment>> AssignAsync(
        Guid enrolmentId,
        decimal score,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var enrolment = await _enrolments.FindByIdAsync(enrolmentId, cancellationToken);
        if (enrolment is null)
        {
            return Result<Enrolment>.Fail(new ValidationError(string.Empty, "Không tìm thấy đăng ký."));
        }

        GradeResult grade;
        try
        {
            grade = _policy.Evaluate(score);
            enrolment.AssignGrade(grade.Score, grade.Letter);
        }
        catch (DomainException ex)
        {
            return Result<Enrolment>.Fail(new ValidationError(nameof(score), ex.Message));
        }

        await _enrolments.UpdateAsync(enrolment, cancellationToken);

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "grade.assign",
                "enrolment",
                enrolment.Id.ToString("D"),
                $"score={grade.Score} letter={grade.Letter} passed={grade.Passed}"),
            cancellationToken);

        return Result<Enrolment>.Ok(enrolment);
    }
}
