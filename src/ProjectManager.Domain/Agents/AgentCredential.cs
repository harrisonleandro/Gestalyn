using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Agents;

/// <summary>Violação de uma regra de agentes ou credenciais, com a mensagem para quem fez o pedido.</summary>
public sealed class AgentRuleException(string message) : InvalidOperationException(message);

/// <summary>
/// Credencial de um agente (ADR-016). O token completo é mostrado uma única vez, na criação; aqui ficam só o
/// SHA-256 dele e um prefixo de identificação. A expiração é obrigatória e a revogação vale na requisição seguinte.
/// </summary>
public sealed class AgentCredential : Entity, IOrganizationOwned, IAuditable
{
    public const int NameMaxLength = 100;
    public const int MaxActivePerAgent = 3;
    public const int MinLifetimeDays = 1;
    public const int MaxLifetimeDays = 365;
    public const string DisabledAgentMessage = "O agente está desativado. Reative-o antes de criar credenciais.";

    [SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "Preenchido pelo EF Core na materialização.")]
    private string[] _scopes;

    public AgentCredential(
        Agent agent,
        string name,
        IEnumerable<string> scopes,
        string tokenHash,
        string prefix,
        DateTimeOffset expiresAt,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (!agent.IsActive)
        {
            throw new AgentRuleException(DisabledAgentMessage);
        }

        OrganizationId = agent.OrganizationId;
        AgentId = agent.Id;
        Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
        _scopes = [.. AgentScopes.Normalize(scopes, out var scopeError) ?? throw new ArgumentException(scopeError, nameof(scopes))];
        TokenHash = AgentTokens.IsHash(tokenHash) ? tokenHash : throw new ArgumentException("Hash de token inválido.", nameof(tokenHash));
        Prefix = AgentTokens.IsPrefix(prefix) ? prefix : throw new ArgumentException("Prefixo de token inválido.", nameof(prefix));
        CreatedByUserId = Guard.RequiredId(createdByUserId, nameof(createdByUserId));

        if (expiresAt < now.AddDays(MinLifetimeDays) || expiresAt > now.AddDays(MaxLifetimeDays))
        {
            throw new AgentRuleException($"A validade deve ser de {MinLifetimeDays} a {MaxLifetimeDays} dias.");
        }

        ExpiresAt = expiresAt;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private AgentCredential(string name, string tokenHash, string prefix)
    {
        Name = name;
        TokenHash = tokenHash;
        Prefix = prefix;
        _scopes = [];
    }

    public Guid OrganizationId { get; private set; }

    public Guid AgentId { get; private set; }

    public string Name { get; private set; }

    /// <summary>SHA-256 do token completo, em hexadecimal. O token nunca é guardado.</summary>
    public string TokenHash { get; private set; }

    /// <summary>Os primeiros caracteres depois de <c>gsk_</c>: identificam a credencial, mas não bastam para usá-la.</summary>
    public string Prefix { get; private set; }

    public IReadOnlyList<string> Scopes => _scopes;

    public DateTimeOffset ExpiresAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Ativa: não revogada e ainda dentro da validade.</summary>
    public bool IsActiveAt(DateTimeOffset now) => !IsRevoked && now < ExpiresAt;

    /// <summary>Revoga a credencial. Revogar de novo não muda a data nem o autor da primeira revogação.</summary>
    public void Revoke(Guid userId, DateTimeOffset now)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedByUserId = Guard.RequiredId(userId, nameof(userId));
    }
}
