using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Projects;

/// <summary>
/// Marco de um projeto: um ponto de entrega com data prevista, opcionalmente ligado a uma fase do mesmo
/// projeto, e marcado como atingido quando alcançado.
/// </summary>
public sealed class Milestone : Entity, IOrganizationOwned, IAuditable
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int MaxPerProject = 100;

    public Milestone(Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.EnsureNotArchived();

        OrganizationId = project.OrganizationId;
        ProjectId = project.Id;
        Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private Milestone(string name) => Name = name;

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public Guid? PhaseId { get; private set; }

    public DateTimeOffset? ReachedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string name) => Name = Guard.RequiredText(name, NameMaxLength, nameof(name));

    public void ChangeDescription(string? description) =>
        Description = Guard.OptionalText(description, DescriptionMaxLength, nameof(description));

    public void ChangeDueDate(DateOnly? dueDate) => DueDate = dueDate;

    /// <summary>Liga o marco a uma fase do mesmo projeto, ou desfaz a ligação (<c>null</c>).</summary>
    public void LinkToPhase(ProjectPhase? phase)
    {
        if (phase is not null && phase.ProjectId != ProjectId)
        {
            throw new ArgumentException("A fase pertence a outro projeto.", nameof(phase));
        }

        PhaseId = phase?.Id;
    }

    /// <summary>Marca o marco como atingido. Marcar de novo mantém a primeira data.</summary>
    public void MarkAsReached(DateTimeOffset now) => ReachedAt ??= Timestamps.TruncateToMicroseconds(now);

    public void MarkAsNotReached() => ReachedAt = null;
}
