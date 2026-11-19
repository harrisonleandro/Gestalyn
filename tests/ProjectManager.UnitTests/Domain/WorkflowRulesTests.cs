using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Regras do workflow (ADR-013): configuração inicial, invariantes, estados finais e aprovação.</summary>
public sealed class WorkflowRulesTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();

    [Fact]
    public void DefaultWorkflow_FollowsTheMasterSpecification()
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));

        Assert.Equal(["Backlog", "A fazer", "Em andamento", "Em revisão", "Bloqueada", "Concluída"], workflow.States.Select(s => s.Name));
        Assert.Equal("Backlog", workflow.InitialState.Name);
        Assert.Equal(["Concluída"], workflow.States.Where(s => s.IsFinal).Select(s => s.Name));
        Assert.Equal(14, workflow.Transitions.Count);
        Assert.True(Find(workflow, "Em revisão", "Concluída")!.RequiresApproval);
        Assert.True(Find(workflow, "Concluída", "Em andamento")!.RequiresApproval);
        Assert.False(Find(workflow, "A fazer", "Em andamento")!.RequiresApproval);
        Assert.Null(Find(workflow, "Backlog", "Concluída"));
    }

    [Theory]
    [InlineData(StateCategory.Todo, false)]
    [InlineData(StateCategory.InProgress, false)]
    [InlineData(StateCategory.Blocked, false)]
    [InlineData(StateCategory.Done, true)]
    [InlineData(StateCategory.Canceled, true)]
    public void OnlyDoneAndCanceled_AreFinal(StateCategory category, bool isFinal)
    {
        Assert.Equal(isFinal, category.IsFinal());
    }

    [Fact]
    public void StateNames_AreUniqueIgnoringCase()
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));
        var canceled = workflow.AddState("Cancelada", StateCategory.Canceled);

        Assert.Throws<WorkflowRuleException>(() => workflow.AddState("backlog", StateCategory.Todo));
        Assert.Throws<WorkflowRuleException>(() => workflow.RenameState(canceled, "CONCLUÍDA"));
        workflow.RenameState(canceled, "Descartada");
        Assert.Equal(7, canceled.Position);
    }

    [Fact]
    public void InitialState_IsUniqueAndNeverFinal()
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));
        var todo = workflow.States.Single(s => s.Name == "A fazer");
        var done = workflow.States.Single(s => s.Name == "Concluída");

        workflow.SetInitialState(todo);

        Assert.Equal([todo], workflow.States.Where(s => s.IsInitial));
        Assert.Throws<WorkflowRuleException>(() => workflow.SetInitialState(done));
        Assert.Throws<WorkflowRuleException>(() => workflow.ChangeCategory(todo, StateCategory.Done));
        Assert.Throws<WorkflowRuleException>(() => workflow.RemoveState(todo));
    }

    [Fact]
    public void Transitions_RejectLoopsDuplicatesAndForeignStates()
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));
        var other = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Outro"));
        var backlog = workflow.States[0];
        var todo = workflow.States[1];

        Assert.Throws<WorkflowRuleException>(() => workflow.AddTransition(backlog, backlog, requiresApproval: false));
        Assert.Throws<WorkflowRuleException>(() => workflow.AddTransition(backlog, todo, requiresApproval: false));
        Assert.Throws<ArgumentException>(() => workflow.AddTransition(backlog, other.States[1], requiresApproval: false));
    }

    [Fact]
    public void RemovingAState_RemovesItsTransitions()
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));
        var blocked = workflow.States.Single(s => s.Name == "Bloqueada");

        workflow.RemoveState(blocked);

        Assert.DoesNotContain(workflow.States, s => s.Name == "Bloqueada");
        Assert.DoesNotContain(workflow.Transitions, t => t.FromStateId == blocked.Id || t.ToStateId == blocked.Id);
        Assert.Equal(8, workflow.Transitions.Count);
    }

    [Fact]
    public void MoveState_RenumbersTheOrder()
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));
        var done = workflow.States.Single(s => s.Name == "Concluída");

        workflow.MoveState(done, 1);

        Assert.Equal(["Concluída", "Backlog", "A fazer", "Em andamento", "Em revisão", "Bloqueada"], workflow.States.Select(s => s.Name));
        Assert.Equal(Enumerable.Range(1, 6), workflow.States.Select(s => s.Position));
    }

    [Fact]
    public void Task_EntersOnlyStatesOfItsProject()
    {
        var project = new Project(OrganizationId, "Projeto");
        var workflow = WorkflowDefinition.CreateDefault(project);
        var foreign = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Outro"));
        var task = new ProjectTask(project, 1, "Tarefa", Guid.CreateVersion7());

        task.EnterState(workflow.InitialState);

        Assert.Equal(workflow.InitialState.Id, task.StateId);
        Assert.Throws<ArgumentException>(() => task.EnterState(foreign.InitialState));
    }

    public static TheoryData<ActorKind, bool, bool, bool, TransitionVerdict> ApprovalCases => new()
    {
        // Tipo de autor, pode aprovar, é responsável, aprovação independente exigida, resultado.
        { ActorKind.User, false, false, false, TransitionVerdict.ApprovalPermissionRequired },
        { ActorKind.User, true, false, false, TransitionVerdict.Allowed },
        { ActorKind.User, true, true, false, TransitionVerdict.Allowed },
        { ActorKind.User, true, true, true, TransitionVerdict.CannotApproveOwnWork },
        { ActorKind.User, true, false, true, TransitionVerdict.Allowed },
        // Agentes nunca aprovam (ADR-016), nem com a permissão nem sem ser responsáveis.
        { ActorKind.Agent, true, true, false, TransitionVerdict.AgentCannotApprove },
        { ActorKind.Agent, true, false, false, TransitionVerdict.AgentCannotApprove },
        { ActorKind.Agent, false, true, false, TransitionVerdict.AgentCannotApprove },
        { ActorKind.Agent, false, false, true, TransitionVerdict.AgentCannotApprove },
    };

    [Theory]
    [MemberData(nameof(ApprovalCases))]
    public void ApprovalTransitions_RequireThePermission_AndAgentsNeverApprove(
        ActorKind kind,
        bool canApprove,
        bool isAssignee,
        bool requireIndependentApproval,
        TransitionVerdict expected)
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));
        var approval = Find(workflow, "Em revisão", "Concluída");
        var done = workflow.States.Single(s => s.Name == "Concluída");

        var verdict = TransitionRules.Evaluate(approval, done, new TransitionActor(kind, canApprove, isAssignee), requireIndependentApproval, openDependencies: 0);

        Assert.Equal(expected, verdict);
    }

    [Fact]
    public void MissingTransitions_AndOpenDependencies_AreRejected()
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));
        var done = workflow.States.Single(s => s.Name == "Concluída");
        var review = workflow.States.Single(s => s.Name == "Em revisão");
        var manager = new TransitionActor(ActorKind.User, CanApprove: true, IsAssignee: false);

        Assert.Equal(TransitionVerdict.NoSuchTransition, TransitionRules.Evaluate(null, done, manager, false, 0));
        Assert.Equal(TransitionVerdict.DependenciesOpen, TransitionRules.Evaluate(Find(workflow, "Em revisão", "Concluída"), done, manager, false, openDependencies: 1));
        Assert.Equal(TransitionVerdict.Allowed, TransitionRules.Evaluate(Find(workflow, "Em andamento", "Em revisão"), review, manager, false, openDependencies: 3));
    }

    private static WorkflowTransition? Find(WorkflowDefinition workflow, string from, string to)
    {
        var origin = workflow.States.Single(s => s.Name == from);
        var target = workflow.States.Single(s => s.Name == to);
        return workflow.FindTransition(origin.Id, target.Id);
    }
}
