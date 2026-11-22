using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Domain.Organizations;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Gestão de agentes por pessoas com <c>agents.manage</c> (ADR-016): agentes, credenciais e acesso a projetos.
/// O token de uma credencial só aparece na resposta da criação, que não pode ser guardada em cache.
/// </summary>
[ApiController]
[Route("api/v1/organizations/{organizationId:guid}/agents")]
public sealed class AgentsController(IAgentService agents, IAgentOperationService operations, ITenantContext tenantContext) : ControllerBase
{
    private ActiveMembership Actor => tenantContext.Membership
        ?? throw new InvalidOperationException("Associação não resolvida.");

    [HttpGet]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<IReadOnlyList<AgentSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        Ok(await agents.ListAsync(cancellationToken));

    [HttpPost]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<AgentDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(Guid organizationId, CreateAgentRequest request, CancellationToken cancellationToken)
    {
        var result = await agents.CreateAsync(Actor, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/organizations/{organizationId}/agents/{result.Value!.Id}", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpGet("{agentId:guid}")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<AgentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid agentId, CancellationToken cancellationToken) =>
        ToResponse(await agents.GetAsync(agentId, cancellationToken));

    [HttpPatch("{agentId:guid}")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<AgentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid agentId, UpdateAgentRequest request, CancellationToken cancellationToken) =>
        ToResponse(await agents.UpdateAsync(agentId, request, cancellationToken));

    /// <summary>Desativa o agente: nenhuma credencial dele vale a partir da requisição seguinte.</summary>
    [HttpPost("{agentId:guid}/disable")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<AgentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableAsync(Guid agentId, CancellationToken cancellationToken) =>
        ToResponse(await agents.DisableAsync(Actor, agentId, cancellationToken));

    /// <summary>Reativa o agente. Credenciais revogadas ou expiradas continuam sem valer.</summary>
    [HttpPost("{agentId:guid}/enable")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<AgentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnableAsync(Guid agentId, CancellationToken cancellationToken) =>
        ToResponse(await agents.EnableAsync(agentId, cancellationToken));

    [HttpGet("{agentId:guid}/credentials")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<IReadOnlyList<AgentCredentialDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListCredentialsAsync(Guid agentId, CancellationToken cancellationToken) =>
        ToResponse(await agents.ListCredentialsAsync(agentId, cancellationToken));

    /// <summary>
    /// Operações do agente (ADR-016, D10), da mais recente para a mais antiga, inclusive as recusadas. Nunca trazem
    /// token, cabeçalhos ou corpos.
    /// </summary>
    [HttpGet("{agentId:guid}/operations")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<Application.Common.PagedResult<AgentOperationItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListOperationsAsync(Guid agentId, [FromQuery] AgentOperationListQuery query, CancellationToken cancellationToken) =>
        ToResponse(await operations.ListAsync(agentId, query, cancellationToken));

    /// <summary>Cria uma credencial. O token completo vem só nesta resposta (<c>Cache-Control: no-store</c>).</summary>
    [HttpPost("{agentId:guid}/credentials")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<CreatedAgentCredential>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCredentialAsync(
        Guid organizationId,
        Guid agentId,
        CreateAgentCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var result = await agents.CreateCredentialAsync(Actor, agentId, request, cancellationToken);
        if (!result.Succeeded)
        {
            return this.ToProblem(result.Error!);
        }

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Created(new Uri($"/api/v1/organizations/{organizationId}/agents/{agentId}/credentials", UriKind.Relative), result.Value);
    }

    [HttpPost("{agentId:guid}/credentials/{credentialId:guid}/revoke")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<AgentCredentialDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeCredentialAsync(Guid agentId, Guid credentialId, CancellationToken cancellationToken) =>
        ToResponse(await agents.RevokeCredentialAsync(Actor, agentId, credentialId, cancellationToken));

    /// <summary>Concede ou altera o acesso do agente ao projeto. Exige também <c>project.members.manage</c> no projeto.</summary>
    [HttpPut("{agentId:guid}/projects/{projectId:guid}")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType<AgentProjectAccessDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetProjectAccessAsync(
        Guid agentId,
        Guid projectId,
        SetAgentProjectAccessRequest request,
        CancellationToken cancellationToken) =>
        ToResponse(await agents.SetProjectAccessAsync(Actor, agentId, projectId, request, cancellationToken));

    [HttpDelete("{agentId:guid}/projects/{projectId:guid}")]
    [OrganizationPermission(OrganizationPermissions.AgentsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveProjectAccessAsync(Guid agentId, Guid projectId, CancellationToken cancellationToken)
    {
        var result = await agents.RemoveProjectAccessAsync(Actor, agentId, projectId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    private IActionResult ToResponse<T>(Application.Common.Result<T> result) =>
        result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
}
