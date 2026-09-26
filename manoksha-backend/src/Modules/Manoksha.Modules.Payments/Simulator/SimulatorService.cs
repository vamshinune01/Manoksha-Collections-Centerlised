using System.Security.Cryptography;
using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Modules.Payments.Application;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Payments.Simulator;

public sealed record SimulatorPaymentDto(
    string ProviderOrderRef,
    decimal Amount,
    string Description,
    string Status,
    DateTimeOffset ExpiresAt,
    bool WindowEnded,
    decimal? PaidAmount,
    string? ProviderPaymentRef,
    string ReturnPath);

/// <param name="PaidAmount">Pay a different amount than requested (to exercise the amount-mismatch safeguard).</param>
/// <param name="SendWebhook">False simulates a lost callback — the poller or the status check must discover the payment.</param>
public sealed record SimulatorApproveRequest(decimal? PaidAmount, bool SendWebhook = true);

public sealed record SimulatorDeclineRequest(bool SendWebhook = true);

/// <summary>The payer's side of the simulated UPI app. Development only; never mapped in Production.</summary>
internal sealed class SimulatorService(ManokshaDbContext db, IUnitOfWork unitOfWork, SimulatorKey key, PaymentWebhookService webhooks, IClock clock)
{
    public async Task<SimulatorPaymentDto> GetAsync(string providerOrderRef, CancellationToken ct) =>
        ToDto(await db.Set<SimulatorTransaction>().AsNoTracking().SingleOrDefaultAsync(t => t.ProviderOrderRef == providerOrderRef, ct) ?? throw NotFound());

    public async Task<SimulatorPaymentDto> ApproveAsync(string providerOrderRef, SimulatorApproveRequest request, CancellationToken ct)
    {
        var t = await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var tx = await LockAsync(providerOrderRef, innerCt);
            var paid = request.PaidAmount ?? tx.Amount;
            if (paid <= 0 || !Money.HasValidScale(paid))
            {
                throw new BusinessRuleException("AMOUNT_INVALID", "Enter a valid amount.", 400);
            }
            tx.Succeed(paid, clock.UtcNow);
            await db.SaveChangesAsync(innerCt);
            return tx;
        }, ct);
        if (request.SendWebhook)
        {
            await DeliverAsync(t, "payment.succeeded", ct);
        }
        return ToDto(t);
    }

    public async Task<SimulatorPaymentDto> DeclineAsync(string providerOrderRef, SimulatorDeclineRequest request, CancellationToken ct)
    {
        var t = await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var tx = await LockAsync(providerOrderRef, innerCt);
            tx.Fail("Declined by the payer", clock.UtcNow);
            await db.SaveChangesAsync(innerCt);
            return tx;
        }, ct);
        if (request.SendWebhook)
        {
            await DeliverAsync(t, "payment.failed", ct);
        }
        return ToDto(t);
    }

    /// <summary>Builds a signed webhook exactly as the provider would, and hands it to the normal webhook pipeline.</summary>
    public string BuildWebhookBody(string providerOrderRef, string eventType, decimal? amount, string? eventId = null) =>
        JsonSerializer.Serialize(new SimulatorWebhookBody(eventId ?? "SIMEVT" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)), providerOrderRef, eventType, amount),
            JsonDefaults.Options);

    private async Task DeliverAsync(SimulatorTransaction t, string eventType, CancellationToken ct)
    {
        var body = BuildWebhookBody(t.ProviderOrderRef, eventType, t.PaidAmount);
        var headers = new Dictionary<string, string> { [SimulatorPaymentGateway.SignatureHeader] = key.Sign(body) };
        await webhooks.ReceiveAsync(SimulatorPaymentGateway.ProviderCode, headers, body, ct);
    }

    private async Task<SimulatorTransaction> LockAsync(string providerOrderRef, CancellationToken ct) =>
        await db.Set<SimulatorTransaction>().FromSqlInterpolated($"SELECT * FROM payments.simulator_transactions WHERE provider_order_ref = {providerOrderRef} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw NotFound();

    private SimulatorPaymentDto ToDto(SimulatorTransaction t) =>
        new(t.ProviderOrderRef, t.Amount, t.Description, t.Status.ToString(), t.ExpiresAt, clock.UtcNow >= t.ExpiresAt, t.PaidAmount, t.ProviderPaymentRef, t.ReturnPath);

    private static NotFoundException NotFound() => new("SIMULATOR_PAYMENT_NOT_FOUND", "Simulated payment not found.");
}
