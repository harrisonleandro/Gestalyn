using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Projects;

/// <summary>Prioridade de um projeto.</summary>
public enum ProjectPriority
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// Projeto de uma organização. O arquivamento é lógico: o projeto arquivado continua no banco e fica
/// somente leitura. A equipe (<see cref="ProjectMember"/>) é uma entidade própria.
/// </summary>
public sealed class Project : Entity, IOrganizationOwned, IAuditable
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    public const string ArchivedMessage = "O projeto está arquivado e é somente leitura.";
    public const string ScheduleMessage = "O prazo não pode ser anterior à data de início.";

    private readonly List<ProjectPhase> _phases = [];
    private readonly List<ProjectTag> _tags = [];
    private readonly List<ProjectRepository> _repositories = [];

    public Project(Guid organizationId, string name, string? description = null)
    {
        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
        Description = Guard.OptionalText(description, DescriptionMaxLength, nameof(description));
        Priority = ProjectPriority.Medium;
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public ProjectPriority Priority { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public Guid? ArchivedByUserId { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Fases em ordem de posição. Para adicionar fases, as existentes precisam estar carregadas.
    /// </summary>
    public IReadOnlyList<ProjectPhase> Phases => _phases.OrderBy(phase => phase.Position).ToList();

    /// <summary>Etiquetas em ordem alfabética. Para alterá-las, as existentes precisam estar carregadas.</summary>
    public IReadOnlyList<ProjectTag> Tags => _tags.OrderBy(tag => tag.NormalizedName, StringComparer.Ordinal).ToList();

    /// <summary>Repositórios associados. Para alterá-los, os existentes precisam estar carregados.</summary>
    public IReadOnlyList<ProjectRepository> Repositories => _repositories.OrderBy(repository => repository.Url, StringComparer.Ordinal).ToList();

    public void Rename(string name)
    {
        EnsureNotArchived();
        Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
    }

    public void ChangeDescription(string? description)
    {
        EnsureNotArchived();
        Description = Guard.OptionalText(description, DescriptionMaxLength, nameof(description));
    }

    public void ChangePriority(ProjectPriority priority)
    {
        EnsureNotArchived();
        Priority = Enum.IsDefined(priority)
            ? priority
            : throw new ArgumentOutOfRangeException(nameof(priority), priority, "Prioridade desconhecida.");
    }

    public void Schedule(DateOnly? startDate, DateOnly? dueDate)
    {
        EnsureNotArchived();
        if (!IsValidSchedule(startDate, dueDate))
        {
            throw new ArgumentException(ScheduleMessage, nameof(dueDate));
        }

        StartDate = startDate;
        DueDate = dueDate;
    }

    public static bool IsValidSchedule(DateOnly? startDate, DateOnly? dueDate) =>
        startDate is null || dueDate is null || dueDate >= startDate;

    /// <summary>
    /// Substitui o conjunto de etiquetas. Etiquetas repetidas (sem diferenciar maiúsculas e minúsculas)
    /// são unificadas, mantendo a primeira grafia informada.
    /// </summary>
    public void SetTags(IEnumerable<string> names)
    {
        EnsureNotArchived();
        var requested = NormalizeTags(names, out var error)
            ?? throw new ArgumentException(error, nameof(names));

        _tags.RemoveAll(tag => !requested.ContainsKey(tag.NormalizedName));
        foreach (var (normalizedName, name) in requested)
        {
            var existing = _tags.Find(tag => tag.NormalizedName == normalizedName);
            if (existing is null)
            {
                _tags.Add(new ProjectTag(this, name));
            }
            else if (existing.Name != name)
            {
                existing.ChangeSpelling(name);
            }
        }
    }

    /// <summary>
    /// Valida e unifica nomes de etiquetas. Retorna <c>null</c> e a mensagem de erro quando o conjunto é
    /// inválido.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? NormalizeTags(IEnumerable<string> names, out string? error)
    {
        ArgumentNullException.ThrowIfNull(names);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > ProjectTag.NameMaxLength)
            {
                error = $"Cada etiqueta deve ter de 1 a {ProjectTag.NameMaxLength} caracteres.";
                return null;
            }

            result.TryAdd(ProjectTag.Normalize(name), name);
        }

        if (result.Count > ProjectTag.MaxPerProject)
        {
            error = $"Um projeto pode ter no máximo {ProjectTag.MaxPerProject} etiquetas.";
            return null;
        }

        error = null;
        return result;
    }

    /// <summary>Associa um repositório. A URL deve vir de <see cref="ProjectRepository.TryNormalizeUrl"/>.</summary>
    public ProjectRepository AddRepository(Uri url, string? name)
    {
        ArgumentNullException.ThrowIfNull(url);
        EnsureNotArchived();

        if (_repositories.Exists(repository => repository.Url == url.AbsoluteUri))
        {
            throw new InvalidOperationException("Este repositório já está associado ao projeto.");
        }

        if (_repositories.Count >= ProjectRepository.MaxPerProject)
        {
            throw new InvalidOperationException($"Um projeto pode ter no máximo {ProjectRepository.MaxPerProject} repositórios.");
        }

        var repository = new ProjectRepository(this, url, name);
        _repositories.Add(repository);

        return repository;
    }

    public bool RemoveRepository(Guid repositoryId)
    {
        EnsureNotArchived();
        return _repositories.RemoveAll(repository => repository.Id == repositoryId) > 0;
    }

    /// <summary>Arquiva o projeto. Arquivar de novo não muda nada.</summary>
    public void Archive(Guid userId, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return;
        }

        ArchivedAt = Timestamps.TruncateToMicroseconds(now);
        ArchivedByUserId = Guard.RequiredId(userId, nameof(userId));
    }

    /// <summary>
    /// Adiciona uma fase ao final do projeto. A fase herda a organização do projeto.
    /// </summary>
    public ProjectPhase AddPhase(string name)
    {
        EnsureNotArchived();
        var nextPosition = _phases.Count == 0 ? 1 : _phases.Max(phase => phase.Position) + 1;
        var phase = new ProjectPhase(this, name, nextPosition);
        _phases.Add(phase);

        return phase;
    }

    public void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new InvalidOperationException(ArchivedMessage);
        }
    }
}
