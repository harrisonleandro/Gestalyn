using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;

namespace ProjectManager.Application.Tasks;

public sealed record AttachmentDetails(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    Guid UploadedByUserId,
    DateTimeOffset CreatedAt);

/// <summary>Conteúdo de um anexo para download. Quem recebe é responsável por descartar o fluxo.</summary>
public sealed record AttachmentContent(AttachmentDetails Details, Stream Content);

/// <summary>
/// Anexos de tarefas (ADR-011). As operações pressupõem que o acesso ao projeto da tarefa já foi
/// verificado. Tamanho, tipo e nome são validados aqui, nunca confiando no cliente.
/// </summary>
public interface IAttachmentService
{
    Task<IReadOnlyList<AttachmentDetails>> ListAsync(Guid taskId, CancellationToken cancellationToken);

    Task<Result<AttachmentDetails>> UploadAsync(
        ActiveMembership actor,
        Guid taskId,
        string? fileName,
        Stream content,
        CancellationToken cancellationToken);

    Task<Result<AttachmentContent>> OpenAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken);
}
