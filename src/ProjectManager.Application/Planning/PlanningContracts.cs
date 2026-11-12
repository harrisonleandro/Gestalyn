using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Application.Planning;

public sealed class CreatePhaseRequest
{
    [Required(ErrorMessage = "Informe o nome da fase.")]
    [StringLength(ProjectPhase.NameMaxLength, ErrorMessage = "O nome deve ter no máximo {1} caracteres.")]
    public required string Name { get; init; }

    [StringLength(ProjectPhase.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }
}

/// <summary>Alteração parcial: em descrição e datas, <c>null</c> limpa o valor e a ausência o mantém.</summary>
public sealed class UpdatePhaseRequest
{
    private readonly string? _description;
    private readonly DateOnly? _startDate;
    private readonly DateOnly? _endDate;

    [StringLength(ProjectPhase.NameMaxLength, MinimumLength = 1, ErrorMessage = "O nome deve ter de {2} a {1} caracteres.")]
    public string? Name { get; init; }

    [StringLength(ProjectPhase.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description
    {
        get => _description;
        init
        {
            _description = value;
            HasDescription = true;
        }
    }

    public DateOnly? StartDate
    {
        get => _startDate;
        init
        {
            _startDate = value;
            HasStartDate = true;
        }
    }

    public DateOnly? EndDate
    {
        get => _endDate;
        init
        {
            _endDate = value;
            HasEndDate = true;
        }
    }

    [JsonIgnore]
    public bool HasDescription { get; private init; }

    [JsonIgnore]
    public bool HasStartDate { get; private init; }

    [JsonIgnore]
    public bool HasEndDate { get; private init; }
}

/// <summary>Nova ordem das fases: a lista completa dos IDs das fases do projeto.</summary>
public sealed class ReorderPhasesRequest
{
    [Required]
    [MaxLength(ProjectPhase.MaxPerProject)]
    public required IReadOnlyList<Guid> PhaseIds { get; init; }
}

public sealed record PhaseDetails(
    Guid Id,
    string Name,
    string? Description,
    int Position,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CreateMilestoneRequest
{
    [Required(ErrorMessage = "Informe o nome do marco.")]
    [StringLength(Milestone.NameMaxLength, ErrorMessage = "O nome deve ter no máximo {1} caracteres.")]
    public required string Name { get; init; }

    [StringLength(Milestone.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description { get; init; }

    public DateOnly? DueDate { get; init; }

    public Guid? PhaseId { get; init; }
}

/// <summary>
/// Alteração parcial: em descrição, data prevista e fase, <c>null</c> limpa o valor e a ausência o
/// mantém. <c>isReached</c> marca ou desmarca o marco como atingido.
/// </summary>
public sealed class UpdateMilestoneRequest
{
    private readonly string? _description;
    private readonly DateOnly? _dueDate;
    private readonly Guid? _phaseId;

    [StringLength(Milestone.NameMaxLength, MinimumLength = 1, ErrorMessage = "O nome deve ter de {2} a {1} caracteres.")]
    public string? Name { get; init; }

    [StringLength(Milestone.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description
    {
        get => _description;
        init
        {
            _description = value;
            HasDescription = true;
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

    public Guid? PhaseId
    {
        get => _phaseId;
        init
        {
            _phaseId = value;
            HasPhaseId = true;
        }
    }

    public bool? IsReached { get; init; }

    [JsonIgnore]
    public bool HasDescription { get; private init; }

    [JsonIgnore]
    public bool HasDueDate { get; private init; }

    [JsonIgnore]
    public bool HasPhaseId { get; private init; }
}

public sealed record MilestoneDetails(
    Guid Id,
    string Name,
    string? Description,
    DateOnly? DueDate,
    Guid? PhaseId,
    DateTimeOffset? ReachedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
