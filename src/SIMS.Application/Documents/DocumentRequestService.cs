using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Documents;

namespace SIMS.Application.Documents;

public interface IDocumentRequestService
{
    Task<Result<DocumentRequest>> CreateAsync(
        Guid clubId,
        Guid documentTypeId,
        DateOnly deadline,
        IReadOnlyCollection<Guid> userIds,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentRequestRow>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentRequestItem>> ListMineAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Người được giao nộp tệp cho yêu cầu; tạo văn bản ở trạng thái chờ duyệt.</summary>
    Task<Result<Document>> SubmitAsync(
        Guid requestUserId,
        Guid userId,
        string title,
        Stream content,
        string fileName,
        string actor,
        CancellationToken cancellationToken = default);
}

public sealed class DocumentRequestService : IDocumentRequestService
{
    private readonly IDocumentRequestRepository _requests;
    private readonly IDocumentRequestUserRepository _requestUsers;
    private readonly IDocumentTypeRepository _types;
    private readonly IDocumentService _documents;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public DocumentRequestService(
        IDocumentRequestRepository requests,
        IDocumentRequestUserRepository requestUsers,
        IDocumentTypeRepository types,
        IDocumentService documents,
        IAuditLog audit,
        IClock clock)
    {
        _requests = requests;
        _requestUsers = requestUsers;
        _types = types;
        _documents = documents;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DocumentRequest>> CreateAsync(
        Guid clubId,
        Guid documentTypeId,
        DateOnly deadline,
        IReadOnlyCollection<Guid> userIds,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return Fail("Chọn ít nhất một thành viên cần nộp.");
        }

        var type = await _types.FindByIdAsync(documentTypeId, cancellationToken).ConfigureAwait(false);
        if (type is null || type.ClubId != clubId)
        {
            return Fail("Loại văn bản không thuộc CLB đã chọn.");
        }

        if (deadline < DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime))
        {
            return Fail("Hạn nộp không được ở quá khứ.");
        }

        DocumentRequest request;
        try
        {
            request = DocumentRequest.Create(clubId, documentTypeId, actor, deadline, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _requests.AddAsync(request, cancellationToken).ConfigureAwait(false);

        foreach (var userId in userIds.Distinct().Where(userId => userId != Guid.Empty))
        {
            await _requestUsers
                .AddAsync(DocumentRequestUser.Create(request.Id, userId), cancellationToken)
                .ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow, actor, "document.request.create", "document_request",
                request.Id.ToString("D"), $"type={type.Code}; recipients={userIds.Count}"),
            cancellationToken).ConfigureAwait(false);

        return Result<DocumentRequest>.Ok(request);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var request = await _requests.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy yêu cầu."));
        }

        var assignments = await _requestUsers.ListByRequestAsync(id, cancellationToken).ConfigureAwait(false);
        if (assignments.Any(item => item.IsSubmitted))
        {
            return Result.Fail(new ValidationError(
                string.Empty,
                "Không thể xoá yêu cầu đã có người nộp văn bản."));
        }

        foreach (var assignment in assignments)
        {
            await _requestUsers.DeleteAsync(assignment.Id, cancellationToken).ConfigureAwait(false);
        }

        await _requests.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow, actor, "document.request.delete", "document_request",
                id.ToString("D"), string.Empty),
            cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<IReadOnlyList<DocumentRequestRow>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var requests = await _requests.ListByClubAsync(clubId, cancellationToken).ConfigureAwait(false);
        var types = (await _types.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(type => type.Id);
        var rows = new List<DocumentRequestRow>(requests.Count);

        foreach (var request in requests)
        {
            var recipients = await _requestUsers
                .ListByRequestAsync(request.Id, cancellationToken)
                .ConfigureAwait(false);

            rows.Add(new DocumentRequestRow(
                request,
                types.TryGetValue(request.DocumentTypeId, out var type) ? type : null,
                recipients));
        }

        return rows
            .OrderByDescending(row => row.Request.CreatedAtUtc)
            .ToArray();
    }

    public async Task<IReadOnlyList<DocumentRequestItem>> ListMineAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var assignments = await _requestUsers.ListByUserAsync(userId, cancellationToken).ConfigureAwait(false);
        var types = (await _types.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(type => type.Id);
        var items = new List<DocumentRequestItem>(assignments.Count);

        foreach (var assignment in assignments)
        {
            var request = await _requests
                .FindByIdAsync(assignment.DocumentRequestId, cancellationToken)
                .ConfigureAwait(false);

            if (request is null)
            {
                continue;
            }

            var submitted = assignment.DocumentId is null
                ? null
                : await _documents.GetAsync(assignment.DocumentId.Value, cancellationToken).ConfigureAwait(false);

            items.Add(new DocumentRequestItem(
                request,
                types.TryGetValue(request.DocumentTypeId, out var type) ? type : null,
                assignment,
                submitted));
        }

        return items
            .OrderByDescending(item => item.Request.CreatedAtUtc)
            .ToArray();
    }

    public async Task<Result<Document>> SubmitAsync(
        Guid requestUserId,
        Guid userId,
        string title,
        Stream content,
        string fileName,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _requestUsers
            .FindByIdAsync(requestUserId, cancellationToken)
            .ConfigureAwait(false);

        if (assignment is null || assignment.UserId != userId)
        {
            return FailDocument("Bạn không có quyền nộp cho yêu cầu này.");
        }

        if (assignment.IsSubmitted)
        {
            return FailDocument("Yêu cầu này đã được nộp.");
        }

        var request = await _requests
            .FindByIdAsync(assignment.DocumentRequestId, cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return FailDocument("Không tìm thấy yêu cầu.");
        }

        var upload = await _documents.UploadAsync(
            request.ClubId,
            request.DocumentTypeId,
            title,
            actor,
            DocumentVisibility.Private,
            content,
            fileName,
            actor,
            cancellationToken).ConfigureAwait(false);

        if (!upload.IsSuccess || upload.Value is null)
        {
            return Result<Document>.Fail(upload.Errors);
        }

        assignment.Submit(upload.Value.Id, _clock.UtcNow);
        await _requestUsers.UpdateAsync(assignment, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow, actor, "document.request.submit", "document_request_user",
                assignment.Id.ToString("D"), $"document={upload.Value.Id:D}"),
            cancellationToken).ConfigureAwait(false);

        return upload;
    }

    private static Result<DocumentRequest> Fail(string message)
        => Result<DocumentRequest>.Fail(new ValidationError(string.Empty, message));

    private static Result<Document> FailDocument(string message)
        => Result<Document>.Fail(new ValidationError(string.Empty, message));
}
