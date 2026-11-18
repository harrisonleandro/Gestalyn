using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Agents;
using ProjectManager.Infrastructure.MultiTenancy;

namespace ProjectManager.API.Authentication;

/// <summary>
/// Esquema <c>AgentToken</c> (ADR-016): autentica <c>Authorization: Bearer gsk_...</c> conferindo a credencial a cada
/// requisição (hash, revogação, estado do agente e validade) e define a organização ativa pela credencial. Qualquer
/// recusa produz o mesmo 401, sem dizer o motivo. Logs só com o ID da credencial, nunca com o token ou o prefixo.
/// </summary>
internal sealed partial class AgentTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IAgentCredentialDirectory directory,
    TenantContext tenantContext,
    TimeProvider timeProvider) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "AgentToken";

    private const string BearerPrefix = "Bearer ";
    private const string FailureMessage = "Credencial de agente inválida.";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header[BearerPrefix.Length..].Trim();
        if (!token.StartsWith(AgentTokens.Marker, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        if (!AgentTokens.IsWellFormed(token))
        {
            LogMalformedToken(Logger);
            return AuthenticateResult.Fail(FailureMessage);
        }

        var match = await directory.FindByTokenAsync(token, Context.RequestAborted);
        if (match is null)
        {
            LogUnknownToken(Logger);
            return AuthenticateResult.Fail(FailureMessage);
        }

        var verdict = match.Evaluate(timeProvider.GetUtcNow());
        if (verdict != AgentCredentialVerdict.Valid)
        {
            LogRejectedCredential(Logger, match.CredentialId, match.AgentId, verdict);
            return AuthenticateResult.Fail(FailureMessage);
        }

        tenantContext.SetActiveAgent(new ActiveAgent(
            match.OrganizationId,
            match.AgentId,
            match.CredentialId,
            match.Scopes.ToHashSet(StringComparer.Ordinal)));

        List<Claim> claims =
        [
            new(AuthClaimTypes.Subject, match.AgentId.ToString()),
            new(AuthClaimTypes.ActorKind, AuthClaimTypes.AgentActor),
            new(AuthClaimTypes.Organization, match.OrganizationId.ToString()),
            new(AuthClaimTypes.CredentialId, match.CredentialId.ToString()),
            .. match.Scopes.Select(scope => new Claim(AuthClaimTypes.Scope, scope)),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, AuthClaimTypes.Subject, roleType: null));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    /// <summary>401 igual para qualquer recusa; o corpo em Problem Details vem das páginas de status.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token de agente malformado.")]
    private static partial void LogMalformedToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token de agente desconhecido.")]
    private static partial void LogUnknownToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credencial de agente {CredentialId} (agente {AgentId}) recusada: {Verdict}.")]
    private static partial void LogRejectedCredential(ILogger logger, Guid credentialId, Guid agentId, AgentCredentialVerdict verdict);
}
