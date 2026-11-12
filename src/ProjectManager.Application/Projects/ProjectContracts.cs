using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Application.Projects;

public sealed class CreateProjectRequest
{
    [Required(ErrorMessage = "Informe o nome do projeto.")]
    [StringLength(Project.NameMaxLength, ErrorMessage = "O nome deve ter no máximo {1} caracteres.")]
    public required string Name { get; init; }

    [StringLength(Project.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description { get; init; }

    public Priority? Priority { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? DueDate { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>
/// Alteração parcial: só os campos enviados mudam. Em descrição e datas, enviar <c>null</c> limpa o
/// valor; omitir o campo o mantém. <c>tags</c> substitui o conjunto inteiro (<c>[]</c> limpa).
/// </summary>
public sealed class UpdateProjectRequest
{
    private readonly string? _description;
    private readonly DateOnly? _startDate;
    private readonly DateOnly? _dueDate;

    [StringLength(Project.NameMaxLength, MinimumLength = 1, ErrorMessage = "O nome deve ter de {2} a {1} caracteres.")]
    public string? Name { get; init; }

    [StringLength(Project.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description
    {
        get => _description;
        init
        {
            _description = value;
            HasDescription = true;
        }
    }

    public Priority? Priority { get; init; }

    public DateOnly? StartDate
    {
        get => _startDate;
        init
        {
            _startDate = value;
            HasStartDate = true;
        }
    }

    public DateOnly? DueDate
    {
        get => _dueDate;
        init
        {
            _dueDate = value;
            HasDueDate = true;
        }
    }

    public IReadOnlyList<string>? Tags { get; init; }

    [JsonIgnore]
    public bool HasDescription { get; private init; }

    [JsonIgnore]
    public bool HasStartDate { get; private init; }

    [JsonIgnore]
    public bool HasDueDate { get; private init; }
}

public sealed class ProjectListQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    [Range(1, int.MaxValue, ErrorMessage = "A página deve ser maior que zero.")]
    public int Page { get; init; } = 1;

    [Range(1, MaxPageSize, ErrorMessage = "O tamanho da página deve ficar entre {1} e {2}.")]
    public int PageSize { get; init; } = DefaultPageSize;

    [StringLength(ProjectTag.NameMaxLength)]
    public string? Tag { get; init; }

    public Priority? Priority { get; init; }

    public bool IncludeArchived { get; init; }
}

public sealed class AddProjectMemberRequest
{
    [Required]
    public required Guid UserId { get; init; }

    [Required]
    public required ProjectRole Role { get; init; }

    public bool IsResponsible { get; init; }
}

public sealed class UpdateProjectMemberRequest
{
    public ProjectRole? Role { get; init; }

    public bool? IsResponsible { get; init; }
}

public sealed class AddProjectRepositoryRequest
{
    [Required(ErrorMessage = "Informe a URL do repositório.")]
    [StringLength(ProjectRepository.UrlMaxLength, ErrorMessage = "A URL deve ter no máximo {1} caracteres.")]
    public required string Url { get; init; }

    [StringLength(ProjectRepository.NameMaxLength, ErrorMessage = "O nome deve ter no máximo {1} caracteres.")]
    public string? Name { get; init; }
}

/// <summary>Projeto na listagem, com o papel do usuário na equipe (se estiver nela).</summary>
public sealed record ProjectSummary(
    Guid Id,
    string Name,
    Priority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    IReadOnlyList<string> Tags,
    DateTimeOffset? ArchivedAt,
    ProjectRole? MyRole);

public sealed record ProjectResponsible(Guid UserId, string DisplayName);

/// <summary>Projeto completo, com o papel e as permissões efetivas do usuário nele.</summary>
public sealed record ProjectDetails(
    Guid Id,
    string Name,
    string? Description,
    Priority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    IReadOnlyList<string> Tags,
    ProjectResponsible? Responsible,
    DateTimeOffset? ArchivedAt,
    Guid? ArchivedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ProjectRole? MyRole,
    IReadOnlyList<string> MyPermissions);

public sealed record ProjectMemberDetails(
    Guid UserId,
    string DisplayName,
    string Email,
    ProjectRole Role,
    bool IsResponsible,
    DateTimeOffset JoinedAt);

public sealed record ProjectRepositoryDetails(Guid Id, string Url, string? Name, RepositoryProvider Provider, DateTimeOffset CreatedAt);
