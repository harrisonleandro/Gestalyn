using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Tarefas, responsáveis, comentários e dependências. Todas as rotas exigem o cabeçalho
/// <c>X-Gestalyn-Organization</c>; nas rotas por ID da tarefa, o acesso é o do projeto dela (ADR-011).
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class TasksController(ITaskService tasks, IAttachmentService attachments, ITenantContext tenantContext) : ControllerBase
{
    /// <summary>Limite da requisição de upload: o arquivo de até 10 MB mais a sobra do multipart.</summary>
    private const long MaxUploadRequestBytes = AttachmentFiles.MaxSizeBytes + (1024 * 1024);

    private ActiveMembership Actor => tenantContext.Membership
        ?? throw new InvalidOperationException("Associação não resolvida.");

    private ProjectAccessGrant Access => HttpContext.Items[typeof(ProjectAccessGrant)] as ProjectAccessGrant
        ?? throw new InvalidOperationException("Acesso ao projeto não resolvido.");

    [HttpPost("projects/{projectId:guid}/tasks")]
    [ProjectPermission(ProjectPermissions.TasksWrite)]
    [ProducesResponseType<TaskDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await tasks.CreateAsync(Actor, Access, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/tasks/{result.Value!.Id}", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Tarefas do projeto, com filtros, ordenação e paginação. Arquivadas ficam fora por padrão.</summary>
    [HttpGet("projects/{projectId:guid}/tasks")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<PagedResult<TaskSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid projectId, [FromQuery] TaskListQuery query, CancellationToken cancellationToken) =>
        Ok(await tasks.ListAsync(Actor, projectId, query, cancellationToken));

    [HttpGet("tasks/{taskId:guid}")]
    [TaskPermission(ProjectPermissions.Read)]
    [ProducesResponseType<TaskDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var result = await tasks.GetAsync(Access, taskId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPatch("tasks/{taskId:guid}")]
    [TaskPermission(ProjectPermissions.TasksWrite)]
    [ProducesResponseType<TaskDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await tasks.UpdateAsync(Access, taskId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Arquivamento lógico: a tarefa fica somente leitura e sai da listagem padrão.</summary>
    [HttpDelete("tasks/{taskId:guid}")]
    [TaskPermission(ProjectPermissions.TasksArchive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var result = await tasks.ArchiveAsync(Actor, taskId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    /// <summary>Torna um membro da equipe do projeto responsável pela tarefa.</summary>
    [HttpPost("tasks/{taskId:guid}/assignments")]
    [TaskPermission(ProjectPermissions.TasksWrite)]
    [ProducesResponseType<TaskDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddAssignmentAsync(Guid taskId, AddAssignmentRequest request, CancellationToken cancellationToken)
    {
        var result = await tasks.AddAssignmentAsync(Access, taskId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpDelete("tasks/{taskId:guid}/assignments/{userId:guid}")]
    [TaskPermission(ProjectPermissions.TasksWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveAssignmentAsync(Guid taskId, Guid userId, CancellationToken cancellationToken)
    {
        var result = await tasks.RemoveAssignmentAsync(taskId, userId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    /// <summary>"Esta tarefa depende de outra" (mesmo projeto, sem ciclos).</summary>
    [HttpPost("tasks/{taskId:guid}/dependencies")]
    [TaskPermission(ProjectPermissions.TasksWrite)]
    [ProducesResponseType<TaskDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddDependencyAsync(Guid taskId, AddDependencyRequest request, CancellationToken cancellationToken)
    {
        var result = await tasks.AddDependencyAsync(Actor, Access, taskId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpDelete("tasks/{taskId:guid}/dependencies/{dependsOnTaskId:guid}")]
    [TaskPermission(ProjectPermissions.TasksWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveDependencyAsync(Guid taskId, Guid dependsOnTaskId, CancellationToken cancellationToken)
    {
        var result = await tasks.RemoveDependencyAsync(taskId, dependsOnTaskId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    [HttpGet("tasks/{taskId:guid}/comments")]
    [TaskPermission(ProjectPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<CommentDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListCommentsAsync(Guid taskId, CancellationToken cancellationToken) =>
        Ok(await tasks.ListCommentsAsync(taskId, cancellationToken));

    [HttpPost("tasks/{taskId:guid}/comments")]
    [TaskPermission(ProjectPermissions.TasksComment)]
    [ProducesResponseType<CommentDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddCommentAsync(Guid taskId, CommentRequest request, CancellationToken cancellationToken)
    {
        var result = await tasks.AddCommentAsync(Actor, taskId, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/tasks/{taskId}/comments", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Só o autor edita o próprio comentário.</summary>
    [HttpPatch("tasks/{taskId:guid}/comments/{commentId:guid}")]
    [TaskPermission(ProjectPermissions.TasksComment)]
    [ProducesResponseType<CommentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCommentAsync(Guid taskId, Guid commentId, CommentRequest request, CancellationToken cancellationToken)
    {
        var result = await tasks.UpdateCommentAsync(Actor, taskId, commentId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Exclui um comentário: o autor ou quem tem <c>tasks.archive</c>.</summary>
    [HttpDelete("tasks/{taskId:guid}/comments/{commentId:guid}")]
    [TaskPermission(ProjectPermissions.Read)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCommentAsync(Guid taskId, Guid commentId, CancellationToken cancellationToken)
    {
        var result = await tasks.DeleteCommentAsync(Actor, Access, taskId, commentId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    [HttpGet("tasks/{taskId:guid}/attachments")]
    [TaskPermission(ProjectPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<AttachmentDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAttachmentsAsync(Guid taskId, CancellationToken cancellationToken) =>
        Ok(await attachments.ListAsync(taskId, cancellationToken));

    /// <summary>
    /// Envia um anexo (`multipart/form-data`, campo `file`). Até 10 MB, só os tipos permitidos, conferidos
    /// pelo conteúdo.
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/attachments")]
    [TaskPermission(ProjectPermissions.TasksWrite)]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType<AttachmentDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadAttachmentAsync(Guid taskId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Envie o arquivo no campo file.");
        }

        await using var content = file.OpenReadStream();
        var result = await attachments.UploadAsync(Actor, taskId, file.FileName, content, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/tasks/{taskId}/attachments/{result.Value!.Id}/content", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>
    /// Baixa o anexo, sempre como arquivo para salvar (nunca exibido no navegador), com o tipo definido pelo
    /// servidor e `X-Content-Type-Options: nosniff`.
    /// </summary>
    [HttpGet("tasks/{taskId:guid}/attachments/{attachmentId:guid}/content")]
    [TaskPermission(ProjectPermissions.Read)]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachmentAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var result = await attachments.OpenAsync(taskId, attachmentId, cancellationToken);
        if (!result.Succeeded)
        {
            return this.ToProblem(result.Error!);
        }

        var headers = Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src none; sandbox";
        headers.CacheControl = "private, no-store";

        var (details, stream) = result.Value!;
        return File(stream, details.ContentType, details.FileName);
    }

    [HttpDelete("tasks/{taskId:guid}/attachments/{attachmentId:guid}")]
    [TaskPermission(ProjectPermissions.TasksWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAttachmentAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var result = await attachments.DeleteAsync(taskId, attachmentId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }
}
