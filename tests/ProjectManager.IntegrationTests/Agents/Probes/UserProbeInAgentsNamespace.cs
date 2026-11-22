using Microsoft.AspNetCore.Mvc;

// Namespace dos controllers de agentes de propósito: sem a marca [AgentApi], o namespace não muda a política.
#pragma warning disable IDE0130 // O namespace não corresponde à pasta: é exatamente o que este teste exercita.
namespace ProjectManager.API.Agents;
#pragma warning restore IDE0130

/// <summary>Controller de pessoas no namespace convencional dos agentes: recebe a política User.</summary>
[ApiController]
[NonController]
[Route("api/v1/__probe/user-in-agents-namespace")]
public sealed class UserProbeInAgentsNamespaceController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}
