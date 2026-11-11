using ProjectManager.Domain.Common;

namespace ProjectManager.Infrastructure.Identity;

public enum SessionRevocationReason
{
    Logout,
    RefreshTokenReuse,
    PasswordReset,
}

/// <summary>
/// Sessão autenticada no servidor. Access tokens só são aceitos enquanto a sessão está ativa,
/// o que torna o logout imediato.
/// </summary>
public sealed class UserSession : Entity, IAuditable
{
    public UserSession(Guid userId, DateTimeOffset expiresAt)
    {
        UserId = userId;

        // O PostgreSQL guarda microssegundos: o valor devolvido no login fica igual ao lido do banco.
        ExpiresAt = Timestamps.TruncateToMicroseconds(expiresAt);
    }

    public Guid UserId { get; private set; }

    /// <summary>Expiração absoluta: a renovação de tokens não estende a sessão.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionRevocationReason? RevocationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, SessionRevocationReason reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
    }
}
