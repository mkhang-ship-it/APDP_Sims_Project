using SIMS.Application.Abstractions;
using SIMS.Domain.Awards;
using SIMS.Domain.Disciplines;
using SIMS.Domain.Documents;

namespace SIMS.Tests.Support;

public sealed class InMemoryDocumentTypeRepository : InMemoryRepository<DocumentType>, IDocumentTypeRepository
{
    public Task<DocumentType?> FindByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(type =>
            type.ClubId == clubId && string.Equals(type.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsByCodeAsync(Guid clubId, string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(type =>
            type.ClubId == clubId && string.Equals(type.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<DocumentType>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DocumentType>>(Items.Where(type => type.ClubId == clubId).ToArray());
}

public sealed class InMemoryDocumentRepository : InMemoryRepository<Document>, IDocumentRepository
{
    public Task<IReadOnlyList<Document>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Document>>(Items.Where(document => document.ClubId == clubId).ToArray());

    public Task<IReadOnlyList<Document>> ListByUploaderAsync(
        string uploadedBy,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Document>>(Items
            .Where(document => string.Equals(document.UploadedBy, uploadedBy, StringComparison.OrdinalIgnoreCase))
            .ToArray());
}

public sealed class InMemoryDocumentAccessRepository
    : InMemoryRepository<DocumentAccess>, IDocumentAccessRepository
{
    public Task<IReadOnlyList<DocumentAccess>> ListByDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DocumentAccess>>(
            Items.Where(access => access.DocumentId == documentId).ToArray());

    public Task<IReadOnlyList<DocumentAccess>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DocumentAccess>>(
            Items.Where(access => access.UserId == userId).ToArray());

    public Task<DocumentAccess?> FindByPairAsync(
        Guid documentId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(access =>
            access.DocumentId == documentId && access.UserId == userId));
}

public sealed class InMemoryDocumentRequestRepository
    : InMemoryRepository<DocumentRequest>, IDocumentRequestRepository
{
    public Task<IReadOnlyList<DocumentRequest>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DocumentRequest>>(
            Items.Where(request => request.ClubId == clubId).ToArray());
}

public sealed class InMemoryDocumentRequestUserRepository
    : InMemoryRepository<DocumentRequestUser>, IDocumentRequestUserRepository
{
    public Task<IReadOnlyList<DocumentRequestUser>> ListByRequestAsync(
        Guid documentRequestId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DocumentRequestUser>>(
            Items.Where(item => item.DocumentRequestId == documentRequestId).ToArray());

    public Task<IReadOnlyList<DocumentRequestUser>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DocumentRequestUser>>(
            Items.Where(item => item.UserId == userId).ToArray());

    public Task<IReadOnlyList<DocumentRequestUser>> ListByDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DocumentRequestUser>>(
            Items.Where(item => item.DocumentId == documentId).ToArray());
}

public sealed class InMemoryDisciplineRepository : InMemoryRepository<Discipline>, IDisciplineRepository
{
    public Task<Discipline?> FindByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(discipline =>
            discipline.ClubId == clubId &&
            string.Equals(discipline.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsByCodeAsync(Guid clubId, string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(discipline =>
            discipline.ClubId == clubId &&
            string.Equals(discipline.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Discipline>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Discipline>>(
            Items.Where(discipline => discipline.ClubId == clubId).ToArray());
}

public sealed class InMemoryDisciplineRecordRepository
    : InMemoryRepository<DisciplineRecord>, IDisciplineRecordRepository
{
    public Task<IReadOnlyList<DisciplineRecord>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DisciplineRecord>>(
            Items.Where(record => record.ClubId == clubId).ToArray());

    public Task<IReadOnlyList<DisciplineRecord>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DisciplineRecord>>(
            Items.Where(record => record.UserId == userId).ToArray());
}

public sealed class InMemoryAwardRepository : InMemoryRepository<Award>, IAwardRepository
{
    public Task<Award?> FindByCodeAsync(
        Guid clubId,
        string code,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(award =>
            award.ClubId == clubId &&
            string.Equals(award.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsByCodeAsync(Guid clubId, string code, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Any(award =>
            award.ClubId == clubId &&
            string.Equals(award.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Award>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Award>>(Items.Where(award => award.ClubId == clubId).ToArray());
}

public sealed class InMemoryAwardRecipientRepository
    : InMemoryRepository<AwardRecipient>, IAwardRecipientRepository
{
    public Task<IReadOnlyList<AwardRecipient>> ListByAwardAsync(
        Guid awardId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AwardRecipient>>(
            Items.Where(recipient => recipient.AwardId == awardId).ToArray());

    public Task<IReadOnlyList<AwardRecipient>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AwardRecipient>>(
            Items.Where(recipient => recipient.UserId == userId).ToArray());
}

public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, byte[]> Files => _files;

    public async Task<string> SaveAsync(
        Stream content,
        string originalFileName,
        string folder,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var path = $"{folder}/{Guid.NewGuid():N}{Path.GetExtension(originalFileName)}";
        _files[path] = buffer.ToArray();

        return path;
    }

    public Stream? OpenRead(string relativePath)
        => _files.TryGetValue(relativePath, out var bytes)
            ? new MemoryStream(bytes, writable: false)
            : null;

    public bool Exists(string relativePath) => _files.ContainsKey(relativePath);

    public void Delete(string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            _files.Remove(relativePath);
        }
    }
}

public sealed class SequenceReferenceCodeGenerator : IReferenceCodeGenerator
{
    private readonly Queue<string> _codes;
    private int _counter;

    public SequenceReferenceCodeGenerator(params string[] codes) => _codes = new Queue<string>(codes);

    public string Next(string prefix)
        => _codes.Count > 0 ? _codes.Dequeue() : $"{prefix}-{++_counter:D6}";
}
