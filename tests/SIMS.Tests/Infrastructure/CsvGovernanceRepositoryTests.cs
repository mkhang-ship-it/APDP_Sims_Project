using System.Text;
using SIMS.Domain.Awards;
using SIMS.Domain.Disciplines;
using SIMS.Domain.Documents;
using SIMS.Infrastructure.Persistence;
using SIMS.Infrastructure.Storage;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvGovernanceRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 1);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-gov-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task Document_type_and_document_round_trip()
    {
        var types = new CsvDocumentTypeRepository(PathFor("document_types.csv"));
        var documents = new CsvDocumentRepository(PathFor("documents.csv"));
        var clubId = Guid.NewGuid();

        var type = DocumentType.Create(clubId, "DT-000123", "Đơn xác nhận", "Mô tả, có phẩy", null, Now);
        await types.AddAsync(type);

        var document = Document.Create(
            clubId, type.Id, "student1", "Đơn xin nghỉ", "documents/a.pdf",
            DocumentVisibility.Restricted, DocumentStatus.Pending, Now);
        document.Reject("faculty1", "Thiếu chữ ký", Now.AddHours(1));
        await documents.AddAsync(document);

        var loadedType = await types.FindByCodeAsync(clubId, "dt-000123");
        var loadedDocument = await documents.ListByUploaderAsync("STUDENT1");

        Assert.NotNull(loadedType);
        Assert.Equal("Mô tả, có phẩy", loadedType!.Description);
        Assert.Single(loadedDocument);
        Assert.Equal(DocumentStatus.Rejected, loadedDocument[0].Status);
        Assert.Equal("Thiếu chữ ký", loadedDocument[0].ReasonRejected);
        Assert.Equal("documents/a.pdf", loadedDocument[0].FilePath);
        Assert.Single(await documents.ListByClubAsync(clubId));
    }

    [Fact]
    public async Task Document_access_and_requests_round_trip()
    {
        var accessRepository = new CsvDocumentAccessRepository(PathFor("document_access.csv"));
        var requestRepository = new CsvDocumentRequestRepository(PathFor("document_requests.csv"));
        var requestUserRepository = new CsvDocumentRequestUserRepository(PathFor("document_request_users.csv"));

        var documentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await accessRepository.AddAsync(DocumentAccess.Create(documentId, userId));

        var request = DocumentRequest.Create(Guid.NewGuid(), Guid.NewGuid(), "faculty1", Today.AddDays(5), Now);
        await requestRepository.AddAsync(request);
        var assignment = DocumentRequestUser.Create(request.Id, userId);
        await requestUserRepository.AddAsync(assignment);
        assignment.Submit(Guid.NewGuid(), Now.AddMinutes(2));
        await requestUserRepository.UpdateAsync(assignment);

        Assert.NotNull(await accessRepository.FindByPairAsync(documentId, userId));
        Assert.Single(await requestRepository.ListByClubAsync(request.ClubId));
        var loadedAssignment = (await requestUserRepository.ListByUserAsync(userId)).Single();
        Assert.True(loadedAssignment.IsSubmitted);
        Assert.NotNull(loadedAssignment.SubmittedAtUtc);
    }

    [Fact]
    public async Task Discipline_and_record_round_trip()
    {
        var disciplines = new CsvDisciplineRepository(PathFor("disciplines.csv"));
        var records = new CsvDisciplineRecordRepository(PathFor("discipline_records.csv"));
        var clubId = Guid.NewGuid();

        var discipline = Discipline.Create(clubId, "KL-000123", "Vắng không phép", -5m, "Mô tả", Now);
        await disciplines.AddAsync(discipline);
        var record = DisciplineRecord.Create(
            Guid.NewGuid(), clubId, discipline.Id, Today, "HK1", "faculty1", "Ghi chú", Now);
        await records.AddAsync(record);
        record.Revoke();
        await records.UpdateAsync(record);

        var loadedDiscipline = await disciplines.FindByCodeAsync(clubId, "KL-000123");
        var loadedRecords = await records.ListByClubAsync(clubId);

        Assert.Equal(-5m, loadedDiscipline!.Points);
        Assert.Single(loadedRecords);
        Assert.False(loadedRecords[0].IsActive);
        Assert.Equal("Ghi chú", loadedRecords[0].Notes);
        Assert.Equal(Today, loadedRecords[0].AppliedDate);
    }

    [Fact]
    public async Task Award_and_recipient_round_trip()
    {
        var awards = new CsvAwardRepository(PathFor("awards.csv"));
        var recipients = new CsvAwardRecipientRepository(PathFor("award_recipients.csv"));
        var clubId = Guid.NewGuid();

        var award = Award.Create(
            clubId, "BK-000123", "Thành viên xuất sắc", "HK1", Today, AwardStatus.Awarded, "Ghi chú, phẩy", Now);
        await awards.AddAsync(award);
        var recipient = AwardRecipient.Create(award.Id, Guid.NewGuid(), "awards/a.pdf", Now);
        await recipients.AddAsync(recipient);

        var loadedAward = await awards.FindByCodeAsync(clubId, "bk-000123");
        var loadedRecipients = await recipients.ListByAwardAsync(award.Id);

        Assert.NotNull(loadedAward);
        Assert.Equal(AwardStatus.Awarded, loadedAward!.Status);
        Assert.Equal("Ghi chú, phẩy", loadedAward.Notes);
        Assert.Single(loadedRecipients);
        Assert.Equal("awards/a.pdf", loadedRecipients[0].Attachment);
    }
}

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-files-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Save_open_and_delete_file()
    {
        var storage = new LocalFileStorage(_directory);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("nội dung tệp"));

        var path = await storage.SaveAsync(content, "don.pdf", "documents");

        Assert.StartsWith("documents/", path, StringComparison.Ordinal);
        Assert.EndsWith(".pdf", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(storage.Exists(path));

        using (var stream = storage.OpenRead(path))
        {
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            Assert.Equal("nội dung tệp", await reader.ReadToEndAsync());
        }

        storage.Delete(path);
        Assert.False(storage.Exists(path));
        Assert.Null(storage.OpenRead(path));
    }

    [Fact]
    public void Rejects_path_traversal()
    {
        var storage = new LocalFileStorage(_directory);

        Assert.Throws<InvalidOperationException>(() => storage.OpenRead("../outside.txt"));
        Assert.Throws<InvalidOperationException>(() => storage.Exists("../../outside.txt"));
    }
}
