using System.Security.Claims;
using ProjectManager.Application.Abstractions.Activity;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.API.Activity;

/// <summary>
/// Autor e origem a partir da requisição (ADR-014, D3). O autor é sempre o <c>sub</c> do token, nunca um valor
/// do corpo. A origem vem do cabeçalho <c>X-Gestalyn-Client</c>, declarado pelo cliente e só informativo. Até
/// existirem credenciais de agente (Fase 10), todo autor é uma pessoa.
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
            return new ActivityActor(actorId, ActorKind.User, origin, httpContext.TraceIdentifier);
        }
    }
}
