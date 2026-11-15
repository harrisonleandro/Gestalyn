namespace ProjectManager.Domain.Workflows;

/// <summary>Resultado da validação de uma transição (ADR-013).</summary>
public enum TransitionVerdict
{
    Allowed,
    NoSuchTransition,
    ApprovalPermissionRequired,
    CannotApproveOwnWork,
    DependenciesOpen,
}

/// <summary>Quem quer mover a tarefa e em que condições.</summary>
public sealed record TransitionActor(ActorKind Kind, bool CanApprove, bool IsAssignee);

/// <summary>
/// Regra única de validação de uma transição, aplicada no backend antes de qualquer mudança de estado:
/// a transição precisa existir; a que exige aprovação exige <c>tasks.approve</c>, e quem é responsável
/// pela tarefa não aprova o próprio trabalho (sempre para agentes; para pessoas, se o workflow exigir);
/// entrar num estado Concluída exige as dependências resolvidas.
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
        ArgumentNullException.ThrowIfNull(actor);

        if (transition is null)
        {
            return TransitionVerdict.NoSuchTransition;
        }

        if (transition.RequiresApproval)
        {
            if (!actor.CanApprove)
            {
                return TransitionVerdict.ApprovalPermissionRequired;
            }

            // Agentes não aprovam o próprio trabalho por padrão (especificação mestre, seção 4.5).
            var selfApprovalForbidden = actor.Kind == ActorKind.Agent || requireIndependentApproval;
            if (actor.IsAssignee && selfApprovalForbidden)
            {
                return TransitionVerdict.CannotApproveOwnWork;
            }
        }

        return target.Category == StateCategory.Done && openDependencies > 0
            ? TransitionVerdict.DependenciesOpen
            : TransitionVerdict.Allowed;
    }
}
