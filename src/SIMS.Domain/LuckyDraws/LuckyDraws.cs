using System.Text.RegularExpressions;
using SIMS.Domain.Common;

namespace SIMS.Domain.LuckyDraws;

/// <summary>Chương trình bốc thăm may mắn của CLB (slug công khai, mã bảo mật, danh sách người tham gia).</summary>
public sealed class LuckyDraw : Entity
{
    private static readonly Regex SlugPattern =
        new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    private LuckyDraw()
    {
        Title = string.Empty;
        Slug = string.Empty;
        SecurityCode = string.Empty;
        Participants = string.Empty;
        CreatedBy = string.Empty;
    }

    public Guid ClubId { get; private set; }

    public string Title { get; private set; }

    public string Slug { get; private set; }

    public string SecurityCode { get; private set; }

    /// <summary>Tên người tham gia, phân tách bằng '|'.</summary>
    public string Participants { get; private set; }

    public DateTimeOffset StartAtUtc { get; private set; }

    public DateTimeOffset EndAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<string> ParticipantList
        => Participants.Length == 0
            ? Array.Empty<string>()
            : Participants.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static LuckyDraw Create(
        Guid clubId,
        string title,
        string slug,
        string? securityCode,
        IEnumerable<string>? participants,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        bool isActive,
        string createdBy,
        DateTimeOffset now)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        if (startAtUtc >= endAtUtc)
        {
            throw new DomainException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        }

        var actor = (createdBy ?? string.Empty).Trim();

        return new LuckyDraw
        {
            ClubId = clubId,
            Title = RequireText(title, 255, "Tên chương trình"),
            Slug = RequireSlug(slug),
            SecurityCode = RequireCode(securityCode),
            Participants = NormalizeParticipants(participants),
            StartAtUtc = startAtUtc,
            EndAtUtc = endAtUtc,
            IsActive = isActive,
            CreatedBy = actor.Length == 0 ? "system" : actor,
            CreatedAtUtc = now,
        };
    }

    public static LuckyDraw Restore(
        Guid id,
        Guid clubId,
        string title,
        string slug,
        string? securityCode,
        string? participants,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        bool isActive,
        string createdBy,
        DateTimeOffset createdAtUtc)
    {
        var draw = Create(
            clubId,
            title,
            slug,
            securityCode,
            ParseParticipants(participants),
            startAtUtc,
            endAtUtc,
            isActive,
            createdBy,
            createdAtUtc);
        draw.Id = id;

        return draw;
    }

    public void Update(
        string title,
        string? securityCode,
        IEnumerable<string>? participants,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        bool isActive)
    {
        if (startAtUtc >= endAtUtc)
        {
            throw new DomainException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        }

        Title = RequireText(title, 255, "Tên chương trình");
        SecurityCode = RequireCode(securityCode);
        Participants = NormalizeParticipants(participants);
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        IsActive = isActive;
    }

    public bool IsOpen(DateTimeOffset now)
        => IsActive && StartAtUtc <= now && EndAtUtc >= now;

    private static string RequireSlug(string slug)
    {
        var value = (slug ?? string.Empty).Trim();

        return value.Length is >= 3 and <= 120 && SlugPattern.IsMatch(value)
            ? value
            : throw new DomainException("Slug phải là chữ thường, số và dấu gạch nối.");
    }

    private static string RequireCode(string? securityCode)
    {
        var value = (securityCode ?? string.Empty).Trim();

        if (value.Length > 20)
        {
            throw new DomainException("Mã bảo mật tối đa 20 ký tự.");
        }

        return value;
    }

    private static string RequireText(string value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new DomainException($"{fieldName} là bắt buộc.");
        }

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text;
    }

    private static string NormalizeParticipants(IEnumerable<string>? participants)
    {
        if (participants is null)
        {
            return string.Empty;
        }

        var names = participants
            .Select(name => (name ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .Select(name => name.Replace("|", " ", StringComparison.Ordinal))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(500)
            .ToArray();

        if (names.Any(name => name.Length > 100))
        {
            throw new DomainException("Tên người tham gia tối đa 100 ký tự.");
        }

        return string.Join('|', names);
    }

    private static IReadOnlyList<string> ParseParticipants(string? participants)
        => string.IsNullOrWhiteSpace(participants)
            ? Array.Empty<string>()
            : participants.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Giải thưởng của chương trình (số lượng, thứ tự, giải đặc biệt).</summary>
public sealed class LuckyPrize : Entity
{
    private LuckyPrize() => Name = string.Empty;

    public Guid LuckyDrawId { get; private set; }

    public string Name { get; private set; }

    public int Quantity { get; private set; }

    public int Order { get; private set; }

    public bool IsSpecial { get; private set; }

    public static LuckyPrize Create(
        Guid luckyDrawId,
        string name,
        int quantity,
        int order,
        bool isSpecial)
    {
        if (luckyDrawId == Guid.Empty)
        {
            throw new DomainException("Chương trình không hợp lệ.");
        }

        if (quantity is < 1 or > 1000)
        {
            throw new DomainException("Số lượng giải phải từ 1 đến 1000.");
        }

        if (order < 0)
        {
            throw new DomainException("Thứ tự giải không hợp lệ.");
        }

        return new LuckyPrize
        {
            LuckyDrawId = luckyDrawId,
            Name = RequireName(name),
            Quantity = quantity,
            Order = order,
            IsSpecial = isSpecial,
        };
    }

    public static LuckyPrize Restore(
        Guid id,
        Guid luckyDrawId,
        string name,
        int quantity,
        int order,
        bool isSpecial)
    {
        var prize = Create(luckyDrawId, name, quantity, order, isSpecial);
        prize.Id = id;

        return prize;
    }

    public void Update(string name, int quantity, bool isSpecial)
    {
        if (quantity is < 1 or > 1000)
        {
            throw new DomainException("Số lượng giải phải từ 1 đến 1000.");
        }

        Name = RequireName(name);
        Quantity = quantity;
        IsSpecial = isSpecial;
    }

    private static string RequireName(string name)
    {
        var value = (name ?? string.Empty).Trim();

        if (value.Length == 0)
        {
            throw new DomainException("Tên giải thưởng là bắt buộc.");
        }

        if (value.Length > 100)
        {
            throw new DomainException("Tên giải thưởng tối đa 100 ký tự.");
        }

        return value;
    }
}

/// <summary>Một lượt trúng thưởng (tên người tham gia + giải + thời điểm).</summary>
public sealed class LuckyWinner : Entity
{
    private LuckyWinner()
    {
        Participant = string.Empty;
        PrizeName = string.Empty;
    }

    public Guid LuckyDrawId { get; private set; }

    public string Participant { get; private set; }

    public string PrizeName { get; private set; }

    public DateTimeOffset WonAtUtc { get; private set; }

    public static LuckyWinner Create(
        Guid luckyDrawId,
        string participant,
        string prizeName,
        DateTimeOffset wonAtUtc)
    {
        if (luckyDrawId == Guid.Empty)
        {
            throw new DomainException("Chương trình không hợp lệ.");
        }

        var name = (participant ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 100)
        {
            throw new DomainException("Tên người trúng thưởng không hợp lệ.");
        }

        var prize = (prizeName ?? string.Empty).Trim();
        if (prize.Length == 0 || prize.Length > 100)
        {
            throw new DomainException("Tên giải thưởng không hợp lệ.");
        }

        return new LuckyWinner
        {
            LuckyDrawId = luckyDrawId,
            Participant = name,
            PrizeName = prize,
            WonAtUtc = wonAtUtc,
        };
    }

    public static LuckyWinner Restore(
        Guid id,
        Guid luckyDrawId,
        string participant,
        string prizeName,
        DateTimeOffset wonAtUtc)
    {
        var winner = Create(luckyDrawId, participant, prizeName, wonAtUtc);
        winner.Id = id;

        return winner;
    }
}
