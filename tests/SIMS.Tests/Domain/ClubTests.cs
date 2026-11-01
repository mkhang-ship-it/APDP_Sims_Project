using SIMS.Domain.Clubs;
using SIMS.Domain.Common;
using SIMS.Domain.Security;

namespace SIMS.Tests.Domain;

public sealed class ClubTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_code()
    {
        var club = Club.Create(" clb01 ", "  Câu lạc bộ Công nghệ  ", Now);

        Assert.Equal("CLB01", club.Code);
        Assert.Equal("Câu lạc bộ Công nghệ", club.Name);
        Assert.True(club.IsActive);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("CLB-01")]
    [InlineData("CLB0112345678901234567890")]
    public void Create_rejects_invalid_code(string code)
    {
        Assert.Throws<DomainException>(() => Club.Create(code, "Câu lạc bộ", Now));
    }

    [Fact]
    public void Create_rejects_invalid_name()
    {
        Assert.Throws<DomainException>(() => Club.Create("CLB01", "A", Now));
    }

    [Fact]
    public void Update_and_deactivate_work()
    {
        var club = Club.Create("CLB01", "CLB Cũ", Now);

        club.UpdateDetails("CLB Mới");
        club.Deactivate();

        Assert.Equal("CLB Mới", club.Name);
        Assert.False(club.IsActive);
    }
}

public sealed class ClubMembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_sets_active_membership()
    {
        var userId = Guid.NewGuid();
        var clubId = Guid.NewGuid();

        var membership = ClubMembership.Create(userId, clubId, Role.Faculty, Now);

        Assert.Equal(userId, membership.UserId);
        Assert.Equal(clubId, membership.ClubId);
        Assert.Equal(Role.Faculty, membership.Role);
        Assert.True(membership.IsActive);
    }

    [Fact]
    public void Create_rejects_empty_ids()
    {
        Assert.Throws<DomainException>(() => ClubMembership.Create(Guid.Empty, Guid.NewGuid(), Role.Student, Now));
        Assert.Throws<DomainException>(() => ClubMembership.Create(Guid.NewGuid(), Guid.Empty, Role.Student, Now));
    }

    [Fact]
    public void Restore_and_deactivate_work()
    {
        var id = Guid.NewGuid();

        var membership = ClubMembership.Restore(id, Guid.NewGuid(), Guid.NewGuid(), Role.Admin, false, Now);

        Assert.Equal(id, membership.Id);
        Assert.False(membership.IsActive);
    }
}
