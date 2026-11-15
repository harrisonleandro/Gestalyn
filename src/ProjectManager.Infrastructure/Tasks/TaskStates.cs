using Microsoft.EntityFrameworkCore;
using ProjectManager.Domain.Workflows;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Tasks;

internal static class TaskStates
{
    /// <summary>Indica se o estado é final (Concluída ou Cancelada): a tarefa nele é somente leitura (ADR-013).</summary>
    public static Task<bool> IsFinalAsync(ProjectManagerDbContext dbContext, Guid stateId, CancellationToken cancellationToken) =>
        dbContext.WorkflowStates.AnyAsync(
            state => state.Id == stateId && (state.Category == StateCategory.Done || state.Category == StateCategory.Canceled),
            cancellationToken);
}
