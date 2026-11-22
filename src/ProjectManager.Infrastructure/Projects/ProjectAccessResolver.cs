using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Projects;

/// <summary>
/// Acesso efetivo a um projeto: o projeto é lido pelo filtro da organização ativa, e o papel na equipe
/// é o do usuário do token.
/// </summary>
internal sealed class ProjectAccessResolver(ProjectManagerDbContext dbContext) : IProjectAccess
{
    public async Task<ProjectAccessGrant?> FindAsync(ActiveMembership membership, Guid projectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(membership);

        var project = await dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                Role = dbContext.ProjectMembers
                    .Where(member => member.ProjectId == p.Id && member.UserId == membership.UserId)
                    .Select(member => (ProjectRole?)member.Role)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return project is null ? null : ProjectAccessGrant.Compute(membership, projectId, project.Role);
    }

    public async Task<ProjectAccessGrant?> FindByTaskAsync(ActiveMembership membership, Guid taskId, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.Tasks
            .AsNoTracking()
            .Where(task => task.Id == taskId)
            .Select(task => (Guid?)task.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        return projectId is null ? null : await FindAsync(membership, projectId.Value, cancellationToken);
    }

    public async Task<ProjectAccessGrant?> FindByWorkflowStateAsync(ActiveMembership membership, Guid stateId, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.WorkflowStates
            .AsNoTracking()
            .Where(state => state.Id == stateId)
            .Select(state => (Guid?)state.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        return projectId is null ? null : await FindAsync(membership, projectId.Value, cancellationToken);
    }

    public async Task<ProjectAccessGrant?> FindByWorkflowTransitionAsync(ActiveMembership membership, Guid transitionId, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.WorkflowTransitions
            .AsNoTracking()
            .Where(transition => transition.Id == transitionId)
            .Select(transition => (Guid?)transition.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        return projectId is null ? null : await FindAsync(membership, projectId.Value, cancellationToken);
    }

    public async Task<ProjectAccessGrant?> FindByDocumentAsync(ActiveMembership membership, Guid documentId, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.ProjectDocuments
            .AsNoTracking()
            .Where(document => document.Id == documentId)
            .Select(document => (Guid?)document.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        return projectId is null ? null : await FindAsync(membership, projectId.Value, cancellationToken);
    }

    public async Task<ProjectAccessGrant?> FindByDecisionAsync(ActiveMembership membership, Guid decisionId, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.Decisions
            .AsNoTracking()
            .Where(decision => decision.Id == decisionId)
            .Select(decision => (Guid?)decision.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        return projectId is null ? null : await FindAsync(membership, projectId.Value, cancellationToken);
    }

    public async Task<ProjectAccessGrant?> FindByWorkReportAsync(ActiveMembership membership, Guid reportId, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.WorkReports
            .AsNoTracking()
            .Where(report => report.Id == reportId)
            .Select(report => (Guid?)report.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        return projectId is null ? null : await FindAsync(membership, projectId.Value, cancellationToken);
    }
}
