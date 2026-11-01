using SIMS.Domain.Students;

namespace SIMS.Tests.Domain;

public sealed class StudentLinkingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static Student NewStudent() => Student.Create(
        "SV001", "Nguyễn Văn A", "a@example.com", new DateOnly(2004, 1, 1), "CNTT", Now);

    [Fact]
    public void Link_user_and_assign_club_are_set()
    {
        var student = NewStudent();
        var userId = Guid.NewGuid();
        var clubId = Guid.NewGuid();

        student.LinkUser(userId);
        student.AssignClub(clubId);

        Assert.Equal(userId, student.UserId);
        Assert.Equal(clubId, student.ClubId);
    }

    [Fact]
    public void Unlink_and_clear_club_reset_to_null()
    {
        var student = NewStudent();
        student.LinkUser(Guid.NewGuid());
        student.AssignClub(Guid.NewGuid());

        student.UnlinkUser();
        student.ClearClub();

        Assert.Null(student.UserId);
        Assert.Null(student.ClubId);
    }

    [Fact]
    public void Restore_keeps_user_and_club()
    {
        var userId = Guid.NewGuid();
        var clubId = Guid.NewGuid();

        var student = Student.Restore(
            Guid.NewGuid(), "SV001", "Nguyễn Văn A", "a@example.com",
            new DateOnly(2004, 1, 1), "CNTT", StudentStatus.Active, Now, userId, clubId);

        Assert.Equal(userId, student.UserId);
        Assert.Equal(clubId, student.ClubId);
    }
}
