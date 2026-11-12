using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Domain.Tasks;

/// <summary>
/// Tarefa de um projeto ("task" na API e no banco; o nome evita colidir com
/// <see cref="System.Threading.Tasks.Task"/>). Tem número sequencial no projeto, fase e marco opcionais
/// do mesmo projeto, vários responsáveis da equipe e arquivamento lógico. O estado da tarefa chega com o
/// workflow da Fase 07 (ADR-011).
/// </summary>
public sealed class ProjectTask : Entity, IOrganizationOwned, IAuditable
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 20000;
    public const int MaxAssignees = 10;

    public const string ArchivedMessage = "A tarefa está arquivada e é somente leitura.";

    private readonly List<TaskAssignment> _assignments = [];
    private readonly List<TaskLabel> _labels = [];

    public ProjectTask(Project project, int number, string title, Guid createdByUserId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        project.EnsureNotArchived();

        OrganizationId = project.OrganizationId;
        ProjectId = project.Id;
        Number = number;
        Title = Guard.RequiredText(title, TitleMaxLength, nameof(title));
        Priority = Priority.Medium;
        CreatedByUserId = Guard.RequiredId(createdByUserId, nameof(createdByUserId));
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private ProjectTask(string title) => Title = title;

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public int Number { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public Priority Priority { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public Guid? PhaseId { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public Guid? ArchivedByUserId { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Responsáveis. Para alterá-los, os existentes precisam estar carregados.</summary>
    public IReadOnlyList<TaskAssignment> Assignments => _assignments;

    /// <summary>Etiquetas em ordem alfabética. Para alterá-las, as existentes precisam estar carregadas.</summary>
    public IReadOnlyList<TaskLabel> Labels => _labels.OrderBy(label => label.NormalizedName, StringComparer.Ordinal).ToList();

    public void Rename(string title)
    {
        EnsureNotArchived();
        Title = Guard.RequiredText(title, TitleMaxLength, nameof(title));
    }

    public void ChangeDescription(string? description)
    {
        EnsureNotArchived();
        Description = Guard.OptionalText(description, DescriptionMaxLength, nameof(description));
    }

    public void ChangePriority(Priority priority)
    {
        EnsureNotArchived();
        Priority = Enum.IsDefined(priority)
            ? priority
            : throw new ArgumentOutOfRangeException(nameof(priority), priority, "Prioridade desconhecida.");
    }

    public void ChangeDueDate(DateOnly? dueDate)
    {
        EnsureNotArchived();
        DueDate = dueDate;
    }

    /// <summary>Coloca a tarefa numa fase do mesmo projeto, ou a tira de qualquer fase (<c>null</c>).</summary>
    public void MoveToPhase(ProjectPhase? phase)
    {
        EnsureNotArchived();
        if (phase is not null && phase.ProjectId != ProjectId)
        {
            throw new ArgumentException("A fase pertence a outro projeto.", nameof(phase));
        }

        PhaseId = phase?.Id;
    }

    /// <summary>Liga a tarefa a um marco do mesmo projeto, ou desfaz a ligação (<c>null</c>).</summary>
    public void LinkToMilestone(Milestone? milestone)
    {
        EnsureNotArchived();
        if (milestone is not null && milestone.ProjectId != ProjectId)
        {
            throw new ArgumentException("O marco pertence a outro projeto.", nameof(milestone));
        }

        MilestoneId = milestone?.Id;
    }

    /// <summary>Substitui o conjunto de etiquetas (mesmas regras das etiquetas de projeto).</summary>
    public void SetLabels(IEnumerable<string> names)
    {
        EnsureNotArchived();
        var requested = Common.Labels.Normalize(names, out var error)
            ?? throw new ArgumentException(error, nameof(names));

        _labels.RemoveAll(label => !requested.ContainsKey(label.NormalizedName));
        foreach (var (normalizedName, name) in requested)
        {
            var existing = _labels.Find(label => label.NormalizedName == normalizedName);
            if (existing is null)
            {
                _labels.Add(new TaskLabel(this, name));
            }
            else if (existing.Name != name)
            {
                existing.ChangeSpelling(name);
            }
        }
    }

    /// <summary>
    /// Torna o usuário responsável. Retorna <c>false</c> se ele já era. A participação na equipe do
    /// projeto é garantida pelo banco (FK composta) e conferida pelo serviço.
    /// </summary>
    public bool Assign(Guid userId, DateTimeOffset now)
    {
        EnsureNotArchived();
        Guard.RequiredId(userId, nameof(userId));

        if (_assignments.Exists(assignment => assignment.UserId == userId))
        {
            return false;
        }

        if (_assignments.Count >= MaxAssignees)
        {
            throw new InvalidOperationException($"Uma tarefa pode ter no máximo {MaxAssignees} responsáveis.");
        }

        _assignments.Add(new TaskAssignment(this, userId, now));
        return true;
    }

    public bool Unassign(Guid userId)
    {
        EnsureNotArchived();
        return _assignments.RemoveAll(assignment => assignment.UserId == userId) > 0;
    }

    /// <summary>Arquiva a tarefa. Arquivar de novo não muda nada.</summary>
    public void Archive(Guid userId, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return;
        }

        ArchivedAt = Timestamps.TruncateToMicroseconds(now);
        ArchivedByUserId = Guard.RequiredId(userId, nameof(userId));
    }

    public void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new InvalidOperationException(ArchivedMessage);
        }
    }
}
