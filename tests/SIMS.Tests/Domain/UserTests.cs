using SIMS.Domain.Common;
using SIMS.Domain.Security;

namespace SIMS.Tests.Domain;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_username()
    {
        var user = User.Create("  Admin.User  ", "Quản trị", Role.Admin, new string('x', 24), Now);

        Assert.Equal("admin.user", user.Username);
        Assert.Equal(Role.Admin, user.Role);
        Assert.True(user.IsActive);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("co khoang trang")]
    [InlineData("ky@tu")]
    public void Create_rejects_invalid_username(string username)
    {
        Assert.Throws<DomainException>(() => User.Create(username, "Quản trị", Role.Admin, new string('x', 24), Now));
    }

    [Fact]
    public void Create_rejects_empty_password_hash()
    {
        Assert.Throws<DomainException>(() => User.Create("admin", "Quản trị", Role.Admin, "short", Now));
    }

    [Fact]
    public void Deactivate_blocks_future_login_state()
    {
        var user = User.Create("admin", "Quản trị", Role.Admin, new string('x', 24), Now);

        user.Deactivate();

        Assert.False(user.IsActive);
    }
}
