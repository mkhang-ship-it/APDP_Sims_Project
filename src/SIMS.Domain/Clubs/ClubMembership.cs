using SIMS.Domain.Common;
using SIMS.Domain.Security;

namespace SIMS.Domain.Clubs;

/// <summary>Thành viên của một CLB, kèm vai trò trong CLB đó (multi-club).</summary>
public sealed class ClubMembership : Entity
{
    private ClubMembership()
    {
    }

    public Guid UserId { get; private set; }

    public Guid ClubId { get; private set; }

    public Role Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset JoinedAtUtc { get; private set; }

    public static ClubMembership Create(Guid userId, Guid clubId, Role role, DateTimeOffset joinedAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Người dùng không hợp lệ.");
        }

        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new DomainException("Vai trò trong CLB không hợp lệ.");
        }

        return new ClubMembership
        {
            UserId = userId,
            ClubId = clubId,
            Role = role,
            IsActive = true,
            JoinedAtUtc = joinedAtUtc,
        };
    }

    public static ClubMembership Restore(
        Guid id,
        Guid userId,
        Guid clubId,
        Role role,
        bool isActive,
        DateTimeOffset joinedAtUtc)
    {
        var membership = Create(userId, clubId, role, joinedAtUtc);
        membership.Id = id;
        membership.IsActive = isActive;

        return membership;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
