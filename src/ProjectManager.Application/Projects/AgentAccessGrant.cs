using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Application.Projects;

public enum AgentOperationKind
{
    Read,
    Write,
}

/// <summary>
/// O que uma rota de agente exige (ADR-016, D4): o escopo da credencial e a permissão de projeto correspondente. É a
/// tabela única de escopos; as rotas de agentes usam estes valores, sem outra fonte.
/// </summary>
public sealed record AgentRequirement(string Scope, string Permission, AgentOperationKind Kind)
{
    /// <summary>Projetos concedidos e contexto do projeto.</summary>
    public static AgentRequirement ContextRead { get; } = new(AgentScopes.ContextRead, ProjectPermissions.Read, AgentOperationKind.Read);

    /// <summary>Listar e ler tarefas.</summary>
    public static AgentRequirement TasksRead { get; } = new(AgentScopes.TasksRead, ProjectPermissions.Read, AgentOperationKind.Read);

    /// <summary>Criar, editar e mover tarefas, sem aprovação.</summary>
    public static AgentRequirement TasksWrite { get; } = new(AgentScopes.TasksWrite, ProjectPermissions.TasksWrite, AgentOperationKind.Write);

    /// <summary>Registrar atividades: a mesma permissão do registro manual por pessoas.</summary>
    public static AgentRequirement ActivitiesWrite { get; } = new(AgentScopes.ActivitiesWrite, ProjectPermissions.DocumentsWrite, AgentOperationKind.Write);

    /// <summary>Enviar relatórios de trabalho: só Colaboradores.</summary>
    public static AgentRequirement ReportsWrite { get; } = new(AgentScopes.ReportsWrite, ProjectPermissions.TasksWrite, AgentOperationKind.Write);

    public static IReadOnlyList<AgentRequirement> All { get; } = [ContextRead, TasksRead, TasksWrite, ActivitiesWrite, ReportsWrite];

    /// <summary>A exigência de um escopo (cada escopo tem exatamente uma).</summary>
    public static AgentRequirement ForScope(string scope) =>
        All.SingleOrDefault(requirement => requirement.Scope == scope)
        ?? throw new ArgumentOutOfRangeException(nameof(scope), scope, "Escopo de agente desconhecido.");
}

public enum AgentAccessOutcome
{
    Allowed,

    /// <summary>Agente desativado: equivale a uma credencial que não autentica (401).</summary>
    AgentDisabled,

    /// <summary>Sem concessão no projeto, ou projeto de outra organização (404).</summary>
    ProjectNotVisible,

    /// <summary>A credencial não tem o escopo da rota (403).</summary>
    ScopeMissing,

    /// <summary>O papel do agente no projeto, somado aos escopos, não concede a permissão (403).</summary>
    PermissionMissing,
}

/// <summary>Decisão de acesso de um agente, com o motivo (que também vai para a auditoria de operações).</summary>
public sealed record AgentAccessDecision(AgentAccessOutcome Outcome, ProjectAccessGrant? Grant)
{
    public bool IsAllowed => Outcome == AgentAccessOutcome.Allowed;
}

/// <summary>
/// Regra única de acesso de agentes a projetos (ADR-016, D4). As permissões do agente num projeto são as do papel
/// (<see cref="ProjectPermissions.For"/>), limitadas às que um agente pode ter e às que os escopos da credencial
/// concedem. <c>tasks.approve</c> e as demais permissões de gestão nunca entram, qualquer que seja o papel ou o escopo.
/// O arquivamento do projeto continua tratado pelos serviços, como para pessoas.
/// </summary>
public static class AgentAccessGrant
{
    /// <summary>As únicas permissões de projeto que um agente pode ter.</summary>
    public static IReadOnlySet<string> EligiblePermissions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ProjectPermissions.Read,
        ProjectPermissions.TasksWrite,
        ProjectPermissions.DocumentsWrite,
    };

    public static AgentAccessDecision Compute(
        ActiveAgent agent,
        bool agentIsActive,
        Guid projectId,
        ProjectRole? role,
        AgentRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!agentIsActive)
        {
            return Denied(AgentAccessOutcome.AgentDisabled);
        }

        // Sem concessão, ou com um papel que agentes não podem ter, o projeto é invisível.
        if (role is not { } projectRole || !AgentProjectAccess.IsAllowed(projectRole))
        {
            return Denied(AgentAccessOutcome.ProjectNotVisible);
        }

        if (!agent.HasScope(requirement.Scope))
        {
            return Denied(AgentAccessOutcome.ScopeMissing);
        }

        var permissions = PermissionsFor(projectRole, agent.Scopes);
        if (!permissions.Contains(requirement.Permission))
        {
            return Denied(AgentAccessOutcome.PermissionMissing);
        }

        return new AgentAccessDecision(
            AgentAccessOutcome.Allowed,
            new ProjectAccessGrant(projectId, projectRole, permissions) { AgentId = agent.AgentId });
    }

    /// <summary>Papel do projeto ∩ permissões possíveis para agentes ∩ permissões dos escopos da credencial.</summary>
    public static IReadOnlySet<string> PermissionsFor(ProjectRole role, IReadOnlySet<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var fromScopes = AgentRequirement.All.Where(requirement => scopes.Contains(requirement.Scope)).Select(requirement => requirement.Permission);
        var permissions = new HashSet<string>(ProjectPermissions.For(role), StringComparer.Ordinal);
        permissions.IntersectWith(EligiblePermissions);
        permissions.IntersectWith(fromScopes);
        return permissions;
    }

    private static AgentAccessDecision Denied(AgentAccessOutcome outcome) => new(outcome, Grant: null);
}
