using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Events;

namespace SIMS.Application.Events;

public interface IEventImageService
{
    Task<Result<EventImage>> AddAsync(
        Guid eventId,
        string? caption,
        Stream content,
        string fileName,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventImage>> ListAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<EventImage?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Stream? OpenFile(EventImage image);
}

public sealed class EventImageService : IEventImageService
{
    private const string ImageFolder = "events";

    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp",
        };

    private readonly IEventImageRepository _images;
    private readonly IEventRepository _events;
    private readonly IFileStorage _files;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public EventImageService(
        IEventImageRepository images,
        IEventRepository events,
        IFileStorage files,
        IAuditLog audit,
        IClock clock)
    {
        _images = images;
        _events = events;
        _files = files;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<EventImage>> AddAsync(
        Guid eventId,
        string? caption,
        Stream content,
        string fileName,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var @event = await _events.FindByIdAsync(eventId, cancellationToken).ConfigureAwait(false);
        if (@event is null)
        {
            return Fail("Không tìm thấy sự kiện.");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return Fail("Chỉ nhận ảnh JPG, JPEG, PNG hoặc WEBP.");
        }

        var storedPath = await _files
            .SaveAsync(content, fileName, ImageFolder, cancellationToken)
            .ConfigureAwait(false);

        EventImage image;
        try
        {
            image = EventImage.Create(eventId, storedPath, caption, actor, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            _files.Delete(storedPath);
            return Fail(ex.Message);
        }

        await _images.AddAsync(image, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow, actor, "event.image.add", "event_image", image.Id.ToString("D"),
                $"event={eventId:D}"),
            cancellationToken).ConfigureAwait(false);

        return Result<EventImage>.Ok(image);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var image = await _images.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (image is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy ảnh."));
        }

        await _images.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        _files.Delete(image.FilePath);
        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow, actor, "event.image.delete", "event_image", id.ToString("D"),
                $"event={image.EventId:D}"),
            cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public Task<IReadOnlyList<EventImage>> ListAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
        => _images.ListByEventAsync(eventId, cancellationToken);

    public Task<EventImage?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _images.FindByIdAsync(id, cancellationToken);

    public Stream? OpenFile(EventImage image) => _files.OpenRead(image.FilePath);

    private static Result<EventImage> Fail(string message)
        => Result<EventImage>.Fail(new ValidationError(string.Empty, message));
}
