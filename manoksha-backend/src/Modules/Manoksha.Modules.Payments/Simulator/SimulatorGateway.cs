using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Manoksha.Modules.Payments.Simulator;

public sealed class SimulatorOptions
{
    /// <summary>HMAC key for simulated webhooks. When empty a random key is generated per process (development only).</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Web app that hosts the simulated UPI payment page (customer web).</summary>
    public string CheckoutBaseUrl { get; set; } = "http://localhost:3002";
}

internal enum SimulatorStatus
{
    Created = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>The simulated provider's own record of a payment order — stands in for the provider's database.</summary>
internal sealed class SimulatorTransaction : Entity
{
    private SimulatorTransaction()
    {
    }

    public SimulatorTransaction(Guid merchantAttemptId, string providerOrderRef, decimal amount, string description, string returnPath, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        MerchantAttemptId = merchantAttemptId;
        ProviderOrderRef = providerOrderRef;
        Amount = amount;
        Description = description;
        ReturnPath = returnPath;
        ExpiresAt = expiresAt;
        Status = SimulatorStatus.Created;
        CreatedAt = now;
    }

    public Guid MerchantAttemptId { get; private set; }

    public string ProviderOrderRef { get; private set; } = default!;

    public decimal Amount { get; private set; }

    public string Description { get; private set; } = default!;

    public string ReturnPath { get; private set; } = default!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public SimulatorStatus Status { get; private set; }

    public decimal? PaidAmount { get; private set; }

    public string? ProviderPaymentRef { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public void Succeed(decimal paidAmount, DateTimeOffset now)
    {
        EnsureOpen();
        Status = SimulatorStatus.Succeeded;
        PaidAmount = paidAmount;
        ProviderPaymentRef = "SIMPAY" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        CompletedAt = now;
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        EnsureOpen();
        Status = SimulatorStatus.Failed;
        FailureReason = reason;
        CompletedAt = now;
    }

    private void EnsureOpen()
    {
        if (Status != SimulatorStatus.Created)
        {
            throw new BusinessRuleException("SIMULATOR_ALREADY_COMPLETED", $"This simulated payment is already {Status}.", 409);
        }
    }
}

/// <summary>
/// Development UPI gateway: sessions and payments live in <c>payments.simulator_transactions</c>, the "customer" pays on a
/// simulated UPI page, and webhooks are HMAC-signed like a real provider's. Refused in Production.
/// </summary>
internal sealed class SimulatorPaymentGateway(ManokshaDbContext db, SimulatorKey key, IOptions<SimulatorOptions> options, IClock clock) : IPaymentGateway
{
    public const string ProviderCode = "simulator";
    public const string SignatureHeader = "X-Simulator-Signature";

    public string Provider => ProviderCode;

    public async Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken)
    {
        var existing = await db.Set<SimulatorTransaction>().AsNoTracking().SingleOrDefaultAsync(t => t.MerchantAttemptId == request.AttemptId, cancellationToken);
        if (existing is null)
        {
            var created = new SimulatorTransaction(request.AttemptId, "SIMORD" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)), request.Amount, request.Description,
                request.ReturnPath, request.ExpiresAt, clock.UtcNow);
            db.Add(created);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                existing = created;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(created).State = EntityState.Detached;
                existing = await db.Set<SimulatorTransaction>().AsNoTracking().SingleAsync(t => t.MerchantAttemptId == request.AttemptId, cancellationToken);
            }
        }
        return new PaymentSession(existing.ProviderOrderRef, $"{options.Value.CheckoutBaseUrl.TrimEnd('/')}/pay/simulator/{existing.ProviderOrderRef}");
    }

    public async Task<ProviderPaymentStatus> GetStatusAsync(string providerOrderRef, CancellationToken cancellationToken)
    {
        var t = await db.Set<SimulatorTransaction>().AsNoTracking().SingleOrDefaultAsync(x => x.ProviderOrderRef == providerOrderRef, cancellationToken)
                ?? throw new PaymentGatewayException($"Unknown simulator order {providerOrderRef}.");
        return t.Status switch
        {
            SimulatorStatus.Succeeded => new ProviderPaymentStatus(t.ProviderOrderRef, ProviderPaymentState.Success, t.PaidAmount, "INR", t.ProviderPaymentRef, null),
            SimulatorStatus.Failed => new ProviderPaymentStatus(t.ProviderOrderRef, ProviderPaymentState.Failed, null, "INR", null, t.FailureReason),
            _ => new ProviderPaymentStatus(t.ProviderOrderRef, ProviderPaymentState.Pending, null, "INR", null, null),
        };
    }

    public ProviderWebhook? ParseWebhook(IReadOnlyDictionary<string, string> headers, string body)
    {
        if (!headers.TryGetValue(SignatureHeader, out var signature) || !key.Verify(body, signature))
        {
            return null;
        }
        try
        {
            var e = JsonSerializer.Deserialize<SimulatorWebhookBody>(body, JsonDefaults.Options);
            return e is { EventId.Length: > 0, ProviderOrderRef.Length: > 0, EventType.Length: > 0 } ? new ProviderWebhook(e.EventId, e.ProviderOrderRef, e.EventType) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record SimulatorWebhookBody(string EventId, string ProviderOrderRef, string EventType, decimal? Amount);

/// <summary>Process-wide HMAC key for simulated webhooks.</summary>
internal sealed class SimulatorKey(IOptions<SimulatorOptions> options)
{
    private readonly byte[] _key = string.IsNullOrWhiteSpace(options.Value.WebhookSecret)
        ? RandomNumberGenerator.GetBytes(32)
        : Encoding.UTF8.GetBytes(options.Value.WebhookSecret);

    public string Sign(string body) => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public bool Verify(string body, string signature) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(body)), Encoding.UTF8.GetBytes(signature));
}
