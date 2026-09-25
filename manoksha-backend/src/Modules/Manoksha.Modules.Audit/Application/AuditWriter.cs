using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Audit.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Audit.Application;

internal sealed class AuditWriter(
    ManokshaDbContext db,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IPermissionService permissionService,
    IClock clock) : IAuditWriter
{
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Action);

        var actorType = currentUser.IsAuthenticated
            ? currentUser.AccountType!.Value.ToString().ToUpperInvariant()
            : currentUser.IsSystem ? "SYSTEM" : "ANONYMOUS";

        string[] roles = [];
        if (currentUser.IsAuthenticated && currentUser.AccountType == AccountType.Internal)
        {
            var access = await permissionService.GetEffectiveAccessAsync(cancellationToken);
            roles = access.Roles.Select(r => r.BranchId is null ? r.RoleCode : $"{r.RoleCode}@{r.BranchId}").Distinct().ToArray();
        }

        var before = Serialize(record.Before);
        var after = Serialize(record.After);
        var occurredAt = clock.UtcNow;
        var hash = ComputeHash(occurredAt, currentUser.UserIdOrNull, actorType, record, before, after);

        db.Add(new AuditLogEntry(
            occurredAt,
            currentUser.UserIdOrNull,
            actorType,
            roles,
            record.BranchId,
            record.Action,
            record.EntityType,
            record.EntityId,
            before,
            after,
            record.Reason,
            requestContext.IpAddress,
            Truncate(requestContext.UserAgent, 500),
            requestContext.CorrelationId,
            hash));
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonDefaults.Options);

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    private static string ComputeHash(DateTimeOffset at, Guid? actor, string actorType, AuditRecord r, string? before, string? after)
    {
        var canonical = string.Join('\u001f',
            at.ToUnixTimeMilliseconds(), actor?.ToString() ?? "", actorType, r.Action, r.EntityType, r.EntityId,
            before ?? "", after ?? "", r.Reason ?? "", r.BranchId?.ToString() ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
