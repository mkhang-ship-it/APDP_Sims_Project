using SIMS.Application.LuckyDraws;
using SIMS.Domain.LuckyDraws;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class LuckyDrawServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        LuckyDrawService Service,
        InMemoryLuckyDrawRepository Draws,
        InMemoryLuckyPrizeRepository Prizes,
        InMemoryLuckyWinnerRepository Winners,
        RecordingAuditLog Audit);

    private static Harness Create()
    {
        var draws = new InMemoryLuckyDrawRepository();
        var prizes = new InMemoryLuckyPrizeRepository();
        var winners = new InMemoryLuckyWinnerRepository();
        var audit = new RecordingAuditLog();
        var service = new LuckyDrawService(draws, prizes, winners, audit, new FakeClock(Now));

        return new Harness(service, draws, prizes, winners, audit);
    }

    private static async Task<LuckyDraw> CreateDrawAsync(
        Harness harness,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        params string[] participants)
    {
        var result = await harness.Service.CreateAsync(
            Guid.NewGuid(),
            "Bốc thăm tháng 11",
            "123456",
            participants.Length == 0 ? new[] { "An", "Bình" } : participants,
            start ?? Now.AddHours(-1),
            end ?? Now.AddHours(3),
            isActive: true,
            "faculty1");

        return result.Value!;
    }

    [Fact]
    public async Task Create_generates_slug_and_audits()
    {
        var harness = Create();

        var result = await harness.Service.CreateAsync(
            Guid.NewGuid(),
            "Bốc thăm may mắn",
            "123456",
            new[] { "An", "Bình", "an" },
            Now.AddHours(-1),
            Now.AddHours(2),
            true,
            "faculty1");

        Assert.True(result.IsSuccess);
        Assert.StartsWith("boc-tham-may-man-", result.Value!.Slug, StringComparison.Ordinal);
        Assert.Equal(2, result.Value.ParticipantList.Count);
        Assert.Equal("lucky.create", harness.Audit.Entries[0].Action);
        Assert.Single(harness.Draws.All);
    }

    [Fact]
    public async Task Prize_rules_and_duplicate_names()
    {
        var harness = Create();
        var draw = await CreateDrawAsync(harness);

        var added = await harness.Service.AddPrizeAsync(draw.Id, "Giải nhất", 1, true, "faculty1");
        var duplicate = await harness.Service.AddPrizeAsync(draw.Id, "giải nhất", 2, false, "faculty1");
        var updated = await harness.Service.UpdatePrizeAsync(
            added.Value!.Id, "Giải nhất", 2, true, "faculty1");

        Assert.True(added.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(2, updated.Value!.Quantity);
        Assert.Single(harness.Prizes.All);
    }

    [Fact]
    public async Task Draw_picks_unique_participants_and_stops_when_prize_is_full()
    {
        var harness = Create();
        var draw = await CreateDrawAsync(harness);
        var prize = (await harness.Service.AddPrizeAsync(draw.Id, "Giải nhất", 1, true, "faculty1")).Value!;

        var first = await harness.Service.DrawAsync(draw.Id, prize.Id, "faculty1");
        var second = await harness.Service.DrawAsync(draw.Id, prize.Id, "faculty1");

        Assert.True(first.IsSuccess);
        Assert.Contains(first.Value!.Participant, draw.ParticipantList);
        Assert.False(second.IsSuccess);
        Assert.Contains("hết lượt", second.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(harness.Winners.All);

        var consolation = (await harness.Service.AddPrizeAsync(
            draw.Id, "Giải khuyến khích", 5, false, "faculty1")).Value!;
        var other = await harness.Service.DrawAsync(draw.Id, consolation.Id, "faculty1");

        Assert.True(other.IsSuccess);
        Assert.NotEqual(first.Value.Participant, other.Value!.Participant);
    }

    [Fact]
    public async Task Draw_requires_open_window_and_available_participants()
    {
        var harness = Create();
        var closed = await CreateDrawAsync(
            harness, Now.AddHours(-5), Now.AddHours(-1), "An");
        var prize = (await harness.Service.AddPrizeAsync(
            closed.Id, "Giải", 5, false, "faculty1")).Value!;

        var closedDraw = await harness.Service.DrawAsync(closed.Id, prize.Id, "faculty1");

        var open = await CreateDrawAsync(harness, participants: "An");
        var openPrize = (await harness.Service.AddPrizeAsync(
            open.Id, "Giải", 5, false, "faculty1")).Value!;
        await harness.Service.DrawAsync(open.Id, openPrize.Id, "faculty1");
        var exhausted = await harness.Service.DrawAsync(open.Id, openPrize.Id, "faculty1");

        Assert.False(closedDraw.IsSuccess);
        Assert.Contains("chưa mở", closedDraw.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(exhausted.IsSuccess);
        Assert.Contains("Không còn người", exhausted.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clear_winners_and_delete_prize_rules()
    {
        var harness = Create();
        var draw = await CreateDrawAsync(harness);
        var prize = (await harness.Service.AddPrizeAsync(draw.Id, "Giải nhất", 1, true, "faculty1")).Value!;
        await harness.Service.DrawAsync(draw.Id, prize.Id, "faculty1");

        var deleteUsed = await harness.Service.DeletePrizeAsync(prize.Id, "faculty1");
        var cleared = await harness.Service.ClearWinnersAsync(draw.Id, "faculty1");
        var deleted = await harness.Service.DeletePrizeAsync(prize.Id, "faculty1");

        Assert.False(deleteUsed.IsSuccess);
        Assert.Equal(1, cleared.Value);
        Assert.True(deleted.IsSuccess);
        Assert.Empty(harness.Winners.All);
        Assert.Empty(harness.Prizes.All);
    }

    [Fact]
    public async Task Update_and_delete_draw_cascade_prizes_and_winners()
    {
        var harness = Create();
        var draw = await CreateDrawAsync(harness);
        var prize = (await harness.Service.AddPrizeAsync(draw.Id, "Giải", 3, false, "faculty1")).Value!;
        await harness.Service.DrawAsync(draw.Id, prize.Id, "faculty1");

        var updated = await harness.Service.UpdateAsync(
            draw.Id, "Bốc thăm mới", null, new[] { "An", "Bình", "Cường" },
            Now.AddHours(-1), Now.AddHours(5), true, "faculty1");
        var deleted = await harness.Service.DeleteAsync(draw.Id, "faculty1");
        var publicView = await harness.Service.GetPublicAsync(draw.Slug);

        Assert.True(updated.IsSuccess);
        Assert.Equal(3, updated.Value!.ParticipantList.Count);
        Assert.True(deleted.IsSuccess);
        Assert.Empty(harness.Draws.All);
        Assert.Empty(harness.Prizes.All);
        Assert.Empty(harness.Winners.All);
        Assert.Null(publicView);
    }
}
