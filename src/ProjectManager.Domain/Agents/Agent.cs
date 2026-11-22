using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Agents;

public enum AgentStatus
{
    Active,
    Disabled,
}

/// <summary>
/// Agente de IA da organização (ADR-016). Não é membro da organização nem acessa a Web: autentica-se por
/// credenciais próprias e só enxerga os projetos concedidos. Nunca é excluído, para preservar a autoria; é
/// desativado, o que bloqueia todas as credenciais dele.
/// </summary>
public sealed class Agent : Entity, IOrganizationOwned, IAuditable
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public Agent(Guid organizationId, string name, string? description, Guid createdByUserId)
    {
        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        CreatedByUserId = Guard.RequiredId(createdByUserId, nameof(createdByUserId));
        (Name, NormalizedName) = ValidName(name);
        Description = Guard.OptionalText(description, DescriptionMaxLength, nameof(description));
        Status = AgentStatus.Active;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private Agent(string name, string normalizedName)
    {
        Name = name;
        NormalizedName = normalizedName;
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Nome em maiúsculas, único na organização.</summary>
    public string NormalizedName { get; private set; }

    public string? Description { get; private set; }

    public AgentStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public Guid? DisabledByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status == AgentStatus.Active;

    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToUpperInvariant();
    }

    public void Update(string name, string? description)
    {
        (Name, NormalizedName) = ValidName(name);
        Description = Guard.OptionalText(description, DescriptionMaxLength, nameof(description));
    }

    /// <summary>Desativa o agente: nenhuma credencial dele vale a partir da requisição seguinte.</summary>
    public void Disable(Guid userId, DateTimeOffset now)
    {
        Status = AgentStatus.Disabled;
        DisabledAt = now;
        DisabledByUserId = Guard.RequiredId(userId, nameof(userId));
    }

    /// <summary>Reativa o agente. Credenciais revogadas ou expiradas continuam sem valer.</summary>
    public void Enable()
    {
        Status = AgentStatus.Active;
        DisabledAt = null;
        DisabledByUserId = null;
    }

    private static (string Name, string NormalizedName) ValidName(string name)
    {
        var valid = Guard.RequiredText(name, NameMaxLength, nameof(name));
        return (valid, NormalizeName(valid));
    }
}
