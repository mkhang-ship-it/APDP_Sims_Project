using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Documents;

namespace SIMS.Application.Documents;

public sealed record DocumentListItem(Document Document, DocumentType? Type);

public sealed record DocumentAccessItem(DocumentAccess Access, Guid UserId);

public sealed record DocumentRequestRow(
    DocumentRequest Request,
    DocumentType? Type,
    IReadOnlyList<DocumentRequestUser> Recipients);

public sealed record DocumentRequestItem(
    DocumentRequest Request,
    DocumentType? Type,
    DocumentRequestUser Assignment,
    Document? SubmittedDocument);

public interface IDocumentService
{
    Task<Result<DocumentType>> CreateTypeAsync(
        Guid clubId, string name, string? description, string? templateUrl, string actor,
        CancellationToken cancellationToken = default);

    Task<Result<DocumentType>> UpdateTypeAsync(
        Guid id, string name, string? description, string? templateUrl, string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteTypeAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentType>> ListTypesAsync(
        Guid? clubId,
        bool activeOnly = false,
        CancellationToken cancellationToken = default);

    Task<DocumentType?> GetTypeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Document>> UploadAsync(
        Guid clubId,
        Guid documentTypeId,
        string title,
        string uploadedBy,
        DocumentVisibility visibility,
        Stream content,
        string fileName,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<Document>> ReviewAsync(
        Guid id, bool approve, string? reason, string actor, CancellationToken cancellationToken = default);

    Task<Result<Document>> SetVisibilityAsync(
        Guid id, DocumentVisibility visibility, string actor, CancellationToken cancellationToken = default);

    Task<Result> SetAccessAsync(
        Guid id, IReadOnlyCollection<Guid> userIds, string actor, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentListItem>> ListAsync(
        Guid? clubId,
        DocumentStatus? status = null,
        Guid? documentTypeId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentListItem>> ListMineAsync(
        Guid? clubId, string uploader, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentListItem>> ListAccessibleAsync(
        Guid clubId, Guid userId, string username, CancellationToken cancellationToken = default);

    Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CanViewAsync(
        Document document, Guid userId, string username, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentAccess>> ListAccessAsync(
        Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Đường dẫn kho file để controller trả FileStream (null nếu thiếu).</summary>
    Stream? OpenFile(Document document);
}

public sealed class DocumentService : IDocumentService
{
    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png", ".txt",
        };

    private const int MaxCodeAttempts = 50;
    private const string DocumentFolder = "documents";

    private readonly IDocumentTypeRepository _types;
    private readonly IDocumentRepository _documents;
    private readonly IDocumentAccessRepository _access;
    private readonly IDocumentRequestUserRepository _requestUsers;
    private readonly IFileStorage _files;
    private readonly IReferenceCodeGenerator _codes;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public DocumentService(
        IDocumentTypeRepository types,
        IDocumentRepository documents,
        IDocumentAccessRepository access,
        IDocumentRequestUserRepository requestUsers,
        IFileStorage files,
        IReferenceCodeGenerator codes,
        IAuditLog audit,
        IClock clock)
    {
        _types = types;
        _documents = documents;
        _access = access;
        _requestUsers = requestUsers;
        _files = files;
        _codes = codes;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DocumentType>> CreateTypeAsync(
        Guid clubId,
        string name,
        string? description,
        string? templateUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var code = await NextCodeAsync(clubId, cancellationToken).ConfigureAwait(false);

        DocumentType type;
        try
        {
            type = DocumentType.Create(clubId, code, name, description, templateUrl, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return FailType(ex.Message);
        }

        await _types.AddAsync(type, cancellationToken).ConfigureAwait(false);
        await AuditAsync("document.type.create", "document_type", type.Id, actor, cancellationToken)
            .ConfigureAwait(false);

        return Result<DocumentType>.Ok(type);
    }

    public async Task<Result<DocumentType>> UpdateTypeAsync(
        Guid id,
        string name,
        string? description,
        string? templateUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var type = await _types.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (type is null)
        {
            return FailType("Không tìm thấy loại văn bản.");
        }

        try
        {
            type.Update(name, description, templateUrl);
        }
        catch (DomainException ex)
        {
            return FailType(ex.Message);
        }

        await _types.UpdateAsync(type, cancellationToken).ConfigureAwait(false);
        await AuditAsync("document.type.update", "document_type", type.Id, actor, cancellationToken)
            .ConfigureAwait(false);

        return Result<DocumentType>.Ok(type);
    }

    public async Task<Result> DeleteTypeAsync(
        Guid id,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var type = await _types.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (type is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy loại văn bản."));
        }

        var documents = await _documents
            .ListByClubAsync(type.ClubId, cancellationToken)
            .ConfigureAwait(false);

        if (documents.Any(document => document.DocumentTypeId == id))
        {
            return Result.Fail(new ValidationError(
                string.Empty,
                "Không thể xoá loại văn bản đang có văn bản sử dụng."));
        }

        await _types.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditAsync("document.type.delete", "document_type", id, actor, cancellationToken)
            .ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<IReadOnlyList<DocumentType>> ListTypesAsync(
        Guid? clubId,
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var types = clubId is null
            ? await _types.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _types.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);

        return types
            .Where(type => !activeOnly || type.IsActive)
            .OrderBy(type => type.Name, StringComparer.CurrentCulture)
            .ToArray();
    }

    public Task<DocumentType?> GetTypeAsync(Guid id, CancellationToken cancellationToken = default)
        => _types.FindByIdAsync(id, cancellationToken);

    public async Task<Result<Document>> UploadAsync(
        Guid clubId,
        Guid documentTypeId,
        string title,
        string uploadedBy,
        DocumentVisibility visibility,
        Stream content,
        string fileName,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var type = await _types.FindByIdAsync(documentTypeId, cancellationToken).ConfigureAwait(false);
        if (type is null || type.ClubId != clubId)
        {
            return FailDocument("Loại văn bản không thuộc CLB đã chọn.");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return FailDocument("Chỉ nhận tệp PDF, Word, ảnh JPG/PNG hoặc TXT.");
        }

        var storedPath = await _files
            .SaveAsync(content, fileName, DocumentFolder, cancellationToken)
            .ConfigureAwait(false);

        Document document;
        try
        {
            document = Document.Create(
                clubId,
                documentTypeId,
                uploadedBy,
                title,
                storedPath,
                visibility,
                DocumentStatus.Pending,
                _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            _files.Delete(storedPath);
            return FailDocument(ex.Message);
        }

        await _documents.AddAsync(document, cancellationToken).ConfigureAwait(false);
        await AuditAsync("document.upload", "document", document.Id, actor, cancellationToken)
            .ConfigureAwait(false);

        return Result<Document>.Ok(document);
    }

    public async Task<Result<Document>> ReviewAsync(
        Guid id,
        bool approve,
        string? reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var document = await _documents.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return FailDocument("Không tìm thấy văn bản.");
        }

        try
        {
            if (approve)
            {
                document.Approve(actor, _clock.UtcNow);
            }
            else
            {
                document.Reject(actor, reason, _clock.UtcNow);
            }
        }
        catch (DomainException ex)
        {
            return FailDocument(ex.Message);
        }

        await _documents.UpdateAsync(document, cancellationToken).ConfigureAwait(false);
        await AuditAsync(
            approve ? "document.approve" : "document.reject",
            "document",
            document.Id,
            actor,
            cancellationToken).ConfigureAwait(false);

        return Result<Document>.Ok(document);
    }

    public async Task<Result<Document>> SetVisibilityAsync(
        Guid id,
        DocumentVisibility visibility,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var document = await _documents.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return FailDocument("Không tìm thấy văn bản.");
        }

        document.SetVisibility(visibility);
        await _documents.UpdateAsync(document, cancellationToken).ConfigureAwait(false);
        await AuditAsync("document.visibility", "document", document.Id, actor, cancellationToken)
            .ConfigureAwait(false);

        return Result<Document>.Ok(document);
    }

    public async Task<Result> SetAccessAsync(
        Guid id,
        IReadOnlyCollection<Guid> userIds,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var document = await _documents.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy văn bản."));
        }

        var existing = await _access.ListByDocumentAsync(id, cancellationToken).ConfigureAwait(false);
        foreach (var access in existing)
        {
            await _access.DeleteAsync(access.Id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var userId in userIds.Distinct().Where(userId => userId != Guid.Empty))
        {
            await _access
                .AddAsync(DocumentAccess.Create(id, userId), cancellationToken)
                .ConfigureAwait(false);
        }

        await AuditAsync(
            "document.access",
            "document",
            document.Id,
            actor,
            cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var document = await _documents.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy văn bản."));
        }

        var linked = await _requestUsers
            .ListByDocumentAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (linked.Count > 0)
        {
            return Result.Fail(new ValidationError(
                string.Empty,
                "Không thể xoá văn bản đã nộp cho yêu cầu."));
        }

        foreach (var access in await _access.ListByDocumentAsync(id, cancellationToken).ConfigureAwait(false))
        {
            await _access.DeleteAsync(access.Id, cancellationToken).ConfigureAwait(false);
        }

        await _documents.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        _files.Delete(document.FilePath);
        await AuditAsync("document.delete", "document", id, actor, cancellationToken)
            .ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<IReadOnlyList<DocumentListItem>> ListAsync(
        Guid? clubId,
        DocumentStatus? status = null,
        Guid? documentTypeId = null,
        CancellationToken cancellationToken = default)
    {
        var documents = clubId is null
            ? await _documents.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _documents.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);
        var types = (await _types.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(type => type.Id);

        return documents
            .Where(document => status is null || document.Status == status)
            .Where(document => documentTypeId is null || document.DocumentTypeId == documentTypeId)
            .OrderByDescending(document => document.CreatedAtUtc)
            .Select(document => new DocumentListItem(
                document,
                types.TryGetValue(document.DocumentTypeId, out var type) ? type : null))
            .ToArray();
    }

    public async Task<IReadOnlyList<DocumentListItem>> ListMineAsync(
        Guid? clubId,
        string uploader,
        CancellationToken cancellationToken = default)
    {
        var documents = (await _documents
            .ListByUploaderAsync(uploader, cancellationToken)
            .ConfigureAwait(false))
            .Where(document => clubId is null || document.ClubId == clubId)
            .ToArray();
        var types = (await _types.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(type => type.Id);

        return documents
            .OrderByDescending(document => document.CreatedAtUtc)
            .Select(document => new DocumentListItem(
                document,
                types.TryGetValue(document.DocumentTypeId, out var type) ? type : null))
            .ToArray();
    }

    public async Task<IReadOnlyList<DocumentListItem>> ListAccessibleAsync(
        Guid clubId,
        Guid userId,
        string username,
        CancellationToken cancellationToken = default)
    {
        var documents = await _documents.ListByClubAsync(clubId, cancellationToken).ConfigureAwait(false);
        var types = (await _types.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(type => type.Id);
        var granted = (await _access.ListByUserAsync(userId, cancellationToken).ConfigureAwait(false))
            .Select(access => access.DocumentId)
            .ToHashSet();

        return documents
            .Where(document => document.Status == DocumentStatus.Approved)
            .Where(document =>
                document.Visibility == DocumentVisibility.Public ||
                document.UploadedBy.Equals(username, StringComparison.OrdinalIgnoreCase) ||
                (document.Visibility == DocumentVisibility.Restricted && granted.Contains(document.Id)))
            .OrderByDescending(document => document.CreatedAtUtc)
            .Select(document => new DocumentListItem(
                document,
                types.TryGetValue(document.DocumentTypeId, out var type) ? type : null))
            .ToArray();
    }

    public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _documents.FindByIdAsync(id, cancellationToken);

    public async Task<bool> CanViewAsync(
        Document document,
        Guid userId,
        string username,
        CancellationToken cancellationToken = default)
    {
        if (document.Status != DocumentStatus.Approved)
        {
            return document.UploadedBy.Equals(username, StringComparison.OrdinalIgnoreCase);
        }

        if (document.Visibility == DocumentVisibility.Public ||
            document.UploadedBy.Equals(username, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return document.Visibility == DocumentVisibility.Restricted &&
               await _access.FindByPairAsync(document.Id, userId, cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<IReadOnlyList<DocumentAccess>> ListAccessAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
        => await _access.ListByDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);

    public Stream? OpenFile(Document document) => _files.OpenRead(document.FilePath);

    private async Task<string> NextCodeAsync(Guid clubId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = _codes.Next("DT");

            if (!await _types.ExistsByCodeAsync(clubId, code, cancellationToken).ConfigureAwait(false))
            {
                return code;
            }
        }

        throw new DomainException("Không sinh được mã loại văn bản duy nhất.");
    }

    private Task AuditAsync(
        string action,
        string entity,
        Guid entityId,
        string actor,
        CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                action,
                entity,
                entityId.ToString("D"),
                string.Empty),
            cancellationToken);

    private static Result<DocumentType> FailType(string message)
        => Result<DocumentType>.Fail(new ValidationError(string.Empty, message));

    private static Result<Document> FailDocument(string message)
        => Result<Document>.Fail(new ValidationError(string.Empty, message));
}
