using System.Data.Common;
using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Authentication;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Agents;

namespace ProjectManager.API.Agents;

/// <summary>
/// Entrada das rotas de agentes (ADR-016, D3, D10 e D11), logo depois da autenticação padrão:
/// <list type="number">
/// <item>token <c>gsk_</c> de um IP acima do limite de falhas: 429, antes de qualquer consulta ao banco;</item>
/// <item>autenticação pelo esquema <c>AgentToken</c> (o resultado fica em cache para a política da rota);</item>
/// <item>rate limiting da credencial no PostgreSQL: acima do limite, 429 com <c>Retry-After</c>;</item>
/// <item>o resto do pipeline;</item>
/// <item>no fim, com a credencial identificada, uma linha de <see cref="AgentOperation"/>, gravada fora da
/// transação da operação. Tokens inexistentes não têm agente nem organização e ficam só no log.</item>
/// </list>
/// </summary>
internal sealed partial class AgentRequestMiddleware(RequestDelegate next, ILogger<AgentRequestMiddleware> logger)
{
    public const string PathPrefix = "/api/v1/agents";

    private const string BearerTokenPrefix = "Bearer " + AgentTokens.Marker;

    public async Task InvokeAsync(
        HttpContext context,
        AgentOperationContext operation,
        AgentAuthFailureLimiter failureLimiter,
        IAgentRateLimiter rateLimiter,
        IAgentOperationWriter writer,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (context.Request.Headers.Authorization.ToString().StartsWith(BearerTokenPrefix, StringComparison.OrdinalIgnoreCase)
            && failureLimiter.IsBlocked(context.Connection.RemoteIpAddress, out var blockedFor))
        {
            LogAddressBlocked(logger);
            await WriteTooManyRequestsAsync(context, blockedFor, "Muitas tentativas com credenciais inválidas.");
            return;
        }

        var startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var statusCode = StatusCodes.Status500InternalServerError;
        try
        {
            var authentication = await context.AuthenticateAsync(AgentTokenAuthenticationHandler.SchemeName);
            if (authentication.Succeeded && tenantContext.Agent is { } agent
                && await IsRateLimitedAsync(context, agent, operation, rateLimiter))
            {
                statusCode = context.Response.StatusCode;
                return;
            }

            await next(context);
            statusCode = context.Response.StatusCode;
        }
        finally
        {
            if (operation.IsIdentified && !operation.SkipRecording)
            {
                // Sem o token de cancelamento da requisição: a auditoria fica mesmo se o cliente desistir.
                await writer.WriteAsync(Entry(context, operation, startedAt, stopwatch.ElapsedMilliseconds, statusCode), CancellationToken.None);
            }
        }
    }

    /// <summary>Banco indisponível no contador: 503. A requisição falharia de qualquer forma, porque a autenticação depende do banco.</summary>
    private async Task<bool> IsRateLimitedAsync(HttpContext context, ActiveAgent agent, AgentOperationContext operation, IAgentRateLimiter rateLimiter)
    {
        AgentRateLimitHit hit;
        try
        {
            hit = await rateLimiter.HitAsync(agent, context.RequestAborted);
        }
        catch (Exception exception) when (exception is DbException || exception.InnerException is DbException)
        {
            LogRateLimiterUnavailable(logger, exception, agent.CredentialId);
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, "Serviço temporariamente indisponível.", "Tente novamente em instantes.");
            return true;
        }

        if (!hit.IsLimited)
        {
            return false;
        }

        operation.Deny(AgentAuthorizationResult.RateLimited, "rate_limited");
        operation.SkipRecording = !hit.IsFirstRejection;
        await WriteTooManyRequestsAsync(context, hit.RetryAfterSeconds, "Limite de requisições da credencial atingido.");
        return true;
    }

    private static AgentOperationEntry Entry(HttpContext context, AgentOperationContext operation, DateTimeOffset startedAt, long elapsedMs, int statusCode)
    {
        var grant = context.Items[typeof(ProjectAccessGrant)] as ProjectAccessGrant;
        Guid? taskId = grant is not null && Guid.TryParse(context.GetRouteValue("taskId") as string, out var parsed) ? parsed : null;

        var result = operation.Result;
        var denialCode = operation.DenialCode;
        if (result == AgentAuthorizationResult.Allowed && statusCode == StatusCodes.Status403Forbidden)
        {
            result = AgentAuthorizationResult.BusinessRuleDenied;
            denialCode = "business_rule";
        }

        var route = context.GetEndpoint() is RouteEndpoint endpoint && endpoint.RoutePattern.RawText is { } raw
            ? "/" + raw.TrimStart('/')
            : "(rota desconhecida)";

        return new AgentOperationEntry(
            operation.OrganizationId!.Value,
            operation.AgentId!.Value,
            operation.CredentialId!.Value,
            startedAt,
            (int)Math.Min(elapsedMs, int.MaxValue),
            context.Request.Method,
            route,
            grant?.ProjectId,
            taskId,
            statusCode,
            result,
            denialCode,
            context.TraceIdentifier,
            string.Equals(context.Response.Headers[IdempotencyRecord.ReplayedHeaderName], "true", StringComparison.Ordinal),
            ActivityOrigins.FromClientHeader(context.Request.Headers[Activity.HttpActivityContext.ClientHeader].ToString()));
    }

    private static Task WriteTooManyRequestsAsync(HttpContext context, int retryAfterSeconds, string title)
    {
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return WriteProblemAsync(context, StatusCodes.Status429TooManyRequests, title, "Aguarde o tempo indicado em Retry-After antes de tentar novamente.");
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = statusCode, Title = title, Detail = detail },
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "IP bloqueado pelo limite de falhas de credenciais de agentes.")]
    private static partial void LogAddressBlocked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rate limiting indisponível para a credencial de agente {CredentialId}.")]
    private static partial void LogRateLimiterUnavailable(ILogger logger, Exception exception, Guid credentialId);
}
