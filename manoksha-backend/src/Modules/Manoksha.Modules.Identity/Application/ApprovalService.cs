using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Application;

public sealed record SetApprovalPinRequest(string CurrentPassword, string Pin);

/// <summary>
/// Approval PINs for on-device approvals. A PIN is 4–6 digits, stored hashed, set by its owner after re-entering their password;
/// 5 wrong entries lock it for 15 minutes. Approvers never share passwords with the device.
/// </summary>
internal sealed class ApprovalService(ManokshaDbContext db, PasswordService passwords, IAuditWriter audit, ICurrentUser currentUser, IOutbox outbox, IClock clock) : IApprovals
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);
    private readonly PasswordHasher<User> _hasher = new();

    public async Task SetMyPinAsync(SetApprovalPinRequest request, CancellationToken ct)
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == currentUser.UserId, ct);
        if (user.AccountType != AccountType.Internal)
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Only staff accounts have approval PINs.");
        }
        if (!passwords.Verify(user, request.CurrentPassword ?? string.Empty))
        {
            throw new BusinessRuleException("PASSWORD_INCORRECT", "Your current password is not correct.", 400);
        }
        var pin = request.Pin ?? string.Empty;
        if (pin.Length is < 4 or > 6 || !pin.All(char.IsAsciiDigit) || pin.Distinct().Count() == 1)
        {
            throw new BusinessRuleException("PIN_INVALID", "Use 4–6 digits (not all the same digit).", 400);
        }
        user.SetApprovalPin(_hasher.HashPassword(user, pin));
        await audit.RecordAsync(new AuditRecord("identity.approval_pin.set", "User", user.Id.ToString()), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ApproverInfo> VerifyAsync(Guid approverUserId, string pin, string permission, Guid branchId, bool ownerOnly, CancellationToken cancellationToken = default)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == approverUserId && u.AccountType == AccountType.Internal && u.Status == UserStatus.Active, cancellationToken)
                   ?? throw new BusinessRuleException("APPROVER_INVALID", "Choose an approver from the list.", 400);
        var now = clock.UtcNow;
        if (user.ApprovalPinLockedUntil is { } until && until > now)
        {
            throw new BusinessRuleException("APPROVAL_PIN_LOCKED", $"Too many wrong PINs. {user.DisplayName}'s PIN is locked for a few minutes.", 423);
        }
        if (user.ApprovalPinHash is null)
        {
            throw new BusinessRuleException("APPROVAL_PIN_NOT_SET", $"{user.DisplayName} has not set an approval PIN yet (My account → Approval PIN).", 409);
        }
        if (_hasher.VerifyHashedPassword(user, user.ApprovalPinHash, pin ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            if (user.RecordApprovalPinFailure(now, MaxFailures, LockFor))
            {
                outbox.Enqueue(new SecurityAlertRaised("APPROVAL_PIN_LOCKED", user.Id, user.DisplayName,
                    $"Approval PIN locked for {LockFor.TotalMinutes:0} minutes after {MaxFailures} wrong PINs on the POS."));
            }
            await audit.RecordAsync(new AuditRecord("identity.approval_pin.failed", "User", user.Id.ToString(), After: new { branchId, permission }), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            throw new BusinessRuleException("APPROVAL_PIN_INVALID", "The approval PIN is not correct.", 400);
        }
        var access = await PermissionService.LoadAsync(db, user.Id, cancellationToken);
        if (ownerOnly ? !access.IsOwner : !access.HasForBranch(permission, branchId))
        {
            throw new ForbiddenException("APPROVER_NOT_AUTHORIZED", ownerOnly ? "Only the Owner can approve this." : $"{user.DisplayName} cannot approve this at this branch.");
        }
        user.RecordApprovalPinSuccess();
        await db.SaveChangesAsync(cancellationToken);
        return new ApproverInfo(user.Id, user.DisplayName, access.IsOwner);
    }

    public async Task<IReadOnlyList<ApproverOption>> ListApproversAsync(string permission, Guid branchId, bool ownerOnly, CancellationToken cancellationToken = default)
    {
        var candidates = await (from a in db.Set<UserRoleAssignment>()
                                join u in db.Set<User>() on a.UserId equals u.Id
                                where a.RevokedAt == null && u.Status == UserStatus.Active && u.AccountType == AccountType.Internal
                                select new { u.Id, u.DisplayName, HasPin = u.ApprovalPinHash != null }).Distinct().ToListAsync(cancellationToken);
        var result = new List<ApproverOption>();
        foreach (var c in candidates)
        {
            var access = await PermissionService.LoadAsync(db, c.Id, cancellationToken);
            if (ownerOnly ? access.IsOwner : access.HasForBranch(permission, branchId))
            {
                result.Add(new ApproverOption(c.Id, c.DisplayName, access.IsOwner, c.HasPin));
            }
        }
        return result.OrderBy(r => r.IsOwner).ThenBy(r => r.DisplayName).ToList();
    }

    public async Task<string> DisplayNameAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Set<User>().Where(u => u.Id == userId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? "?";
}
