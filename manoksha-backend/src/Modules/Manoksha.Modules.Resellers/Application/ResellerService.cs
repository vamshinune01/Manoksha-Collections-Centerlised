using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Modules.Resellers.Domain;
using Manoksha.Modules.Wallet.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Manoksha.Modules.Resellers.Application;

public sealed record ResellerCreated(Guid ResellerId, string ResellerNumber) : IIntegrationEvent
{
    public static string EventType => "resellers.reseller_created";
}

public sealed record CommercialTermsChanged(Guid ResellerId, int Version) : IIntegrationEvent
{
    public static string EventType => "resellers.commercial_terms_changed";
}

public sealed record ResellerStatusChanged(Guid ResellerId, string Status) : IIntegrationEvent
{
    public static string EventType => "resellers.status_changed";
}

/// <summary>
/// Owner-only reseller onboarding and lifecycle (SPEC §6, §27.6; ADR-001 §17–19). Creation, activation, status and
/// commercial-term changes are all audited.
/// </summary>
internal sealed class ResellerService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IResellerAccounts accounts,
    IWallets wallets,
    IAuditWriter audit,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock) : IResellerDirectory, IResellerLoginGate
{
    public async Task<IReadOnlyList<ResellerSummaryDto>> ListAsync(string? q, string? status, CancellationToken ct)
    {
        var query = db.Set<Reseller>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ResellerStatus>(status, true, out var st))
        {
            query = query.Where(r => r.Status == st);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var pattern = $"%{term}%";
            var mobile = MobileNumber.TryNormalize(term, out var m) ? m : null;
            var number = term.ToUpperInvariant();
            query = query.Where(r => EF.Functions.ILike(r.Profile.ContactName, pattern) || EF.Functions.ILike(r.Profile.BusinessName!, pattern)
                || r.ResellerNumber == number || r.MobileE164 == mobile);
        }
        var resellers = await query.OrderByDescending(r => r.CreatedAt).Take(500).ToListAsync(ct);
        var result = new List<ResellerSummaryDto>();
        foreach (var r in resellers)
        {
            var terms = await GetCurrentTermsAsync(r.Id, ct);
            var wallet = await wallets.FindAsync(r.Id, ct);
            result.Add(new ResellerSummaryDto(r.Id, r.ResellerNumber, r.Profile.ContactName, r.Profile.BusinessName, r.MobileE164, r.Profile.City, r.Status.ToString(),
                terms.DiscountPct, wallet?.Balance ?? 0m, r.CreatedAt, r.ActivatedAt));
        }
        return result;
    }

    public async Task<ResellerDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Set<Reseller>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        return await ToDetailAsync(r, ct);
    }

    public Task<ResellerDetailDto> CreateAsync(CreateResellerRequest req, CancellationToken ct)
    {
        RequireReason(req.Reason);
        var mobile = MobileNumber.Normalize(req.Mobile);
        var profile = ValidateProfile(req.ContactName, req.BusinessName, req.Email, req.AddressLine, req.City, req.State, req.Pin, req.Notes);
        Percent.Validate(req.ResellerDiscountPct);

        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var userId = await accounts.CreatePendingAsync(mobile, req.BusinessName ?? req.ContactName, req.Email, innerCt);
            var seq = await db.Database.SqlQuery<long>($"SELECT nextval('resellers.reseller_number_seq') AS \"Value\"").SingleAsync(innerCt);
            var now = clock.UtcNow;
            var reseller = new Reseller($"RS-{seq:D5}", userId, profile, mobile, currentUser.UserId, now);
            db.Add(reseller);
            db.Add(new ResellerStatusChange(reseller.Id, null, ResellerStatus.Pending, currentUser.UserId, req.Reason, now));
            var term = new CommercialTerm(reseller.Id, 1, req.ResellerDiscountPct, req.Notes?.Trim(), "Initial commercial terms", currentUser.UserId, now);
            db.Add(term);
            await wallets.OpenAsync(reseller.Id, innerCt);

            await audit.RecordAsync(new AuditRecord("resellers.reseller.created", "Reseller", reseller.Id.ToString(),
                After: new { reseller.ResellerNumber, mobile = MobileNumber.Mask(mobile), profile.ContactName, profile.BusinessName, status = "Pending", discountPct = req.ResellerDiscountPct, walletOpeningBalance = 0m },
                Reason: req.Reason), innerCt);
            outbox.Enqueue(new ResellerCreated(reseller.Id, reseller.ResellerNumber));
            try
            {
                await db.SaveChangesAsync(innerCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new ConflictException("RESELLER_MOBILE_EXISTS", "A reseller is already registered with this mobile number.");
            }
            return await ToDetailAsync(reseller, innerCt);
        }, ct);
    }

    public Task<ResellerDetailDto> UpdateAsync(Guid id, UpdateResellerRequest req, CancellationToken ct)
    {
        RequireReason(req.Reason);
        var profile = ValidateProfile(req.ContactName, req.BusinessName, req.Email, req.AddressLine, req.City, req.State, req.Pin, req.Notes);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var r = await db.Set<Reseller>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            var before = r.Profile;
            r.UpdateProfile(profile);
            await audit.RecordAsync(new AuditRecord("resellers.reseller.updated", "Reseller", id.ToString(), before, profile, req.Reason), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await ToDetailAsync(r, innerCt);
        }, ct);
    }

    public Task<ResellerDetailDto> ChangeStatusAsync(Guid id, ChangeResellerStatusRequest req, CancellationToken ct)
    {
        RequireReason(req.Reason);
        if (!Enum.TryParse<ResellerStatus>(req.Status, true, out var to))
        {
            throw new BusinessRuleException("RESELLER_STATUS_INVALID", "Unknown status.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var r = await db.Set<Reseller>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            var from = r.Status;
            r.ChangeStatusByOwner(to);
            db.Add(new ResellerStatusChange(id, from, to, currentUser.UserId, req.Reason, clock.UtcNow));
            if (to == ResellerStatus.Suspended)
            {
                // SUSPENDED resellers cannot sign in at all (ADR-001 §17).
                await accounts.RevokeSessionsAsync(r.UserId, "RESELLER_SUSPENDED", innerCt);
            }
            await audit.RecordAsync(new AuditRecord("resellers.reseller.status_changed", "Reseller", id.ToString(),
                new { status = from.ToString() }, new { status = to.ToString() }, req.Reason), innerCt);
            outbox.Enqueue(new ResellerStatusChanged(id, to.ToString()));
            await db.SaveChangesAsync(innerCt);
            return await ToDetailAsync(r, innerCt);
        }, ct);
    }

    /// <summary>New immutable version; future pricing only — historical orders keep their snapshot (SPEC §15).</summary>
    public Task<ResellerDetailDto> ChangeTermsAsync(Guid id, ChangeTermsRequest req, CancellationToken ct)
    {
        RequireReason(req.Reason);
        Percent.Validate(req.ResellerDiscountPct);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var r = await db.Set<Reseller>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            if (r.Status == ResellerStatus.Closed)
            {
                throw new BusinessRuleException("RESELLER_CLOSED", "Terms of a closed reseller cannot change.");
            }
            // Serialize concurrent term edits for this reseller.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM resellers.resellers WHERE id = {id} FOR UPDATE", innerCt);
            var current = await db.Set<CommercialTerm>().Where(t => t.ResellerId == id).OrderByDescending(t => t.Version).FirstAsync(innerCt);
            var term = new CommercialTerm(id, current.Version + 1, req.ResellerDiscountPct, req.Notes?.Trim(), req.Reason, currentUser.UserId, clock.UtcNow);
            db.Add(term);
            await audit.RecordAsync(new AuditRecord("resellers.commercial_terms.changed", "Reseller", id.ToString(),
                new { version = current.Version, discountPct = current.DiscountPct, current.Notes },
                new { version = term.Version, discountPct = term.DiscountPct, term.Notes }, req.Reason), innerCt);
            outbox.Enqueue(new CommercialTermsChanged(id, term.Version));
            await db.SaveChangesAsync(innerCt);
            return await ToDetailAsync(r, innerCt);
        }, ct);
    }

    // ---- Reseller self-service (own data only: resolved from the token, never from input) ----

    public async Task<ResellerSelfDto> GetSelfAsync(CancellationToken ct)
    {
        var r = await LoadSelfAsync(ct);
        var terms = await GetCurrentTermsAsync(r.Id, ct);
        var wallet = await wallets.FindAsync(r.Id, ct);
        return new ResellerSelfDto(r.ResellerNumber, r.Profile.ContactName, r.Profile.BusinessName, r.MobileE164, r.Profile.Email, r.Status.ToString(),
            r.Status == ResellerStatus.Active, terms.DiscountPct, terms.Version, wallet?.Balance ?? 0m);
    }

    public async Task<IReadOnlyList<ResellerTermDto>> GetSelfTermsAsync(CancellationToken ct)
    {
        var r = await LoadSelfAsync(ct);
        var terms = await db.Set<CommercialTerm>().AsNoTracking().Where(t => t.ResellerId == r.Id).OrderByDescending(t => t.Version).ToListAsync(ct);
        return terms.Select((t, i) => new ResellerTermDto(t.Version, t.DiscountPct, t.Notes, t.EffectiveFrom, i == 0)).ToList();
    }

    private async Task<Reseller> LoadSelfAsync(CancellationToken ct) =>
        await db.Set<Reseller>().AsNoTracking().SingleOrDefaultAsync(r => r.UserId == currentUser.UserId, ct)
        ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");

    // ---- Contracts ----

    public async Task<ResellerInfo?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Set<Reseller>().AsNoTracking().Where(r => r.UserId == userId).Select(r => ToInfo(r)).SingleOrDefaultAsync(cancellationToken);

    public async Task<ResellerInfo?> FindAsync(Guid resellerId, CancellationToken cancellationToken = default) =>
        await db.Set<Reseller>().AsNoTracking().Where(r => r.Id == resellerId).Select(r => ToInfo(r)).SingleOrDefaultAsync(cancellationToken);

    public async Task<ResellerTerms> GetCurrentTermsAsync(Guid resellerId, CancellationToken cancellationToken = default)
    {
        var t = await db.Set<CommercialTerm>().AsNoTracking().Where(x => x.ResellerId == resellerId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("RESELLER_TERMS_NOT_FOUND", "The reseller has no commercial terms.");
        return new ResellerTerms(resellerId, t.Id, t.Version, t.DiscountPct);
    }

    /// <summary>
    /// After mobile OTP verification: PENDING → ACTIVE when eligible; then sign-in rules per status (ADR-001 §17).
    /// </summary>
    public async Task<ResellerLoginDecision> OnMobileVerifiedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var r = await db.Set<Reseller>().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (r is null)
        {
            return ResellerLoginDecision.Deny("RESELLER_ACCOUNT_NOT_ACTIVE", "No reseller account is registered for this number.");
        }
        switch (r.Status)
        {
            case ResellerStatus.Pending:
                var hasTerms = await db.Set<CommercialTerm>().AnyAsync(t => t.ResellerId == r.Id, cancellationToken);
                var wallet = await wallets.FindAsync(r.Id, cancellationToken);
                if (!hasTerms || wallet is null)
                {
                    return ResellerLoginDecision.Deny("RESELLER_NOT_ELIGIBLE", "Your reseller account setup is incomplete. Please contact Manoksha Collections.");
                }
                r.ActivateAfterVerification(clock.UtcNow);
                db.Add(new ResellerStatusChange(r.Id, ResellerStatus.Pending, ResellerStatus.Active, null, "Mobile number verified by OTP", clock.UtcNow));
                await audit.RecordAsync(new AuditRecord("resellers.reseller.activated", "Reseller", r.Id.ToString(),
                    new { status = "Pending" }, new { status = "Active", verification = "MOBILE_OTP" }, "Mobile number verified by OTP"), cancellationToken);
                outbox.Enqueue(new ResellerStatusChanged(r.Id, nameof(ResellerStatus.Active)));
                return ResellerLoginDecision.Allow;
            case ResellerStatus.Active:
            case ResellerStatus.Frozen:
                return ResellerLoginDecision.Allow;
            case ResellerStatus.Closed when r.ActivatedAt is not null:
                return ResellerLoginDecision.Allow;
            case ResellerStatus.Suspended:
                return ResellerLoginDecision.Deny("RESELLER_SUSPENDED", "Your reseller account is suspended. Please contact Manoksha Collections.");
            default:
                return ResellerLoginDecision.Deny("RESELLER_ACCOUNT_NOT_ACTIVE", "No active reseller account is registered for this number.");
        }
    }

    private async Task<ResellerDetailDto> ToDetailAsync(Reseller r, CancellationToken ct)
    {
        var terms = await db.Set<CommercialTerm>().AsNoTracking().Where(t => t.ResellerId == r.Id).OrderByDescending(t => t.Version).ToListAsync(ct);
        var history = await db.Set<ResellerStatusChange>().AsNoTracking().Where(s => s.ResellerId == r.Id).OrderByDescending(s => s.OccurredAt).ToListAsync(ct);
        var wallet = await wallets.FindAsync(r.Id, ct);
        var termDtos = terms.Select((t, i) => new CommercialTermDto(t.Id, t.Version, t.DiscountPct, t.Notes, t.Reason, t.EffectiveFrom, i == 0)).ToList();
        var p = r.Profile;
        return new ResellerDetailDto(r.Id, r.ResellerNumber, r.UserId, r.MobileE164, r.Status.ToString(),
            new ResellerProfileDto(p.ContactName, p.BusinessName, p.Email, p.AddressLine, p.City, p.State, p.Pin, p.Notes),
            wallet?.Balance ?? 0m, termDtos[0], termDtos,
            history.Select(h => new StatusChangeDto(h.FromStatus?.ToString(), h.ToStatus.ToString(), h.Reason, h.ActorUserId, h.OccurredAt)).ToList(),
            r.CreatedAt, r.ActivatedAt);
    }

    private static ResellerInfo ToInfo(Reseller r) =>
        new(r.Id, r.ResellerNumber, r.UserId, r.Status.ToString(), r.Profile.ContactName, r.Profile.BusinessName, r.Status == ResellerStatus.Active);

    private static ResellerProfile ValidateProfile(string contactName, string? businessName, string email, string address, string city, string state, string pin, string? notes)
    {
        if (string.IsNullOrWhiteSpace(contactName) || string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state))
        {
            throw new BusinessRuleException("RESELLER_DETAILS_REQUIRED", "Name, address, city and state are required.", 400);
        }
        if (!EmailAddress.IsValid(email))
        {
            throw new BusinessRuleException("EMAIL_INVALID", "Enter a valid email address.", 400);
        }
        var trimmedPin = (pin ?? string.Empty).Trim();
        if (trimmedPin.Length != 6 || !trimmedPin.All(char.IsAsciiDigit) || trimmedPin[0] == '0')
        {
            throw new BusinessRuleException("PIN_INVALID", "Enter a valid 6-digit PIN code.", 400);
        }
        return new ResellerProfile(contactName.Trim(), string.IsNullOrWhiteSpace(businessName) ? null : businessName.Trim(), email.Trim(),
            address.Trim(), city.Trim(), state.Trim(), trimmedPin, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for this action.", 400);
        }
    }

    private static NotFoundException NotFound() => new("RESELLER_NOT_FOUND", "Reseller not found.");
}
