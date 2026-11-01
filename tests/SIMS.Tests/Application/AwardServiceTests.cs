using System.Text;
using SIMS.Application.Awards;
using SIMS.Domain.Awards;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class AwardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 1);

    private sealed record Harness(
        AwardService Service,
        InMemoryAwardRepository Awards,
        InMemoryAwardRecipientRepository Recipients,
        InMemoryFileStorage Files,
        RecordingAuditLog Audit);

    private static Harness Create()
    {
        var awards = new InMemoryAwardRepository();
        var recipients = new InMemoryAwardRecipientRepository();
        var files = new InMemoryFileStorage();
        var audit = new RecordingAuditLog();
        var service = new AwardService(
            awards, recipients, files,
            new SequenceReferenceCodeGenerator(), audit, new FakeClock(Now));

        return new Harness(service, awards, recipients, files, audit);
    }

    [Fact]
    public async Task Create_and_update_award()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();

        var created = await harness.Service.CreateAsync(
            clubId, "Thành viên xuất sắc", "HK1", Today, AwardStatus.Pending, "Ghi chú", "faculty1");
        var updated = await harness.Service.UpdateAsync(
            created.Value!.Id, "Thành viên xuất sắc", "HK2", Today.AddDays(5),
            AwardStatus.Awarded, null, "faculty1");

        Assert.True(created.IsSuccess);
        Assert.Equal("BK-000001", created.Value.Code);
        Assert.True(updated.IsSuccess);
        Assert.Equal(AwardStatus.Awarded, updated.Value!.Status);
        Assert.Single(harness.Awards.All);
        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "award.update");
    }

    [Fact]
    public async Task Recipients_keep_attachment_and_reject_duplicates()
    {
        var harness = Create();
        var award = (await harness.Service.CreateAsync(
            Guid.NewGuid(), "Giải thưởng", "HK1", Today, AwardStatus.Awarded, null, "faculty1")).Value!;
        var userId = Guid.NewGuid();

        var added = await harness.Service.AddRecipientAsync(
            award.Id, userId, new MemoryStream(Encoding.UTF8.GetBytes("pdf")), "chung-nhan.pdf", "faculty1");
        var duplicate = await harness.Service.AddRecipientAsync(
            award.Id, userId, attachment: null, fileName: null, actor: "faculty1");
        var recipients = await harness.Service.ListRecipientsAsync(award.Id);

        Assert.True(added.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Single(recipients);
        Assert.Single(harness.Files.Files);
        Assert.False(string.IsNullOrWhiteSpace(recipients[0].Attachment));
    }

    [Fact]
    public async Task Remove_recipient_and_delete_award_remove_files()
    {
        var harness = Create();
        var award = (await harness.Service.CreateAsync(
            Guid.NewGuid(), "Giải thưởng", "HK1", Today, AwardStatus.Awarded, null, "faculty1")).Value!;
        var first = (await harness.Service.AddRecipientAsync(
            award.Id, Guid.NewGuid(), new MemoryStream(Encoding.UTF8.GetBytes("a")), "a.pdf", "faculty1")).Value!;
        await harness.Service.AddRecipientAsync(
            award.Id, Guid.NewGuid(), new MemoryStream(Encoding.UTF8.GetBytes("b")), "b.png", "faculty1");

        var removed = await harness.Service.RemoveRecipientAsync(award.Id, first.Id, "faculty1");
        var deleted = await harness.Service.DeleteAsync(award.Id, "faculty1");

        Assert.True(removed.IsSuccess);
        Assert.True(deleted.IsSuccess);
        Assert.Empty(harness.Awards.All);
        Assert.Empty(harness.Recipients.All);
        Assert.Empty(harness.Files.Files);
    }

    [Fact]
    public async Task Add_recipient_rejects_invalid_attachment_extension()
    {
        var harness = Create();
        var award = (await harness.Service.CreateAsync(
            Guid.NewGuid(), "Giải thưởng", "HK1", Today, AwardStatus.Awarded, null, "faculty1")).Value!;

        var rejected = await harness.Service.AddRecipientAsync(
            award.Id, Guid.NewGuid(), new MemoryStream(Encoding.UTF8.GetBytes("x")), "chung-nhan.exe", "faculty1");

        Assert.False(rejected.IsSuccess);
        Assert.Empty(harness.Recipients.All);
        Assert.Empty(harness.Files.Files);
    }

    [Fact]
    public async Task Lists_awards_by_club_with_recipients()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var first = (await harness.Service.CreateAsync(
            clubId, "Giải A", "HK1", Today, AwardStatus.Awarded, null, "faculty1")).Value!;
        await harness.Service.CreateAsync(
            Guid.NewGuid(), "Giải B", "HK1", Today, AwardStatus.Pending, null, "faculty1");
        await harness.Service.AddRecipientAsync(
            first.Id, Guid.NewGuid(), attachment: null, fileName: null, actor: "faculty1");

        var items = await harness.Service.ListAsync(clubId);

        Assert.Single(items);
        Assert.Single(items[0].Recipients);
    }
}
