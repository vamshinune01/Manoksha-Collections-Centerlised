using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Modules.Wallet.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Manoksha.Modules.Wallet.Application;

public sealed record DepositSubmittedEvent(Guid DepositId, Guid ResellerId, decimal Amount) : IIntegrationEvent
{
    public static string EventType => "wallet.deposit_submitted";
}

public sealed record DepositDecidedEvent(Guid DepositId, Guid ResellerId, string Status) : IIntegrationEvent
{
    public static string EventType => "wallet.deposit_decided";
}

/// <summary>
/// Direct/PhonePe deposits (SPEC §17.2): the reseller submits amount + reference + screenshot (ADR-001 §20); the Owner approves
/// (immutable credit) or rejects (no credit; request and proof kept). Approval is idempotent — a request can credit at most once.
/// </summary>
internal sealed class DepositService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    WalletService wallets,
    IResellerDirectory resellers,
    IFileStorage storage,
    IAuditWriter audit,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock)
{
    public const string ProofContainer = "wallet-proofs";
    private const long MaxProofBytes = 5 * 1024 * 1024;
    private static readonly string[] Methods = ["PHONEPE", "UPI", "BANK_TRANSFER"];

    public async Task<DepositDto> SubmitAsync(decimal amount, string method, string reference, string? note, IFormFile? proof, CancellationToken ct)
    {
        var reseller = await SelfAsync(ct);
        if (!reseller.CanTransact)
        {
            throw new ForbiddenException("RESELLER_CANNOT_TRANSACT", $"Deposits are not available while your account is {reseller.Status}.");
        }
        if (amount <= 0 || amount > 10_000_000 || !Money.HasValidScale(amount))
        {
            throw new BusinessRuleException("AMOUNT_INVALID", "Enter a valid amount (in rupees, up to 2 decimals).", 400);
        }
        var normalizedMethod = (method ?? string.Empty).Trim().ToUpperInvariant();
        if (!Methods.Contains(normalizedMethod))
        {
            throw new BusinessRuleException("DEPOSIT_METHOD_INVALID", "Method must be PhonePe, UPI or bank transfer.", 400);
        }
        var normalizedReference = new string((reference ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (normalizedReference.Length is < 6 or > 100)
        {
            throw new BusinessRuleException("DEPOSIT_REFERENCE_REQUIRED", "Enter the payment reference (UTR / transaction id).", 400);
        }
        if (proof is null || proof.Length == 0)
        {
            throw new BusinessRuleException("DEPOSIT_PROOF_REQUIRED", "Attach the payment screenshot.", 400);
        }
        var (contentType, extension) = await ValidateProofAsync(proof, ct);

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var fileId = Uuid7.NewGuid();
            StoredObject stored;
            await using (var stream = proof.OpenReadStream())
            {
                stored = await storage.PutAsync(ProofContainer, $"{reseller.ResellerId:N}/{fileId:N}{extension}", stream, contentType, innerCt);
            }
            db.Add(new WalletFile(fileId, stored.ObjectKey, contentType, stored.Size, stored.Sha256, currentUser.UserId, clock.UtcNow));

            var seq = await db.Database.SqlQuery<long>($"SELECT nextval('wallet.deposit_seq') AS \"Value\"").SingleAsync(innerCt);
            var deposit = new DepositRequest($"DEP-{seq:D6}", reseller.ResellerId, amount, normalizedMethod, normalizedReference, fileId, note?.Trim(), currentUser.UserId, clock.UtcNow);
            db.Add(deposit);
            await audit.RecordAsync(new AuditRecord("wallet.deposit.submitted", "DepositRequest", deposit.Id.ToString(),
                After: new { deposit.Number, amount, method = normalizedMethod, reference = normalizedReference }), innerCt);
            outbox.Enqueue(new DepositSubmittedEvent(deposit.Id, reseller.ResellerId, amount));
            try
            {
                await db.SaveChangesAsync(innerCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new ConflictException("DEPOSIT_REFERENCE_USED", "This payment reference has already been submitted.");
            }
            return (await ToDtosAsync([deposit], innerCt))[0];
        }, ct);
    }

    public async Task<IReadOnlyList<DepositDto>> ListMineAsync(CancellationToken ct)
    {
        var reseller = await SelfAsync(ct);
        return await ToDtosAsync(await db.Set<DepositRequest>().AsNoTracking().Where(d => d.ResellerId == reseller.ResellerId)
            .OrderByDescending(d => d.SubmittedAt).Take(200).ToListAsync(ct), ct);
    }

    public async Task<IReadOnlyList<DepositDto>> ListAsync(string? status, Guid? resellerId, CancellationToken ct)
    {
        var q = db.Set<DepositRequest>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DepositStatus>(status, true, out var st))
        {
            q = q.Where(d => d.Status == st);
        }
        if (resellerId is { } r)
        {
            q = q.Where(d => d.ResellerId == r);
        }
        return await ToDtosAsync(await q.OrderByDescending(d => d.SubmittedAt).Take(300).ToListAsync(ct), ct);
    }

    /// <summary>Owner approval → one immutable credit. A second approval (double click, two tabs) is rejected (SPEC §17.2, §33).</summary>
    public Task<DepositDto> ApproveAsync(Guid id, ReviewDepositRequest r, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var deposit = await LockAsync(id, innerCt);
            if (deposit.Status != DepositStatus.Pending)
            {
                throw new ConflictException("DEPOSIT_ALREADY_DECIDED", "This deposit request was already decided; no additional credit was made.");
            }
            var entry = await wallets.PostAsync(deposit.ResellerId, LedgerEntryType.Deposit, LedgerDirection.Credit, deposit.Amount, null, null, deposit.Id, null,
                $"Deposit {deposit.Number} ({deposit.Method} {deposit.Reference})", innerCt);
            deposit.ApproveAndCredit(currentUser.UserId, entry.Id, r.Note, clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("wallet.deposit.approved", "DepositRequest", id.ToString(),
                new { status = "Pending", balance = entry.BalanceBefore },
                new { status = "Credited", ledgerEntryId = entry.Id, amount = deposit.Amount, balance = entry.BalanceAfter }, r.Note), innerCt);
            outbox.Enqueue(new DepositDecidedEvent(id, deposit.ResellerId, deposit.Status.ToString()));
            return (await ToDtosAsync([deposit], innerCt))[0];
        }, ct);

    public Task<DepositDto> RejectAsync(Guid id, RejectDepositRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required to reject a deposit.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var deposit = await LockAsync(id, innerCt);
            deposit.Reject(currentUser.UserId, r.Reason.Trim(), clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("wallet.deposit.rejected", "DepositRequest", id.ToString(),
                new { status = "Pending" }, new { status = "Rejected", deposit.Amount }, r.Reason), innerCt);
            outbox.Enqueue(new DepositDecidedEvent(id, deposit.ResellerId, deposit.Status.ToString()));
            return (await ToDtosAsync([deposit], innerCt))[0];
        }, ct);
    }

    /// <summary>Streams the proof. Resellers may open only their own; the Owner (wallet.view) any.</summary>
    public async Task<(Stream Content, string ContentType)> OpenProofAsync(Guid depositId, bool asReseller, CancellationToken ct)
    {
        var deposit = await db.Set<DepositRequest>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == depositId, ct) ?? throw NotFound();
        if (asReseller && deposit.ResellerId != (await SelfAsync(ct)).ResellerId)
        {
            throw NotFound(); // never reveal another reseller's deposit
        }
        var file = await db.Set<WalletFile>().AsNoTracking().SingleAsync(f => f.Id == deposit.ProofFileId, ct);
        return (await storage.OpenReadAsync(ProofContainer, file.ObjectKey, ct), file.ContentType);
    }

    private async Task<DepositRequest> LockAsync(Guid id, CancellationToken ct) =>
        await db.Set<DepositRequest>().FromSqlInterpolated($"SELECT *, xmin FROM wallet.deposit_requests WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
        ?? throw NotFound();

    private async Task<ResellerInfo> SelfAsync(CancellationToken ct) =>
        await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");

    /// <summary>Accepts JPEG, PNG, WEBP or PDF up to 5 MB, checked by content signature (not just the file name).</summary>
    private static async Task<(string ContentType, string Extension)> ValidateProofAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > MaxProofBytes)
        {
            throw new BusinessRuleException("DEPOSIT_PROOF_TOO_LARGE", "The screenshot must be 5 MB or smaller.", 400);
        }
        var header = new byte[12];
        await using (var s = file.OpenReadStream())
        {
            _ = await s.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        }
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ("image/jpeg", ".jpg");
        }
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
        {
            return ("image/png", ".png");
        }
        if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return ("image/webp", ".webp");
        }
        if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
        {
            return ("application/pdf", ".pdf");
        }
        throw new BusinessRuleException("DEPOSIT_PROOF_TYPE_INVALID", "Upload a JPEG, PNG, WEBP image or a PDF.", 400);
    }

    private async Task<IReadOnlyList<DepositDto>> ToDtosAsync(IReadOnlyList<DepositRequest> list, CancellationToken ct)
    {
        var result = new List<DepositDto>();
        var cache = new Dictionary<Guid, ResellerInfo?>();
        foreach (var d in list)
        {
            if (!cache.TryGetValue(d.ResellerId, out var info))
            {
                cache[d.ResellerId] = info = await resellers.FindAsync(d.ResellerId, ct);
            }
            result.Add(new DepositDto(d.Id, d.Number, d.ResellerId, info?.ResellerNumber, info?.BusinessName ?? info?.ContactName, d.Amount, d.Method, d.Reference,
                d.ResellerNote, d.Status.ToString(), d.SubmittedAt, d.ReviewedAt, d.ReviewNote, d.LedgerEntryId));
        }
        return result;
    }

    private static NotFoundException NotFound() => new("DEPOSIT_NOT_FOUND", "Deposit request not found.");
}
