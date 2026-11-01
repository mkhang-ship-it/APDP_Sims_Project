using SIMS.Domain.Common;
using SIMS.Domain.Enrolments;

namespace SIMS.Tests.Domain;

public sealed class EnrolmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly Guid CourseId = Guid.NewGuid();

    [Fact]
    public void Create_sets_active_status()
    {
        var enrolment = Enrolment.Create(StudentId, CourseId, Now);

        Assert.Equal(StudentId, enrolment.StudentId);
        Assert.Equal(CourseId, enrolment.CourseId);
        Assert.Equal(EnrolmentStatus.Active, enrolment.Status);
        Assert.True(enrolment.IsActive);
        Assert.False(enrolment.HasGrade);
    }

    [Fact]
    public void Create_rejects_empty_ids()
    {
        Assert.Throws<DomainException>(() => Enrolment.Create(Guid.Empty, CourseId, Now));
        Assert.Throws<DomainException>(() => Enrolment.Create(StudentId, Guid.Empty, Now));
    }

    [Fact]
    public void AssignGrade_sets_score_letter_and_completed()
    {
        var enrolment = Enrolment.Create(StudentId, CourseId, Now);

        enrolment.AssignGrade(8.456m, "b");

        Assert.Equal(8.46m, enrolment.Score);
        Assert.Equal("B", enrolment.GradeLetter);
        Assert.Equal(EnrolmentStatus.Completed, enrolment.Status);
        Assert.True(enrolment.HasGrade);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(10.1)]
    public void AssignGrade_rejects_out_of_range_score(decimal score)
    {
        var enrolment = Enrolment.Create(StudentId, CourseId, Now);

        Assert.Throws<DomainException>(() => enrolment.AssignGrade(score, "A"));
    }

    [Fact]
    public void AssignGrade_rejects_dropped_enrolment()
    {
        var enrolment = Enrolment.Create(StudentId, CourseId, Now);
        enrolment.Drop();

        Assert.Throws<DomainException>(() => enrolment.AssignGrade(7m, "C"));
    }

    [Fact]
    public void Drop_sets_dropped_and_is_idempotent()
    {
        var enrolment = Enrolment.Create(StudentId, CourseId, Now);

        enrolment.Drop();
        enrolment.Drop();

        Assert.Equal(EnrolmentStatus.Dropped, enrolment.Status);
        Assert.False(enrolment.IsActive);
    }

    [Fact]
    public void IsPassed_requires_completed_and_threshold()
    {
        var enrolment = Enrolment.Create(StudentId, CourseId, Now);

        Assert.False(enrolment.IsPassed(5m));

        enrolment.AssignGrade(4.5m, "F");
        Assert.False(enrolment.IsPassed(5m));

        enrolment.AssignGrade(5m, "E");
        Assert.True(enrolment.IsPassed(5m));
    }

    [Fact]
    public void Restore_keeps_grade_data()
    {
        var id = Guid.NewGuid();

        var enrolment = Enrolment.Restore(id, StudentId, CourseId, EnrolmentStatus.Completed, Now, 9.25m, "A");

        Assert.Equal(id, enrolment.Id);
        Assert.Equal(9.25m, enrolment.Score);
        Assert.Equal("A", enrolment.GradeLetter);
        Assert.Equal(EnrolmentStatus.Completed, enrolment.Status);
    }
}
