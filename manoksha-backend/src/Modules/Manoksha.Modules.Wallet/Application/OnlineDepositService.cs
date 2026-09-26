using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Payments.Contracts;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Modules.Wallet.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Wallet.Application;

public sealed record StartOnlineDepositRequest(decimal Amount);

/// <param name="Payment">Payment progress; <c>RedirectUrl</c> is where the reseller pays while the status is PENDING.</param>
public sealed record OnlineDepositDto(Guid Id, string Number, decimal Amount, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    string? ProviderPaymentRef, OnlineDepositPaymentDto? Payment);

/// <summary>What the idempotent part produced (stored with the Idempotency-Key and replayed for repeats).</summary>
public sealed record OnlineDepositStage(Guid DepositId, Guid AttemptId);

public sealed record OnlineDepositPaymentDto(Guid AttemptId, string Status, string? RedirectUrl, DateTimeOffset ExpiresAt);

public sealed record WalletDepositCredited(Guid ResellerId, Guid DepositId, string Number, decimal Amount) : IIntegrationEvent
{
    public static string EventType => "wallet.online_deposit_credited";
}

/// <summary>
/// Provider-confirmed online wallet deposits (SPEC §17.1). The reseller chooses the amount; the wallet is credited only by the
/// payment pipeline after an authoritative, validated success — never by the browser. Idempotent by Idempotency-Key.
/// </summary>
internal sealed class OnlineDepositService(
    ManokshaDbContext db,
    IIdempotencyService idempotency,
    IResellerDirectory resellers,
    IPayments payments,
    ISettingsReader settings,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<OnlineDepositDto> StartAsync(StartOnlineDepositRequest request, string idempotencyKey, CancellationToken ct)
    {
        var stage = await idempotency.ExecuteAsync("wallet.online_deposit", idempotencyKey, request, innerCt => CreateAsync(request, innerCt), ct);
        await payments.StartSessionAsync(stage.AttemptId, ct);
        return await GetMineAsync(stage.DepositId, sync: false, ct);
    }

    public async Task<IReadOnlyList<OnlineDepositDto>> ListMineAsync(CancellationToken ct)
    {
        var me = await SelfAsync(ct);
        var ids = await db.Set<OnlineDeposit>().AsNoTracking().Where(d => d.ResellerId == me.ResellerId).OrderByDescending(d => d.CreatedAt).Take(100)
            .Select(d => d.Id).ToListAsync(ct);
        var list = new List<OnlineDepositDto>();
        foreach (var id in ids)
        {
            list.Add(await ToDtoAsync(id, sync: false, ct));
        }
        return list;
    }

    /// <summary>The reseller's own deposit; while the payment is pending the provider is consulted (never the browser).</summary>
    public async Task<OnlineDepositDto> GetMineAsync(Guid id, bool sync, CancellationToken ct)
    {
        var me = await SelfAsync(ct);
        if (!await db.Set<OnlineDeposit>().AnyAsync(d => d.Id == id && d.ResellerId == me.ResellerId, ct))
        {
            throw new NotFoundException("DEPOSIT_NOT_FOUND", "Deposit not found.");
        }
        return await ToDtoAsync(id, sync, ct);
    }

    private async Task<OnlineDepositStage> CreateAsync(StartOnlineDepositRequest request, CancellationToken ct)
    {
        var reseller = await SelfAsync(ct);
        if (!reseller.CanTransact)
        {
            throw new ForbiddenException("RESELLER_CANNOT_DEPOSIT", $"Deposits are not available while your account is {reseller.Status}.");
        }
        if (request.Amount <= 0 || !Money.HasValidScale(request.Amount))
        {
            throw new BusinessRuleException("AMOUNT_INVALID", "Enter an amount greater than zero with at most 2 decimals.", 400);
        }
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('wallet.deposit_seq') AS \"Value\"").SingleAsync(ct);
        var now = clock.UtcNow;
        var deposit = new OnlineDeposit($"DEP-{seq:D6}", reseller.ResellerId, request.Amount, currentUser.UserId, now);
        db.Add(deposit);
        await db.SaveChangesAsync(ct);

        var minutes = await settings.GetAsync<int>(SettingKeys.OnlineDepositMinutes, ct);
        var attempt = await payments.CreateAttemptAsync(new NewPaymentAttempt(PaymentPurposes.WalletDeposit, deposit.Id, deposit.Number, currentUser.UserId,
            deposit.Amount, now.AddMinutes(minutes), $"Manoksha wallet deposit {deposit.Number}", "/reseller/wallet"), ct);
        deposit.AttachPayment(attempt.Id);
        await audit.RecordAsync(new AuditRecord("wallet.online_deposit.started", "OnlineDeposit", deposit.Id.ToString(),
            After: new { deposit.Number, resellerId = reseller.ResellerId, deposit.Amount, paymentAttemptId = attempt.Id }), ct);
        await db.SaveChangesAsync(ct);
        return new OnlineDepositStage(deposit.Id, attempt.Id);
    }

    private async Task<OnlineDepositDto> ToDtoAsync(Guid id, bool sync, CancellationToken ct)
    {
        var attempt = await payments.FindLatestAsync(PaymentPurposes.WalletDeposit, id, ct);
        if (sync && attempt is { Status: "INITIATED" or "PENDING" })
        {
            attempt = await payments.SyncAsync(attempt.Id, ct);
        }
        var d = await db.Set<OnlineDeposit>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return new OnlineDepositDto(d.Id, d.Number, d.Amount, d.Status.ToString(), d.CreatedAt, d.CompletedAt, d.ProviderPaymentRef,
            attempt is null ? null : new OnlineDepositPaymentDto(attempt.Id, attempt.Status, attempt.Status == "PENDING" ? attempt.RedirectUrl : null, attempt.ExpiresAt));
    }

    private async Task<ResellerInfo> SelfAsync(CancellationToken ct) =>
        await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");
}

/// <summary>WALLET_DEPOSIT payments: credit exactly once (unique ledger link), inside the payment-processing transaction.</summary>
internal sealed class WalletDepositPaymentHandler(ManokshaDbContext db, WalletService wallets, IAuditWriter audit, IOutbox outbox, IClock clock) : IPaymentPurposeHandler
{
    public string Purpose => PaymentPurposes.WalletDeposit;

    public async Task<PaymentHandlingResult> OnPaymentSucceededAsync(PaymentSuccess success, CancellationToken cancellationToken)
    {
        var deposit = await LockAsync(success.ReferenceId, cancellationToken);
        if (deposit.Status == OnlineDepositStatus.Credited)
        {
            return new PaymentHandlingResult(PaymentOutcome.Confirmed);
        }
        if (success.Amount != deposit.Amount)
        {
            return new PaymentHandlingResult(PaymentOutcome.NotFulfillable, "AMOUNT_MISMATCH", $"Deposit {deposit.Number}: payment amount differs from the deposit amount.");
        }
        var entry = await wallets.PostAsync(deposit.ResellerId, LedgerEntryType.Deposit, LedgerDirection.Credit, deposit.Amount, null, null, null, null,
            $"Online UPI deposit {deposit.Number} ({success.ProviderPaymentRef})", cancellationToken, onlineDepositId: deposit.Id);
        deposit.Credit(entry.Id, success.ProviderPaymentRef, clock.UtcNow);
        await audit.RecordAsync(new AuditRecord("wallet.online_deposit.credited", "OnlineDeposit", deposit.Id.ToString(),
            After: new { deposit.Number, deposit.ResellerId, deposit.Amount, ledgerEntryId = entry.Id, entry.BalanceBefore, entry.BalanceAfter, success.ProviderPaymentRef,
                late = !success.WithinWindow }), cancellationToken);
        outbox.Enqueue(new WalletDepositCredited(deposit.ResellerId, deposit.Id, deposit.Number, deposit.Amount));
        await db.SaveChangesAsync(cancellationToken);
        return new PaymentHandlingResult(PaymentOutcome.Confirmed);
    }

    public async Task OnPaymentFailedAsync(Guid referenceId, string reason, CancellationToken cancellationToken)
    {
        (await LockAsync(referenceId, cancellationToken)).Close(OnlineDepositStatus.Failed, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task OnPaymentExpiredAsync(Guid referenceId, CancellationToken cancellationToken)
    {
        (await LockAsync(referenceId, cancellationToken)).Close(OnlineDepositStatus.Expired, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<OnlineDeposit> LockAsync(Guid id, CancellationToken ct)
    {
        db.Forget<OnlineDeposit>(d => d.Id == id);
        return await db.Set<OnlineDeposit>().FromSqlInterpolated($"SELECT *, xmin FROM wallet.online_deposits WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("DEPOSIT_NOT_FOUND", "Deposit not found.");
    }
}
