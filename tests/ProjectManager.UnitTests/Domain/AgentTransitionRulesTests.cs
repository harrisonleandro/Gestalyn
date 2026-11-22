using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.UnitTests.Domain;

/// <summary>
/// Regras de transição para agentes (ADR-016, D5): nunca aprovam e nunca entram em Concluída, inclusive em workflows
/// personalizados; podem pedir Cancelada sem aprovação. As regras de pessoas não mudam.
/// </summary>
public sealed class AgentTransitionRulesTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();

    /// <summary>Agente em todas as combinações que poderiam liberá-lo por engano.</summary>
    public static TheoryData<bool, bool> AgentConditions => new()
    {
        { false, false },
        { true, false },
        { false, true },
        { true, true },
    };

    [Theory]
    [MemberData(nameof(AgentConditions))]
    public void Agent_NeverExecutesApprovalTransitions_InTheDefaultWorkflow(bool canApprove, bool isAssignee)
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));
        var agent = new TransitionActor(ActorKind.Agent, canApprove, isAssignee);

        foreach (var transition in workflow.Transitions.Where(t => t.RequiresApproval))
        {
            var target = workflow.States.Single(s => s.Id == transition.ToStateId);
            foreach (var independent in new[] { false, true })
            {
                Assert.Equal(TransitionVerdict.AgentCannotApprove, TransitionRules.Evaluate(transition, target, agent, independent, openDependencies: 0));
            }
        }
    }

    [Theory]
    [MemberData(nameof(AgentConditions))]
    public void Agent_NeverEntersDone_EvenThroughACustomTransitionWithoutApproval(bool canApprove, bool isAssignee)
    {
        var (workflow, doing, done, _) = CustomWorkflow();
        var shortcut = workflow.FindTransition(doing.Id, done.Id)!;
        var agent = new TransitionActor(ActorKind.Agent, canApprove, isAssignee);

        Assert.False(shortcut.RequiresApproval);
        Assert.Equal(TransitionVerdict.AgentCannotEnterDone, TransitionRules.Evaluate(shortcut, done, agent, requireIndependentApproval: false, openDependencies: 0));
        Assert.Equal(TransitionVerdict.AgentCannotEnterDone, TransitionRules.Evaluate(shortcut, done, agent, requireIndependentApproval: true, openDependencies: 3));
        Assert.False(TransitionRules.IsOffered(false, StateCategory.Done, agent, requireIndependentApproval: false));
    }

    [Fact]
    public void Agent_MayCancel_WhenTheTransitionExistsWithoutApproval()
    {
        var (workflow, doing, _, canceled) = CustomWorkflow();
        var review = workflow.States.Single(s => s.Name == "Em revisão");
        var agent = new TransitionActor(ActorKind.Agent, CanApprove: false, IsAssignee: false);

        Assert.Equal(TransitionVerdict.Allowed, TransitionRules.Evaluate(workflow.FindTransition(doing.Id, canceled.Id), canceled, agent, false, 0));
        Assert.Equal(TransitionVerdict.AgentCannotApprove, TransitionRules.Evaluate(workflow.FindTransition(review.Id, canceled.Id), canceled, agent, false, 0));
        Assert.Equal(TransitionVerdict.NoSuchTransition, TransitionRules.Evaluate(workflow.FindTransition(workflow.InitialState.Id, canceled.Id), canceled, agent, false, 0));
    }

    [Fact]
    public void Agent_MayMoveTheTaskToReview_WhereAPersonApproves()
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Projeto"));
        var doing = workflow.States.Single(s => s.Name == "Em andamento");
        var review = workflow.States.Single(s => s.Name == "Em revisão");
        var done = workflow.States.Single(s => s.Name == "Concluída");
        var agent = new TransitionActor(ActorKind.Agent, CanApprove: false, IsAssignee: false);
        var manager = new TransitionActor(ActorKind.User, CanApprove: true, IsAssignee: false);

        Assert.Equal(TransitionVerdict.Allowed, TransitionRules.Evaluate(workflow.FindTransition(doing.Id, review.Id), review, agent, false, 0));
        Assert.Equal(TransitionVerdict.Allowed, TransitionRules.Evaluate(workflow.FindTransition(review.Id, done.Id), done, manager, false, 0));
    }

    [Fact]
    public void OfferedTransitions_ForPeople_AreExactlyThoseOfThePreviousRule()
    {
        // A regra que TaskService usava antes desta etapa para montar as transições disponíveis.
        static bool PreviousRule(bool requiresApproval, bool canApprove, bool isAssignee, bool independent) =>
            !requiresApproval || (canApprove && !(isAssignee && independent));

        bool[] flags = [false, true];
        foreach (var requiresApproval in flags)
        {
            foreach (var canApprove in flags)
            {
                foreach (var isAssignee in flags)
                {
                    foreach (var independent in flags)
                    {
                        foreach (var category in Enum.GetValues<StateCategory>())
                        {
                            var person = new TransitionActor(ActorKind.User, canApprove, isAssignee);
                            Assert.True(
                                PreviousRule(requiresApproval, canApprove, isAssignee, independent) == TransitionRules.IsOffered(requiresApproval, category, person, independent),
                                $"aprovação={requiresApproval}, pode aprovar={canApprove}, responsável={isAssignee}, independente={independent}, categoria={category}");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void OfferedTransitions_ForAgents_ExcludeApprovalAndDone()
    {
        var agent = new TransitionActor(ActorKind.Agent, CanApprove: true, IsAssignee: false);

        foreach (var category in Enum.GetValues<StateCategory>())
        {
            Assert.False(TransitionRules.IsOffered(requiresApproval: true, category, agent, requireIndependentApproval: false));
            Assert.Equal(category != StateCategory.Done, TransitionRules.IsOffered(requiresApproval: false, category, agent, requireIndependentApproval: false));
        }
    }

    /// <summary>Workflow padrão mais um atalho "Em andamento → Concluída" sem aprovação e um estado Cancelada.</summary>
    private static (WorkflowDefinition Workflow, WorkflowState Doing, WorkflowState Done, WorkflowState Canceled) CustomWorkflow()
    {
        var workflow = WorkflowDefinition.CreateDefault(new Project(OrganizationId, "Personalizado"));
        var doing = workflow.States.Single(s => s.Name == "Em andamento");
        var review = workflow.States.Single(s => s.Name == "Em revisão");
        var done = workflow.States.Single(s => s.Name == "Concluída");
        var canceled = workflow.AddState("Cancelada", StateCategory.Canceled);

        workflow.AddTransition(doing, done, requiresApproval: false);
        workflow.AddTransition(doing, canceled, requiresApproval: false);
        workflow.AddTransition(review, canceled, requiresApproval: true);
        return (workflow, doing, done, canceled);
    }
}
