using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Domain.Documents;

/// <summary>Estado de uma decisão (ADR-014, D6). Só a proposta pode ser editada.</summary>
public enum DecisionStatus
{
    Proposed,
    Accepted,
    Rejected,
    Superseded,
}

/// <summary>Violação de uma regra das decisões. A API responde 409.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Sempre criada com a mensagem da regra violada.")]
public sealed class DecisionRuleException(string message) : InvalidOperationException(message);

/// <summary>
/// Decisão técnica do projeto, no estilo de um ADR (ADR-014, D6): proposta, depois aceita ou rejeitada; uma
/// aceita pode ser substituída por outra aceita do mesmo projeto. Decidida, fica imutável.
/// </summary>
public sealed class Decision : Entity, IOrganizationOwned, IAuditable
{
    public const int TitleMaxLength = 200;
    public const int TextMaxLength = 20_000;

    public Decision(Project project, string title, string context, string outcome, Guid proposedById)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.EnsureNotArchived();

        OrganizationId = project.OrganizationId;
        ProjectId = project.Id;
        ProposedById = Guard.RequiredId(proposedById, nameof(proposedById));
        Title = Guard.RequiredText(title, TitleMaxLength, nameof(title));
        Context = Guard.RequiredText(context, TextMaxLength, nameof(context));
        Outcome = Guard.RequiredText(outcome, TextMaxLength, nameof(outcome));
        Status = DecisionStatus.Proposed;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private Decision(string title, string context, string outcome)
    {
        Title = title;
        Context = context;
        Outcome = outcome;
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Title { get; private set; }

    /// <summary>O problema e as forças em jogo.</summary>
    public string Context { get; private set; }

    /// <summary>O que foi decidido.</summary>
    public string Outcome { get; private set; }

    public string? Alternatives { get; private set; }

    public string? Consequences { get; private set; }

    public Guid? TaskId { get; private set; }

    public DecisionStatus Status { get; private set; }

    public Guid ProposedById { get; private set; }

    public Guid? DecidedById { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Decisão aceita que substituiu esta.</summary>
    public Guid? SupersededById { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string title)
    {
        EnsureProposed();
        Title = Guard.RequiredText(title, TitleMaxLength, nameof(title));
    }

    public void ChangeContext(string context)
    {
        EnsureProposed();
        Context = Guard.RequiredText(context, TextMaxLength, nameof(context));
    }

    public void ChangeOutcome(string outcome)
    {
        EnsureProposed();
        Outcome = Guard.RequiredText(outcome, TextMaxLength, nameof(outcome));
    }

    public void ChangeAlternatives(string? alternatives)
    {
        EnsureProposed();
        Alternatives = Guard.OptionalText(alternatives, TextMaxLength, nameof(alternatives));
    }

    public void ChangeConsequences(string? consequences)
    {
        EnsureProposed();
        Consequences = Guard.OptionalText(consequences, TextMaxLength, nameof(consequences));
    }

    /// <summary>Relaciona a decisão a uma tarefa do mesmo projeto, ou desfaz a ligação (<c>null</c>).</summary>
    public void RelateToTask(Tasks.ProjectTask? task)
    {
        EnsureProposed();
        if (task is not null && task.ProjectId != ProjectId)
        {
            throw new ArgumentException("A tarefa pertence a outro projeto.", nameof(task));
        }

        TaskId = task?.Id;
    }

    public void Accept(Guid userId, DateTimeOffset now) => Decide(DecisionStatus.Accepted, userId, now);

    public void Reject(Guid userId, DateTimeOffset now) => Decide(DecisionStatus.Rejected, userId, now);

    /// <summary>Substitui esta decisão aceita por outra aceita do mesmo projeto.</summary>
    public void SupersedeWith(Decision replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        if (Status != DecisionStatus.Accepted)
        {
            throw new DecisionRuleException("Só uma decisão aceita pode ser substituída.");
        }

        if (replacement.Id == Id)
        {
            throw new DecisionRuleException("Uma decisão não pode substituir a si mesma.");
        }

        if (replacement.ProjectId != ProjectId)
        {
            throw new DecisionRuleException("A decisão que substitui precisa ser do mesmo projeto.");
        }

        if (replacement.Status != DecisionStatus.Accepted)
        {
            throw new DecisionRuleException("A decisão que substitui precisa estar aceita.");
        }

        Status = DecisionStatus.Superseded;
        SupersededById = replacement.Id;
    }

    private void Decide(DecisionStatus status, Guid userId, DateTimeOffset now)
    {
        EnsureProposed();
        Status = status;
        DecidedById = Guard.RequiredId(userId, nameof(userId));
        DecidedAt = Timestamps.TruncateToMicroseconds(now);
    }

    private void EnsureProposed()
    {
        if (Status != DecisionStatus.Proposed)
        {
            throw new DecisionRuleException("A decisão já foi decidida e não pode mais ser alterada.");
        }
    }
}
