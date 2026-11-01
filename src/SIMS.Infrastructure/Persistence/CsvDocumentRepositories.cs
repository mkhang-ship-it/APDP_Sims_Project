using System.Globalization;
using SIMS.Application.Abstractions;
using SIMS.Domain.Common;
using SIMS.Domain.Documents;

namespace SIMS.Infrastructure.Persistence;

public sealed class CsvDocumentTypeRepository : CsvRepository<DocumentType>, IDocumentTypeRepository
{
    private const int ColumnCount = 8;

    public CsvDocumentTypeRepository(string filePath)
        : base(
            filePath,
            "id",
            "club_id",
            "code",
            "name",
            "description",
            "template_url",
            "is_active",
            "created_at_utc")
    {
    }

    public CsvDocumentTypeRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<DocumentType?> FindByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(type =>
            type.ClubId == clubId &&
            string.Equals(type.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ExistsByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default)
        => await FindByCodeAsync(clubId, code, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<DocumentType>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(type => type.ClubId == clubId).ToArray();
    }

    protected override string[] ToCells(DocumentType type) =>
    [
        type.Id.ToString("D"),
        type.ClubId.ToString("D"),
        type.Code,
        type.Name,
        type.Description,
        type.TemplateUrl,
        type.IsActive ? "true" : "false",
        type.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override DocumentType? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var clubId) ||
            !bool.TryParse(cells[6], out var isActive) ||
            !DateTimeOffset.TryParse(cells[7], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return DocumentType.Restore(id, clubId, cells[2], cells[3], cells[4], cells[5], isActive, createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvDocumentRepository : CsvRepository<Document>, IDocumentRepository
{
    private const int ColumnCount = 12;

    public CsvDocumentRepository(string filePath)
        : base(
            filePath,
            "id",
            "club_id",
            "document_type_id",
            "uploaded_by",
            "title",
            "file_path",
            "visibility",
            "status",
            "reason_rejected",
            "reviewed_by",
            "reviewed_at_utc",
            "created_at_utc")
    {
    }

    public CsvDocumentRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<Document>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(document => document.ClubId == clubId).ToArray();
    }

    public async Task<IReadOnlyList<Document>> ListByUploaderAsync(
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items
            .Where(document => string.Equals(document.UploadedBy, uploadedBy, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    protected override string[] ToCells(Document document) =>
    [
        document.Id.ToString("D"),
        document.ClubId.ToString("D"),
        document.DocumentTypeId.ToString("D"),
        document.UploadedBy,
        document.Title,
        document.FilePath,
        document.Visibility.ToString(),
        document.Status.ToString(),
        document.ReasonRejected,
        document.ReviewedBy,
        document.ReviewedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        document.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override Document? Map(string[] cells)
    {
        if (cells.Length != ColumnCount ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var clubId) ||
            !Guid.TryParse(cells[2], out var typeId) ||
            !Enum.TryParse<DocumentVisibility>(cells[6], ignoreCase: true, out var visibility) ||
            !Enum.TryParse<DocumentStatus>(cells[7], ignoreCase: true, out var status) ||
            !DateTimeOffset.TryParse(cells[11], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        DateTimeOffset? reviewedAt = null;
        if (!string.IsNullOrWhiteSpace(cells[10]))
        {
            if (!DateTimeOffset.TryParse(cells[10], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return null;
            }

            reviewedAt = parsed;
        }

        try
        {
            return Document.Restore(
                id, clubId, typeId, cells[3], cells[4], cells[5], visibility, status,
                cells[8], cells[9], reviewedAt, createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvDocumentAccessRepository : CsvRepository<DocumentAccess>, IDocumentAccessRepository
{
    public CsvDocumentAccessRepository(string filePath)
        : base(filePath, "id", "document_id", "user_id")
    {
    }

    public CsvDocumentAccessRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<DocumentAccess>> ListByDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(access => access.DocumentId == documentId).ToArray();
    }

    public async Task<IReadOnlyList<DocumentAccess>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(access => access.UserId == userId).ToArray();
    }

    public async Task<DocumentAccess?> FindByPairAsync(
        Guid documentId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.FirstOrDefault(access => access.DocumentId == documentId && access.UserId == userId);
    }

    protected override string[] ToCells(DocumentAccess access) =>
    [
        access.Id.ToString("D"),
        access.DocumentId.ToString("D"),
        access.UserId.ToString("D"),
    ];

    protected override DocumentAccess? Map(string[] cells)
    {
        if (cells.Length != 3 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var documentId) ||
            !Guid.TryParse(cells[2], out var userId))
        {
            return null;
        }

        try
        {
            return DocumentAccess.Restore(id, documentId, userId);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvDocumentRequestRepository : CsvRepository<DocumentRequest>, IDocumentRequestRepository
{
    public CsvDocumentRequestRepository(string filePath)
        : base(
            filePath,
            "id",
            "club_id",
            "document_type_id",
            "requested_by",
            "deadline",
            "created_at_utc")
    {
    }

    public CsvDocumentRequestRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<DocumentRequest>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(request => request.ClubId == clubId).ToArray();
    }

    protected override string[] ToCells(DocumentRequest request) =>
    [
        request.Id.ToString("D"),
        request.ClubId.ToString("D"),
        request.DocumentTypeId.ToString("D"),
        request.RequestedBy,
        request.Deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        request.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
    ];

    protected override DocumentRequest? Map(string[] cells)
    {
        if (cells.Length != 6 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var clubId) ||
            !Guid.TryParse(cells[2], out var typeId) ||
            !DateOnly.TryParseExact(cells[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var deadline) ||
            !DateTimeOffset.TryParse(cells[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return null;
        }

        try
        {
            return DocumentRequest.Restore(id, clubId, typeId, cells[3], deadline, createdAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}

public sealed class CsvDocumentRequestUserRepository
    : CsvRepository<DocumentRequestUser>, IDocumentRequestUserRepository
{
    public CsvDocumentRequestUserRepository(string filePath)
        : base(filePath, "id", "document_request_id", "user_id", "document_id", "submitted_at_utc")
    {
    }

    public CsvDocumentRequestUserRepository(IRowStore store)
        : base(store)
    {
    }

    public async Task<IReadOnlyList<DocumentRequestUser>> ListByRequestAsync(
        Guid documentRequestId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(item => item.DocumentRequestId == documentRequestId).ToArray();
    }

    public async Task<IReadOnlyList<DocumentRequestUser>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(item => item.UserId == userId).ToArray();
    }

    public async Task<IReadOnlyList<DocumentRequestUser>> ListByDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);

        return items.Where(item => item.DocumentId == documentId).ToArray();
    }

    protected override string[] ToCells(DocumentRequestUser item) =>
    [
        item.Id.ToString("D"),
        item.DocumentRequestId.ToString("D"),
        item.UserId.ToString("D"),
        item.DocumentId?.ToString("D") ?? string.Empty,
        item.SubmittedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
    ];

    protected override DocumentRequestUser? Map(string[] cells)
    {
        if (cells.Length != 5 ||
            !Guid.TryParse(cells[0], out var id) ||
            !Guid.TryParse(cells[1], out var requestId) ||
            !Guid.TryParse(cells[2], out var userId))
        {
            return null;
        }

        Guid? documentId = null;
        if (!string.IsNullOrWhiteSpace(cells[3]))
        {
            if (!Guid.TryParse(cells[3], out var parsedDocumentId))
            {
                return null;
            }

            documentId = parsedDocumentId;
        }

        DateTimeOffset? submittedAt = null;
        if (!string.IsNullOrWhiteSpace(cells[4]))
        {
            if (!DateTimeOffset.TryParse(cells[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return null;
            }

            submittedAt = parsed;
        }

        try
        {
            return DocumentRequestUser.Restore(id, requestId, userId, documentId, submittedAt);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
