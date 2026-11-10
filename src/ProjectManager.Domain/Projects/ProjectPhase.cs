using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Projects;

/// <summary>
/// Fase de um projeto. Pertence sempre à mesma organização do projeto. Os demais atributos
/// (datas, objetivos, estado) entram na Fase 06.
/// </summary>
public sealed class ProjectPhase : Entity, IOrganizationOwned, IAuditable
{
    public const int NameMaxLength = 200;

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

    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string name) => Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
}
