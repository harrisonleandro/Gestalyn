using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Projects;

/// <summary>
/// Projeto de uma organização. Nesta fase contém apenas os dados mínimos; os demais atributos
/// (responsáveis, datas, prioridade, arquivamento) entram na Fase 05.
/// </summary>
public sealed class Project : Entity, IOrganizationOwned, IAuditable
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    private readonly List<ProjectPhase> _phases = [];

    public Project(Guid organizationId, string name, string? description = null)
    {
        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
        Description = Guard.OptionalText(description, DescriptionMaxLength, nameof(description));
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Fases em ordem de posição. Para adicionar fases, as existentes precisam estar carregadas.
    /// </summary>
    public IReadOnlyList<ProjectPhase> Phases => _phases.OrderBy(phase => phase.Position).ToList();

    public void Rename(string name) => Name = Guard.RequiredText(name, NameMaxLength, nameof(name));

    public void ChangeDescription(string? description) =>
        Description = Guard.OptionalText(description, DescriptionMaxLength, nameof(description));

    /// <summary>
    /// Adiciona uma fase ao final do projeto. A fase herda a organização do projeto.
    /// </summary>
    public ProjectPhase AddPhase(string name)
    {
        var nextPosition = _phases.Count == 0 ? 1 : _phases.Max(phase => phase.Position) + 1;
        var phase = new ProjectPhase(this, name, nextPosition);
        _phases.Add(phase);

        return phase;
    }
}
