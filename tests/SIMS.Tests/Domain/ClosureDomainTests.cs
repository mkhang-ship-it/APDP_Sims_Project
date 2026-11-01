using SIMS.Domain.Common;
using SIMS.Domain.Events;
using SIMS.Domain.FeatureRequests;
using SIMS.Domain.Security;
using SIMS.Domain.ShortUrls;

namespace SIMS.Tests.Domain;

public sealed class ClosureDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Short_url_validates_code_target_and_expiry()
    {
        var url = ShortUrl.Create(
            Guid.NewGuid(), "Abc123", "https://example.com/path", "Trang", Now.AddDays(1), "faculty1", Now);

        Assert.Equal("Abc123", url.Code);
        Assert.False(url.IsExpired(Now));
        Assert.True(url.IsExpired(Now.AddDays(2)));

        Assert.Throws<DomainException>(() => ShortUrl.Create(
            Guid.NewGuid(), "a", "https://example.com", null, null, "x", Now));
        Assert.Throws<DomainException>(() => ShortUrl.Create(
            Guid.NewGuid(), "Abc123", "ftp://example.com", null, null, "x", Now));
        Assert.Throws<DomainException>(() => ShortUrl.Create(
            Guid.Empty, "Abc123", "https://example.com", null, null, "x", Now));
    }

    [Fact]
    public void Short_url_visit_truncates_long_values()
    {
        var visit = ShortUrlVisit.Create(Guid.NewGuid(), Now, new string('1', 100), new string('u', 400));

        Assert.Equal(64, visit.IpAddress.Length);
        Assert.Equal(300, visit.UserAgent.Length);
    }

    [Fact]
    public void Feature_request_content_and_status_rules()
    {
        var request = FeatureRequest.Create(Guid.NewGuid(), "Đề xuất", "Nội dung", "student1", Now);

        request.UpdateStatus(FeatureRequestStatus.InProgress, "Đang xem xét", Now.AddHours(1));
        Assert.Equal(FeatureRequestStatus.InProgress, request.Status);
        Assert.Equal("Đang xem xét", request.Response);
        Assert.Throws<DomainException>(() => request.UpdateContent("Sửa", "Nội dung", Now));

        Assert.Throws<DomainException>(() =>
            FeatureRequest.Create(null, " ", "Nội dung", "student1", Now));
        Assert.Throws<DomainException>(() =>
            FeatureRequest.Create(null, "Đề xuất", " ", "student1", Now));
    }

    [Fact]
    public void User_profile_and_event_image_validation()
    {
        var user = User.Create("student1", "Tên cũ", Role.Student, new string('h', 32), Now);
        user.UpdateProfile("Tên mới", "new@example.com");

        Assert.Equal("Tên mới", user.DisplayName);
        Assert.Equal("new@example.com", user.Email);
        Assert.Throws<DomainException>(() => user.UpdateProfile(" ", null));
        Assert.Throws<DomainException>(() => user.UpdateProfile("Tên", "khong-hop-le"));

        var image = EventImage.Create(Guid.NewGuid(), "events/a.png", "Ảnh", "faculty1", Now);
        Assert.Equal("Ảnh", image.Caption);
        Assert.Throws<DomainException>(() => EventImage.Create(Guid.Empty, "events/a.png", null, "x", Now));
        Assert.Throws<DomainException>(() =>
            EventImage.Create(Guid.NewGuid(), "events/a.png", new string('c', 201), "x", Now));
    }
}
