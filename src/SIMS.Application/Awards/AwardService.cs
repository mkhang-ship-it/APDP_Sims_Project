using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Awards;
using SIMS.Domain.Common;

namespace SIMS.Application.Awards;

public sealed record AwardListItem(Award Award, IReadOnlyList<AwardRecipient> Recipients);

public interface IAwardService
{
    Task<Result<Award>> CreateAsync(
        Guid clubId, string title, string? period, DateOnly awardDate, AwardStatus status,
        string? notes, string actor, CancellationToken cancellationToken = default);

    Task<Result<Award>> UpdateAsync(
        Guid id, string title, string? period, DateOnly awardDate, AwardStatus status,
        string? notes, string actor, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<Result<AwardRecipient>> AddRecipientAsync(
        Guid awardId,
        Guid userId,
        Stream? attachment,
        string? fileName,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> RemoveRecipientAsync(
        Guid awardId,
        Guid recipientId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AwardListItem>> ListAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default);

    Task<Award?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AwardRecipient>> ListRecipientsAsync(
        Guid awardId,
        CancellationToken cancellationToken = default);

    Stream? OpenAttachment(AwardRecipient recipient);
}

public sealed class AwardService : IAwardService
{
    private const int MaxCodeAttempts = 50;
    private const string AttachmentFolder = "awards";

    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".jpg", ".jpeg", ".png",
        };

    private readonly IAwardRepository _awards;
    private readonly IAwardRecipientRepository _recipients;
    private readonly IFileStorage _files;
    private readonly IReferenceCodeGenerator _codes;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AwardService(
        IAwardRepository awards,
        IAwardRecipientRepository recipients,
        IFileStorage files,
        IReferenceCodeGenerator codes,
        IAuditLog audit,
        IClock clock)
    {
        _awards = awards;
        _recipients = recipients;
        _files = files;
        _codes = codes;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<Award>> CreateAsync(
        Guid clubId,
        string title,
        string? period,
        DateOnly awardDate,
        AwardStatus status,
        string? notes,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var code = await NextCodeAsync(clubId, cancellationToken).ConfigureAwait(false);

        Award award;
        try
        {
            award = Award.Create(clubId, code, title, period, awardDate, status, notes, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _awards.AddAsync(award, cancellationToken).ConfigureAwait(false);
        await AuditAsync("award.create", award.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<Award>.Ok(award);
    }

    public async Task<Result<Award>> UpdateAsync(
        Guid id,
        string title,
        string? period,
        DateOnly awardDate,
        AwardStatus status,
        string? notes,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var award = await _awards.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (award is null)
        {
            return Fail("Không tìm thấy giải thưởng.");
        }

        try
        {
            award.Update(title, period, awardDate, status, notes);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _awards.UpdateAsync(award, cancellationToken).ConfigureAwait(false);
        await AuditAsync("award.update", award.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<Award>.Ok(award);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var award = await _awards.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (award is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy giải thưởng."));
        }

        foreach (var recipient in await _recipients
                     .ListByAwardAsync(id, cancellationToken)
                     .ConfigureAwait(false))
        {
            _files.Delete(recipient.Attachment);
            await _recipients.DeleteAsync(recipient.Id, cancellationToken).ConfigureAwait(false);
        }

        await _awards.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditAsync("award.delete", id, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<Result<AwardRecipient>> AddRecipientAsync(
        Guid awardId,
        Guid userId,
        Stream? attachment,
        string? fileName,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var award = await _awards.FindByIdAsync(awardId, cancellationToken).ConfigureAwait(false);
        if (award is null)
        {
            return FailRecipient("Không tìm thấy giải thưởng.");
        }

        var existing = await _recipients.ListByAwardAsync(awardId, cancellationToken).ConfigureAwait(false);
        if (existing.Any(recipient => recipient.UserId == userId))
        {
            return FailRecipient("Thành viên đã có trong danh sách nhận.");
        }

        var storedPath = string.Empty;

        if (attachment is not null && !string.IsNullOrWhiteSpace(fileName))
        {
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            {
                return FailRecipient("Chỉ nhận tệp PDF, JPG hoặc PNG cho chứng nhận.");
            }

            storedPath = await _files
                .SaveAsync(attachment, fileName, AttachmentFolder, cancellationToken)
                .ConfigureAwait(false);
        }

        AwardRecipient recipient;
        try
        {
            recipient = AwardRecipient.Create(awardId, userId, storedPath, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            _files.Delete(storedPath);
            return FailRecipient(ex.Message);
        }

        await _recipients.AddAsync(recipient, cancellationToken).ConfigureAwait(false);
        await AuditAsync("award.recipient.add", recipient.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<AwardRecipient>.Ok(recipient);
    }

    public async Task<Result> RemoveRecipientAsync(
        Guid awardId,
        Guid recipientId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var recipient = await _recipients.FindByIdAsync(recipientId, cancellationToken).ConfigureAwait(false);
        if (recipient is null || recipient.AwardId != awardId)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy người nhận."));
        }

        _files.Delete(recipient.Attachment);
        await _recipients.DeleteAsync(recipientId, cancellationToken).ConfigureAwait(false);
        await AuditAsync("award.recipient.remove", recipientId, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<IReadOnlyList<AwardListItem>> ListAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        var awards = clubId is null
            ? await _awards.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _awards.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);
        var rows = new List<AwardListItem>(awards.Count);

        foreach (var award in awards.OrderByDescending(item => item.AwardDate))
        {
            var recipients = await _recipients
                .ListByAwardAsync(award.Id, cancellationToken)
                .ConfigureAwait(false);
            rows.Add(new AwardListItem(award, recipients));
        }

        return rows;
    }

    public Task<Award?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _awards.FindByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<AwardRecipient>> ListRecipientsAsync(
        Guid awardId,
        CancellationToken cancellationToken = default)
        => _recipients.ListByAwardAsync(awardId, cancellationToken);

    public Stream? OpenAttachment(AwardRecipient recipient) => _files.OpenRead(recipient.Attachment);

    private async Task<string> NextCodeAsync(Guid clubId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = _codes.Next("BK");

            if (!await _awards.ExistsByCodeAsync(clubId, code, cancellationToken).ConfigureAwait(false))
            {
                return code;
            }
        }

        throw new DomainException("Không sinh được mã giải thưởng duy nhất.");
    }

    private Task AuditAsync(string action, Guid entityId, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(_clock.UtcNow, actor, action, "award", entityId.ToString("D"), string.Empty),
            cancellationToken);

    private static Result<Award> Fail(string message)
        => Result<Award>.Fail(new ValidationError(string.Empty, message));

    private static Result<AwardRecipient> FailRecipient(string message)
        => Result<AwardRecipient>.Fail(new ValidationError(string.Empty, message));
}
