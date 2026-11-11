using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Projetos, equipes e repositórios. A criação e a listagem ficam sob a organização da rota; as demais
/// rotas são por ID do projeto e exigem o cabeçalho <c>X-Gestalyn-Organization</c> (ADR-010).
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class ProjectsController(IProjectService projects, ITenantContext tenantContext) : ControllerBase
{
    private ActiveMembership Actor => tenantContext.Membership
        ?? throw new InvalidOperationException("Associação não resolvida.");

    private ProjectAccessGrant Access => HttpContext.Items[typeof(ProjectAccessGrant)] as ProjectAccessGrant
        ?? throw new InvalidOperationException("Acesso ao projeto não resolvido.");

    /// <summary>Cria um projeto; o usuário autenticado entra na equipe como Gerente.</summary>
    [HttpPost("organizations/{organizationId:guid}/projects")]
    [OrganizationPermission(OrganizationPermissions.ProjectsCreate)]
    [ProducesResponseType<ProjectDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var result = await projects.CreateAsync(Actor, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/projects/{result.Value!.Id}", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>
    /// Projetos visíveis para o usuário: todos, com <c>projects.read_all</c>; senão, só os projetos em cuja
    /// equipe ele está. Arquivados ficam fora, salvo com <c>includeArchived=true</c>.
    /// </summary>
    [HttpGet("organizations/{organizationId:guid}/projects")]
    [OrganizationPermission]
    [ProducesResponseType<PagedResult<ProjectSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync([FromQuery] ProjectListQuery query, CancellationToken cancellationToken) =>
        Ok(await projects.ListAsync(Actor, query, cancellationToken));

    [HttpGet("projects/{projectId:guid}")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<ProjectDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await projects.GetAsync(Access, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Alteração parcial dos dados, datas, prioridade e etiquetas do projeto.</summary>
    [HttpPatch("projects/{projectId:guid}")]
    [ProjectPermission(ProjectPermissions.Update)]
    [ProducesResponseType<ProjectDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        var result = await projects.UpdateAsync(Access, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Arquivamento lógico: o projeto fica somente leitura e sai da listagem padrão.</summary>
    [HttpDelete("projects/{projectId:guid}")]
    [ProjectPermission(ProjectPermissions.Archive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await projects.ArchiveAsync(Actor, projectId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    [HttpGet("projects/{projectId:guid}/members")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<ProjectMemberDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMembersAsync(Guid projectId, CancellationToken cancellationToken) =>
        Ok(await projects.ListMembersAsync(projectId, cancellationToken));

    /// <summary>Adiciona um membro da organização à equipe do projeto.</summary>
    [HttpPost("projects/{projectId:guid}/members")]
    [ProjectPermission(ProjectPermissions.MembersManage)]
    [ProducesResponseType<ProjectMemberDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMemberAsync(Guid projectId, AddProjectMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await projects.AddMemberAsync(projectId, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/projects/{projectId}/members", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Altera o papel de um membro da equipe ou o marca como responsável.</summary>
    [HttpPatch("projects/{projectId:guid}/members/{userId:guid}")]
    [ProjectPermission(ProjectPermissions.MembersManage)]
    [ProducesResponseType<ProjectMemberDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMemberAsync(
        Guid projectId,
        Guid userId,
        UpdateProjectMemberRequest request,
        CancellationToken cancellationToken)
    {
        var result = await projects.UpdateMemberAsync(projectId, userId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpDelete("projects/{projectId:guid}/members/{userId:guid}")]
    [ProjectPermission(ProjectPermissions.MembersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        var result = await projects.RemoveMemberAsync(projectId, userId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    [HttpGet("projects/{projectId:guid}/repositories")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<ProjectRepositoryDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListRepositoriesAsync(Guid projectId, CancellationToken cancellationToken) =>
        Ok(await projects.ListRepositoriesAsync(projectId, cancellationToken));

    /// <summary>Associa um repositório (somente metadados; nenhuma chamada ao provedor).</summary>
    [HttpPost("projects/{projectId:guid}/repositories")]
    [ProjectPermission(ProjectPermissions.Update)]
    [ProducesResponseType<ProjectRepositoryDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddRepositoryAsync(Guid projectId, AddProjectRepositoryRequest request, CancellationToken cancellationToken)
    {
        var result = await projects.AddRepositoryAsync(projectId, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/projects/{projectId}/repositories", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpDelete("projects/{projectId:guid}/repositories/{repositoryId:guid}")]
    [ProjectPermission(ProjectPermissions.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveRepositoryAsync(Guid projectId, Guid repositoryId, CancellationToken cancellationToken)
    {
        var result = await projects.RemoveRepositoryAsync(projectId, repositoryId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }
}
