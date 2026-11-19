namespace ProjectManager.Domain.Workflows;

/// <summary>Resultado da validação de uma transição (ADR-013 e ADR-016).</summary>
public enum TransitionVerdict
{
    Allowed,
    NoSuchTransition,
    ApprovalPermissionRequired,
    CannotApproveOwnWork,
    DependenciesOpen,

    /// <summary>Agentes nunca executam transições que exigem aprovação (ADR-016).</summary>
    AgentCannotApprove,

    /// <summary>Agentes nunca entram em estados da categoria Concluída, mesmo sem aprovação (ADR-016).</summary>
    AgentCannotEnterDone,
}

/// <summary>Quem quer mover a tarefa e em que condições.</summary>
public sealed record TransitionActor(ActorKind Kind, bool CanApprove, bool IsAssignee);

/// <summary>
/// Regra única de validação de uma transição, aplicada no backend antes de qualquer mudança de estado e também no
/// cálculo das transições oferecidas:
/// <list type="number">
/// <item>a transição precisa existir no workflow;</item>
/// <item>um agente nunca executa uma transição que exige aprovação e nunca entra num estado Concluída, qualquer que
/// seja o workflow (ADR-016); pode, por exemplo, pedir Cancelada quando a transição existe sem aprovação;</item>
/// <item>para pessoas, a transição que exige aprovação exige <c>tasks.approve</c>, e quem é responsável pela tarefa não
/// aprova o próprio trabalho se o workflow exigir aprovação independente;</item>
/// <item>entrar num estado Concluída exige as dependências resolvidas.</item>
/// </list>
/// </summary>
public static class TransitionRules
{
    public static TransitionVerdict Evaluate(
        WorkflowTransition? transition,
        WorkflowState target,
        TransitionActor actor,
        bool requireIndependentApproval,
        int openDependencies)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Evaluate(transition?.RequiresApproval, target.Category, actor, requireIndependentApproval, openDependencies);
    }

    /// <summary>
    /// A mesma regra sobre os dados da transição: <paramref name="requiresApproval"/> nulo significa que não há
    /// transição do estado atual até o destino.
    /// </summary>
    public static TransitionVerdict Evaluate(
        bool? requiresApproval,
        StateCategory targetCategory,
        TransitionActor actor,
        bool requireIndependentApproval,
        int openDependencies)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (requiresApproval is not { } approval)
        {
            return TransitionVerdict.NoSuchTransition;
        }

        if (actor.Kind == ActorKind.Agent)
        {
            if (approval)
            {
                return TransitionVerdict.AgentCannotApprove;
            }

            if (targetCategory == StateCategory.Done)
            {
                return TransitionVerdict.AgentCannotEnterDone;
            }
        }
        else if (approval)
        {
            if (!actor.CanApprove)
            {
                return TransitionVerdict.ApprovalPermissionRequired;
            }

            if (actor.IsAssignee && requireIndependentApproval)
            {
                return TransitionVerdict.CannotApproveOwnWork;
            }
        }

        return targetCategory == StateCategory.Done && openDependencies > 0
            ? TransitionVerdict.DependenciesOpen
            : TransitionVerdict.Allowed;
    }

    /// <summary>
    /// A transição aparece entre as disponíveis para o ator. As dependências abertas não escondem a opção: elas são
    /// conferidas ao executar, com a mensagem de quais faltam.
    /// </summary>
    public static bool IsOffered(bool requiresApproval, StateCategory targetCategory, TransitionActor actor, bool requireIndependentApproval) =>
        Evaluate(requiresApproval, targetCategory, actor, requireIndependentApproval, openDependencies: 0) == TransitionVerdict.Allowed;
}
