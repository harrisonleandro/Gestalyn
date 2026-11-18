using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Domain.Agents;

/// <summary>
/// Acesso de um agente a um projeto (ADR-016), com papel Colaborador ou Observador. Agentes nunca são Gerentes.
/// O acesso pertence ao agente: continua válido se quem o concedeu perder a permissão.
/// </summary>
public sealed class AgentProjectAccess : Entity, IOrganizationOwned, IAuditable
{
    public const string ManagerNotAllowedMessage = "Agentes só podem ser Colaboradores ou Observadores de um projeto.";

    public AgentProjectAccess(Agent agent, Project project, ProjectRole role, Guid grantedByUserId)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(project);
        if (agent.OrganizationId != project.OrganizationId)
        {
            throw new ArgumentException("O agente e o projeto precisam ser da mesma organização.", nameof(project));
        }

        OrganizationId = agent.OrganizationId;
        AgentId = agent.Id;
        ProjectId = project.Id;
        Role = AllowedRole(role);
        GrantedByUserId = Guard.RequiredId(grantedByUserId, nameof(grantedByUserId));
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private AgentProjectAccess()
    {
    }

    public Guid OrganizationId { get; private set; }

    public Guid AgentId { get; private set; }

    public Guid ProjectId { get; private set; }

    public ProjectRole Role { get; private set; }

    public Guid GrantedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static bool IsAllowed(ProjectRole role) => role is ProjectRole.Contributor or ProjectRole.Viewer;

    public void ChangeRole(ProjectRole role, Guid grantedByUserId)
    {
        Role = AllowedRole(role);
        GrantedByUserId = Guard.RequiredId(grantedByUserId, nameof(grantedByUserId));
    }

    private static ProjectRole AllowedRole(ProjectRole role) =>
        IsAllowed(role) ? role : throw new AgentRuleException(ManagerNotAllowedMessage);
}
