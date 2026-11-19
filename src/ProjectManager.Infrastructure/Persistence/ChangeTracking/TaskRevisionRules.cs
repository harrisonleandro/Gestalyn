using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ProjectManager.Domain.Tasks;

namespace ProjectManager.Infrastructure.Persistence.ChangeTracking;

/// <summary>
/// Ponto único que sobe a versão das tarefas (ADR-016, D6): toda tarefa alterada sai com a versão lida mais um. A
/// versão é token de concorrência, então o <c>UPDATE</c> só acontece se ninguém a mudou desde a leitura.
/// </summary>
internal static class TaskRevisionRules
{
    public static void Apply(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries<ProjectTask>().Where(entry => entry.State == EntityState.Modified))
        {
            var revision = entry.Property(task => task.Revision);
            revision.CurrentValue = revision.OriginalValue + 1;
        }
    }
}
