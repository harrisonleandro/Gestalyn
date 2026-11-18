using Microsoft.AspNetCore.Mvc;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;

namespace ProjectManager.API.Agents;

/// <summary>Rotas do próprio agente (ADR-016), com a política <c>Agent</c> aplicada pela convenção deste namespace.</summary>
[ApiController]
[Route("api/v1/agents")]
[Tags("Agentes")]
public sealed class AgentSelfController(IAgentWorkService work, ITenantContext tenantContext) : ControllerBase
{
    private ActiveAgent Agent => tenantContext.Agent
        ?? throw new InvalidOperationException("Agente não resolvido.");

    /// <summary>O agente, a organização e a credencial em uso (prefixo, escopos e validade). Não exige escopo.</summary>
    [HttpGet("me")]
    [ProducesResponseType<AgentMe>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMeAsync(CancellationToken cancellationToken) =>
        Ok(await work.GetMeAsync(Agent, cancellationToken));
}
