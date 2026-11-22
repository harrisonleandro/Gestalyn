using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Domain.Agents;

/// <summary>Testes declarados pelo agente: comando e contagens. É declaração, nunca verificação (ADR-016, D8).</summary>
public sealed record WorkReportTests(string? Command, int? Passed, int? Failed, int? Skipped);

/// <summary>Commit declarado pelo agente: SHA de 40 ou 64 caracteres hexadecimais e o repositório, se houver.</summary>
public sealed record WorkReportCommit(string Sha, string? Repository);

/// <summary>O que o agente declarou no relatório.</summary>
public sealed record WorkReportDeclaration(
    string Summary,
    string? Changes,
    IReadOnlyList<string>? Files,
    WorkReportTests? Tests,
    WorkReportCommit? Commit);

/// <summary>Um estado do workflow, com o nome do momento do recebimento.</summary>
public sealed record WorkReportStateSnapshot(Guid StateId, string Name);

/// <summary>
/// Relatório de trabalho de um agente (ADR-016, D8): o que o agente declarou e o retrato do recebimento (estado da
/// tarefa e estado sugerido, com os nomes daquele momento). Imutável e permanente, inclusive no banco. Não muda o
/// estado da tarefa, e as verificações posteriores ficam em outra tabela.
/// </summary>
public sealed class WorkReport : Entity, IOrganizationOwned
{
    public const int SummaryMaxLength = 2_000;
    public const int ChangesMaxLength = 20_000;
    public const int MaxFiles = 200;
    public const int FilePathMaxLength = 500;
    public const int TestsCommandMaxLength = 500;
    public const int CommitRepositoryMaxLength = 200;
    public const int StateNameMaxLength = WorkflowState.NameMaxLength;

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    [SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "O EF Core grava o campo ao materializar a coluna files.")]
    private string[] _files;

    public WorkReport(
        Guid organizationId,
        Guid projectId,
        Guid? taskId,
        Guid agentId,
        Guid credentialId,
        WorkReportDeclaration declaration,
        WorkReportStateSnapshot? taskState,
        WorkReportStateSnapshot? suggestedState,
        string? requestId,
        DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        if (taskId is null != taskState is null)
        {
            throw new ArgumentException("O estado da tarefa acompanha a tarefa, e só ela.", nameof(taskState));
        }

        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        ProjectId = Guard.RequiredId(projectId, nameof(projectId));
        TaskId = taskId;
        AgentId = Guard.RequiredId(agentId, nameof(agentId));
        CredentialId = Guard.RequiredId(credentialId, nameof(credentialId));

        Summary = Guard.RequiredText(declaration.Summary, SummaryMaxLength, nameof(declaration));
        Changes = Guard.OptionalText(declaration.Changes, ChangesMaxLength, nameof(declaration));
        _files = [.. NormalizeFiles(declaration.Files)];

        if (declaration.Tests is { } tests)
        {
            TestsCommand = Guard.OptionalText(tests.Command, TestsCommandMaxLength, nameof(declaration));
            TestsPassed = NonNegative(tests.Passed);
            TestsFailed = NonNegative(tests.Failed);
            TestsSkipped = NonNegative(tests.Skipped);
        }

        if (declaration.Commit is { } commit)
        {
            CommitSha = IsCommitSha(commit.Sha)
                ? commit.Sha.ToLowerInvariant()
                : throw new ArgumentException("SHA de commit inválido.", nameof(declaration));
            CommitRepository = Guard.OptionalText(commit.Repository, CommitRepositoryMaxLength, nameof(declaration));
        }

        TaskStateId = taskState?.StateId;
        TaskStateName = taskState is null ? null : Guard.RequiredText(taskState.Name, StateNameMaxLength, nameof(taskState));
        SuggestedStateId = suggestedState?.StateId;
        SuggestedStateName = suggestedState is null ? null : Guard.RequiredText(suggestedState.Name, StateNameMaxLength, nameof(suggestedState));
        RequestId = requestId is { Length: > ActivityActor.RequestIdMaxLength } ? requestId[..ActivityActor.RequestIdMaxLength] : requestId;
        ReceivedAt = Timestamps.TruncateToMicroseconds(receivedAt);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private WorkReport(string summary)
    {
        Summary = summary;
        _files = [];
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid? TaskId { get; private set; }

    public Guid AgentId { get; private set; }

    public Guid CredentialId { get; private set; }

    public string Summary { get; private set; }

    /// <summary>Markdown declarado pelo agente.</summary>
    public string? Changes { get; private set; }

    public IReadOnlyList<string> Files => _files;

    public string? TestsCommand { get; private set; }

    public int? TestsPassed { get; private set; }

    public int? TestsFailed { get; private set; }

    public int? TestsSkipped { get; private set; }

    /// <summary>SHA em minúsculas.</summary>
    public string? CommitSha { get; private set; }

    public string? CommitRepository { get; private set; }

    /// <summary>Estado da tarefa no recebimento.</summary>
    public Guid? TaskStateId { get; private set; }

    public string? TaskStateName { get; private set; }

    public Guid? SuggestedStateId { get; private set; }

    public string? SuggestedStateName { get; private set; }

    public string? RequestId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>SHA-1 (40) ou SHA-256 (64) em hexadecimal.</summary>
    public static bool IsCommitSha(string? sha) =>
        sha is { Length: 40 or 64 } && !sha.AsSpan().ContainsAnyExcept(HexDigits);

    private static IEnumerable<string> NormalizeFiles(IReadOnlyList<string>? files)
    {
        if (files is null)
        {
            return [];
        }

        if (files.Count > MaxFiles)
        {
            throw new ArgumentException($"No máximo {MaxFiles} arquivos.", nameof(files));
        }

        return files.Select(file => Guard.RequiredText(file, FilePathMaxLength, nameof(files)));
    }

    private static int? NonNegative(int? value) =>
        value is < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "As contagens de testes não podem ser negativas.") : value;
}
