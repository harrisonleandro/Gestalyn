using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Domain.Workflows;

/// <summary>Violação de uma regra do workflow (ADR-013). A API responde 409.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Sempre criada com a mensagem da regra violada.")]
public sealed class WorkflowRuleException(string message) : InvalidOperationException(message);

/// <summary>
/// Workflow de um projeto (ADR-013): estados com categoria, transições explícitas e configurações. Há um
/// por projeto, criado junto com ele. Para alterar estados e transições, eles precisam estar carregados.
/// </summary>
public sealed class WorkflowDefinition : Entity, IOrganizationOwned, IAuditable
{
    public const int MaxStates = 30;
    public const int MaxTransitions = 200;

    private readonly List<WorkflowState> _states = [];
    private readonly List<WorkflowTransition> _transitions = [];

    private WorkflowDefinition(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        OrganizationId = project.OrganizationId;
        ProjectId = project.Id;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private WorkflowDefinition()
    {
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>
    /// Quando ligado, quem é responsável pela tarefa não executa transições que exigem aprovação. Para
    /// agentes, a regra vale sempre (ADR-013).
    /// </summary>
    public bool RequireIndependentApproval { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<WorkflowState> States => _states.OrderBy(state => state.Position).ToList();

    public IReadOnlyList<WorkflowTransition> Transitions => _transitions;

    public WorkflowState InitialState => _states.Single(state => state.IsInitial);

    /// <summary>Configuração inicial da especificação mestre (seção 4.5), com as transições do ADR-013.</summary>
    public static WorkflowDefinition CreateDefault(Project project)
    {
        var workflow = new WorkflowDefinition(project);
        var backlog = workflow.AddState("Backlog", StateCategory.Todo);
        var todo = workflow.AddState("A fazer", StateCategory.Todo);
        var doing = workflow.AddState("Em andamento", StateCategory.InProgress);
        var review = workflow.AddState("Em revisão", StateCategory.InProgress);
        var blocked = workflow.AddState("Bloqueada", StateCategory.Blocked);
        var done = workflow.AddState("Concluída", StateCategory.Done);
        workflow.SetInitialState(backlog);

        (WorkflowState From, WorkflowState To, bool RequiresApproval)[] transitions =
        [
            (backlog, todo, false),
            (todo, backlog, false),
            (todo, doing, false),
            (doing, todo, false),
            (doing, review, false),
            (review, doing, false),
            (review, done, true),
            (backlog, blocked, false),
            (todo, blocked, false),
            (doing, blocked, false),
            (review, blocked, false),
            (blocked, todo, false),
            (blocked, doing, false),
            (done, doing, true),
        ];

        foreach (var (from, to, requiresApproval) in transitions)
        {
            workflow.AddTransition(from, to, requiresApproval);
        }

        return workflow;
    }

    public WorkflowState AddState(string name, StateCategory category)
    {
        if (_states.Count >= MaxStates)
        {
            throw new WorkflowRuleException($"Um workflow pode ter no máximo {MaxStates} estados.");
        }

        EnsureUniqueName(name, except: null);
        var position = _states.Count == 0 ? 1 : _states.Max(state => state.Position) + 1;
        var state = new WorkflowState(this, name, category, position);
        _states.Add(state);

        return state;
    }

    public void RenameState(WorkflowState state, string name)
    {
        EnsureOwn(state);
        EnsureUniqueName(name, except: state);
        state.Rename(name);
    }

    public void ChangeCategory(WorkflowState state, StateCategory category)
    {
        EnsureOwn(state);
        if (state.IsInitial && category.IsFinal())
        {
            throw new WorkflowRuleException("O estado inicial não pode ser final.");
        }

        state.ChangeCategory(category);
    }

    public void SetInitialState(WorkflowState state)
    {
        EnsureOwn(state);
        if (state.IsFinal)
        {
            throw new WorkflowRuleException("O estado inicial não pode ser final.");
        }

        _states.ForEach(other => other.SetInitial(other == state));
    }

    /// <summary>Move o estado para a posição informada (1 = primeiro) e renumera os demais.</summary>
    public void MoveState(WorkflowState state, int position)
    {
        EnsureOwn(state);
        var ordered = States.Where(other => other != state).ToList();
        ordered.Insert(Math.Clamp(position, 1, ordered.Count + 1) - 1, state);

        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].MoveTo(index + 1);
        }
    }

    /// <summary>Remove um estado e as transições ligadas a ele. Quem chama garante que nenhuma tarefa o usa.</summary>
    public void RemoveState(WorkflowState state)
    {
        EnsureOwn(state);
        if (state.IsInitial)
        {
            throw new WorkflowRuleException("O estado inicial não pode ser excluído. Defina outro estado inicial antes.");
        }

        _transitions.RemoveAll(transition => transition.FromStateId == state.Id || transition.ToStateId == state.Id);
        _states.Remove(state);
    }

    public WorkflowTransition AddTransition(WorkflowState from, WorkflowState to, bool requiresApproval)
    {
        EnsureOwn(from);
        EnsureOwn(to);

        if (from == to)
        {
            throw new WorkflowRuleException("Uma transição precisa ligar dois estados diferentes.");
        }

        if (_transitions.Exists(transition => transition.FromStateId == from.Id && transition.ToStateId == to.Id))
        {
            throw new WorkflowRuleException("Esta transição já existe.");
        }

        if (_transitions.Count >= MaxTransitions)
        {
            throw new WorkflowRuleException($"Um workflow pode ter no máximo {MaxTransitions} transições.");
        }

        var transition = new WorkflowTransition(this, from, to, requiresApproval);
        _transitions.Add(transition);

        return transition;
    }

    public bool RemoveTransition(Guid transitionId) => _transitions.RemoveAll(transition => transition.Id == transitionId) > 0;

    public WorkflowTransition? FindTransition(Guid fromStateId, Guid toStateId) =>
        _transitions.Find(transition => transition.FromStateId == fromStateId && transition.ToStateId == toStateId);

    public void ChangeRequireIndependentApproval(bool required) => RequireIndependentApproval = required;

    private void EnsureOwn(WorkflowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_states.Contains(state))
        {
            throw new ArgumentException("O estado pertence a outro workflow.", nameof(state));
        }
    }

    private void EnsureUniqueName(string name, WorkflowState? except)
    {
        var normalized = Guard.RequiredText(name, WorkflowState.NameMaxLength, nameof(name));
        if (_states.Exists(state => state != except && string.Equals(state.Name, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new WorkflowRuleException("Já existe um estado com este nome no workflow.");
        }
    }
}
