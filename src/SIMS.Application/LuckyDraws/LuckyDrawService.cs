using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.LuckyDraws;

namespace SIMS.Application.LuckyDraws;

public sealed record LuckyDrawRow(
    LuckyDraw Draw,
    IReadOnlyList<LuckyPrize> Prizes,
    IReadOnlyList<LuckyWinner> Winners);

public sealed record PublicDrawView(
    LuckyDraw Draw,
    IReadOnlyList<LuckyPrize> Prizes,
    IReadOnlyList<LuckyWinner> Winners,
    bool RequiresSecurityCode);

public interface ILuckyDrawService
{
    Task<Result<LuckyDraw>> CreateAsync(
        Guid clubId,
        string title,
        string? securityCode,
        IEnumerable<string>? participants,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        bool isActive,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result<LuckyDraw>> UpdateAsync(
        Guid id,
        string title,
        string? securityCode,
        IEnumerable<string>? participants,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        bool isActive,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default);

    Task<Result<LuckyPrize>> AddPrizeAsync(
        Guid drawId, string name, int quantity, bool isSpecial, string actor,
        CancellationToken cancellationToken = default);

    Task<Result<LuckyPrize>> UpdatePrizeAsync(
        Guid prizeId, string name, int quantity, bool isSpecial, string actor,
        CancellationToken cancellationToken = default);

    Task<Result> DeletePrizeAsync(Guid prizeId, string actor, CancellationToken cancellationToken = default);

    Task<Result<LuckyWinner>> DrawAsync(
        Guid drawId, Guid prizeId, string actor, CancellationToken cancellationToken = default);

    Task<Result<int>> ClearWinnersAsync(Guid drawId, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LuckyDrawRow>> ListAsync(Guid? clubId, CancellationToken cancellationToken = default);

    Task<LuckyDrawRow?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PublicDrawView?> GetPublicAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Sinh slug không dấu + 8 ký tự ngẫu nhiên; dùng chung cho create/seed.</summary>
    static string BuildSlug(string title)
    {
        var normalized = (title ?? "lucky-draw")
            .Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => ch is 'đ' or 'Đ' ? 'd' : ch)
            .ToArray();

        var builder = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var baseSlug = builder.ToString().Trim('-');
        if (baseSlug.Length == 0)
        {
            baseSlug = "lucky-draw";
        }

        if (baseSlug.Length > 100)
        {
            baseSlug = baseSlug[..100].Trim('-');
        }

        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

        return $"{baseSlug}-{suffix}";
    }
}

public sealed class LuckyDrawService : ILuckyDrawService
{
    private readonly ILuckyDrawRepository _draws;
    private readonly ILuckyPrizeRepository _prizes;
    private readonly ILuckyWinnerRepository _winners;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public LuckyDrawService(
        ILuckyDrawRepository draws,
        ILuckyPrizeRepository prizes,
        ILuckyWinnerRepository winners,
        IAuditLog audit,
        IClock clock)
    {
        _draws = draws;
        _prizes = prizes;
        _winners = winners;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<LuckyDraw>> CreateAsync(
        Guid clubId,
        string title,
        string? securityCode,
        IEnumerable<string>? participants,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        bool isActive,
        string actor,
        CancellationToken cancellationToken = default)
    {
        LuckyDraw draw;
        try
        {
            var slug = await NextSlugAsync(title, cancellationToken).ConfigureAwait(false);
            draw = LuckyDraw.Create(
                clubId, title, slug, securityCode, participants, startAtUtc, endAtUtc, isActive,
                actor, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _draws.AddAsync(draw, cancellationToken).ConfigureAwait(false);
        await AuditAsync("lucky.create", draw.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<LuckyDraw>.Ok(draw);
    }

    public async Task<Result<LuckyDraw>> UpdateAsync(
        Guid id,
        string title,
        string? securityCode,
        IEnumerable<string>? participants,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        bool isActive,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var draw = await _draws.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (draw is null)
        {
            return Fail("Không tìm thấy chương trình.");
        }

        try
        {
            draw.Update(title, securityCode, participants, startAtUtc, endAtUtc, isActive);
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }

        await _draws.UpdateAsync(draw, cancellationToken).ConfigureAwait(false);
        await AuditAsync("lucky.update", draw.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<LuckyDraw>.Ok(draw);
    }

    public async Task<Result> DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var draw = await _draws.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (draw is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy chương trình."));
        }

        foreach (var winner in await _winners.ListByDrawAsync(id, cancellationToken).ConfigureAwait(false))
        {
            await _winners.DeleteAsync(winner.Id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var prize in await _prizes.ListByDrawAsync(id, cancellationToken).ConfigureAwait(false))
        {
            await _prizes.DeleteAsync(prize.Id, cancellationToken).ConfigureAwait(false);
        }

        await _draws.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditAsync("lucky.delete", id, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<Result<LuckyPrize>> AddPrizeAsync(
        Guid drawId,
        string name,
        int quantity,
        bool isSpecial,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var draw = await _draws.FindByIdAsync(drawId, cancellationToken).ConfigureAwait(false);
        if (draw is null)
        {
            return FailPrize("Không tìm thấy chương trình.");
        }

        var existing = await _prizes.ListByDrawAsync(drawId, cancellationToken).ConfigureAwait(false);
        if (existing.Any(prize => string.Equals(prize.Name, name?.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        {
            return FailPrize("Tên giải đã tồn tại trong chương trình.");
        }

        LuckyPrize prize;
        try
        {
            prize = LuckyPrize.Create(drawId, name, quantity, existing.Count + 1, isSpecial);
        }
        catch (DomainException ex)
        {
            return FailPrize(ex.Message);
        }

        await _prizes.AddAsync(prize, cancellationToken).ConfigureAwait(false);
        await AuditAsync("lucky.prize.add", prize.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<LuckyPrize>.Ok(prize);
    }

    public async Task<Result<LuckyPrize>> UpdatePrizeAsync(
        Guid prizeId,
        string name,
        int quantity,
        bool isSpecial,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var prize = await _prizes.FindByIdAsync(prizeId, cancellationToken).ConfigureAwait(false);
        if (prize is null)
        {
            return FailPrize("Không tìm thấy giải thưởng.");
        }

        var duplicates = (await _prizes.ListByDrawAsync(prize.LuckyDrawId, cancellationToken).ConfigureAwait(false))
            .Any(item => item.Id != prizeId &&
                         string.Equals(item.Name, name?.Trim(), StringComparison.CurrentCultureIgnoreCase));

        if (duplicates)
        {
            return FailPrize("Tên giải đã tồn tại trong chương trình.");
        }

        try
        {
            prize.Update(name, quantity, isSpecial);
        }
        catch (DomainException ex)
        {
            return FailPrize(ex.Message);
        }

        await _prizes.UpdateAsync(prize, cancellationToken).ConfigureAwait(false);
        await AuditAsync("lucky.prize.update", prize.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<LuckyPrize>.Ok(prize);
    }

    public async Task<Result> DeletePrizeAsync(
        Guid prizeId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var prize = await _prizes.FindByIdAsync(prizeId, cancellationToken).ConfigureAwait(false);
        if (prize is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy giải thưởng."));
        }

        var winners = await _winners.ListByDrawAsync(prize.LuckyDrawId, cancellationToken).ConfigureAwait(false);
        if (winners.Any(winner => winner.PrizeName == prize.Name))
        {
            return Result.Fail(new ValidationError(
                string.Empty,
                "Không thể xoá giải đã có người trúng."));
        }

        await _prizes.DeleteAsync(prizeId, cancellationToken).ConfigureAwait(false);
        await AuditAsync("lucky.prize.delete", prizeId, actor, cancellationToken).ConfigureAwait(false);

        return Result.Ok();
    }

    public async Task<Result<LuckyWinner>> DrawAsync(
        Guid drawId,
        Guid prizeId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var draw = await _draws.FindByIdAsync(drawId, cancellationToken).ConfigureAwait(false);
        if (draw is null)
        {
            return FailWinner("Không tìm thấy chương trình.");
        }

        if (!draw.IsOpen(_clock.UtcNow))
        {
            return FailWinner("Chương trình chưa mở hoặc đã kết thúc.");
        }

        var prize = await _prizes.FindByIdAsync(prizeId, cancellationToken).ConfigureAwait(false);
        if (prize is null || prize.LuckyDrawId != drawId)
        {
            return FailWinner("Giải thưởng không thuộc chương trình.");
        }

        var winners = await _winners.ListByDrawAsync(drawId, cancellationToken).ConfigureAwait(false);
        var wonByPrize = winners.Count(winner => winner.PrizeName == prize.Name);

        if (wonByPrize >= prize.Quantity)
        {
            return FailWinner("Giải thưởng đã hết lượt.");
        }

        var wonNames = winners
            .Select(winner => winner.Participant)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var candidates = draw.ParticipantList
            .Where(name => !wonNames.Contains(name))
            .ToArray();

        if (candidates.Length == 0)
        {
            return FailWinner("Không còn người tham gia chưa trúng thưởng.");
        }

        var picked = candidates[RandomNumberGenerator.GetInt32(candidates.Length)];
        var winner = LuckyWinner.Create(drawId, picked, prize.Name, _clock.UtcNow);

        await _winners.AddAsync(winner, cancellationToken).ConfigureAwait(false);
        await AuditAsync("lucky.draw", winner.Id, actor, cancellationToken).ConfigureAwait(false);

        return Result<LuckyWinner>.Ok(winner);
    }

    public async Task<Result<int>> ClearWinnersAsync(
        Guid drawId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var draw = await _draws.FindByIdAsync(drawId, cancellationToken).ConfigureAwait(false);
        if (draw is null)
        {
            return Result<int>.Fail(new ValidationError(string.Empty, "Không tìm thấy chương trình."));
        }

        var winners = await _winners.ListByDrawAsync(drawId, cancellationToken).ConfigureAwait(false);
        foreach (var winner in winners)
        {
            await _winners.DeleteAsync(winner.Id, cancellationToken).ConfigureAwait(false);
        }

        await AuditAsync("lucky.winners.clear", drawId, actor, cancellationToken).ConfigureAwait(false);

        return Result<int>.Ok(winners.Count);
    }

    public async Task<IReadOnlyList<LuckyDrawRow>> ListAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default)
    {
        var draws = clubId is null
            ? await _draws.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _draws.ListByClubAsync(clubId.Value, cancellationToken).ConfigureAwait(false);

        var rows = new List<LuckyDrawRow>(draws.Count);
        foreach (var draw in draws.OrderByDescending(item => item.CreatedAtUtc))
        {
            rows.Add(await BuildRowAsync(draw, cancellationToken).ConfigureAwait(false));
        }

        return rows;
    }

    public async Task<LuckyDrawRow?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var draw = await _draws.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return draw is null ? null : await BuildRowAsync(draw, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PublicDrawView?> GetPublicAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var draw = await _draws.FindBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        if (draw is null)
        {
            return null;
        }

        var row = await BuildRowAsync(draw, cancellationToken).ConfigureAwait(false);

        return new PublicDrawView(
            row.Draw,
            row.Prizes,
            row.Winners,
            draw.SecurityCode.Length > 0);
    }

    private async Task<LuckyDrawRow> BuildRowAsync(LuckyDraw draw, CancellationToken cancellationToken)
    {
        var prizes = await _prizes.ListByDrawAsync(draw.Id, cancellationToken).ConfigureAwait(false);
        var winners = await _winners.ListByDrawAsync(draw.Id, cancellationToken).ConfigureAwait(false);

        return new LuckyDrawRow(
            draw,
            prizes.OrderBy(prize => prize.Order).ToArray(),
            winners.OrderByDescending(winner => winner.WonAtUtc).ToArray());
    }

    private async Task<string> NextSlugAsync(string title, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var slug = ILuckyDrawService.BuildSlug(title);

            if (!await _draws.ExistsBySlugAsync(slug, cancellationToken).ConfigureAwait(false))
            {
                return slug;
            }
        }

        throw new DomainException("Không sinh được slug duy nhất.");
    }

    private Task AuditAsync(string action, Guid entityId, string actor, CancellationToken cancellationToken)
        => _audit.WriteAsync(
            new AuditEntry(_clock.UtcNow, actor, action, "lucky_draw", entityId.ToString("D"), string.Empty),
            cancellationToken);

    private static Result<LuckyDraw> Fail(string message)
        => Result<LuckyDraw>.Fail(new ValidationError(string.Empty, message));

    private static Result<LuckyPrize> FailPrize(string message)
        => Result<LuckyPrize>.Fail(new ValidationError(string.Empty, message));

    private static Result<LuckyWinner> FailWinner(string message)
        => Result<LuckyWinner>.Fail(new ValidationError(string.Empty, message));
}
