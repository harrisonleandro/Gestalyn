using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Organizations;

/// <summary>
/// Convite para entrar em uma organização, vinculado a um e-mail. É de uso único, expira e pode ser
/// revogado. Somente o hash do token é armazenado.
/// </summary>
public sealed class Invitation : Entity, IOrganizationOwned, IAuditable
{
    public const int EmailMaxLength = 256;

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public Invitation(Guid organizationId, string email, Role role, Guid invitedByUserId, string tokenHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        Email = NormalizeEmail(email);
        RoleId = role.OrganizationId == OrganizationId
            ? role.Id
            : throw new ArgumentException("O papel pertence a outra organização.", nameof(role));
        InvitedByUserId = Guard.RequiredId(invitedByUserId, nameof(invitedByUserId));
        TokenHash = tokenHash;
        ExpiresAt = Timestamps.TruncateToMicroseconds(now + Lifetime);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private Invitation(string email, string tokenHash)
    {
        Email = email;
        TokenHash = tokenHash;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>E-mail convidado, normalizado em minúsculas.</summary>
    public string Email { get; private set; }

    public Guid RoleId { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    public bool IsFor(string email) => string.Equals(Email, NormalizeEmail(email), StringComparison.Ordinal);

    public void Accept(Guid userId, DateTimeOffset now)
    {
        if (!IsPending(now))
        {
            throw new InvalidOperationException("O convite não está pendente.");
        }

        AcceptedAt = now;
        AcceptedByUserId = Guard.RequiredId(userId, nameof(userId));
    }

    public void Revoke(DateTimeOffset now)
    {
        if (AcceptedAt is not null)
        {
            throw new InvalidOperationException("Um convite aceito não pode ser revogado.");
        }

        RevokedAt ??= now;
    }

    public static string NormalizeEmail(string email)
    {
        var normalized = Guard.RequiredText(email, EmailMaxLength, nameof(email)).ToLowerInvariant();

        return normalized.Contains('@', StringComparison.Ordinal)
            ? normalized
            : throw new ArgumentException("Informe um e-mail válido.", nameof(email));
    }
}
