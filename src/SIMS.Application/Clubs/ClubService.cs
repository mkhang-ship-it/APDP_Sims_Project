using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Clubs;
using SIMS.Domain.Security;

namespace SIMS.Application.Clubs;

public sealed record ClubInput(string Code, string Name);

public sealed class ClubValidator : IValidator<ClubInput>
{
    public ValidationResult Validate(ClubInput input)
    {
        var errors = new List<ValidationError>();
        var code = (input.Code ?? string.Empty).Trim();

        if (code.Length is < 2 or > 20)
        {
            errors.Add(new ValidationError(nameof(ClubInput.Code), "Mã CLB phải có từ 2 đến 20 ký tự."));
        }
        else if (!code.All(char.IsLetterOrDigit))
        {
            errors.Add(new ValidationError(nameof(ClubInput.Code), "Mã CLB chỉ được chứa chữ và số."));
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 2 or > 120)
        {
            errors.Add(new ValidationError(nameof(ClubInput.Name), "Tên CLB phải có từ 2 đến 120 ký tự."));
        }

        return errors.Count == 0 ? ValidationResult.Ok : ValidationResult.Fail(errors);
    }
}

public interface IClubService
{
    Task<IReadOnlyList<Club>> ListAsync(CancellationToken cancellationToken = default);

    Task<Club?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Club>> CreateAsync(ClubInput input, string actor, CancellationToken cancellationToken = default);

    Task<Result<Club>> UpdateAsync(Guid id, ClubInput input, string actor, CancellationToken cancellationToken = default);

    Task<Result> AssignMemberAsync(
        Guid clubId,
        Guid userId,
        Role role,
        string actor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClubMembership>> ListMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClubMembership>> ListMembersAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);

    Task<Club?> GetPrimaryClubAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Use case CLB + multi-club membership. Audit cho mọi thao tác ghi.</summary>
public sealed class ClubService : IClubService
{
    private readonly IClubRepository _clubs;
    private readonly IClubMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly IValidator<ClubInput> _validator;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public ClubService(
        IClubRepository clubs,
        IClubMembershipRepository memberships,
        IUserRepository users,
        IValidator<ClubInput> validator,
        IAuditLog audit,
        IClock clock)
    {
        _clubs = clubs;
        _memberships = memberships;
        _users = users;
        _validator = validator;
        _audit = audit;
        _clock = clock;
    }

    public Task<IReadOnlyList<Club>> ListAsync(CancellationToken cancellationToken = default)
        => _clubs.ListAsync(cancellationToken);

    public Task<Club?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _clubs.FindByIdAsync(id, cancellationToken);

    public async Task<Result<Club>> CreateAsync(
        ClubInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return Result<Club>.Fail(validation.Errors);
        }

        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await _clubs.ExistsByCodeAsync(code, null, cancellationToken))
        {
            return Result<Club>.Fail(new ValidationError(nameof(ClubInput.Code), $"Mã CLB '{code}' đã tồn tại."));
        }

        var club = Club.Create(input.Code ?? string.Empty, input.Name, _clock.UtcNow);
        await _clubs.AddAsync(club, cancellationToken);

        await _audit.WriteAsync(
            new AuditEntry(_clock.UtcNow, actor, "club.create", "club", club.Id.ToString("D"), $"{club.Code} · {club.Name}"),
            cancellationToken);

        return Result<Club>.Ok(club);
    }

    public async Task<Result<Club>> UpdateAsync(
        Guid id,
        ClubInput input,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(input);
        if (!validation.IsValid)
        {
            return Result<Club>.Fail(validation.Errors);
        }

        var club = await _clubs.FindByIdAsync(id, cancellationToken);
        if (club is null)
        {
            return Result<Club>.Fail(new ValidationError(string.Empty, "Không tìm thấy CLB."));
        }

        if (!string.Equals(club.Code, (input.Code ?? string.Empty).Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            return Result<Club>.Fail(new ValidationError(nameof(ClubInput.Code), "Không được đổi mã CLB sau khi tạo."));
        }

        club.UpdateDetails(input.Name);
        await _clubs.UpdateAsync(club, cancellationToken);

        await _audit.WriteAsync(
            new AuditEntry(_clock.UtcNow, actor, "club.update", "club", club.Id.ToString("D"), $"{club.Code} · {club.Name}"),
            cancellationToken);

        return Result<Club>.Ok(club);
    }

    public async Task<Result> AssignMemberAsync(
        Guid clubId,
        Guid userId,
        Role role,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var club = await _clubs.FindByIdAsync(clubId, cancellationToken);
        if (club is null || !club.IsActive)
        {
            return Result.Fail(new ValidationError(string.Empty, "CLB không tồn tại hoặc đã đóng."));
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Không tìm thấy người dùng."));
        }

        if (await _memberships.FindByPairAsync(userId, clubId, cancellationToken) is not null)
        {
            return Result.Fail(new ValidationError(string.Empty, "Người dùng đã là thành viên của CLB này."));
        }

        var membership = ClubMembership.Create(userId, clubId, role, _clock.UtcNow);
        await _memberships.AddAsync(membership, cancellationToken);

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "club.member.assign",
                "club_membership",
                membership.Id.ToString("D"),
                $"user={userId} club={club.Code} role={role}"),
            cancellationToken);

        return Result.Ok();
    }

    public Task<IReadOnlyList<ClubMembership>> ListMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => _memberships.ListByUserAsync(userId, cancellationToken);

    public Task<IReadOnlyList<ClubMembership>> ListMembersAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
        => _memberships.ListByClubAsync(clubId, cancellationToken);

    public async Task<Club?> GetPrimaryClubAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var memberships = await _memberships.ListByUserAsync(userId, cancellationToken);
        var active = memberships.FirstOrDefault(m => m.IsActive);

        return active is null ? null : await _clubs.FindByIdAsync(active.ClubId, cancellationToken);
    }
}
