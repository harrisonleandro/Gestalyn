using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Workflows;

/// <summary>Transição permitida entre dois estados do mesmo workflow.</summary>
public sealed class WorkflowTransition : Entity, IOrganizationOwned, IAuditable
{
    internal WorkflowTransition(WorkflowDefinition workflow, WorkflowState from, WorkflowState to, bool requiresApproval)
    {
        OrganizationId = workflow.OrganizationId;
        ProjectId = workflow.ProjectId;
        FromStateId = from.Id;
        ToStateId = to.Id;
        RequiresApproval = requiresApproval;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private WorkflowTransition()
    {
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid FromStateId { get; private set; }

    public Guid ToStateId { get; private set; }

    /// <summary>Só quem tem <c>tasks.approve</c> executa, e sem aprovar o próprio trabalho (ADR-013).</summary>
    public bool RequiresApproval { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
