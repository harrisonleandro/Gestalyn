using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Refresh token de uma sessão. Somente o hash SHA-256 é armazenado. Cada token é de uso único:
/// ao ser trocado fica marcado como consumido, e reapresentá-lo indica reuso (possível roubo).
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

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Consume(DateTimeOffset now) => ConsumedAt ??= now;
}
