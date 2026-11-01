using SIMS.Domain.Awards;
using SIMS.Domain.Common;
using SIMS.Domain.Disciplines;
using SIMS.Domain.Documents;

namespace SIMS.Tests.Domain;

public sealed class GovernanceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Document_type_validates_code_and_trims_name()
    {
        var type = DocumentType.Create(Guid.NewGuid(), "DT-000123", "  Đơn xác nhận  ", "Mô tả", null, Now);

        Assert.Equal("Đơn xác nhận", type.Name);
        Assert.True(type.IsActive);
        Assert.Throws<DomainException>(() =>
            DocumentType.Create(Guid.NewGuid(), "DT-12", "Sai mã", null, null, Now));
        Assert.Throws<DomainException>(() =>
            DocumentType.Create(Guid.Empty, "DT-000123", "Hợp lệ", null, null, Now));
    }

    [Fact]
    public void Document_approve_and_reject_guard_reason()
    {
        var document = Document.Create(
            Guid.NewGuid(), Guid.NewGuid(), "student1", "Đơn xin nghỉ",
            "documents/a.pdf", DocumentVisibility.Private, DocumentStatus.Pending, Now);

        Assert.Throws<DomainException>(() => document.Reject("faculty1", "   ", Now));

        document.Reject("faculty1", "Thiếu chữ ký", Now);
        Assert.Equal(DocumentStatus.Rejected, document.Status);
        Assert.Equal("Thiếu chữ ký", document.ReasonRejected);

        document.Approve("faculty1", Now.AddMinutes(1));
        Assert.Equal(DocumentStatus.Approved, document.Status);
        Assert.Equal(string.Empty, document.ReasonRejected);
        Assert.Equal("faculty1", document.ReviewedBy);
        Assert.NotNull(document.ReviewedAtUtc);
    }

    [Fact]
    public void Document_request_user_can_submit_only_once()
    {
        var request = DocumentRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), "faculty1", new DateOnly(2026, 11, 10), Now);
        var assignment = DocumentRequestUser.Create(request.Id, Guid.NewGuid());

        assignment.Submit(Guid.NewGuid(), Now);

        Assert.True(assignment.IsSubmitted);
        Assert.Throws<DomainException>(() => assignment.Submit(Guid.NewGuid(), Now));
        Assert.False(request.IsExpired(new DateOnly(2026, 11, 9)));
        Assert.True(request.IsExpired(new DateOnly(2026, 11, 11)));
    }

    [Fact]
    public void Discipline_accepts_negative_points_and_rejects_out_of_range()
    {
        var discipline = Discipline.Create(Guid.NewGuid(), "KL-000001", "Vắng không phép", -5m, "Mô tả", Now);

        Assert.Equal(-5m, discipline.Points);
        Assert.Throws<DomainException>(() =>
            Discipline.Create(Guid.NewGuid(), "KL-000002", "Sai điểm", 1001m, null, Now));
        Assert.Throws<DomainException>(() =>
            Discipline.Create(Guid.NewGuid(), "KL-12", "Sai mã", 5m, null, Now));
    }

    [Fact]
    public void Discipline_record_revoke_blocks_update()
    {
        var record = DisciplineRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 20),
            "HK1", "faculty1", "Ghi chú", Now);

        record.Update(record.DisciplineId, new DateOnly(2026, 10, 21), "HK2", "Đã sửa");
        Assert.Equal("Đã sửa", record.Notes);

        record.Revoke();
        record.Revoke();
        Assert.False(record.IsActive);
        Assert.Throws<DomainException>(() =>
            record.Update(record.DisciplineId, new DateOnly(2026, 10, 22), null, null));
    }

    [Fact]
    public void Award_validates_status_and_recipient_attachment()
    {
        var award = Award.Create(
            Guid.NewGuid(), "BK-000001", "Thành viên xuất sắc", "HK1",
            new DateOnly(2026, 11, 1), AwardStatus.Pending, "Ghi chú", Now);

        award.Update("Thành viên xuất sắc", "HK1", new DateOnly(2026, 11, 2), AwardStatus.Awarded, null);
        Assert.Equal(AwardStatus.Awarded, award.Status);

        var recipient = AwardRecipient.Create(award.Id, Guid.NewGuid(), " awards/a.pdf ", Now);
        Assert.Equal("awards/a.pdf", recipient.Attachment);

        Assert.Throws<DomainException>(() =>
            Award.Create(Guid.NewGuid(), "BK-12", "Sai mã", null, new DateOnly(2026, 11, 1), AwardStatus.Pending, null, Now));
        Assert.Throws<DomainException>(() =>
            Award.Create(Guid.NewGuid(), "BK-000002", "Thiếu ngày", null, default, AwardStatus.Pending, null, Now));
    }
}
