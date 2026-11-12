using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Projects;

/// <summary>
/// Fase de um projeto, com posição na ordem das fases. Pertence sempre à mesma organização e ao mesmo
/// projeto que a criou.
/// </summary>
public sealed class ProjectPhase : Entity, IOrganizationOwned, IAuditable
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int MaxPerProject = 50;

    public const string ScheduleMessage = "A data de fim da fase não pode ser anterior à data de início.";

    internal ProjectPhase(Project project, string name, int position)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);

        OrganizationId = project.OrganizationId;
        ProjectId = project.Id;
        Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
        Position = position;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private ProjectPhase(string name) => Name = name;

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string name) => Name = Guard.RequiredText(name, NameMaxLength, nameof(name));

    public void ChangeDescription(string? description) =>
        Description = Guard.OptionalText(description, DescriptionMaxLength, nameof(description));

    public void Schedule(DateOnly? startDate, DateOnly? endDate)
    {
        if (!IsValidSchedule(startDate, endDate))
        {
            throw new ArgumentException(ScheduleMessage, nameof(endDate));
        }

        StartDate = startDate;
        EndDate = endDate;
    }

    public static bool IsValidSchedule(DateOnly? startDate, DateOnly? endDate) =>
        startDate is null || endDate is null || endDate >= startDate;
}
