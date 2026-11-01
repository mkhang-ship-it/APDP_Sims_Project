using SIMS.Domain.Common;

namespace SIMS.Domain.Events;

/// <summary>
/// Sự kiện của CLB: lịch, sức chứa, trạng thái và token check-in.
/// Token là "nội dung QR": link <c>/Checkin?code=TOKEN</c> có thể in/chiếu cho sinh viên quét.
/// </summary>
public sealed class Event : Entity
{
    private Event()
    {
        Code = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        Location = string.Empty;
        CheckinToken = string.Empty;
    }

    public string Code { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public DateTimeOffset EndsAtUtc { get; private set; }

    public string Location { get; private set; }

    public int Capacity { get; private set; }

    public EventStatus Status { get; private set; }

    public Guid? ClubId { get; private set; }

    public string CheckinToken { get; private set; }

    /// <summary>Toạ độ địa điểm (tuỳ chọn). Có toạ độ thì check-in bị giới hạn bán kính 5 km.</summary>
    public GeoPoint? Venue { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Event Create(
        string code,
        string title,
        string description,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string location,
        int capacity,
        Guid? clubId,
        DateTimeOffset createdAtUtc,
        GeoPoint? venue = null)
    {
        ValidateWindow(startsAtUtc, endsAtUtc);

        return new Event
        {
            Code = NormalizeCode(code),
            Title = NormalizeTitle(title),
            Description = NormalizeDescription(description),
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            Location = NormalizeLocation(location),
            Capacity = NormalizeCapacity(capacity),
            Status = EventStatus.Draft,
            ClubId = clubId is null || clubId.Value == Guid.Empty ? null : clubId,
            CheckinToken = NewToken(),
            Venue = venue,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public static Event Restore(
        Guid id,
        string code,
        string title,
        string description,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string location,
        int capacity,
        EventStatus status,
        Guid? clubId,
        string checkinToken,
        DateTimeOffset createdAtUtc,
        GeoPoint? venue = null)
    {
        var @event = Create(
            code, title, description, startsAtUtc, endsAtUtc, location, capacity, clubId, createdAtUtc, venue);
        @event.Id = id;
        @event.Status = status;
        @event.CheckinToken = NormalizeToken(checkinToken);

        return @event;
    }

    public void UpdateDetails(
        string title,
        string description,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string location,
        int capacity,
        GeoPoint? venue = null)
    {
        ValidateWindow(startsAtUtc, endsAtUtc);

        Title = NormalizeTitle(title);
        Description = NormalizeDescription(description);
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Location = NormalizeLocation(location);
        Capacity = NormalizeCapacity(capacity);
        Venue = venue;
    }

    public void Publish()
    {
        if (Status is EventStatus.Completed or EventStatus.Cancelled)
        {
            throw new DomainException("Sự kiện đã kết thúc hoặc đã huỷ, không thể công bố.");
        }

        Status = EventStatus.Published;
    }

    public void Cancel()
    {
        if (Status == EventStatus.Completed)
        {
            throw new DomainException("Sự kiện đã kết thúc, không thể huỷ.");
        }

        Status = EventStatus.Cancelled;
    }

    public void Complete()
    {
        if (Status == EventStatus.Cancelled)
        {
            throw new DomainException("Sự kiện đã huỷ, không thể hoàn thành.");
        }

        Status = EventStatus.Completed;
    }

    public void RotateCheckinToken() => CheckinToken = NewToken();

    /// <summary>Chỉ nhận check-in khi đã công bố và đang trong khoảng thời gian (mở sớm 1 giờ).</summary>
    public bool IsOpenForCheckin(DateTimeOffset now)
        => Status == EventStatus.Published && now >= StartsAtUtc.AddHours(-1) && now <= EndsAtUtc;

    private static void ValidateWindow(DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        if (endsAtUtc <= startsAtUtc)
        {
            throw new DomainException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        }
    }

    private static string NormalizeCode(string code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (value.Length is < 3 or > 24)
        {
            throw new DomainException("Mã sự kiện phải có từ 3 đến 24 ký tự.");
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '-' and not '_')
            {
                throw new DomainException("Mã sự kiện chỉ được chứa chữ, số, '-' hoặc '_'.");
            }
        }

        return value;
    }

    private static string NormalizeTitle(string title)
    {
        var value = (title ?? string.Empty).Trim();

        if (value.Length is < 3 or > 160)
        {
            throw new DomainException("Tên sự kiện phải có từ 3 đến 160 ký tự.");
        }

        return value;
    }

    private static string NormalizeDescription(string description)
    {
        var value = (description ?? string.Empty).Trim();

        if (value.Length > 2000)
        {
            throw new DomainException("Mô tả sự kiện tối đa 2000 ký tự.");
        }

        return value;
    }

    private static string NormalizeLocation(string location)
    {
        var value = (location ?? string.Empty).Trim();

        if (value.Length is < 2 or > 160)
        {
            throw new DomainException("Địa điểm phải có từ 2 đến 160 ký tự.");
        }

        return value;
    }

    private static int NormalizeCapacity(int capacity)
    {
        if (capacity is < 1 or > 1000)
        {
            throw new DomainException("Sức chứa sự kiện phải từ 1 đến 1000.");
        }

        return capacity;
    }

    private static string NormalizeToken(string token)
    {
        var value = (token ?? string.Empty).Trim().ToUpperInvariant();

        if (value.Length is < 8 or > 24)
        {
            throw new DomainException("Token check-in không hợp lệ.");
        }

        return value;
    }

    private static string NewToken()
        => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}
