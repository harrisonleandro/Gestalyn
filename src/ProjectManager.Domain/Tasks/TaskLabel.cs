using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Tasks;

/// <summary>Etiqueta de uma tarefa, em texto livre, única na tarefa sem diferenciar maiúsculas e minúsculas.</summary>
public sealed class TaskLabel : IOrganizationOwned
{
    internal TaskLabel(ProjectTask task, string name)
    {
        OrganizationId = task.OrganizationId;
        ProjectId = task.ProjectId;
        TaskId = task.Id;
        Name = name;
        NormalizedName = Labels.NormalizeName(name);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private TaskLabel(string name, string normalizedName)
    {
        Name = name;
        NormalizedName = normalizedName;
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    internal void ChangeSpelling(string name) => Name = name;
}
