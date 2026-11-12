using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Abstractions.Storage;
using ProjectManager.Application.Common;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Tasks;

/// <summary>
/// Anexos de tarefas (ADR-011): tamanho, tipo e nome validados no servidor; o arquivo é gravado antes da
/// linha no banco e apagado se a gravação da linha falhar.
/// </summary>
internal sealed partial class AttachmentService(
    ProjectManagerDbContext dbContext,
    IFileStorage storage,
    ILogger<AttachmentService> logger) : IAttachmentService
{
    private const string TaskNotFoundMessage = "Tarefa não encontrada.";
    private const string AttachmentNotFoundMessage = "Anexo não encontrado.";

    public async Task<IReadOnlyList<AttachmentDetails>> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var attachments = await dbContext.TaskAttachments
            .AsNoTracking()
            .Where(attachment => attachment.TaskId == taskId)
            .OrderBy(attachment => attachment.CreatedAt)
            .ToListAsync(cancellationToken);

        return attachments.Select(ToDetails).ToList();
    }

    public async Task<Result<AttachmentDetails>> UploadAsync(
        ActiveMembership actor,
        Guid taskId,
        string? fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(content);

        var task = await dbContext.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<AttachmentDetails>(ErrorKind.NotFound, TaskNotFoundMessage);
        }

        if (task.IsArchived)
        {
            return Result.Failure<AttachmentDetails>(ErrorKind.Conflict, ProjectTask.ArchivedMessage);
        }

        if (await dbContext.Projects.AnyAsync(p => p.Id == task.ProjectId && p.ArchivedAt != null, cancellationToken))
        {
            return Result.Failure<AttachmentDetails>(ErrorKind.Conflict, Project.ArchivedMessage);
        }

        if (await dbContext.TaskAttachments.CountAsync(a => a.TaskId == taskId, cancellationToken) >= TaskAttachment.MaxPerTask)
        {
            return Result.Failure<AttachmentDetails>(ErrorKind.Validation, $"Uma tarefa pode ter no máximo {TaskAttachment.MaxPerTask} anexos.");
        }

        await using var buffer = await ReadLimitedAsync(content, cancellationToken);
        if (buffer is null)
        {
            return Result.Failure<AttachmentDetails>(ErrorKind.Validation, $"O arquivo deve ter no máximo {AttachmentFiles.MaxSizeBytes / (1024 * 1024)} MB.");
        }

        if (buffer.Length == 0)
        {
            return Result.Failure<AttachmentDetails>(ErrorKind.Validation, "O arquivo está vazio.");
        }

        var safeName = AttachmentFiles.SanitizeFileName(fileName);
        var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
        var type = AttachmentFiles.Identify(safeName, bytes);
        if (type is null)
        {
            return Result.Failure<AttachmentDetails>(ErrorKind.Validation, AttachmentFiles.NotAllowedMessage);
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes.Span));
        var attachment = new TaskAttachment(task, safeName, type, buffer.Length, sha256, actor.UserId);

        buffer.Position = 0;
        await storage.SaveAsync(attachment.StorageKey, buffer, cancellationToken);

        dbContext.TaskAttachments.Add(attachment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(attachment.StorageKey, CancellationToken.None);
            throw;
        }

        LogAttachmentUploaded(logger, attachment.Id, taskId, actor.UserId, attachment.SizeBytes);
        return Result.Success(ToDetails(attachment));
    }

    public async Task<Result<AttachmentContent>> OpenAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await dbContext.TaskAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.TaskId == taskId && a.Id == attachmentId, cancellationToken);
        if (attachment is null)
        {
            return Result.Failure<AttachmentContent>(ErrorKind.NotFound, AttachmentNotFoundMessage);
        }

        var stream = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        if (stream is null)
        {
            LogAttachmentFileMissing(logger, attachment.Id);
            return Result.Failure<AttachmentContent>(ErrorKind.NotFound, AttachmentNotFoundMessage);
        }

        return Result.Success(new AttachmentContent(ToDetails(attachment), stream));
    }

    public async Task<Result> DeleteAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var task = await dbContext.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure(ErrorKind.NotFound, TaskNotFoundMessage);
        }

        if (task.IsArchived || await dbContext.Projects.AnyAsync(p => p.Id == task.ProjectId && p.ArchivedAt != null, cancellationToken))
        {
            return Result.Failure(ErrorKind.Conflict, task.IsArchived ? ProjectTask.ArchivedMessage : Project.ArchivedMessage);
        }

        var attachment = await dbContext.TaskAttachments.SingleOrDefaultAsync(a => a.TaskId == taskId && a.Id == attachmentId, cancellationToken);
        if (attachment is null)
        {
            return Result.Failure(ErrorKind.NotFound, AttachmentNotFoundMessage);
        }

        dbContext.TaskAttachments.Remove(attachment);
        await dbContext.SaveChangesAsync(cancellationToken);

        // A linha já saiu: o arquivo deixou de ser acessível. Uma falha aqui só deixa um órfão no disco.
        try
        {
            await storage.DeleteAsync(attachment.StorageKey, cancellationToken);
        }
        catch (IOException exception)
        {
            LogAttachmentFileNotDeleted(logger, exception, attachment.Id);
        }

        return Result.Success();
    }

    /// <summary>Copia o conteúdo para a memória, parando em <see cref="AttachmentFiles.MaxSizeBytes"/> + 1 byte.</summary>
    private static async Task<MemoryStream?> ReadLimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var block = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + read > AttachmentFiles.MaxSizeBytes)
            {
                await buffer.DisposeAsync();
                return null;
            }

            buffer.Write(block, 0, read);
        }

        return buffer;
    }

    private static AttachmentDetails ToDetails(TaskAttachment attachment) =>
        new(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.Sha256, attachment.UploadedByUserId, attachment.CreatedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Anexo {AttachmentId} ({SizeBytes} bytes) enviado para a tarefa {TaskId} pelo usuário {UserId}.")]
    private static partial void LogAttachmentUploaded(ILogger logger, Guid attachmentId, Guid taskId, Guid userId, long sizeBytes);

    [LoggerMessage(Level = LogLevel.Error, Message = "O arquivo do anexo {AttachmentId} não foi encontrado no armazenamento.")]
    private static partial void LogAttachmentFileMissing(ILogger logger, Guid attachmentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível apagar o arquivo do anexo {AttachmentId}; ele ficou órfão no armazenamento.")]
    private static partial void LogAttachmentFileNotDeleted(ILogger logger, Exception exception, Guid attachmentId);
}
