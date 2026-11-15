using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Workflows;

/// <summary>Estado do workflow de um projeto. Pertence sempre ao mesmo projeto do workflow.</summary>
public sealed class WorkflowState : Entity, IOrganizationOwned, IAuditable
{
    public const int NameMaxLength = 60;

    internal WorkflowState(WorkflowDefinition workflow, string name, StateCategory category, int position)
    {
        OrganizationId = workflow.OrganizationId;
        ProjectId = workflow.ProjectId;
        Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
        Category = EnsureDefined(category);
        Position = position;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private WorkflowState(string name) => Name = name;

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public StateCategory Category { get; private set; }

    public bool IsFinal => Category.IsFinal();

    public int Position { get; private set; }

    /// <summary>Estado em que as tarefas novas entram. Há exatamente um por workflow.</summary>
    public bool IsInitial { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal void Rename(string name) => Name = Guard.RequiredText(name, NameMaxLength, nameof(name));

    internal void ChangeCategory(StateCategory category) => Category = EnsureDefined(category);

    internal void MoveTo(int position) => Position = position;

    internal void SetInitial(bool isInitial) => IsInitial = isInitial;

    private static StateCategory EnsureDefined(StateCategory category) =>
        Enum.IsDefined(category) ? category : throw new ArgumentOutOfRangeException(nameof(category), category, "Categoria desconhecida.");
}
