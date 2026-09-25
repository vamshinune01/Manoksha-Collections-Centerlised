using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Application;

/// <summary>Issues, rotates and revokes sessions (refresh-token families).</summary>
internal sealed class SessionService(ManokshaDbContext db, TokenService tokens, IRequestContext requestContext, IClock clock)
{
    public async Task<TokenPair> IssueAsync(User user, string audience, CancellationToken ct)
    {
        if (!Audiences.IsValidFor(audience, user.AccountType))
        {
            throw new ForbiddenException("AUDIENCE_NOT_ALLOWED", "This account cannot sign in to this application.");
        }
        var now = clock.UtcNow;
        var session = new AuthSession(user.Id, audience, now, now.AddDays(tokens.Options.SessionAbsoluteDays), requestContext.IpAddress, requestContext.UserAgent);
        db.Add(session);
        var pair = CreatePair(user, session, now);
        await db.SaveChangesAsync(ct);
        return pair;
    }

    public async Task<TokenPair> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = TokenService.HashToken(refreshToken ?? string.Empty);
        var token = await db.Set<RefreshToken>().AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw InvalidRefresh();
        var session = await db.Set<AuthSession>().SingleAsync(s => s.Id == token.SessionId, ct);
        if (!session.IsActive(now) || token.ExpiresAt <= now)
        {
            throw InvalidRefresh();
        }

        // Single-use rotation. If the token was already used, it has been replayed: revoke the whole session.
        var marked = await db.Set<RefreshToken>()
            .Where(t => t.Id == token.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        if (marked == 0)
        {
            session.Revoke(now, "REFRESH_TOKEN_REUSE");
            await db.SaveChangesAsync(ct);
            throw InvalidRefresh();
        }

        var user = await db.Set<User>().SingleAsync(u => u.Id == session.UserId, ct);
        if (user.Status != UserStatus.Active)
        {
            session.Revoke(now, "USER_NOT_ACTIVE");
            await db.SaveChangesAsync(ct);
            throw InvalidRefresh();
        }

        session.Touch(now);
        var pair = CreatePair(user, session, now);
        await db.SaveChangesAsync(ct);
        return pair;
    }

    public async Task RevokeByRefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var hash = TokenService.HashToken(refreshToken ?? string.Empty);
        var sessionId = await db.Set<RefreshToken>().Where(t => t.TokenHash == hash).Select(t => (Guid?)t.SessionId).SingleOrDefaultAsync(ct);
        if (sessionId is null)
        {
            return;
        }
        var session = await db.Set<AuthSession>().SingleAsync(s => s.Id == sessionId, ct);
        session.Revoke(clock.UtcNow, "LOGOUT");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Revokes every active session of the user (tracked; caller saves).</summary>
    public async Task<int> RevokeAllAsync(Guid userId, string reason, CancellationToken ct)
    {
        var sessions = await db.Set<AuthSession>().Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var s in sessions)
        {
            s.Revoke(clock.UtcNow, reason);
        }
        return sessions.Count;
    }

    private TokenPair CreatePair(User user, AuthSession session, DateTimeOffset now)
    {
        var (access, accessExpires) = tokens.CreateAccessToken(user, session.Audience, session.Id);
        var refresh = TokenService.NewRefreshToken();
        var refreshExpires = Min(now.AddDays(tokens.Options.RefreshTokenDays), session.ExpiresAt);
        db.Add(new RefreshToken(session.Id, TokenService.HashToken(refresh), now, refreshExpires));
        return new TokenPair(access, accessExpires, refresh, refreshExpires);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static BusinessRuleException InvalidRefresh() =>
        new("REFRESH_TOKEN_INVALID", "Your session has expired. Please sign in again.", 401);
}
