using SIMS.Domain.Documents;

namespace SIMS.Application.Abstractions;

public interface IDocumentTypeRepository : IRepository<DocumentType>
{
    Task<DocumentType?> FindByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentType>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);
}

public interface IDocumentRepository : IRepository<Document>
{
    Task<IReadOnlyList<Document>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Document>> ListByUploaderAsync(
        string uploadedBy,
        CancellationToken cancellationToken = default);
}

public interface IDocumentAccessRepository : IRepository<DocumentAccess>
{
    Task<IReadOnlyList<DocumentAccess>> ListByDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentAccess>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<DocumentAccess?> FindByPairAsync(
        Guid documentId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface IDocumentRequestRepository : IRepository<DocumentRequest>
{
    Task<IReadOnlyList<DocumentRequest>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);
}

public interface IDocumentRequestUserRepository : IRepository<DocumentRequestUser>
{
    Task<IReadOnlyList<DocumentRequestUser>> ListByRequestAsync(
        Guid documentRequestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentRequestUser>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentRequestUser>> ListByDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
