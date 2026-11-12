using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Tasks;

/// <summary>
/// Dependência "bloqueada por": a tarefa <see cref="TaskId"/> depende da tarefa
/// <see cref="DependsOnTaskId"/>. As duas são do mesmo projeto, e o grafo de dependências não tem ciclos
/// (ADR-011).
/// </summary>
public sealed class TaskDependency : IOrganizationOwned
{
    public const int MaxPerTask = 50;

    public TaskDependency(ProjectTask task, ProjectTask dependsOn, Guid createdByUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(dependsOn);
        task.EnsureNotArchived();

        if (task.Id == dependsOn.Id)
        {
            throw new ArgumentException("Uma tarefa não pode depender de si mesma.", nameof(dependsOn));
        }

        if (task.ProjectId != dependsOn.ProjectId)
        {
            throw new ArgumentException("As tarefas precisam ser do mesmo projeto.", nameof(dependsOn));
        }

        OrganizationId = task.OrganizationId;
        ProjectId = task.ProjectId;
        TaskId = task.Id;
        DependsOnTaskId = dependsOn.Id;
        CreatedByUserId = Guard.RequiredId(createdByUserId, nameof(createdByUserId));
        CreatedAt = Timestamps.TruncateToMicroseconds(now);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private TaskDependency()
    {
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid DependsOnTaskId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Indica se acrescentar "<paramref name="taskId"/> depende de <paramref name="dependsOnTaskId"/>"
    /// criaria um ciclo, isto é, se <paramref name="dependsOnTaskId"/> já depende, direta ou
    /// indiretamente, de <paramref name="taskId"/>. <paramref name="edges"/> são as dependências
    /// existentes do projeto (tarefa, tarefa da qual depende).
    /// </summary>
    public static bool WouldCreateCycle(IEnumerable<(Guid TaskId, Guid DependsOnTaskId)> edges, Guid taskId, Guid dependsOnTaskId)
    {
        ArgumentNullException.ThrowIfNull(edges);

        if (taskId == dependsOnTaskId)
        {
            return true;
        }

        var dependsOn = edges
            .GroupBy(edge => edge.TaskId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.DependsOnTaskId).ToList());

        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(dependsOnTaskId);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current == taskId)
            {
                return true;
            }

            if (!visited.Add(current) || !dependsOn.TryGetValue(current, out var next))
            {
                continue;
            }

            next.ForEach(pending.Push);
        }

        return false;
    }
}
