using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Infrastructure.Common;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Ciclo de vida das sessões: criação, rotação idempotente de refresh tokens com detecção de reuso e
/// revogação (ADR-012).
/// </summary>
internal sealed partial class SessionManager(
    ProjectManagerDbContext dbContext,
    AccessTokenIssuer accessTokenIssuer,
    RefreshTokenDerivation derivation,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider,
    ILogger<SessionManager> logger)
{
    /// <summary>Tentativas quando outra requisição altera o mesmo token ao mesmo tempo.</summary>
    private const int MaxRefreshAttempts = 3;

    public async Task<AuthTokens> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var session = new UserSession(userId, timeProvider.GetUtcNow() + options.Value.SessionLifetime);
        var refreshToken = OpaqueTokens.Generate();
        dbContext.UserRefreshTokens.Add(new UserRefreshToken(session, OpaqueTokens.Hash(refreshToken)));

        dbContext.UserSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateTokens(session, refreshToken);
    }

    /// <summary>
    /// Troca o refresh token. O sucessor é derivado do token apresentado, então requisições simultâneas
    /// recebem o mesmo sucessor (repetição). Reapresentar um token fora da janela, ou depois que o
    /// sucessor já foi usado, revoga a sessão: a cadeia divergiu, o que indica roubo.
    /// </summary>
    /// <returns>Novos tokens, ou <c>null</c> se o refresh token for inválido ou reutilizado.</returns>
    public async Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = OpaqueTokens.Hash(refreshToken);
        var successorToken = derivation.Successor(refreshToken);
        var successorHash = OpaqueTokens.Hash(successorToken);

        for (var attempt = 1; attempt <= MaxRefreshAttempts; attempt++)
        {
            var now = timeProvider.GetUtcNow();
            var stored = await dbContext.UserRefreshTokens
                .Include(token => token.Session)
                .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

            if (stored is null)
            {
                return null;
            }

            var successorReplayable = stored.ReplacedByTokenId is { } successorId
                && await dbContext.UserRefreshTokens.AnyAsync(
                    token => token.Id == successorId && token.ConsumedAt == null && token.TokenHash == successorHash,
                    cancellationToken);

            var action = RefreshDecision.Decide(
                stored,
                successorReplayable,
                now,
                options.Value.RefreshReplayWindow,
                options.Value.MaxRefreshReplays);

            switch (action)
            {
                case RefreshAction.Rotate:
                    var successor = new UserRefreshToken(stored.Session, successorHash);
                    dbContext.UserRefreshTokens.Add(successor);
                    stored.ReplaceWith(successor, now);
                    break;

                case RefreshAction.Replay:
                    // Incremento atômico e condicional: repetições simultâneas não conflitam entre si, e
                    // passar do limite cai na revogação na próxima decisão.
                    var maxReplays = options.Value.MaxRefreshReplays;
                    var replayed = await dbContext.UserRefreshTokens
                        .Where(token => token.Id == stored.Id && token.ReplayCount < maxReplays)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ReplayCount, token => token.ReplayCount + 1), cancellationToken);
                    if (replayed == 0)
                    {
                        dbContext.ChangeTracker.Clear();
                        continue;
                    }

                    LogRefreshReplay(logger, stored.Session.UserId, stored.SessionId, stored.ReplayCount + 1);
                    return CreateTokens(stored.Session, successorToken);

                case RefreshAction.RevokeForReuse:
                    stored.Session.Revoke(now, SessionRevocationReason.RefreshTokenReuse);
                    break;

                default:
                    return null;
            }

            // Conflito: outra requisição trocou o mesmo token ao mesmo tempo. Relê o estado e decide de
            // novo (em geral, vira uma repetição com o sucessor que a outra gravou).
            if (!await dbContext.TrySaveChangesAsync(cancellationToken))
            {
                dbContext.ChangeTracker.Clear();
                continue;
            }

            if (action == RefreshAction.RevokeForReuse)
            {
                LogRefreshTokenReuse(logger, stored.Session.UserId, stored.SessionId);
                return null;
            }

            return CreateTokens(stored.Session, successorToken);
        }

        LogRefreshContended(logger, MaxRefreshAttempts);
        return null;
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

    private AuthTokens CreateTokens(UserSession session, string refreshToken)
    {
        var (accessToken, accessTokenExpiresAt) = accessTokenIssuer.Issue(session.UserId, session.Id);

        return new AuthTokens(accessToken, accessTokenExpiresAt, refreshToken, session.ExpiresAt);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reuso de refresh token detectado; sessão {SessionId} do usuário {UserId} revogada.")]
    private static partial void LogRefreshTokenReuse(ILogger logger, Guid userId, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refresh repetido dentro da janela na sessão {SessionId} do usuário {UserId} (repetição {ReplayCount}).")]
    private static partial void LogRefreshReplay(ILogger logger, Guid userId, Guid sessionId, int replayCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh recusado depois de {Attempts} conflitos seguidos no mesmo token.")]
    private static partial void LogRefreshContended(ILogger logger, int attempts);
}
