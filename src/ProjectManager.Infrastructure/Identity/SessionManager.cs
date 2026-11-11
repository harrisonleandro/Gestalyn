using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Ciclo de vida das sessões: criação, rotação de refresh tokens com detecção de reuso e revogação.
/// </summary>
internal sealed partial class SessionManager(
    ProjectManagerDbContext dbContext,
    AccessTokenIssuer accessTokenIssuer,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider,
    ILogger<SessionManager> logger)
{
    public async Task<AuthTokens> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var session = new UserSession(userId, timeProvider.GetUtcNow() + options.Value.SessionLifetime);
        var refreshToken = AddRefreshToken(session);

        dbContext.UserSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateTokens(session, refreshToken);
    }

    /// <returns>Novos tokens, ou <c>null</c> se o refresh token for inválido.</returns>
    public async Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tokenHash = RefreshTokens.Hash(refreshToken);

        var stored = await dbContext.UserRefreshTokens
            .Include(token => token.Session)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (stored is null)
        {
            return null;
        }

        if (stored.ConsumedAt is { } consumedAt)
        {
            if (now - consumedAt <= options.Value.RefreshReuseGracePeriod)
            {
                // Requisições paralelas do mesmo cliente com o mesmo token: só a primeira renova.
                return null;
            }

            // Um token já trocado foi reapresentado: trate a sessão como comprometida.
            stored.Session.Revoke(now, SessionRevocationReason.RefreshTokenReuse);
            await dbContext.SaveChangesAsync(cancellationToken);
            LogRefreshTokenReuse(logger, stored.Session.UserId, stored.SessionId);
            return null;
        }

        if (!stored.Session.IsActive(now) || stored.ExpiresAt <= now)
        {
            return null;
        }

        stored.Consume(now);
        var newRefreshToken = AddRefreshToken(stored.Session);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra requisição trocou o mesmo token ao mesmo tempo; somente a primeira vence.
            return null;
        }

        return CreateTokens(stored.Session, newRefreshToken);
    }

    public async Task RevokeAsync(Guid userId, Guid sessionId, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        var session = await dbContext.UserSessions
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken);

        if (session is null)
        {
            return;
        }

        session.Revoke(timeProvider.GetUtcNow(), reason);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var activeSessions = await dbContext.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in activeSessions)
        {
            session.Revoke(now, reason);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> IsActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        return dbContext.UserSessions.AnyAsync(
            s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now,
            cancellationToken);
    }

    private string AddRefreshToken(UserSession session)
    {
        var refreshToken = RefreshTokens.Generate();
        dbContext.UserRefreshTokens.Add(new UserRefreshToken(session, RefreshTokens.Hash(refreshToken)));

        return refreshToken;
    }

    private AuthTokens CreateTokens(UserSession session, string refreshToken)
    {
        var (accessToken, accessTokenExpiresAt) = accessTokenIssuer.Issue(session.UserId, session.Id);

        return new AuthTokens(accessToken, accessTokenExpiresAt, refreshToken, session.ExpiresAt);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reuso de refresh token detectado; sessão {SessionId} do usuário {UserId} revogada.")]
    private static partial void LogRefreshTokenReuse(ILogger logger, Guid userId, Guid sessionId);
}
