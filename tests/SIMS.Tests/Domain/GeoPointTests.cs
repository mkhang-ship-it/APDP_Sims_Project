using SIMS.Domain.Common;
using SIMS.Domain.Events;

namespace SIMS.Tests.Domain;

public sealed class GeoPointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Distance_is_zero_for_same_point_and_matches_known_city_pair()
    {
        var hcmc = new GeoPoint(10.7769, 106.7009);
        var thuDuc = new GeoPoint(10.8231, 106.6297);

        Assert.Equal(0d, hcmc.DistanceKmTo(hcmc), 5);
        Assert.InRange(hcmc.DistanceKmTo(thuDuc), 9d, 11d);
        Assert.False(hcmc.IsWithin(thuDuc));
        Assert.True(hcmc.IsWithin(new GeoPoint(10.78, 106.70)));
    }

    [Fact]
    public void Distance_between_hanoi_and_hcmc_is_about_1140_km()
    {
        var hanoi = new GeoPoint(21.0278, 105.8342);
        var hcmc = new GeoPoint(10.7769, 106.7009);

        Assert.InRange(hanoi.DistanceKmTo(hcmc), 1100d, 1180d);
        Assert.False(hanoi.IsWithin(hcmc));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    public void Invalid_coordinates_are_rejected(double latitude, double longitude)
    {
        Assert.Throws<DomainException>(() => new GeoPoint(latitude, longitude));
    }

    [Fact]
    public void Event_keeps_venue_through_update_and_restore()
    {
        var venue = new GeoPoint(10.7769, 106.7009);
        var @event = Event.Create(
            "EVT001", "Ngày hội", "Mô tả", Now, Now.AddHours(3), "Hội trường A", 50, null, Now, venue);

        Assert.Equal(venue, @event.Venue);

        @event.UpdateDetails("Ngày hội", "Mô tả mới", Now, Now.AddHours(4), "Hội trường B", 60);
        Assert.Null(@event.Venue);

        var restored = Event.Restore(
            @event.Id, @event.Code, @event.Title, @event.Description, @event.StartsAtUtc, @event.EndsAtUtc,
            @event.Location, @event.Capacity, @event.Status, @event.ClubId, @event.CheckinToken,
            @event.CreatedAtUtc, venue);

        Assert.Equal(venue, restored.Venue);
        Assert.Equal(60, restored.Capacity);
    }

    [Fact]
    public void Attendance_keeps_checkin_location_and_distance()
    {
        var location = new GeoPoint(10.7769, 106.7009);
        var attendance = Attendance.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Qr", Now, location, 1.25);

        Assert.Equal(location, attendance.CheckinLocation);
        Assert.Equal(1.25, attendance.DistanceKm);

        Assert.Throws<DomainException>(() => Attendance.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Qr", Now, location, -1));
    }

    [Fact]
    public void Plan_details_are_normalized_and_validated()
    {
        var plan = EventPlan.Create(Guid.NewGuid(), "  Tóm tắt  ", Now);

        plan.UpdateDetails(
            "Kế hoạch mới",
            "  Ban truyền thông ",
            new DateOnly(2026, 11, 15),
            " Ban giám hiệu ",
            " Sinh viên năm 1 ",
            " Tổ chức tại hội trường ",
            " CLB Công nghệ ",
            " Cố vấn học tập ",
            Now.AddMinutes(5));

        Assert.Equal("Kế hoạch mới", plan.Summary);
        Assert.Equal("Ban truyền thông", plan.PreparedBy);
        Assert.Equal(new DateOnly(2026, 11, 15), plan.ImplementationDate);
        Assert.Equal("Ban giám hiệu", plan.ApprovedBy);
        Assert.Equal("Cố vấn học tập", plan.AdvisorCoordinator);
        Assert.Equal(Now.AddMinutes(5), plan.UpdatedAtUtc);

        Assert.Throws<DomainException>(() => plan.UpdateDetails(
            new string('x', 2001), null, null, null, null, null, null, null, Now));
    }
}
