namespace Manoksha.Modules.Identity.Contracts;

/// <summary>Reseller login identities. Only the Owner creates resellers (SPEC §5.3); they start PENDING.</summary>
public interface IResellerAccounts
{
    /// <summary>Creates a PENDING reseller login. Throws RESELLER_MOBILE_EXISTS when the mobile already has a reseller login.</summary>
    Task<Guid> CreatePendingAsync(string mobileE164, string displayName, string? email, CancellationToken cancellationToken = default);

    /// <summary>Signs the reseller out everywhere (used when suspending).</summary>
    Task RevokeSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken = default);
}

public sealed record ResellerLoginDecision(bool Allowed, string? DenyCode = null, string? DenyMessage = null)
{
    public static ResellerLoginDecision Allow { get; } = new(true);

    public static ResellerLoginDecision Deny(string code, string message) => new(false, code, message);
}

/// <summary>
/// Implemented by the Resellers module and consulted after a reseller's mobile OTP is verified. It performs PENDING → ACTIVE
/// activation and applies the per-status sign-in rules (ADR-001 §17). Lives in Identity's contracts so that the dependency
/// points Resellers → Identity (no cycle).
/// </summary>
public interface IResellerLoginGate
{
    Task<ResellerLoginDecision> OnMobileVerifiedAsync(Guid userId, CancellationToken cancellationToken = default);
}
