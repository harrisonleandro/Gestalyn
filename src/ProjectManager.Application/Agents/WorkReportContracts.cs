using System.ComponentModel.DataAnnotations;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Application.Agents;

/// <summary>Relatório de trabalho enviado por um agente (ADR-016, D8). Tudo aqui é declaração do agente.</summary>
public sealed class SubmitWorkReportRequest : IValidatableObject
{
    [Required(ErrorMessage = "Informe o projeto.")]
    public Guid? ProjectId { get; init; }

    /// <summary>Tarefa do relatório, se houver; precisa ser do projeto.</summary>
    public Guid? TaskId { get; init; }

    [NotWhiteSpace(ErrorMessage = "Informe o resumo do trabalho.")]
    [StringLength(WorkReport.SummaryMaxLength, ErrorMessage = "O resumo deve ter no máximo {1} caracteres.")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>Markdown com as alterações.</summary>
    [StringLength(WorkReport.ChangesMaxLength, ErrorMessage = "As alterações devem ter no máximo {1} caracteres.")]
    public string? Changes { get; init; }

    public IReadOnlyList<string>? Files { get; init; }

    public WorkReportTestsRequest? Tests { get; init; }

    public WorkReportCommitRequest? Commit { get; init; }

    /// <summary>ID de um estado do workflow do projeto que o agente sugere. O relatório não muda o estado da tarefa.</summary>
    public Guid? SuggestedStatus { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Files is { } files)
        {
            if (files.Count > WorkReport.MaxFiles)
            {
                yield return new ValidationResult($"Informe no máximo {WorkReport.MaxFiles} arquivos.", [nameof(Files)]);
            }

            if (files.Any(file => string.IsNullOrWhiteSpace(file) || file.Trim().Length > WorkReport.FilePathMaxLength))
            {
                yield return new ValidationResult(
                    $"Cada arquivo deve ter de 1 a {WorkReport.FilePathMaxLength} caracteres.", [nameof(Files)]);
            }
        }

        if (SuggestedStatus is { } suggested && suggested == Guid.Empty)
        {
            yield return new ValidationResult("O estado sugerido é inválido.", [nameof(SuggestedStatus)]);
        }
    }

    public WorkReportDeclaration ToDeclaration() => new(
        Summary,
        Changes,
        Files,
        Tests is null ? null : new WorkReportTests(Tests.Command, Tests.Passed, Tests.Failed, Tests.Skipped),
        Commit is null ? null : new WorkReportCommit(Commit.Sha, Commit.Repository));
}

public sealed class WorkReportTestsRequest
{
    [StringLength(WorkReport.TestsCommandMaxLength, ErrorMessage = "O comando deve ter no máximo {1} caracteres.")]
    public string? Command { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "As contagens de testes não podem ser negativas.")]
    public int? Passed { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "As contagens de testes não podem ser negativas.")]
    public int? Failed { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "As contagens de testes não podem ser negativas.")]
    public int? Skipped { get; init; }
}

public sealed class WorkReportCommitRequest : IValidatableObject
{
    [Required(ErrorMessage = "Informe o SHA do commit.")]
    public required string Sha { get; init; }

    [StringLength(WorkReport.CommitRepositoryMaxLength, ErrorMessage = "O repositório deve ter no máximo {1} caracteres.")]
    public string? Repository { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!WorkReport.IsCommitSha(Sha))
        {
            yield return new ValidationResult("O SHA do commit deve ter 40 ou 64 caracteres hexadecimais.", [nameof(Sha)]);
        }
    }
}

/// <summary>Valores dos níveis de evidência (ADR-016, D8) e da transição sugerida.</summary>
public static class WorkReportLevels
{
    public const string NotVerified = "notVerified";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string NotApplicable = "notApplicable";

    public const string DirectTransition = "direct";
    public const string NoTransition = "none";
}

/// <summary>
/// Os cinco níveis, sempre separados: declarado (o conteúdo), aceito (recebido e validado pela API, não aprovado por
/// uma pessoa), testes e CI (não verificados nesta fase) e aprovação humana, calculada pelo histórico da tarefa.
/// </summary>
public sealed record WorkReportEvidence(bool Declared, bool Accepted, string TestsExecuted, string CiConfirmed, string HumanApproval);

/// <summary>Estado atual da tarefa.</summary>
public sealed record WorkReportTaskStatus(Guid StateId, string Name, StateCategory Category);

/// <summary>Resposta do envio (201). O relatório não muda o estado da tarefa.</summary>
public sealed record WorkReportReceipt(
    Guid ReportId,
    bool Accepted,
    Guid ActivityCreated,
    WorkReportTaskStatus? TaskStatus,
    bool? ApprovalRequired,
    string SuggestedTransition,
    WorkReportEvidence Evidence);

/// <summary>Primeira conclusão da tarefa por uma pessoa depois do relatório.</summary>
public sealed record WorkReportApproval(Guid ApprovedById, string? ApprovedByName, DateTimeOffset ApprovedAt);

/// <summary>Relatório na lista, sem o texto longo.</summary>
public sealed record WorkReportSummary(
    Guid Id,
    Guid ProjectId,
    Guid? TaskId,
    ActorReference Agent,
    string Summary,
    WorkReportStateSnapshot? SuggestedState,
    DateTimeOffset ReceivedAt,
    WorkReportEvidence Evidence);

/// <summary>Relatório completo, rotulado como declaração do agente, com o retrato do recebimento e os níveis.</summary>
public sealed record WorkReportDetails(
    Guid Id,
    Guid ProjectId,
    Guid? TaskId,
    ActorReference Agent,
    string Summary,
    string? Changes,
    IReadOnlyList<string> Files,
    WorkReportTests? Tests,
    WorkReportCommit? Commit,
    WorkReportStateSnapshot? TaskStateAtReceipt,
    WorkReportStateSnapshot? SuggestedState,
    string? RequestId,
    DateTimeOffset ReceivedAt,
    WorkReportEvidence Evidence,
    WorkReportApproval? Approval);

public sealed class WorkReportListQuery : PageQuery;

/// <summary>Relatórios de trabalho: envio por agentes e leitura por pessoas com <c>project.read</c>.</summary>
public interface IWorkReportService
{
    /// <summary>Recebe o relatório do agente da concessão. Não muda o estado da tarefa.</summary>
    Task<Result<WorkReportReceipt>> SubmitAsync(ProjectAccessGrant access, SubmitWorkReportRequest request, CancellationToken cancellationToken);

    Task<PagedResult<WorkReportSummary>> ListByProjectAsync(ProjectAccessGrant access, WorkReportListQuery query, CancellationToken cancellationToken);

    Task<Result<PagedResult<WorkReportSummary>>> ListByTaskAsync(ProjectAccessGrant access, Guid taskId, WorkReportListQuery query, CancellationToken cancellationToken);

    Task<Result<WorkReportDetails>> GetAsync(ProjectAccessGrant access, Guid reportId, CancellationToken cancellationToken);
}
