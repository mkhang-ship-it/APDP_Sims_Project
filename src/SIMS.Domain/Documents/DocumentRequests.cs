using SIMS.Domain.Common;

namespace SIMS.Domain.Documents;

/// <summary>Yêu cầu nộp văn bản cho một loại văn bản, có hạn nộp.</summary>
public sealed class DocumentRequest : Entity
{
    private DocumentRequest() => RequestedBy = string.Empty;

    public Guid ClubId { get; private set; }

    public Guid DocumentTypeId { get; private set; }

    public string RequestedBy { get; private set; }

    public DateOnly Deadline { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static DocumentRequest Create(
        Guid clubId,
        Guid documentTypeId,
        string requestedBy,
        DateOnly deadline,
        DateTimeOffset now)
    {
        if (clubId == Guid.Empty || documentTypeId == Guid.Empty)
        {
            throw new DomainException("CLB hoặc loại văn bản không hợp lệ.");
        }

        if (deadline == default)
        {
            throw new DomainException("Hạn nộp là bắt buộc.");
        }

        var actor = (requestedBy ?? string.Empty).Trim();

        return new DocumentRequest
        {
            ClubId = clubId,
            DocumentTypeId = documentTypeId,
            RequestedBy = actor.Length == 0 ? "system" : actor,
            Deadline = deadline,
            CreatedAtUtc = now,
        };
    }

    public static DocumentRequest Restore(
        Guid id,
        Guid clubId,
        Guid documentTypeId,
        string requestedBy,
        DateOnly deadline,
        DateTimeOffset createdAtUtc)
    {
        var request = Create(clubId, documentTypeId, requestedBy, deadline, createdAtUtc);
        request.Id = id;

        return request;
    }

    public void UpdateDeadline(DateOnly deadline)
    {
        if (deadline == default)
        {
            throw new DomainException("Hạn nộp là bắt buộc.");
        }

        Deadline = deadline;
    }

    public bool IsExpired(DateOnly today) => Deadline < today;
}

/// <summary>Một người được giao nộp văn bản cho yêu cầu; liên kết tới văn bản đã nộp.</summary>
public sealed class DocumentRequestUser : Entity
{
    private DocumentRequestUser()
    {
    }

    public Guid DocumentRequestId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    public bool IsSubmitted => DocumentId is not null;

    public static DocumentRequestUser Create(Guid documentRequestId, Guid userId)
    {
        if (documentRequestId == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainException("Người nhận yêu cầu văn bản không hợp lệ.");
        }

        return new DocumentRequestUser { DocumentRequestId = documentRequestId, UserId = userId };
    }

    public static DocumentRequestUser Restore(
        Guid id,
        Guid documentRequestId,
        Guid userId,
        Guid? documentId,
        DateTimeOffset? submittedAtUtc)
    {
        var item = Create(documentRequestId, userId);
        item.Id = id;
        item.DocumentId = documentId;
        item.SubmittedAtUtc = submittedAtUtc;

        return item;
    }

    public void Submit(Guid documentId, DateTimeOffset now)
    {
        if (documentId == Guid.Empty)
        {
            throw new DomainException("Văn bản nộp không hợp lệ.");
        }

        if (IsSubmitted)
        {
            throw new DomainException("Yêu cầu này đã được nộp.");
        }

        DocumentId = documentId;
        SubmittedAtUtc = now;
    }
}
