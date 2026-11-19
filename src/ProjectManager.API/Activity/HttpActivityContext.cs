using System.Security.Claims;
using ProjectManager.Application.Abstractions.Activity;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.API.Activity;

/// <summary>
/// Autor e origem a partir da requisição (ADR-014, D3). O autor é sempre o <c>sub</c> do token, nunca um valor
/// do corpo. A origem vem do cabeçalho <c>X-Gestalyn-Client</c>, declarado pelo cliente e só informativo. Um principal
/// de agente (<c>actor_kind=agent</c>, ADR-016) registra o agente como autor, com <see cref="ActorKind.Agent"/>.
/// </summary>
internal sealed class HttpActivityContext(IHttpContextAccessor httpContextAccessor) : IActivityContext
{
    public const string ClientHeader = "X-Gestalyn-Client";

    public ActivityActor Actor
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext is null)
            {
                return ActivityActor.System;
            }

            Guid? actorId = Guid.TryParse(httpContext.User.FindFirstValue(AuthClaimTypes.Subject), out var userId) ? userId : null;
            var origin = ActivityOrigins.FromClientHeader(httpContext.Request.Headers[ClientHeader].ToString());
            var kind = httpContext.User.HasClaim(AuthClaimTypes.ActorKind, AuthClaimTypes.AgentActor) ? ActorKind.Agent : ActorKind.User;
            return new ActivityActor(actorId, kind, origin, httpContext.TraceIdentifier);
        }
    }
}
