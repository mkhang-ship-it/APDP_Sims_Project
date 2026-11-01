using SIMS.Domain.Common;
using SIMS.Domain.Events;

namespace SIMS.Tests.Domain;

public sealed class EventTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static Event NewEvent(int capacity = 50)
        => Event.Create("EVT001", "Ngày hội công nghệ", "Mô tả", Now.AddHours(1), Now.AddHours(5), "Hội trường A", capacity, null, Now);

    [Fact]
    public void Create_starts_as_draft_with_token()
    {
        var @event = NewEvent();

        Assert.Equal(EventStatus.Draft, @event.Status);
        Assert.Equal(12, @event.CheckinToken.Length);
        Assert.False(@event.IsOpenForCheckin(Now));
    }

    [Fact]
    public void Create_rejects_invalid_window()
    {
        Assert.Throws<DomainException>(() => Event.Create(
            "EVT001", "Sự kiện", string.Empty, Now.AddHours(2), Now.AddHours(1), "A1", 10, null, Now));
    }

    [Fact]
    public void Publish_opens_checkin_window()
    {
        var @event = NewEvent();
        @event.Publish();

        Assert.Equal(EventStatus.Published, @event.Status);
        Assert.True(@event.IsOpenForCheckin(Now.AddHours(1)));
        Assert.False(@event.IsOpenForCheckin(Now.AddHours(-3)));
        Assert.False(@event.IsOpenForCheckin(Now.AddHours(6)));
    }

    [Fact]
    public void Cancel_and_complete_follow_status_rules()
    {
        var @event = NewEvent();
        @event.Publish();
        @event.Complete();

        Assert.Equal(EventStatus.Completed, @event.Status);
        Assert.Throws<DomainException>(() => @event.Cancel());
        Assert.Throws<DomainException>(() => @event.Publish());
    }

    [Fact]
    public void Rotate_token_changes_value()
    {
        var @event = NewEvent();
        var original = @event.CheckinToken;

        @event.RotateCheckinToken();

        Assert.NotEqual(original, @event.CheckinToken);
    }

    [Fact]
    public void Restore_keeps_status_token_and_club()
    {
        var id = Guid.NewGuid();
        var clubId = Guid.NewGuid();

        var @event = Event.Restore(
            id, "EVT009", "Sự kiện", "Mô tả", Now, Now.AddHours(2), "A1", 20,
            EventStatus.Published, clubId, "ABCDEF123456", Now);

        Assert.Equal(id, @event.Id);
        Assert.Equal(EventStatus.Published, @event.Status);
        Assert.Equal("ABCDEF123456", @event.CheckinToken);
        Assert.Equal(clubId, @event.ClubId);
    }
}

public sealed class AttendanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Qr")]
    [InlineData("Link")]
    [InlineData("Manual")]
    public void Create_accepts_supported_methods(string method)
    {
        var attendance = Attendance.Create(Guid.NewGuid(), Guid.NewGuid(), method, Now);

        Assert.Equal(method, attendance.Method);
    }

    [Fact]
    public void Create_rejects_invalid_input()
    {
        Assert.Throws<DomainException>(() => Attendance.Create(Guid.Empty, Guid.NewGuid(), "Qr", Now));
        Assert.Throws<DomainException>(() => Attendance.Create(Guid.NewGuid(), Guid.Empty, "Qr", Now));
        Assert.Throws<DomainException>(() => Attendance.Create(Guid.NewGuid(), Guid.NewGuid(), "Fax", Now));
    }
}
