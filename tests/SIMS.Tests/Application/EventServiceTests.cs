using SIMS.Application.Events;
using SIMS.Domain.Events;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class EventServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static EventService Create(out InMemoryEventRepository events, out RecordingAuditLog audit)
    {
        events = new InMemoryEventRepository();
        audit = new RecordingAuditLog();

        return new EventService(events, new EventValidator(), audit, new FakeClock(Now));
    }

    private static EventInput ValidInput(string code = "EVT001") => new(
        code,
        "Ngày hội công nghệ",
        "Mô tả",
        Now.AddHours(1),
        Now.AddHours(5),
        "Hội trường A",
        50);

    [Fact]
    public async Task Create_persists_draft_event_and_audits()
    {
        var service = Create(out var events, out var audit);

        var result = await service.CreateAsync(ValidInput(), Guid.NewGuid(), "faculty1");

        Assert.True(result.IsSuccess);
        Assert.Equal(EventStatus.Draft, result.Value!.Status);
        Assert.Single(events.All);
        Assert.Equal("event.create", audit.Entries[0].Action);
    }

    [Fact]
    public async Task Create_rejects_duplicate_code_and_invalid_window()
    {
        var service = Create(out _, out _);
        await service.CreateAsync(ValidInput(), null, "faculty1");

        var duplicate = await service.CreateAsync(ValidInput("evt001"), null, "faculty1");
        var invalidWindow = await service.CreateAsync(
            new EventInput("EVT002", "Sai giờ", string.Empty, Now.AddHours(5), Now.AddHours(1), "A", 10),
            null,
            "faculty1");

        Assert.False(duplicate.IsSuccess);
        Assert.False(invalidWindow.IsSuccess);
    }

    [Fact]
    public async Task Publish_and_rotate_change_state()
    {
        var service = Create(out _, out var audit);
        var created = await service.CreateAsync(ValidInput(), null, "faculty1");
        var originalToken = created.Value!.CheckinToken;

        var published = await service.PublishAsync(created.Value.Id, "faculty1");
        var rotated = await service.RotateCheckinTokenAsync(created.Value.Id, "faculty1");

        Assert.True(published.IsSuccess);
        Assert.True(rotated.IsSuccess);
        Assert.NotEqual(originalToken, rotated.Value);
        Assert.Contains(audit.Entries, e => e.Action == "event.publish");
        Assert.Contains(audit.Entries, e => e.Action == "event.checkin.rotate");
    }

    [Fact]
    public async Task Update_rejects_code_change_and_missing_event()
    {
        var service = Create(out _, out _);
        var created = await service.CreateAsync(ValidInput(), null, "faculty1");

        var changed = await service.UpdateAsync(
            created.Value!.Id,
            new EventInput("EVT999", "Khác", string.Empty, Now.AddHours(1), Now.AddHours(2), "A", 10),
            "faculty1");

        var missing = await service.UpdateAsync(
            Guid.NewGuid(),
            ValidInput(),
            "faculty1");

        Assert.False(changed.IsSuccess);
        Assert.False(missing.IsSuccess);
    }
}
