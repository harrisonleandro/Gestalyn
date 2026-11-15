using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Common;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;
using ProjectManager.Infrastructure.Activity;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Workflows;

/// <summary>
/// Configuração do workflow (ADR-013). Toda edição marca o workflow como alterado: o <c>xmin</c> da linha
/// serializa edições simultâneas e protege as invariantes que ficam no domínio (estado inicial único, nomes
/// únicos). Violações de regra respondem 409.
/// </summary>
internal sealed class WorkflowService(ProjectManagerDbContext dbContext, ActivityRecorder activity) : IWorkflowService
{
    private const string WorkflowNotFoundMessage = "Workflow não encontrado.";
    private const string StateNotFoundMessage = "Estado não encontrado.";
    private const string ConflictMessage = "O workflow foi alterado por outra pessoa. Recarregue e tente novamente.";

    public async Task<Result<WorkflowDetails>> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var workflow = await LoadAsync(projectId, tracked: false, cancellationToken);

        return workflow is null
            ? Result.Failure<WorkflowDetails>(ErrorKind.NotFound, WorkflowNotFoundMessage)
            : Result.Success(await ToDetailsAsync(workflow, cancellationToken));
    }

    public async Task<Result<WorkflowDetails>> UpdateSettingsAsync(Guid projectId, UpdateWorkflowRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var found = await LoadWritableAsync(projectId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<WorkflowDetails>(found.Error.Kind, found.Error.Message);
        }

        var workflow = found.Value!;
        var initialBefore = workflow.States.Single(s => s.IsInitial);
        if (request.InitialStateId is { } initialStateId)
        {
            var state = workflow.States.FirstOrDefault(s => s.Id == initialStateId);
            if (state is null)
            {
                return Result.Failure<WorkflowDetails>(ErrorKind.NotFound, StateNotFoundMessage);
            }

            if (Apply(() => workflow.SetInitialState(state)) is { } ruleError)
            {
                return Result.Failure<WorkflowDetails>(ErrorKind.Conflict, ruleError);
            }
        }

        if (request.RequireIndependentApproval is { } required)
        {
            workflow.ChangeRequireIndependentApproval(required);
        }

        var settingsChanges = activity.ChangesOf(workflow).Set("initialState", initialBefore.Name, workflow.States.Single(s => s.IsInitial).Name);
        if (!settingsChanges.IsEmpty)
        {
            activity.Record(ActivityActions.WorkflowUpdated, ActivityEntityTypes.Workflow, workflow.Id, "Configurações do workflow alteradas", projectId, changes: settingsChanges);
        }

        return await SaveAsync(workflow, cancellationToken)
            ? Result.Success(await ToDetailsAsync(workflow, cancellationToken))
            : Result.Failure<WorkflowDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result<WorkflowStateDetails>> AddStateAsync(Guid projectId, CreateStateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var found = await LoadWritableAsync(projectId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<WorkflowStateDetails>(found.Error.Kind, found.Error.Message);
        }

        WorkflowState? state = null;
        if (Apply(() => state = found.Value!.AddState(request.Name, request.Category!.Value)) is { } ruleError)
        {
            return Result.Failure<WorkflowStateDetails>(ErrorKind.Conflict, ruleError);
        }

        activity.Record(
            ActivityActions.WorkflowStateAdded,
            ActivityEntityTypes.WorkflowState,
            state!.Id,
            $"Estado '{state.Name}' ({state.Category}) criado",
            projectId);
        return await SaveAsync(found.Value!, cancellationToken)
            ? Result.Success(ToDetails(state!, taskCount: 0))
            : Result.Failure<WorkflowStateDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result<WorkflowStateDetails>> UpdateStateAsync(Guid stateId, UpdateStateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var found = await LoadByStateAsync(stateId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<WorkflowStateDetails>(found.Error.Kind, found.Error.Message);
        }

        var (workflow, state) = found.Value;
        var (nameBefore, categoryBefore) = (state.Name, state.Category);
        var ruleError = Apply(() =>
        {
            if (request.Name is not null)
            {
                workflow.RenameState(state, request.Name);
            }

            if (request.Category is { } category)
            {
                workflow.ChangeCategory(state, category);
            }
        });
        if (ruleError is not null)
        {
            return Result.Failure<WorkflowStateDetails>(ErrorKind.Conflict, ruleError);
        }

        var stateChanges = new ActivityChanges().Set("name", nameBefore, state.Name).Set("category", categoryBefore, state.Category);
        if (!stateChanges.IsEmpty)
        {
            activity.Record(ActivityActions.WorkflowStateUpdated, ActivityEntityTypes.WorkflowState, state.Id, $"Estado '{state.Name}' alterado", workflow.ProjectId, changes: stateChanges);
        }

        return await SaveAsync(workflow, cancellationToken)
            ? Result.Success(ToDetails(state, await CountTasksAsync(state.Id, cancellationToken)))
            : Result.Failure<WorkflowStateDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result<WorkflowDetails>> MoveStateAsync(Guid stateId, MoveStateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var found = await LoadByStateAsync(stateId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<WorkflowDetails>(found.Error.Kind, found.Error.Message);
        }

        var (workflow, state) = found.Value;
        var positionBefore = state.Position;
        workflow.MoveState(state, request.Position);
        activity.Record(
            ActivityActions.WorkflowStateMoved,
            ActivityEntityTypes.WorkflowState,
            state.Id,
            $"Estado '{state.Name}' movido para a posição {state.Position}",
            workflow.ProjectId,
            changes: new ActivityChanges().Set("position", positionBefore, state.Position));

        return await SaveAsync(workflow, cancellationToken)
            ? Result.Success(await ToDetailsAsync(workflow, cancellationToken))
            : Result.Failure<WorkflowDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result> DeleteStateAsync(Guid stateId, CancellationToken cancellationToken)
    {
        var found = await LoadByStateAsync(stateId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure(found.Error.Kind, found.Error.Message);
        }

        var (workflow, state) = found.Value;
        if (await CountTasksAsync(state.Id, cancellationToken) > 0)
        {
            return Result.Failure(ErrorKind.Conflict, "O estado tem tarefas e não pode ser excluído. Mova as tarefas para outro estado antes.");
        }

        var linked = workflow.Transitions.Where(t => t.FromStateId == state.Id || t.ToStateId == state.Id).ToList();
        if (Apply(() => workflow.RemoveState(state)) is { } ruleError)
        {
            return Result.Failure(ErrorKind.Conflict, ruleError);
        }

        // Exclusões explícitas: sair da coleção do workflow, sozinho, deixaria órfãos numa relação obrigatória.
        dbContext.WorkflowTransitions.RemoveRange(linked);
        dbContext.WorkflowStates.Remove(state);
        activity.Record(ActivityActions.WorkflowStateDeleted, ActivityEntityTypes.WorkflowState, state.Id, $"Estado '{state.Name}' excluído", workflow.ProjectId);

        return await SaveAsync(workflow, cancellationToken)
            ? Result.Success()
            : Result.Failure(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result<WorkflowTransitionDetails>> AddTransitionAsync(CreateTransitionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var found = await LoadByStateAsync(request.FromStateId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<WorkflowTransitionDetails>(found.Error.Kind, found.Error.Message);
        }

        var (workflow, from) = found.Value;
        var to = workflow.States.FirstOrDefault(s => s.Id == request.ToStateId);
        if (to is null)
        {
            return Result.Failure<WorkflowTransitionDetails>(ErrorKind.NotFound, "Estado de destino não encontrado neste projeto.");
        }

        WorkflowTransition? transition = null;
        if (Apply(() => transition = workflow.AddTransition(from, to, request.RequiresApproval)) is { } ruleError)
        {
            return Result.Failure<WorkflowTransitionDetails>(ErrorKind.Conflict, ruleError);
        }

        activity.Record(
            ActivityActions.WorkflowTransitionAdded,
            ActivityEntityTypes.WorkflowTransition,
            transition!.Id,
            $"Transição '{from.Name}' → '{to.Name}' criada" + (transition.RequiresApproval ? " (exige aprovação)" : string.Empty),
            workflow.ProjectId);
        return await SaveAsync(workflow, cancellationToken)
            ? Result.Success(ToDetails(transition!))
            : Result.Failure<WorkflowTransitionDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result> DeleteTransitionAsync(Guid transitionId, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.WorkflowTransitions
            .Where(t => t.Id == transitionId)
            .Select(t => (Guid?)t.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);
        if (projectId is null)
        {
            return Result.Failure(ErrorKind.NotFound, "Transição não encontrada.");
        }

        var found = await LoadWritableAsync(projectId.Value, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure(found.Error.Kind, found.Error.Message);
        }

        var transition = found.Value!.Transitions.Single(t => t.Id == transitionId);
        found.Value.RemoveTransition(transitionId);
        dbContext.WorkflowTransitions.Remove(transition);
        var names = found.Value.States.ToDictionary(s => s.Id, s => s.Name);
        activity.Record(
            ActivityActions.WorkflowTransitionRemoved,
            ActivityEntityTypes.WorkflowTransition,
            transition.Id,
            $"Transição '{names[transition.FromStateId]}' → '{names[transition.ToStateId]}' removida",
            projectId.Value);

        return await SaveAsync(found.Value, cancellationToken)
            ? Result.Success()
            : Result.Failure(ErrorKind.Conflict, ConflictMessage);
    }

    private async Task<WorkflowDefinition?> LoadAsync(Guid projectId, bool tracked, CancellationToken cancellationToken)
    {
        var query = dbContext.Workflows.Include(w => w.States).Include(w => w.Transitions).AsSplitQuery();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(w => w.ProjectId == projectId, cancellationToken);
    }

    /// <summary>Workflow rastreado para alteração; projeto arquivado é somente leitura.</summary>
    private async Task<Result<WorkflowDefinition>> LoadWritableAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (await dbContext.Projects.AnyAsync(p => p.Id == projectId && p.ArchivedAt != null, cancellationToken))
        {
            return Result.Failure<WorkflowDefinition>(ErrorKind.Conflict, Project.ArchivedMessage);
        }

        var workflow = await LoadAsync(projectId, tracked: true, cancellationToken);

        return workflow is null
            ? Result.Failure<WorkflowDefinition>(ErrorKind.NotFound, WorkflowNotFoundMessage)
            : Result.Success(workflow);
    }

    private async Task<Result<(WorkflowDefinition Workflow, WorkflowState State)>> LoadByStateAsync(Guid stateId, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.WorkflowStates
            .Where(s => s.Id == stateId)
            .Select(s => (Guid?)s.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);
        if (projectId is null)
        {
            return Result.Failure<(WorkflowDefinition, WorkflowState)>(ErrorKind.NotFound, StateNotFoundMessage);
        }

        var found = await LoadWritableAsync(projectId.Value, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<(WorkflowDefinition, WorkflowState)>(found.Error.Kind, found.Error.Message);
        }

        return Result.Success((found.Value!, found.Value!.States.Single(s => s.Id == stateId)));
    }

    private static string? Apply(Action edit)
    {
        try
        {
            edit();
            return null;
        }
        catch (WorkflowRuleException exception)
        {
            return exception.Message;
        }
    }

    private async Task<bool> SaveAsync(WorkflowDefinition workflow, CancellationToken cancellationToken)
    {
        dbContext.Entry(workflow).State = EntityState.Modified;
        return await dbContext.TrySaveChangesAsync(cancellationToken);
    }

    private Task<int> CountTasksAsync(Guid stateId, CancellationToken cancellationToken) =>
        dbContext.Tasks.CountAsync(task => task.StateId == stateId, cancellationToken);

    private async Task<WorkflowDetails> ToDetailsAsync(WorkflowDefinition workflow, CancellationToken cancellationToken)
    {
        var counts = await dbContext.Tasks
            .Where(task => task.ProjectId == workflow.ProjectId)
            .GroupBy(task => task.StateId)
            .Select(group => new { StateId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.StateId, row => row.Count, cancellationToken);

        return new WorkflowDetails(
            workflow.ProjectId,
            workflow.RequireIndependentApproval,
            workflow.States.Select(state => ToDetails(state, counts.GetValueOrDefault(state.Id))).ToList(),
            workflow.Transitions.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(ToDetails).ToList());
    }

    private static WorkflowStateDetails ToDetails(WorkflowState state, int taskCount) =>
        new(state.Id, state.Name, state.Category, state.IsFinal, state.IsInitial, state.Position, taskCount);

    private static WorkflowTransitionDetails ToDetails(WorkflowTransition transition) =>
        new(transition.Id, transition.FromStateId, transition.ToStateId, transition.RequiresApproval);
}
