namespace Manoksha.Modules.Identity.Contracts;

public sealed record ApproverInfo(Guid UserId, string DisplayName, bool IsOwner);

public sealed record ApproverOption(Guid UserId, string DisplayName, bool IsOwner, bool HasPin);

/// <summary>On-device approvals (POS discounts): the approver identifies themselves with their own PIN (Phase 8 decision).</summary>
public interface IApprovals
{
    /// <summary>
    /// Verifies the approver's PIN and that they hold <paramref name="permission"/> for the branch (or are the Owner when
    /// <paramref name="ownerOnly"/>). Wrong PINs count towards a temporary lock. Must be called outside the business transaction.
    /// </summary>
    Task<ApproverInfo> VerifyAsync(Guid approverUserId, string pin, string permission, Guid branchId, bool ownerOnly, CancellationToken cancellationToken = default);

    /// <summary>Active internal users who could approve at this branch.</summary>
    Task<IReadOnlyList<ApproverOption>> ListApproversAsync(string permission, Guid branchId, bool ownerOnly, CancellationToken cancellationToken = default);

    Task<string> DisplayNameAsync(Guid userId, CancellationToken cancellationToken = default);
}
