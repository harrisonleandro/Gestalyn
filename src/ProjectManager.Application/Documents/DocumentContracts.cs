using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Documents;

namespace ProjectManager.Application.Documents;

public sealed record DocumentSummary(
    Guid Id,
    string Title,
    int CurrentVersion,
    Guid CreatedById,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DocumentDetails(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Body,
    int CurrentVersion,
    Guid CreatedById,
    DateTimeOffset? ArchivedAt,
    Guid? ArchivedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DocumentVersionSummary(int Number, string Title, Guid AuthorId, string? AuthorName, DateTimeOffset CreatedAt);

public sealed record DocumentVersionDetails(int Number, string Title, string Body, Guid AuthorId, string? AuthorName, DateTimeOffset CreatedAt);

public sealed class CreateDocumentRequest
{
    [NotWhiteSpace(ErrorMessage = "Informe o título do documento.")]
    [StringLength(ProjectDocument.TitleMaxLength, ErrorMessage = "O título deve ter no máximo {1} caracteres.")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Corpo em Markdown, guardado como veio. Pode começar vazio.</summary>
    [StringLength(ProjectDocument.BodyMaxLength, ErrorMessage = "O documento deve ter no máximo {1} caracteres.")]
    public string Body { get; init; } = string.Empty;
}

/// <summary>Nova versão do documento, a partir da versão que o autor abriu.</summary>
public sealed class UpdateDocumentRequest
{
    [NotWhiteSpace(ErrorMessage = "Informe o título do documento.")]
    [StringLength(ProjectDocument.TitleMaxLength, ErrorMessage = "O título deve ter no máximo {1} caracteres.")]
    public string Title { get; init; } = string.Empty;

    [StringLength(ProjectDocument.BodyMaxLength, ErrorMessage = "O documento deve ter no máximo {1} caracteres.")]
    public string Body { get; init; } = string.Empty;

    [Required(ErrorMessage = "Informe a versão que você editou.")]
    [Range(1, int.MaxValue, ErrorMessage = "A versão deve ser positiva.")]
    public int? ExpectedVersion { get; init; }
}

public sealed record DecisionSummary(
    Guid Id,
    string Title,
    DecisionStatus Status,
    Guid? TaskId,
    Guid ProposedById,
    DateTimeOffset? DecidedAt,
    Guid? SupersededById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DecisionDetails(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Context,
    string Outcome,
    string? Alternatives,
    string? Consequences,
    Guid? TaskId,
    DecisionStatus Status,
    Guid ProposedById,
    Guid? DecidedById,
    DateTimeOffset? DecidedAt,
    Guid? SupersededById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CreateDecisionRequest
{
    [NotWhiteSpace(ErrorMessage = "Informe o título da decisão.")]
    [StringLength(Decision.TitleMaxLength, ErrorMessage = "O título deve ter no máximo {1} caracteres.")]
    public string Title { get; init; } = string.Empty;

    [NotWhiteSpace(ErrorMessage = "Descreva o contexto da decisão.")]
    [StringLength(Decision.TextMaxLength, ErrorMessage = "O contexto deve ter no máximo {1} caracteres.")]
    public string Context { get; init; } = string.Empty;

    [NotWhiteSpace(ErrorMessage = "Descreva o que foi decidido.")]
    [StringLength(Decision.TextMaxLength, ErrorMessage = "A decisão deve ter no máximo {1} caracteres.")]
    public string Outcome { get; init; } = string.Empty;

    [StringLength(Decision.TextMaxLength, ErrorMessage = "As alternativas devem ter no máximo {1} caracteres.")]
    public string? Alternatives { get; init; }

    [StringLength(Decision.TextMaxLength, ErrorMessage = "As consequências devem ter no máximo {1} caracteres.")]
    public string? Consequences { get; init; }

    public Guid? TaskId { get; init; }
}

/// <summary>Alteração parcial de uma proposta. Campos ausentes não mudam.</summary>
public sealed class UpdateDecisionRequest
{
    private readonly string? _alternatives;
    private readonly string? _consequences;
    private readonly Guid? _taskId;

    [NotWhiteSpace(ErrorMessage = "Informe o título da decisão.")]
    [StringLength(Decision.TitleMaxLength, ErrorMessage = "O título deve ter no máximo {1} caracteres.")]
    public string? Title { get; init; }

    [NotWhiteSpace(ErrorMessage = "Descreva o contexto da decisão.")]
    [StringLength(Decision.TextMaxLength, ErrorMessage = "O contexto deve ter no máximo {1} caracteres.")]
    public string? Context { get; init; }

    [NotWhiteSpace(ErrorMessage = "Descreva o que foi decidido.")]
    [StringLength(Decision.TextMaxLength, ErrorMessage = "A decisão deve ter no máximo {1} caracteres.")]
    public string? Outcome { get; init; }

    [StringLength(Decision.TextMaxLength, ErrorMessage = "As alternativas devem ter no máximo {1} caracteres.")]
    public string? Alternatives
    {
        get => _alternatives;
        init
        {
            _alternatives = value;
            HasAlternatives = true;
        }
    }

    [StringLength(Decision.TextMaxLength, ErrorMessage = "As consequências devem ter no máximo {1} caracteres.")]
    public string? Consequences
    {
        get => _consequences;
        init
        {
            _consequences = value;
            HasConsequences = true;
        }
    }

    public Guid? TaskId
    {
        get => _taskId;
        init
        {
            _taskId = value;
            HasTaskId = true;
        }
    }

    [JsonIgnore]
    public bool HasAlternatives { get; private init; }

    [JsonIgnore]
    public bool HasConsequences { get; private init; }

    [JsonIgnore]
    public bool HasTaskId { get; private init; }
}

public sealed class SupersedeDecisionRequest
{
    [Required(ErrorMessage = "Informe a decisão que substitui esta.")]
    public Guid? SupersededById { get; init; }
}

/// <summary>Documentos do projeto (ADR-014, D5). O acesso ao projeto já foi verificado.</summary>
public interface IDocumentService
{
    Task<IReadOnlyList<DocumentSummary>> ListAsync(Guid projectId, bool includeArchived, CancellationToken cancellationToken);

    Task<Result<DocumentDetails>> CreateAsync(ProjectAccessGrant access, CreateDocumentRequest request, CancellationToken cancellationToken);

    Task<Result<DocumentDetails>> GetAsync(Guid documentId, CancellationToken cancellationToken);

    Task<Result<DocumentDetails>> UpdateAsync(ProjectAccessGrant access, Guid documentId, UpdateDocumentRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentVersionSummary>> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken);

    Task<Result<DocumentVersionDetails>> GetVersionAsync(Guid documentId, int number, CancellationToken cancellationToken);

    Task<Result<DocumentDetails>> ArchiveAsync(ProjectAccessGrant access, Guid documentId, CancellationToken cancellationToken);

    Task<Result<DocumentDetails>> RestoreAsync(Guid documentId, CancellationToken cancellationToken);
}

/// <summary>Decisões do projeto (ADR-014, D6). O acesso ao projeto já foi verificado.</summary>
public interface IDecisionService
{
    Task<IReadOnlyList<DecisionSummary>> ListAsync(Guid projectId, DecisionStatus? status, CancellationToken cancellationToken);

    Task<Result<DecisionDetails>> CreateAsync(ProjectAccessGrant access, CreateDecisionRequest request, CancellationToken cancellationToken);

    Task<Result<DecisionDetails>> GetAsync(Guid decisionId, CancellationToken cancellationToken);

    Task<Result<DecisionDetails>> UpdateAsync(Guid decisionId, UpdateDecisionRequest request, CancellationToken cancellationToken);

    Task<Result<DecisionDetails>> AcceptAsync(ProjectAccessGrant access, Guid decisionId, CancellationToken cancellationToken);

    Task<Result<DecisionDetails>> RejectAsync(ProjectAccessGrant access, Guid decisionId, CancellationToken cancellationToken);

    Task<Result<DecisionDetails>> SupersedeAsync(Guid decisionId, SupersedeDecisionRequest request, CancellationToken cancellationToken);
}
