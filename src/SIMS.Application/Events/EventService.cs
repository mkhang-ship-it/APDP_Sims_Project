using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Events;

namespace SIMS.Application.Events;

public sealed record EventInput(
    string Code,
    string Title,
    string Description,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string Location,
    int Capacity,
    GeoPoint? Venue = null);

public sealed class EventValidator : IValidator<EventInput>
{
    public ValidationResult Validate(EventInput input)
    {
        var errors = new List<ValidationError>();
        var code = (input.Code ?? string.Empty).Trim();

        if (code.Length is < 3 or > 24)
        {
            errors.Add(new ValidationError(nameof(EventInput.Code), "Mã sự kiện phải có từ 3 đến 24 ký tự."));
        }
        else if (!code.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
        {
            errors.Add(new ValidationError(nameof(EventInput.Code), "Mã sự kiện chỉ được chứa chữ, số, '-' hoặc '_'."));
        }

        var title = (input.Title ?? string.Empty).Trim();
        if (title.Length is < 3 or > 160)
        {
            errors.Add(new ValidationError(nameof(EventInput.Title), "Tên sự kiện phải có từ 3 đến 160 ký tự."));
        }

        if (input.EndsAtUtc <= input.StartsAtUtc)
        {
            errors.Add(new ValidationError(nameof(EventInput.EndsAtUtc), "Thời gian kết thúc phải sau thời gian bắt đầu."));
        }

        var location = (input.Location ?? string.Empty).Trim();
        if (location.Length is < 2 or > 160)
        {
            errors.Add(new ValidationError(nameof(EventInput.Location), "Địa điểm phải có từ 2 đến 160 ký tự."));
        }

        if (input.Capacity is < 1 or > 1000)
        {
            errors.Add(new ValidationError(nameof(EventInput.Capacity), "Sức chứa phải từ 1 đến 1000."));
        }

        if ((input.Description ?? string.Empty).Trim().Length > 2000)
        {
            errors.Add(new ValidationError(nameof(EventInput.Description), "Mô tả tối đa 2000 ký tự."));
        }

        return errors.Count == 0 ? ValidationResult.Ok : ValidationResult.Fail(errors);
    }
}

public interface IEventService
{
    Task<IReadOnlyList<Event>> ListAsync(Guid? clubId, CancellationToken cancellationToken = default);

    Task<Event?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Event>> CreateAsync(
        EventInput input,
        Guid? clubId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<Event>> UpdateAsync(
        Guid id,
        EventInput input,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> PublishAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<Result> CancelAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<Result> CompleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<Result<string>> RotateCheckinTokenAsync(Guid id, string actor, CancellationToken cancellationToken = default);
}

/// <summary>Use case EVENTS: CRUD, công bố/huỷ/hoàn thành, xoay token check-in, audit mọi ghi.</summary>
public sealed class EventService : IEventService
{
    private readonly IEventRepository _events;
    private readonly IValidator<EventInput> _validator;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public EventService(
        IEventRepository events,
        IValidator<EventInput> validator,
        IAuditLog audit,
        IClock clock)
    {
        _events = events;
        _validator = validator;
        _audit = audit;
        _clock = clock;
    }

    public Task<IReadOnlyList<Event>> ListAsync(Guid? clubId, CancellationToken cancellationToken = default)
        => clubId is null || clubId == Guid.Empty
            ? _events.ListAsync(cancellationToken)
            : _events.ListByClubAsync(clubId.Value, cancellationToken);

    public Task<Event?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _events.FindByIdAsync(id, cancellationToken);

    public async Task<Result<Event>> CreateAsync(
        EventInput input,
        Guid? clubId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return Result<Event>.Fail(validation.Errors);
        }

        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await _events.ExistsByCodeAsync(code, null, cancellationToken))
        {
            return Result<Event>.Fail(new ValidationError(nameof(EventInput.Code), $"Mã sự kiện '{code}' đã tồn tại."));
        }

        var created = Event.Create(
            input.Code ?? string.Empty,
            input.Title,
            input.Description,
            input.StartsAtUtc,
            input.EndsAtUtc,
            input.Location,
            input.Capacity,
            clubId,
            _clock.UtcNow,
            input.Venue);

        await _events.AddAsync(created, cancellationToken);
        await AuditAsync("event.create", created, actor, cancellationToken);

        return Result<Event>.Ok(created);
    }

    public async Task<Result<Event>> UpdateAsync(
        Guid id,
        EventInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return Result<Event>.Fail(validation.Errors);
        }

        var existing = await _events.FindByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Result<Event>.Fail(new ValidationError(string.Empty, "Không tìm thấy sự kiện."));
        }

        if (!string.Equals(existing.Code, (input.Code ?? string.Empty).Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            return Result<Event>.Fail(new ValidationError(nameof(EventInput.Code), "Không được đổi mã sự kiện sau khi tạo."));
        }

        existing.UpdateDetails(
            input.Title,
            input.Description,
            input.StartsAtUtc,
            input.EndsAtUtc,
            input.Location,
            input.Capacity,
            input.Venue);

        await _events.UpdateAsync(existing, cancellationToken);
        await AuditAsync("event.update", existing, actor, cancellationToken);

        return Result<Event>.Ok(existing);
    }

    public Task<Result> PublishAsync(Guid id, string actor, CancellationToken cancellationToken = default)
        => ChangeStatusAsync(id, actor, e => e.Publish(), "event.publish", cancellationToken);

    public Task<Result> CancelAsync(Guid id, string actor, CancellationToken cancellationToken = default)
        => ChangeStatusAsync(id, actor, e => e.Cancel(), "event.cancel", cancellationToken);

    public Task<Result> CompleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
        => ChangeStatusAsync(id, actor, e => e.Complete(), "event.complete", cancellationToken);

    public async Task<Result<string>> RotateCheckinTokenAsync(
        Guid id,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var existing = await _events.FindByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Result<string>.Fail(new ValidationError(string.Empty, "Không tìm thấy sự kiện."));
        }

        existing.RotateCheckinToken();
        await _events.UpdateAsync(existing, cancellationToken);
        await AuditAsync("event.checkin.rotate", existing, actor, cancellationToken);

        return Result<string>.Ok(existing.CheckinToken);
    }

    private async Task<Result> ChangeStatusAsync(
        Guid id,
        string actor,
        Action<Event> change,
        string action,
        CancellationToken cancellationToken)
    {
        var existing = await _events.FindByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy sự kiện."));
        }

        try
        {
            change(existing);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Fail(new ValidationError(string.Empty, ex.Message));
        }

        await _events.UpdateAsync(existing, cancellationToken);
        await AuditAsync(action, existing, actor, cancellationToken);

        return Result.Ok();
    }

    private Task AuditAsync(string action, Event @event, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                action,
                "event",
                @event.Id.ToString("D"),
                $"{@event.Code} · {@event.Title} · {@event.Status}"),
            cancellationToken);
}
