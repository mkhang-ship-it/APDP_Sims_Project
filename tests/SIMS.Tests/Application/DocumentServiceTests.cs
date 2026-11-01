using System.Text;
using SIMS.Application.Documents;
using SIMS.Domain.Documents;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class DocumentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        DocumentService Service,
        InMemoryDocumentTypeRepository Types,
        InMemoryDocumentRepository Documents,
        InMemoryDocumentAccessRepository Access,
        InMemoryDocumentRequestUserRepository RequestUsers,
        InMemoryFileStorage Files,
        RecordingAuditLog Audit);

    private static Harness Create()
    {
        var types = new InMemoryDocumentTypeRepository();
        var documents = new InMemoryDocumentRepository();
        var access = new InMemoryDocumentAccessRepository();
        var requestUsers = new InMemoryDocumentRequestUserRepository();
        var files = new InMemoryFileStorage();
        var audit = new RecordingAuditLog();
        var service = new DocumentService(
            types,
            documents,
            access,
            requestUsers,
            files,
            new SequenceReferenceCodeGenerator(),
            audit,
            new FakeClock(Now));

        return new Harness(service, types, documents, access, requestUsers, files, audit);
    }

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<(Guid ClubId, DocumentType Type)> SeedTypeAsync(Harness harness)
    {
        var clubId = Guid.NewGuid();
        var created = await harness.Service.CreateTypeAsync(
            clubId, "Đơn xác nhận", "Mô tả", null, "faculty1");

        return (clubId, created.Value!);
    }

    [Fact]
    public async Task Create_type_generates_code_and_audits()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();

        var result = await harness.Service.CreateTypeAsync(
            clubId, "  Đơn xác nhận  ", "Mô tả", "https://example.com/mau", "faculty1");

        Assert.True(result.IsSuccess);
        Assert.Equal("DT-000001", result.Value!.Code);
        Assert.Equal("Đơn xác nhận", result.Value.Name);
        Assert.Single(harness.Types.All);
        Assert.Equal("document.type.create", harness.Audit.Entries[0].Action);
    }

    [Fact]
    public async Task Upload_creates_pending_document_and_approve_changes_status()
    {
        var harness = Create();
        var (clubId, type) = await SeedTypeAsync(harness);

        var upload = await harness.Service.UploadAsync(
            clubId, type.Id, "Đơn xin nghỉ", "student1", DocumentVisibility.Private,
            Content("nội dung"), "don.txt", "student1");

        Assert.True(upload.IsSuccess);
        Assert.Equal(DocumentStatus.Pending, upload.Value!.Status);
        Assert.Single(harness.Files.Files);

        var approve = await harness.Service.ReviewAsync(upload.Value.Id, true, null, "faculty1");

        Assert.True(approve.IsSuccess);
        Assert.Equal(DocumentStatus.Approved, approve.Value!.Status);
        Assert.Equal("faculty1", approve.Value.ReviewedBy);
        Assert.Contains(harness.Audit.Entries, entry => entry.Action == "document.approve");
    }

    [Fact]
    public async Task Upload_rejects_bad_extension_and_wrong_club()
    {
        var harness = Create();
        var (clubId, type) = await SeedTypeAsync(harness);

        var badExtension = await harness.Service.UploadAsync(
            clubId, type.Id, "Tệp lạ", "student1", DocumentVisibility.Private,
            Content("x"), "virus.exe", "student1");
        var wrongClub = await harness.Service.UploadAsync(
            Guid.NewGuid(), type.Id, "Sai CLB", "student1", DocumentVisibility.Private,
            Content("x"), "don.txt", "student1");

        Assert.False(badExtension.IsSuccess);
        Assert.False(wrongClub.IsSuccess);
        Assert.Empty(harness.Files.Files);
        Assert.Empty(harness.Documents.All);
    }

    [Fact]
    public async Task Restricted_document_requires_access_and_shows_in_accessible_list()
    {
        var harness = Create();
        var (clubId, type) = await SeedTypeAsync(harness);
        var upload = await harness.Service.UploadAsync(
            clubId, type.Id, "Tài liệu nội bộ", "faculty1", DocumentVisibility.Restricted,
            Content("x"), "noi-bo.pdf", "faculty1");
        var document = upload.Value!;
        await harness.Service.ReviewAsync(document.Id, true, null, "faculty1");
        var studentId = Guid.NewGuid();

        var before = await harness.Service.CanViewAsync(document, studentId, "student1");

        var grant = await harness.Service.SetAccessAsync(document.Id, new[] { studentId }, "faculty1");
        var after = await harness.Service.CanViewAsync(document, studentId, "student1");
        var accessible = await harness.Service
            .ListAccessibleAsync(clubId, studentId, "student1");

        Assert.True(grant.IsSuccess);
        Assert.False(before);
        Assert.True(after);
        Assert.Single(accessible);
    }

    [Fact]
    public async Task Delete_removes_file_and_access_but_type_with_documents_is_protected()
    {
        var harness = Create();
        var (clubId, type) = await SeedTypeAsync(harness);
        var upload = await harness.Service.UploadAsync(
            clubId, type.Id, "Tài liệu", "faculty1", DocumentVisibility.Private,
            Content("x"), "tai-lieu.pdf", "faculty1");
        var document = upload.Value!;
        await harness.Service.SetAccessAsync(document.Id, new[] { Guid.NewGuid() }, "faculty1");

        var deleteType = await harness.Service.DeleteTypeAsync(type.Id, "faculty1");
        Assert.False(deleteType.IsSuccess);

        var deleted = await harness.Service.DeleteAsync(document.Id, "faculty1");

        Assert.True(deleted.IsSuccess);
        Assert.Empty(harness.Documents.All);
        Assert.Empty(harness.Access.All);
        Assert.Empty(harness.Files.Files);

        var deleteTypeAfter = await harness.Service.DeleteTypeAsync(type.Id, "faculty1");
        Assert.True(deleteTypeAfter.IsSuccess);
    }

    [Fact]
    public async Task Lists_filter_by_status_type_and_uploader()
    {
        var harness = Create();
        var (clubId, type) = await SeedTypeAsync(harness);
        var first = await harness.Service.UploadAsync(
            clubId, type.Id, "A", "student1", DocumentVisibility.Private, Content("a"), "a.txt", "student1");
        await harness.Service.UploadAsync(
            clubId, type.Id, "B", "faculty1", DocumentVisibility.Private, Content("b"), "b.txt", "faculty1");
        await harness.Service.ReviewAsync(first.Value!.Id, true, null, "faculty1");

        var pending = await harness.Service.ListAsync(clubId, DocumentStatus.Pending);
        var byType = await harness.Service.ListAsync(clubId, null, type.Id);
        var mine = await harness.Service.ListMineAsync(clubId, "STUDENT1");

        Assert.Single(pending);
        Assert.Equal(2, byType.Count);
        Assert.Single(mine);
        Assert.Equal("A", mine[0].Document.Title);
    }
}
