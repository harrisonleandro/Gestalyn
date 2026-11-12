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
}
