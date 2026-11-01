using System.Text;
using SIMS.Application.Abstractions;
using SIMS.Application.Events;
using SIMS.Application.FeatureRequests;
using SIMS.Application.Finance;
using SIMS.Application.Payments;
using SIMS.Application.Security;
using SIMS.Application.Settings;
using SIMS.Application.ShortUrls;
using SIMS.Domain.Events;
using SIMS.Domain.FeatureRequests;
using SIMS.Domain.Finance;
using SIMS.Domain.Security;
using SIMS.Domain.ShortUrls;
using SIMS.Tests.Support;

namespace SIMS.Tests.Application;

public sealed class ProfileServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => "hash:value:" + password;

        public bool Verify(string password, string encodedHash) => encodedHash == "hash:value:" + password;
    }

    private static (ProfileService Service, InMemoryUserRepository Users, RecordingAuditLog Audit) Create()
    {
        var users = new InMemoryUserRepository();
        users.Seed(User.Create("student1", "Sinh viên", Role.Student, "hash:value:Old@12345", Now, "old@example.com"));
        var audit = new RecordingAuditLog();
        var service = new ProfileService(users, new FakeHasher(), audit, new FakeClock(Now));

        return (service, users, audit);
    }

    [Fact]
    public async Task Update_profile_and_validate_email()
    {
        var (service, users, audit) = Create();

        var updated = await service.UpdateProfileAsync(
            "student1", "Tên mới", "new@example.com", "student1");
        var invalid = await service.UpdateProfileAsync(
            "student1", "Tên mới", "khong-hop-le", "student1");

        Assert.True(updated.IsSuccess);
        Assert.Equal("Tên mới", users.All.Single().DisplayName);
        Assert.False(invalid.IsSuccess);
        Assert.Contains(audit.Entries, entry => entry.Action == "profile.update");
    }

    [Fact]
    public async Task Change_password_requires_current_and_minimum_length()
    {
        var (service, users, _) = Create();

        var wrongCurrent = await service.ChangePasswordAsync(
            "student1", "sai", "New@12345", "student1");
        var tooShort = await service.ChangePasswordAsync(
            "student1", "Old@12345", "123", "student1");
        var success = await service.ChangePasswordAsync(
            "student1", "Old@12345", "New@12345", "student1");

        Assert.False(wrongCurrent.IsSuccess);
        Assert.False(tooShort.IsSuccess);
        Assert.True(success.IsSuccess);
        Assert.Equal("hash:value:New@12345", users.All.Single().PasswordHash);
    }
}

public sealed class ShortUrlServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ShortUrlService Service,
        InMemoryShortUrlRepository Urls,
        InMemoryShortUrlVisitRepository Visits,
        RecordingAuditLog Audit);

    private static Harness Create()
    {
        var urls = new InMemoryShortUrlRepository();
        var visits = new InMemoryShortUrlVisitRepository();
        var audit = new RecordingAuditLog();
        var service = new ShortUrlService(urls, visits, audit, new FakeClock(Now));

        return new Harness(service, urls, visits, audit);
    }

    [Fact]
    public async Task Create_resolve_and_count_visits()
    {
        var harness = Create();

        var created = await harness.Service.CreateAsync(
            Guid.NewGuid(), "https://example.com/abc", "Trang A", null, "faculty1");
        await harness.Service.RecordVisitAsync(created.Value!.Id, "127.0.0.1", "test-agent");
        var resolved = await harness.Service.ResolveAsync(created.Value.Code);
        var rows = await harness.Service.ListAsync(created.Value.ClubId);

        Assert.True(created.IsSuccess);
        Assert.True(resolved.IsSuccess);
        Assert.Equal("https://example.com/abc", resolved.Value!.TargetUrl);
        Assert.Single(rows);
        Assert.Equal(1, rows[0].VisitCount);
    }

    [Fact]
    public async Task Resolve_rejects_expired_and_paused_links()
    {
        var harness = Create();
        var expired = await harness.Service.CreateAsync(
            Guid.NewGuid(), "https://example.com", null, Now.AddMinutes(-1), "faculty1");
        var paused = await harness.Service.CreateAsync(
            Guid.NewGuid(), "https://example.com", null, null, "faculty1");
        await harness.Service.SetActiveAsync(paused.Value!.Id, false, "faculty1");

        var expiredResult = await harness.Service.ResolveAsync(expired.Value!.Code);
        var pausedResult = await harness.Service.ResolveAsync(paused.Value.Code);
        var missing = await harness.Service.ResolveAsync("khongtontai");

        Assert.False(expiredResult.IsSuccess);
        Assert.Contains("hết hạn", expiredResult.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(pausedResult.IsSuccess);
        Assert.False(missing.IsSuccess);
    }

    [Fact]
    public async Task Delete_removes_visits()
    {
        var harness = Create();
        var created = await harness.Service.CreateAsync(
            Guid.NewGuid(), "https://example.com", null, null, "faculty1");
        await harness.Service.RecordVisitAsync(created.Value!.Id, null, null);

        var deleted = await harness.Service.DeleteAsync(created.Value.Id, "faculty1");

        Assert.True(deleted.IsSuccess);
        Assert.Empty(harness.Urls.All);
        Assert.Empty(harness.Visits.All);
    }
}

public sealed class EventImageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        EventImageService Service,
        InMemoryEventRepository Events,
        InMemoryEventImageRepository Images,
        InMemoryFileStorage Files);

    private static async Task<Harness> CreateAsync()
    {
        var events = new InMemoryEventRepository();
        await events.AddAsync(Event.Create(
            "EVT001", "Sự kiện", "Mô tả", Now.AddHours(1), Now.AddHours(4), "Hội trường A", 10, null, Now));
        var images = new InMemoryEventImageRepository();
        var files = new InMemoryFileStorage();
        var service = new EventImageService(
            images, events, files, new RecordingAuditLog(), new FakeClock(Now));

        return new Harness(service, events, images, files);
    }

    [Fact]
    public async Task Add_and_delete_image_with_file()
    {
        var harness = await CreateAsync();
        var @event = harness.Events.All.Single();

        var added = await harness.Service.AddAsync(
            @event.Id, "Ảnh khai mạc", new MemoryStream(Encoding.UTF8.GetBytes("img")), "a.png", "faculty1");
        var file = harness.Service.OpenFile(added.Value!);
        var list = await harness.Service.ListAsync(@event.Id);
        var deleted = await harness.Service.DeleteAsync(added.Value!.Id, "faculty1");

        Assert.True(added.IsSuccess);
        Assert.NotNull(file);
        Assert.Single(list);
        Assert.True(deleted.IsSuccess);
        Assert.Empty(harness.Images.All);
        Assert.Empty(harness.Files.Files);
    }

    [Fact]
    public async Task Add_rejects_invalid_extension_and_missing_event()
    {
        var harness = await CreateAsync();

        var badFile = await harness.Service.AddAsync(
            harness.Events.All.Single().Id, null, new MemoryStream(), "a.exe", "faculty1");
        var missingEvent = await harness.Service.AddAsync(
            Guid.NewGuid(), null, new MemoryStream(), "a.png", "faculty1");

        Assert.False(badFile.IsSuccess);
        Assert.False(missingEvent.IsSuccess);
        Assert.Empty(harness.Images.All);
    }
}

public sealed class FeatureRequestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private static (FeatureRequestService Service, InMemoryFeatureRequestRepository Repository) Create()
    {
        var repository = new InMemoryFeatureRequestRepository();
        var service = new FeatureRequestService(repository, new RecordingAuditLog(), new FakeClock(Now));

        return (service, repository);
    }

    [Fact]
    public async Task Create_list_and_update_status()
    {
        var (service, _) = Create();
        var clubId = Guid.NewGuid();

        var first = await service.CreateAsync(clubId, "Đề xuất A", "Nội dung", "student1");
        var second = await service.CreateAsync(null, "Đề xuất B", "Nội dung", "student2");
        var updated = await service.UpdateStatusAsync(
            first.Value!.Id, FeatureRequestStatus.InProgress, "Đang xem xét", "faculty1");

        var staff = await service.ListForStaffAsync(clubId);
        var mine = await service.ListMineAsync("student1");

        Assert.True(first.IsSuccess);
        Assert.True(updated.IsSuccess);
        Assert.Single(staff);
        Assert.Single(mine);
        Assert.Equal(FeatureRequestStatus.InProgress, mine[0].Status);
        Assert.NotEqual(first.Value.Id, second.Value!.Id);
    }

    [Fact]
    public async Task Owner_can_edit_only_while_open_and_others_cannot()
    {
        var (service, _) = Create();
        var created = await service.CreateAsync(Guid.NewGuid(), "Đề xuất", "Nội dung", "student1");

        var foreign = await service.UpdateContentAsync(
            created.Value!.Id, "Sửa", "Nội dung", "student2");
        var updated = await service.UpdateContentAsync(
            created.Value.Id, "Đã sửa", "Nội dung mới", "student1");
        await service.UpdateStatusAsync(
            created.Value.Id, FeatureRequestStatus.Closed, "Xong", "faculty1");
        var afterClosed = await service.UpdateContentAsync(
            created.Value.Id, "Lại sửa", "Nội dung", "student1");

        Assert.False(foreign.IsSuccess);
        Assert.True(updated.IsSuccess);
        Assert.False(afterClosed.IsSuccess);
    }
}

public sealed class PaymentWebhookServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        PaymentWebhookService Service,
        InvoiceService InvoicesService,
        InMemoryInvoiceRepository Invoices,
        InMemoryTransactionRepository Transactions,
        SettingsService Settings);

    private static Harness Create()
    {
        var users = new InMemoryUserRepository();
        var invoices = new InMemoryInvoiceRepository();
        var transactions = new InMemoryTransactionRepository();
        var audit = new RecordingAuditLog();
        var clock = new FakeClock(Now);
        var invoiceService = new InvoiceService(
            invoices, transactions, new SequenceInvoiceCodeGenerator(), audit, clock);
        var settings = new SettingsService(new InMemorySettingRepository(), audit, clock);
        var webhook = new PaymentWebhookService(invoices, invoiceService, settings, clock);

        return new Harness(webhook, invoiceService, invoices, transactions, settings);
    }

    [Fact]
    public async Task Paid_transfer_marks_invoice_paid_and_caps_amount()
    {
        var harness = Create();
        var created = await harness.InvoicesService.CreateAsync(
            new InvoiceBatchInput(
                Guid.NewGuid(), "Phí", 100_000m, DateOnly.FromDateTime(Now.UtcDateTime), "HK1", null),
            new[] { Guid.NewGuid() },
            "faculty1");
        var invoice = created.Value!.Single();

        var result = await harness.Service.HandleSePayAsync(
            new SePayPayload("in", invoice.Code.Replace("-", string.Empty), 150_000m, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, result.Value!.Status);
        Assert.Single(harness.Transactions.All);
        Assert.Equal(100_000m, harness.Transactions.All[0].PaidAmount);

        var duplicate = await harness.Service.HandleSePayAsync(
            new SePayPayload("in", invoice.Code, 100_000m, null));
        Assert.True(duplicate.IsSuccess);
        Assert.Single(harness.Transactions.All);
    }

    [Fact]
    public async Task Rejects_outgoing_missing_and_unknown_invoice()
    {
        var harness = Create();

        var outgoing = await harness.Service.HandleSePayAsync(
            new SePayPayload("out", "INV2026000001", 1000m, null));
        var missingCode = await harness.Service.HandleSePayAsync(
            new SePayPayload("in", null, 1000m, null));
        var unknown = await harness.Service.HandleSePayAsync(
            new SePayPayload("in", "INV-2099-999999", 1000m, null));

        Assert.False(outgoing.IsSuccess);
        Assert.False(missingCode.IsSuccess);
        Assert.False(unknown.IsSuccess);
        Assert.Contains("Không tìm thấy", unknown.Errors[0].Message);
    }

    [Fact]
    public void Api_key_authorization_is_constant_time_and_optional_prefix()
    {
        Assert.False(PaymentWebhookService.IsConfigured(""));
        Assert.True(PaymentWebhookService.IsConfigured("secret-key-123"));
        Assert.True(PaymentWebhookService.IsAuthorized("Apikey secret-key-123", "secret-key-123"));
        Assert.True(PaymentWebhookService.IsAuthorized("secret-key-123", "secret-key-123"));
        Assert.False(PaymentWebhookService.IsAuthorized("Apikey wrong", "secret-key-123"));
        Assert.False(PaymentWebhookService.IsAuthorized(null, "secret-key-123"));
    }
}
