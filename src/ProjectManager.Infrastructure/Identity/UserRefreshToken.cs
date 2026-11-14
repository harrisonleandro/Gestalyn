using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Refresh token de uma sessão. Somente o hash SHA-256 é armazenado. Cada token é de uso único: ao ser
/// trocado fica marcado como consumido e ligado ao sucessor. Reapresentá-lo só é aceito como repetição
/// dentro da janela e enquanto o sucessor não foi usado; fora disso, indica reuso (ADR-012).
/// </summary>
public sealed class UserRefreshToken : Entity, IAuditable
{
    public UserRefreshToken(UserSession session, string tokenHash)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        Session = session;
        SessionId = session.Id;
        TokenHash = tokenHash;
        ExpiresAt = session.ExpiresAt;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private UserRefreshToken(string tokenHash) => TokenHash = tokenHash;

    public Guid SessionId { get; private set; }

    public UserSession Session { get; private set; } = null!;

    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>Token emitido na troca deste. <c>null</c> enquanto não foi trocado.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Repetições aceitas dentro da janela (requisições simultâneas com este token).</summary>
    public int ReplayCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Marca o token como trocado pelo sucessor.</summary>
    public void ReplaceWith(UserRefreshToken successor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(successor);
        if (ConsumedAt is not null)
        {
            throw new InvalidOperationException("O refresh token já foi trocado.");
        }

        ConsumedAt = now;
        ReplacedByTokenId = successor.Id;
    }
}
