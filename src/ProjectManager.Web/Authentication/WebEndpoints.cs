using Microsoft.Net.Http.Headers;
using ProjectManager.Web.Api;

namespace ProjectManager.Web.Authentication;

/// <summary>Marca endpoints que não são páginas, mas chamam a API em nome do usuário: o token é renovado antes deles.</summary>
internal sealed class ApiTokenEndpointMetadata;

/// <summary>Endpoints da Web fora das páginas Razor e o cabeçalho de segurança das respostas (ADR-015).</summary>
internal static class WebEndpoints
{
    public const string LastOrganizationCookie = ".Gestalyn.Organization";

    /// <summary>
    /// Content-Security-Policy: scripts, estilos e imagens só da própria origem (estilos inline são os de largura das barras
    /// de progresso). Reforça a renderização segura do Markdown (D6).
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseWebSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers[HeaderNames.ContentSecurityPolicy] = ContentSecurityPolicy;
                headers[HeaderNames.XContentTypeOptions] = "nosniff";
                headers["Referrer-Policy"] = "same-origin";
                return Task.CompletedTask;
            });
            await next();
        });

    /// <summary>
    /// Telas de agentes (ADR-016, D13): a resposta da criação de credencial traz o token, então nenhuma delas pode ser
    /// guardada em cache, nem pelo navegador nem por um proxy.
    /// </summary>
    public static IApplicationBuilder UseNoStoreForAgentPages(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (IsAgentPage(context.Request.Path))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    context.Response.Headers.Pragma = "no-cache";
                    return Task.CompletedTask;
                });
            }

            await next();
        });

    internal static bool IsAgentPage(PathString path) =>
        path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) is ["o", var id, "agentes", ..] && Guid.TryParse(id, out _);

    /// <summary>
    /// Guarda a última organização aberta (só o ID, sem dado sensível) para o redirecionamento depois de entrar (D3).
    /// </summary>
    public static IApplicationBuilder UseLastOrganizationCookie(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var segments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments is ["o", var id, ..] && Guid.TryParse(id, out var organizationId) && context.User.Identity?.IsAuthenticated == true
                && context.Request.Cookies[LastOrganizationCookie] != organizationId.ToString())
            {
                context.Response.Cookies.Append(LastOrganizationCookie, organizationId.ToString(), new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                    MaxAge = TimeSpan.FromDays(90),
                    IsEssential = true,
                });
            }

            await next();
        });

    /// <summary>
    /// Download de anexo pela Web: repassa o arquivo da API como anexo, sem expor o token ao navegador (D2). A API decide o
    /// acesso; um 404 ou 403 dela vira o mesmo código aqui.
    /// </summary>
    public static IEndpointRouteBuilder MapWebEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/o/{organizationId:guid}/tarefas/{taskId:guid}/anexos/{attachmentId:guid}", async (
            HttpContext httpContext,
            TasksApi tasks,
            Guid organizationId,
            Guid taskId,
            Guid attachmentId) =>
        {
            var response = await tasks.OpenAttachmentAsync(organizationId, taskId, attachmentId);
            if (response is null)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                return Results.StatusCode(status);
            }

            httpContext.Response.RegisterForDispose(response);
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName ?? "anexo";
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            return Results.File(await response.Content.ReadAsStreamAsync(httpContext.RequestAborted), contentType, fileName.Trim('"'));
        })
        .RequireAuthorization()
        .WithMetadata(new ApiTokenEndpointMetadata());

        return endpoints;
    }
}
