using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManager.API.Agents;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Infrastructure.Persistence;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Agents.Probes;

/// <summary>
/// Controllers usados só nos testes da política por marca (ADR-016) e do isolamento no pipeline real de agentes. Têm
/// <c>[NonController]</c>: a descoberta normal os ignora, e cada teste adiciona apenas os que precisa.
/// </summary>
internal static class Probes
{
    public static WebApplicationFactory<Program> WithProbes(this ApiFactory factory, params Type[] controllers) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().ConfigureApplicationPartManager(manager =>
                manager.FeatureProviders.Add(new ProbeControllerProvider(controllers)))));

    private sealed class ProbeControllerProvider(Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers.Select(type => type.GetTypeInfo()).Where(type => !feature.Controllers.Contains(type)))
            {
                feature.Controllers.Add(controller);
            }
        }
    }
}

/// <summary>
/// API de agentes fora do namespace convencional (<c>ProjectManager.API.Agents</c>): a marca, e não o namespace, decide
/// a política. Expõe o que o contexto da requisição do agente enxerga no banco, para testar o isolamento de verdade.
/// </summary>
[ApiController]
[AgentApi]
[NonController]
[Route("api/v1/agents/__probe")]
public sealed class AgentIsolationProbeController(ProjectManagerDbContext dbContext, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("visible")]
    public Task<VisibleData> VisibleAsync(CancellationToken cancellationToken) => ReadVisibleAsync(cancellationToken);

    /// <summary>Rota com <c>{organizationId}</c>: a guarda de organização confere também a rota.</summary>
    [HttpGet("organizations/{organizationId:guid}/visible")]
    public Task<VisibleData> VisibleInOrganizationAsync(CancellationToken cancellationToken) => ReadVisibleAsync(cancellationToken);

    /// <summary>Busca uma credencial pelo ID, como faria qualquer rota futura de agentes.</summary>
    [HttpGet("credentials/{credentialId:guid}")]
    public async Task<IActionResult> CredentialAsync(Guid credentialId, CancellationToken cancellationToken) =>
        await dbContext.AgentCredentials.AnyAsync(c => c.Id == credentialId, cancellationToken) ? Ok() : NotFound();

    /// <summary>Rota absoluta na ação, dentro do prefixo de agentes, num controller marcado.</summary>
    [HttpGet("/api/v1/agents/__probe-absolute")]
    public IActionResult Absolute() => Ok(tenantContext.Agent?.AgentId);

    private async Task<VisibleData> ReadVisibleAsync(CancellationToken cancellationToken) =>
        new(
            tenantContext.OrganizationId,
            await dbContext.Agents.Select(a => a.Id).ToListAsync(cancellationToken),
            await dbContext.AgentCredentials.Select(c => c.Id).ToListAsync(cancellationToken),
            await dbContext.AgentProjectAccess.Select(a => a.ProjectId).ToListAsync(cancellationToken),
            await dbContext.Projects.Select(p => p.Id).ToListAsync(cancellationToken),
            await dbContext.Organizations.Select(o => o.Id).ToListAsync(cancellationToken));
}

public sealed record VisibleData(
    Guid? OrganizationId,
    IReadOnlyList<Guid> Agents,
    IReadOnlyList<Guid> Credentials,
    IReadOnlyList<Guid> GrantedProjects,
    IReadOnlyList<Guid> Projects,
    IReadOnlyList<Guid> Organizations);

/// <summary>Controller de pessoas fora do prefixo de agentes, com rota customizada: recebe a política User.</summary>
[ApiController]
[NonController]
[Route("api/v1/__probe/user")]
public sealed class UserProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>Marcado como API de agentes, mas com a rota fora do prefixo: a API não pode iniciar.</summary>
[ApiController]
[AgentApi]
[NonController]
[Route("api/v1/__probe/marked-outside")]
public sealed class MarkedOutsidePrefixProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>Marcado, com a rota do controller dentro do prefixo, mas uma ação com rota absoluta fora dele.</summary>
[ApiController]
[AgentApi]
[NonController]
[Route("api/v1/agents/__probe/escape")]
public sealed class MarkedWithEscapingActionProbeController : ControllerBase
{
    [HttpGet("/api/v1/projects/__probe-escape")]
    public IActionResult Get() => Ok();
}

/// <summary>Rota sob o prefixo de agentes sem a marca: a API não pode iniciar.</summary>
[ApiController]
[NonController]
[Route("api/v1/agents/__probe/unmarked")]
public sealed class UnmarkedAgentRouteProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>Sem rota no controller e sem a marca, com uma ação de rota absoluta sob o prefixo de agentes.</summary>
[ApiController]
[NonController]
public sealed class UnmarkedActionRouteProbeController : ControllerBase
{
    [HttpGet("/api/v1/agents/__probe/action-only")]
    public IActionResult Get() => Ok();
}
