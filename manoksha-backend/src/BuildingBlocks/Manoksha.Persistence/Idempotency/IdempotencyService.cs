using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Persistence.Idempotency;

internal sealed class IdempotencyService(ManokshaDbContext db, IUnitOfWork unitOfWork, ICurrentUser currentUser, IClock clock)
    : IIdempotencyService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public Task<TResponse> ExecuteAsync<TResponse>(
        string scope,
        string key,
        object request,
        Func<CancellationToken, Task<TResponse>> operation,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserIdOrNull ?? Guid.Empty;
        var requestHash = Hash(JsonSerializer.Serialize(request, JsonDefaults.Options));

        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var now = clock.UtcNow;
            // The unique primary key serializes concurrent requests with the same key: a second insert waits
            // for the first transaction, then either conflicts (first committed) or proceeds (first rolled back).
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO platform.idempotency_records (scope, user_id, key, request_hash, status, created_at, expires_at)
                VALUES ({scope}, {userId}, {key}, {requestHash}, 'IN_PROGRESS', {now}, {now + Retention})
                ON CONFLICT DO NOTHING
                """, ct);

            if (inserted == 0)
            {
                var existing = await db.IdempotencyRecords.AsNoTracking()
                    .SingleAsync(r => r.Scope == scope && r.UserId == userId && r.Key == key, ct);
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new BusinessRuleException(ErrorCodes.IdempotencyKeyReused,
                        "This Idempotency-Key was already used for a different request.", 422);
                }
                return JsonSerializer.Deserialize<TResponse>(existing.ResponseBody ?? "null", JsonDefaults.Options)!;
            }

            var response = await operation(ct);
            await db.SaveChangesAsync(ct);

            var body = JsonSerializer.Serialize(response, JsonDefaults.Options);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE platform.idempotency_records
                SET status = 'COMPLETED', response_body = {body}::jsonb, completed_at = {clock.UtcNow}
                WHERE scope = {scope} AND user_id = {userId} AND key = {key}
                """, ct);
            return response;
        }, cancellationToken);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
