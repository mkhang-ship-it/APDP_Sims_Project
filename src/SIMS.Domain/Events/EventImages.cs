using SIMS.Domain.Common;

namespace SIMS.Domain.Events;

/// <summary>Ảnh của sự kiện (file lưu ngoài CSV, trong data/files/events).</summary>
public sealed class EventImage : Entity
{
    private EventImage()
    {
        FilePath = string.Empty;
        Caption = string.Empty;
        UploadedBy = string.Empty;
    }

    public Guid EventId { get; private set; }

    public string FilePath { get; private set; }

    public string Caption { get; private set; }

    public string UploadedBy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static EventImage Create(
        Guid eventId,
        string filePath,
        string? caption,
        string uploadedBy,
        DateTimeOffset now)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("Sự kiện không hợp lệ.");
        }

        var path = (filePath ?? string.Empty).Trim();
        if (path.Length == 0 || path.Length > 500)
        {
            throw new DomainException("Đường dẫn ảnh không hợp lệ.");
        }

        var text = (caption ?? string.Empty).Trim();
        if (text.Length > 200)
        {
            throw new DomainException("Chú thích ảnh tối đa 200 ký tự.");
        }

        var actor = (uploadedBy ?? string.Empty).Trim();

        return new EventImage
        {
            EventId = eventId,
            FilePath = path,
            Caption = text,
            UploadedBy = actor.Length == 0 ? "system" : actor,
            CreatedAtUtc = now,
        };
    }

    public static EventImage Restore(
        Guid id,
        Guid eventId,
        string filePath,
        string? caption,
        string uploadedBy,
        DateTimeOffset createdAtUtc)
    {
        var image = Create(eventId, filePath, caption, uploadedBy, createdAtUtc);
        image.Id = id;

        return image;
    }
}
