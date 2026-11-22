using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Agents;

namespace ProjectManager.API.Agents;

/// <summary>
/// Idempotência obrigatória dos POSTs de agentes (ADR-016, D9). Roda depois da autenticação e da autorização da rota:
/// <list type="number">
/// <item><c>Idempotency-Key</c> ausente ou inválido: 400;</item>
/// <item>mesma chave em processamento: 409;</item>
/// <item>mesma chave com outro método, rota (inclusive outro ID no caminho), query ou corpo: 422;</item>
/// <item>mesma requisição: reavalia o acesso ao projeto guardado e só então devolve a resposta original, com
/// <c>Idempotency-Replayed: true</c>; sem acesso, a recusa atual (403, 404 ou 401);</item>
/// <item>chave nova: a operação e a resposta são gravadas na mesma transação. Respostas que dependem de autorização,
/// limite ou falha (401, 403, 404, 429 e 5xx) não são guardadas, e o que a operação gravou é desfeito.</item>
/// </list>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AgentIdempotentAttribute(string scope) : Attribute, IAsyncResourceFilter
{
    public string Scope { get; } = scope;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var cancellationToken = httpContext.RequestAborted;
        var agent = httpContext.RequestServices.GetRequiredService<ITenantContext>().Agent;
        if (agent is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var keys = httpContext.Request.Headers[IdempotencyRecord.HeaderName];
        if (keys.Count != 1 || !IdempotencyRecord.IsValidKey(keys[0]))
        {
            context.Result = Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Chave de idempotência ausente ou inválida.",
                $"Envie o cabeçalho {IdempotencyRecord.HeaderName} com 1 a {IdempotencyRecord.KeyMaxLength} caracteres ASCII visíveis.");
            return;
        }

        var request = new IdempotentRequest(
            agent,
            keys[0]!,
            httpContext.Request.Method,
            context.ActionDescriptor.AttributeRouteInfo?.Template ?? httpContext.Request.Path.ToString(),
            await HashRequestAsync(httpContext.Request, cancellationToken));

        await using var session = await httpContext.RequestServices.GetRequiredService<IAgentIdempotencyStore>().BeginAsync(request, cancellationToken);
        if (session is null)
        {
            context.Result = Problem(httpContext, StatusCodes.Status409Conflict, "Requisição com esta chave ainda em processamento.", detail: null);
            return;
        }

        if (session.Existing is { } stored)
        {
            context.Result = await ReplayAsync(httpContext, agent, request, stored, cancellationToken);
            await session.AbandonAsync(cancellationToken);
            return;
        }

        await ExecuteAndStoreAsync(httpContext, session, next, cancellationToken);
    }

    /// <summary>
    /// Hash do caminho concreto, da query e do corpo: a mesma chave com o mesmo corpo em outra tarefa
    /// (<c>tasks/{taskId}/transitions</c>) é outra requisição, nunca a repetição da primeira.
    /// </summary>
    private static async Task<string> HashRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var copy = new MemoryStream();
        var target = Encoding.UTF8.GetBytes($"{request.Path}{request.QueryString}\n");
        await copy.WriteAsync(target, cancellationToken);
        await request.Body.CopyToAsync(copy, cancellationToken);
        request.Body.Position = 0;
        return Convert.ToHexString(SHA256.HashData(copy.ToArray()));
    }

    private async Task<IActionResult> ReplayAsync(
        HttpContext httpContext,
        ActiveAgent agent,
        IdempotentRequest request,
        StoredIdempotentResponse stored,
        CancellationToken cancellationToken)
    {
        if (!stored.Matches(request))
        {
            return Problem(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                "Chave de idempotência já usada em outra requisição.",
                "A mesma chave só pode repetir o mesmo método, a mesma rota e o mesmo corpo.");
        }

        // Reautorização: uma credencial que perdeu o acesso ao projeto não recupera a resposta guardada.
        if (stored.ProjectId is { } projectId)
        {
            var requirement = AgentRequirement.ForScope(Scope);
            var decision = await httpContext.RequestServices.GetRequiredService<IAgentProjectAccess>()
                .FindAsync(agent, projectId, requirement, cancellationToken);
            if (AgentResourcePermissionAttribute.ToResult(httpContext, decision, requirement, "Projeto não encontrado.") is { } denied)
            {
                return denied;
            }
        }

        httpContext.Response.Headers[IdempotencyRecord.ReplayedHeaderName] = "true";
        if (stored.Location is not null)
        {
            httpContext.Response.Headers.Location = stored.Location;
        }

        return new ContentResult { StatusCode = stored.StatusCode, Content = stored.Body, ContentType = "application/json; charset=utf-8" };
    }

    private static async Task ExecuteAndStoreAsync(
        HttpContext httpContext,
        IIdempotencySession session,
        ResourceExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        var original = httpContext.Response.Body;
        await using var buffer = new MemoryStream();
        httpContext.Response.Body = buffer;
        ResourceExecutedContext executed;
        try
        {
            executed = await next();
        }
        finally
        {
            httpContext.Response.Body = original;
        }

        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            await session.AbandonAsync(cancellationToken);
            return;
        }

        var status = httpContext.Response.StatusCode;
        if (IsStorable(status))
        {
            if (status >= StatusCodes.Status400BadRequest)
            {
                await session.DiscardOperationAsync(cancellationToken);
            }

            var grant = httpContext.Items[typeof(ProjectAccessGrant)] as ProjectAccessGrant;
            var location = httpContext.Response.Headers.Location.ToString();
            await session.CompleteAsync(
                status,
                Encoding.UTF8.GetString(buffer.ToArray()),
                string.IsNullOrEmpty(location) ? null : location,
                grant?.ProjectId,
                cancellationToken);
        }
        else
        {
            await session.AbandonAsync(cancellationToken);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, cancellationToken);
    }

    /// <summary>Guardadas: sucesso e recusas determinísticas (validação, conflito, entidade improcessável).</summary>
    private static bool IsStorable(int status) =>
        status is >= 200 and < 300
            or StatusCodes.Status400BadRequest
            or StatusCodes.Status409Conflict
            or StatusCodes.Status422UnprocessableEntity;

    private static ObjectResult Problem(HttpContext httpContext, int statusCode, string title, string? detail)
    {
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        return new ObjectResult(factory.CreateProblemDetails(httpContext, statusCode, title, detail: detail)) { StatusCode = statusCode };
    }
}
