using Manoksha.SharedKernel;

namespace Manoksha.Modules.Identity.Domain;

/// <summary>
/// One-time link for a staff member to set their own password (Owner-created account; no open staff sign-up — SPEC §5.1).
/// Only a SHA-256 hash of the token is stored.
/// </summary>
internal sealed class UserInvitation : Entity
{
    private UserInvitation()
    {
    }

    public UserInvitation(Guid userId, string tokenHash, DateTimeOffset expiresAt, Guid createdBy, DateTimeOffset now)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = default!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && RevokedAt is null && now < ExpiresAt;

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
