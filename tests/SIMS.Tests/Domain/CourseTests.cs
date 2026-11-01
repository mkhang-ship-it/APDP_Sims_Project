using SIMS.Domain.Common;
using SIMS.Domain.Courses;

namespace SIMS.Tests.Domain;

public sealed class CourseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_code_and_keeps_capacity()
    {
        var course = Course.Create("  cs101 ", "  Nhập môn lập trình  ", 30, null, Now);

        Assert.Equal("CS101", course.Code);
        Assert.Equal("Nhập môn lập trình", course.Title);
        Assert.Equal(30, course.Capacity);
        Assert.Null(course.PrerequisiteCode);
        Assert.Equal(CourseStatus.Active, course.Status);
        Assert.False(course.HasPrerequisite);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public void Create_rejects_invalid_capacity(int capacity)
    {
        Assert.Throws<DomainException>(() => Course.Create("CS101", "Nhập môn", capacity, null, Now));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("CS-101")]
    [InlineData("CS1011234567890123456")]
    public void Create_rejects_invalid_code(string code)
    {
        Assert.Throws<DomainException>(() => Course.Create(code, "Nhập môn", 30, null, Now));
    }

    [Fact]
    public void Create_rejects_self_prerequisite()
    {
        Assert.Throws<DomainException>(() => Course.Create("CS101", "Nhập môn", 30, "cs101", Now));
    }

    [Fact]
    public void Update_details_changes_fields_but_keeps_code()
    {
        var course = Course.Create("CS101", "Nhập môn", 30, null, Now);

        course.UpdateDetails("Nhập môn lập trình", 45, "MA101");

        Assert.Equal("CS101", course.Code);
        Assert.Equal(45, course.Capacity);
        Assert.Equal("MA101", course.PrerequisiteCode);
        Assert.True(course.HasPrerequisite);
    }

    [Fact]
    public void Restore_keeps_id_and_status()
    {
        var id = Guid.NewGuid();

        var course = Course.Restore(id, "cs102", "Cấu trúc dữ liệu", 20, "CS101", CourseStatus.Archived, Now);

        Assert.Equal(id, course.Id);
        Assert.Equal("CS102", course.Code);
        Assert.Equal(CourseStatus.Archived, course.Status);
        Assert.Equal("CS101", course.PrerequisiteCode);
    }
}
