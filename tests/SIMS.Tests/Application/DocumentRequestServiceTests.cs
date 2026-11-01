using System.Text;
using SIMS.Application.Documents;
using SIMS.Domain.Documents;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class DocumentRequestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        DocumentRequestService Service,
        DocumentService Documents,
        InMemoryDocumentTypeRepository Types,
        InMemoryDocumentRequestRepository Requests,
        InMemoryDocumentRequestUserRepository RequestUsers,
        InMemoryDocumentRepository DocumentRepository);

    private static Harness Create()
    {
        var types = new InMemoryDocumentTypeRepository();
        var documents = new InMemoryDocumentRepository();
        var access = new InMemoryDocumentAccessRepository();
        var requestUsers = new InMemoryDocumentRequestUserRepository();
        var requests = new InMemoryDocumentRequestRepository();
        var files = new InMemoryFileStorage();
        var audit = new RecordingAuditLog();
        var clock = new FakeClock(Now);

        var documentService = new DocumentService(
            types, documents, access, requestUsers, files,
            new SequenceReferenceCodeGenerator(), audit, clock);

        var requestService = new DocumentRequestService(
            requests, requestUsers, types, documentService, audit, clock);

        return new Harness(requestService, documentService, types, requests, requestUsers, documents);
    }

    [Fact]
    public async Task Create_request_adds_recipients_and_validates()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var type = await harness.Documents.CreateTypeAsync(clubId, "Đơn xác nhận", null, null, "faculty1");
        var studentId = Guid.NewGuid();

        var created = await harness.Service.CreateAsync(
            clubId, type.Value!.Id, new DateOnly(2026, 11, 10), new[] { studentId }, "faculty1");
        var noMembers = await harness.Service.CreateAsync(
            clubId, type.Value.Id, new DateOnly(2026, 11, 10), Array.Empty<Guid>(), "faculty1");
        var pastDeadline = await harness.Service.CreateAsync(
            clubId, type.Value.Id, new DateOnly(2026, 10, 1), new[] { studentId }, "faculty1");

        Assert.True(created.IsSuccess);
        Assert.Single(harness.Requests.All);
        Assert.Single(harness.RequestUsers.All);
        Assert.False(noMembers.IsSuccess);
        Assert.False(pastDeadline.IsSuccess);
    }

    [Fact]
    public async Task Submit_creates_document_and_marks_assignment_processed()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var type = await harness.Documents.CreateTypeAsync(clubId, "Đơn xác nhận", null, null, "faculty1");
        var request = await harness.Service.CreateAsync(
            clubId, type.Value!.Id, new DateOnly(2026, 11, 10), new[] { studentId }, "faculty1");
        var requestId = request.Value!.Id;
        var assignment = harness.RequestUsers.All.Single();

        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("nội dung"));
        var submitted = await harness.Service.SubmitAsync(
            assignment.Id, studentId, "Đơn của tôi", content, "don.txt", "student1");
        var second = await harness.Service.SubmitAsync(
            assignment.Id, studentId, "Nộp lại", new MemoryStream(), "don.txt", "student1");
        var foreign = await harness.Service.SubmitAsync(
            assignment.Id, Guid.NewGuid(), "Người khác", new MemoryStream(), "don.txt", "other");

        Assert.True(submitted.IsSuccess);
        Assert.Equal(DocumentStatus.Pending, submitted.Value!.Status);
        Assert.True(assignment.IsSubmitted);
        Assert.Equal(submitted.Value.Id, assignment.DocumentId);
        Assert.False(second.IsSuccess);
        Assert.False(foreign.IsSuccess);

        var mine = await harness.Service.ListMineAsync(studentId);
        Assert.Single(mine);
        Assert.Equal(requestId, mine[0].Request.Id);
        Assert.Equal("Đơn xác nhận", mine[0].Type!.Name);
        Assert.NotNull(mine[0].SubmittedDocument);
    }

    [Fact]
    public async Task Delete_request_is_blocked_after_submission()
    {
        var harness = Create();
        var clubId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var type = await harness.Documents.CreateTypeAsync(clubId, "Đơn xác nhận", null, null, "faculty1");
        var request = await harness.Service.CreateAsync(
            clubId, type.Value!.Id, new DateOnly(2026, 11, 10), new[] { studentId }, "faculty1");
        var assignment = harness.RequestUsers.All.Single();

        var deletedBeforeSubmit = await harness.Service.DeleteAsync(request.Value!.Id, "faculty1");
        Assert.True(deletedBeforeSubmit.IsSuccess);

        var second = await harness.Service.CreateAsync(
            clubId, type.Value.Id, new DateOnly(2026, 11, 12), new[] { studentId }, "faculty1");
        var secondAssignment = harness.RequestUsers.All.Single();
        await harness.Service.SubmitAsync(
            secondAssignment.Id, studentId, "Đơn", new MemoryStream(), "don.txt", "student1");

        var deletedAfterSubmit = await harness.Service.DeleteAsync(second.Value!.Id, "faculty1");
        Assert.False(deletedAfterSubmit.IsSuccess);
        Assert.NotEqual(assignment.Id, secondAssignment.Id);
    }
}
