using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Documents;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Documents;
using ProjectManager.Domain.Projects;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Documentos e decisões do projeto (ADR-014). As rotas por ID do recurso exigem o cabeçalho
/// <c>X-Gestalyn-Organization</c> e calculam o acesso pelo projeto do documento ou da decisão.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class DocumentsController(IDocumentService documents, IDecisionService decisions) : ControllerBase
{
    private ProjectAccessGrant Access => HttpContext.Items[typeof(ProjectAccessGrant)] as ProjectAccessGrant
        ?? throw new InvalidOperationException("Acesso ao projeto não resolvido.");

    [HttpGet("projects/{projectId:guid}/documents")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<DocumentSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocumentsAsync(Guid projectId, [FromQuery] bool includeArchived, CancellationToken cancellationToken) =>
        Ok(await documents.ListAsync(projectId, includeArchived, cancellationToken));

    [HttpPost("projects/{projectId:guid}/documents")]
    [ProjectPermission(ProjectPermissions.DocumentsWrite)]
    [ProducesResponseType<DocumentDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken cancellationToken)
    {
        var result = await documents.CreateAsync(Access, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/documents/{result.Value!.Id}", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpGet("documents/{documentId:guid}")]
    [DocumentPermission(ProjectPermissions.Read)]
    [ProducesResponseType<DocumentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await documents.GetAsync(documentId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Nova versão do documento. A versão esperada diferente da atual responde 409.</summary>
    [HttpPut("documents/{documentId:guid}")]
    [DocumentPermission(ProjectPermissions.DocumentsWrite)]
    [ProducesResponseType<DocumentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDocumentAsync(Guid documentId, UpdateDocumentRequest request, CancellationToken cancellationToken)
    {
        var result = await documents.UpdateAsync(Access, documentId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("documents/{documentId:guid}/versions")]
    [DocumentPermission(ProjectPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<DocumentVersionSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken) =>
        Ok(await documents.ListVersionsAsync(documentId, cancellationToken));

    [HttpGet("documents/{documentId:guid}/versions/{number:int}")]
    [DocumentPermission(ProjectPermissions.Read)]
    [ProducesResponseType<DocumentVersionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersionAsync(Guid documentId, int number, CancellationToken cancellationToken)
    {
        var result = await documents.GetVersionAsync(documentId, number, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("documents/{documentId:guid}/archive")]
    [DocumentPermission(ProjectPermissions.DocumentsManage)]
    [ProducesResponseType<DocumentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await documents.ArchiveAsync(Access, documentId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("documents/{documentId:guid}/restore")]
    [DocumentPermission(ProjectPermissions.DocumentsManage)]
    [ProducesResponseType<DocumentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await documents.RestoreAsync(documentId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("projects/{projectId:guid}/decisions")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<DecisionSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDecisionsAsync(Guid projectId, [FromQuery] DecisionStatus? status, CancellationToken cancellationToken) =>
        Ok(await decisions.ListAsync(projectId, status, cancellationToken));

    [HttpPost("projects/{projectId:guid}/decisions")]
    [ProjectPermission(ProjectPermissions.DocumentsWrite)]
    [ProducesResponseType<DecisionDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProposeDecisionAsync(CreateDecisionRequest request, CancellationToken cancellationToken)
    {
        var result = await decisions.CreateAsync(Access, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/decisions/{result.Value!.Id}", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpGet("decisions/{decisionId:guid}")]
    [DecisionPermission(ProjectPermissions.Read)]
    [ProducesResponseType<DecisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDecisionAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var result = await decisions.GetAsync(decisionId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Altera uma proposta. Decisão já decidida responde 409.</summary>
    [HttpPatch("decisions/{decisionId:guid}")]
    [DecisionPermission(ProjectPermissions.DocumentsWrite)]
    [ProducesResponseType<DecisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDecisionAsync(Guid decisionId, UpdateDecisionRequest request, CancellationToken cancellationToken)
    {
        var result = await decisions.UpdateAsync(decisionId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("decisions/{decisionId:guid}/accept")]
    [DecisionPermission(ProjectPermissions.DocumentsManage)]
    [ProducesResponseType<DecisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptDecisionAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var result = await decisions.AcceptAsync(Access, decisionId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("decisions/{decisionId:guid}/reject")]
    [DecisionPermission(ProjectPermissions.DocumentsManage)]
    [ProducesResponseType<DecisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectDecisionAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var result = await decisions.RejectAsync(Access, decisionId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Substitui esta decisão aceita por outra aceita do mesmo projeto.</summary>
    [HttpPost("decisions/{decisionId:guid}/supersede")]
    [DecisionPermission(ProjectPermissions.DocumentsManage)]
    [ProducesResponseType<DecisionDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SupersedeDecisionAsync(Guid decisionId, SupersedeDecisionRequest request, CancellationToken cancellationToken)
    {
        var result = await decisions.SupersedeAsync(decisionId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
